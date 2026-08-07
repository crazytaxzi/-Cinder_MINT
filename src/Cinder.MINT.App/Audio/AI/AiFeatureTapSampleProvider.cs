using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.AI;

internal sealed class AiFeatureTapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<MintAiContentMode> _contentMode;
    private readonly Action<AiFeatureFrame> _onFrame;

    private readonly int _channels;
    private readonly int _sampleRate;
    private readonly int _targetFrames;

    private float _lowState;
    private float _midState;
    private float _previousMono;
    private float _previousHighRatio;
    private bool _hasPreviousSample;

    private long _frames;
    private double _sumSquares;
    private double _lowSquares;
    private double _midSquares;
    private double _highSquares;
    private double _differenceSum;
    private long _zeroCrossings;
    private float _peak;

    public AiFeatureTapSampleProvider(
        ISampleProvider source,
        Func<MintAiContentMode> contentMode,
        Action<AiFeatureFrame> onFrame)
    {
        _source = source;
        _contentMode = contentMode;
        _onFrame = onFrame;
        _channels = source.WaveFormat.Channels;
        _sampleRate = source.WaveFormat.SampleRate;
        _targetFrames = Math.Max(256, _sampleRate / 10);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        Analyze(buffer, offset, read);
        return read;
    }

    private void Analyze(float[] buffer, int offset, int read)
    {
        float lowAlpha = OnePoleAlpha(250f);
        float midAlpha = OnePoleAlpha(2600f);

        int end = offset + read;
        for (int i = offset; i < end; i += _channels)
        {
            float mono = 0f;
            int availableChannels = Math.Min(_channels, end - i);
            for (int c = 0; c < availableChannels; c++)
                mono += buffer[i + c];
            mono /= Math.Max(availableChannels, 1);

            _lowState += lowAlpha * (mono - _lowState);
            _midState += midAlpha * (mono - _midState);

            float low = _lowState;
            float mid = _midState - _lowState;
            float high = mono - _midState;

            float absolute = Math.Abs(mono);
            float difference = _hasPreviousSample ? Math.Abs(mono - _previousMono) : 0f;

            if (_hasPreviousSample &&
                Math.Sign(mono) != Math.Sign(_previousMono) &&
                absolute > 0.0005f)
                _zeroCrossings++;

            _hasPreviousSample = true;
            _previousMono = mono;
            _peak = Math.Max(_peak, absolute);
            _sumSquares += mono * mono;
            _lowSquares += low * low;
            _midSquares += mid * mid;
            _highSquares += high * high;
            _differenceSum += difference;
            _frames++;

            if (_frames >= _targetFrames)
                PublishAndReset();
        }
    }

    private void PublishAndReset()
    {
        double frames = Math.Max(_frames, 1);
        float rms = (float)Math.Sqrt(_sumSquares / frames);
        float lowRms = (float)Math.Sqrt(_lowSquares / frames);
        float midRms = (float)Math.Sqrt(_midSquares / frames);
        float highRms = (float)Math.Sqrt(_highSquares / frames);
        float avgDifference = (float)(_differenceSum / frames);
        float zcr = Math.Clamp((float)(_zeroCrossings / frames) * 4f, 0f, 1f);

        float rmsDb = LinearToDb(Math.Max(rms, 0.000001f));
        float peakDb = LinearToDb(Math.Max(_peak, 0.000001f));
        float crestDb = Math.Max(0f, peakDb - rmsDb);

        float loudness = Normalize(rmsDb, -60f, -8f);
        float peak = Normalize(peakDb, -30f, -1f);
        float crest = Normalize(crestDb, 2f, 18f);

        float energySum = lowRms + midRms + highRms + 0.000001f;
        float lowRatio = Math.Clamp(lowRms / energySum, 0f, 1f);
        float midRatio = Math.Clamp(midRms / energySum, 0f, 1f);
        float highRatio = Math.Clamp(highRms / energySum, 0f, 1f);

        float normalizedDiff = Math.Clamp(avgDifference / Math.Max(rms, 0.002f) * 0.55f, 0f, 1f);
        float transient = Math.Clamp(normalizedDiff * 0.72f + crest * 0.28f, 0f, 1f);

        MintAiContentMode mode = _contentMode();
        float speechProbability = mode switch
        {
            MintAiContentMode.Voice or MintAiContentMode.RvcVoice =>
                Math.Clamp(0.34f + loudness * 0.48f + crest * 0.20f - zcr * 0.08f, 0f, 1f),
            MintAiContentMode.Music => 0.12f,
            MintAiContentMode.Mixed => 0.35f,
            _ => Math.Clamp(loudness * 0.55f + crest * 0.18f + midRatio * 0.28f - zcr * 0.10f, 0f, 1f)
        };

        float sibilance = Math.Clamp(
            (highRatio * 1.55f + normalizedDiff * 0.28f - 0.28f) *
            (0.35f + speechProbability * 0.80f),
            0f,
            1f);

        float noise = Math.Clamp(
            (1f - crest) * 0.34f +
            zcr * 0.31f +
            highRatio * 0.18f +
            (1f - Math.Max(speechProbability, 0.25f)) * 0.17f,
            0f,
            1f);

        float harshness = Math.Clamp(
            midRatio * 0.58f +
            highRatio * 0.68f +
            peak * 0.18f -
            0.34f,
            0f,
            1f);

        float metallicity = Math.Clamp(
            Math.Abs(highRatio - _previousHighRatio) * 2.8f +
            highRatio * 0.44f +
            zcr * 0.24f +
            harshness * 0.18f -
            transient * 0.10f,
            0f,
            1f);
        _previousHighRatio = highRatio;

        if (mode == MintAiContentMode.RvcVoice)
            metallicity = Math.Clamp(metallicity * 1.18f + 0.08f, 0f, 1f);

        _onFrame(new AiFeatureFrame(
            loudness,
            peak,
            crest,
            lowRatio,
            midRatio,
            highRatio,
            sibilance,
            transient,
            noise,
            harshness,
            metallicity,
            speechProbability));

        _frames = 0;
        _sumSquares = 0;
        _lowSquares = 0;
        _midSquares = 0;
        _highSquares = 0;
        _differenceSum = 0;
        _zeroCrossings = 0;
        _peak = 0f;
    }

    private float OnePoleAlpha(float cutoffHz)
    {
        float dt = 1f / _sampleRate;
        float rc = 1f / (2f * MathF.PI * cutoffHz);
        return dt / (rc + dt);
    }

    private static float Normalize(float value, float minimum, float maximum) =>
        Math.Clamp((value - minimum) / Math.Max(maximum - minimum, 0.000001f), 0f, 1f);

    private static float LinearToDb(float value) =>
        20f * MathF.Log10(Math.Max(value, 0.000001f));
}
