using Cinder.MINT.Models;
using NAudio.Dsp;
using NAudio.Wave;

namespace Cinder.MINT.Audio.Dsp;

/// <summary>
/// Low-latency deterministic DSP. AI nodes never synthesize audio: their neural
/// controllers update a private runtime profile and this provider performs the
/// bounded signal processing.
/// </summary>
public sealed class MintDspSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly DspConfiguration _config;
    private readonly AudioLevelState _levels;
    private readonly int _sampleRate;
    private readonly int _channels;

    private BiQuadFilter[] _highPass = [];
    private BiQuadFilter[] _lowEq = [];
    private BiQuadFilter[] _midEq = [];
    private BiQuadFilter[] _highEq = [];

    private float _lastHighPassHz = float.NaN;
    private float _lastLowGainDb = float.NaN;
    private float _lastMidGainDb = float.NaN;
    private float _lastHighGainDb = float.NaN;

    private float _gateEnvelope;
    private float _compressorEnvelope;
    private float _riderEnvelope = 0.05f;
    private float _riderGain = 1f;
    private float _deEssEnvelope;
    private float _previousSample;
    private float _duckGain = 1f;
    private float _limiterGain = 1f;
    private float _noiseFloorDb = -70f;

    public MintDspSampleProvider(
        ISampleProvider source,
        DspConfiguration config,
        AudioLevelState levels)
    {
        _source = source;
        _config = config;
        _levels = levels;
        _sampleRate = source.WaveFormat.SampleRate;
        _channels = source.WaveFormat.Channels;
        RefreshFilters(config.Profile, true);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        MintProfile p = _config.Profile;
        RefreshFilters(p, false);

        int channels = WaveFormat.Channels;
        int sampleRate = WaveFormat.SampleRate;
        float inputGain = DbToLinear(p.InputGainDb);
        float peak = 0f;
        double sumSquares = 0;

        float gateRelease = Coefficient(Milliseconds(p.GateReleaseMs, 30f, 600f), sampleRate);
        float compAttack = Coefficient(Milliseconds(p.CompressorAttackMs, 1f, 100f), sampleRate);
        float compRelease = Coefficient(Milliseconds(p.CompressorReleaseMs, 30f, 1000f), sampleRate);
        float riderSeconds = Milliseconds(p.RiderSpeedMs, 250f, 3000f);
        float riderEnvelopeCoefficient = Coefficient(riderSeconds, sampleRate);
        float riderGainCoefficient = Coefficient(Math.Clamp(riderSeconds * 0.35f, 0.08f, 1.2f), sampleRate);
        float duckAttack = Coefficient(Milliseconds(p.DuckerAttackMs, 1f, 500f), sampleRate);
        float duckRelease = Coefficient(Milliseconds(p.DuckerReleaseMs, 20f, 3000f), sampleRate);
        float limiterRelease = Coefficient(Milliseconds(p.LimiterReleaseMs, 10f, 1000f), sampleRate);

        for (int i = 0; i < read; i++)
        {
            int channel = i % channels;
            float sample = buffer[offset + i] * inputGain;
            float absolute = Math.Abs(sample);

            float instantDb = LinearToDb(Math.Max(absolute, 0.000001f));
            if (p.AutoMode && instantDb < _noiseFloorDb + 12f)
                _noiseFloorDb = 0.9995f * _noiseFloorDb + 0.0005f * instantDb;

            if (_config.HighPassEnabled)
                sample = _highPass[channel].Transform(sample);

            if (_config.GateEnabled)
            {
                float thresholdDb = p.AutoMode
                    ? Math.Max(p.GateThresholdDb, _noiseFloorDb + 7f)
                    : p.GateThresholdDb;
                float threshold = DbToLinear(thresholdDb);
                float target = absolute >= threshold
                    ? 1f
                    : Math.Clamp(absolute / Math.Max(threshold, 0.000001f), 0.08f, 1f);

                _gateEnvelope = target > _gateEnvelope
                    ? target
                    : gateRelease * _gateEnvelope + (1f - gateRelease) * target;
                sample *= _gateEnvelope;
            }

            if (_config.EqEnabled)
            {
                sample = _lowEq[channel].Transform(sample);
                sample = _midEq[channel].Transform(sample);
                sample = _highEq[channel].Transform(sample);
            }

            if (_config.DeEsserEnabled)
            {
                float hf = Math.Abs(sample - _previousSample);
                _previousSample = sample;
                _deEssEnvelope = 0.96f * _deEssEnvelope + 0.04f * hf;
                float trigger = 0.055f - Math.Clamp(p.DeEsserAmount, 0f, 1f) * 0.028f;
                if (_deEssEnvelope > trigger)
                {
                    float reduction = 1f - Math.Clamp(
                        (_deEssEnvelope - trigger) * 8f * p.DeEsserAmount,
                        0f,
                        0.42f);
                    sample *= reduction;
                }
            }

            absolute = Math.Abs(sample);
            _riderEnvelope = riderEnvelopeCoefficient * _riderEnvelope
                             + (1f - riderEnvelopeCoefficient) * absolute;

            if (_config.RiderEnabled)
            {
                float target = DbToLinear(p.TargetDb);
                float desiredGain = Math.Clamp(
                    target / Math.Max(_riderEnvelope * 1.35f, 0.0001f),
                    0.35f,
                    _config.IsProgram ? 2.0f : 2.8f);

                _riderGain = riderGainCoefficient * _riderGain
                             + (1f - riderGainCoefficient) * desiredGain;
                sample *= _riderGain;
            }

            if (_config.CompressorEnabled)
            {
                absolute = Math.Abs(sample);
                _compressorEnvelope = absolute > _compressorEnvelope
                    ? compAttack * _compressorEnvelope + (1f - compAttack) * absolute
                    : compRelease * _compressorEnvelope + (1f - compRelease) * absolute;

                float amount = Math.Clamp(p.Compression, 0f, 1f);
                float thresholdDb = -10f - amount * 14f;
                float ratio = 1.5f + amount * 5.5f;
                float envelopeDb = LinearToDb(Math.Max(_compressorEnvelope, 0.000001f));
                if (envelopeDb > thresholdDb)
                {
                    float compressedDb = thresholdDb + (envelopeDb - thresholdDb) / ratio;
                    sample *= DbToLinear(compressedDb - envelopeDb);
                }
            }

            if (_config.DuckerEnabled && _config.IsProgram)
            {
                float activity = _levels.VoiceActivity;
                float desiredDuck = activity > DbToLinear(p.DuckerThresholdDb)
                    ? DbToLinear(p.DuckingDb)
                    : 1f;
                float coefficient = desiredDuck < _duckGain ? duckAttack : duckRelease;
                _duckGain = coefficient * _duckGain + (1f - coefficient) * desiredDuck;
                sample *= _duckGain;
            }

            if (_config.LimiterEnabled)
            {
                float ceiling = DbToLinear(Math.Clamp(p.LimiterCeilingDb, -12f, -0.1f));
                absolute = Math.Abs(sample);
                float desired = absolute > ceiling
                    ? ceiling / Math.Max(absolute, 0.000001f)
                    : 1f;

                _limiterGain = desired < _limiterGain
                    ? desired
                    : limiterRelease * _limiterGain + (1f - limiterRelease);
                sample *= _limiterGain;
                sample = Math.Clamp(sample, -ceiling, ceiling);
            }

            buffer[offset + i] = sample;
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
        }

        float rms = (float)Math.Sqrt(sumSquares / Math.Max(read, 1));
        float peakDb = LinearToDb(Math.Max(peak, 0.000001f));

        if (_config.IsVoice)
        {
            _levels.VoicePeakDb = peakDb;
            float speechThreshold = DbToLinear(Math.Max(p.GateThresholdDb + 8f, -42f));
            _levels.VoiceActivity = Math.Clamp((rms - speechThreshold) / 0.12f, 0f, 1f);
        }
        else if (_config.IsProgram)
        {
            _levels.ProgramPeakDb = peakDb;
        }
        else if (_config.IsMaster)
        {
            _levels.MasterPeakDb = peakDb;
        }

        return read;
    }

    private void RefreshFilters(MintProfile p, bool force)
    {
        float hp = Math.Clamp(p.HighPassHz, 30f, 220f);
        float low = Math.Clamp(p.LowGainDb, -12f, 12f);
        float mid = Math.Clamp(p.MidGainDb, -12f, 12f);
        float high = Math.Clamp(p.HighGainDb, -12f, 12f);

        if (force || Math.Abs(hp - _lastHighPassHz) >= 0.35f)
        {
            _highPass = Create(_channels, () => BiQuadFilter.HighPassFilter(_sampleRate, hp, 0.707f));
            _lastHighPassHz = hp;
        }

        if (force || Math.Abs(low - _lastLowGainDb) >= 0.06f)
        {
            _lowEq = Create(_channels, () => BiQuadFilter.LowShelf(_sampleRate, 140, 0.8f, low));
            _lastLowGainDb = low;
        }

        if (force || Math.Abs(mid - _lastMidGainDb) >= 0.06f)
        {
            _midEq = Create(_channels, () => BiQuadFilter.PeakingEQ(_sampleRate, 1800, 0.9f, mid));
            _lastMidGainDb = mid;
        }

        if (force || Math.Abs(high - _lastHighGainDb) >= 0.06f)
        {
            _highEq = Create(_channels, () => BiQuadFilter.HighShelf(_sampleRate, 6200, 0.8f, high));
            _lastHighGainDb = high;
        }
    }

    private static BiQuadFilter[] Create(int count, Func<BiQuadFilter> factory) =>
        Enumerable.Range(0, count).Select(_ => factory()).ToArray();

    private static float Milliseconds(float milliseconds, float minimum, float maximum) =>
        Math.Clamp(milliseconds, minimum, maximum) / 1000f;

    private static float Coefficient(float seconds, int sampleRate) =>
        MathF.Exp(-1f / Math.Max(1f, seconds * sampleRate));

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);

    private static float LinearToDb(float value) =>
        20f * MathF.Log10(Math.Max(value, 0.000001f));
}
