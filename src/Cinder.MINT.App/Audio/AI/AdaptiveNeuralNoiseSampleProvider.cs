using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.AI;

/// <summary>
/// Low-latency deterministic spectral suppressor controlled by the per-node neural brain.
/// V2 adds fast voice activity/noise-floor tracking, hard idle suppression, minimum-statistics
/// noise estimation, and automatic dominant-channel handling for broken stereo voice devices.
/// No waveform content is generated; the neural layer only controls bounded DSP behavior.
/// </summary>
internal sealed class AdaptiveNeuralNoiseSampleProvider : ISampleProvider
{
    private const int FftSize = 512;
    private const int HopSize = 256;
    private const float Epsilon = 1e-12f;

    private readonly ISampleProvider _source;
    private readonly MintProfile _runtime;
    private readonly NoiseObservationState _observation;
    private readonly int _channels;
    private readonly float[] _window = new float[FftSize];
    private readonly ChannelState[] _states;
    private readonly List<float> _input = new(FftSize * 8);
    private readonly Queue<float> _output = new(FftSize * 8);
    private float[] _readScratch = new float[FftSize * 4];

    private float _noiseRms = 0.006f;
    private bool _noiseRmsPrimed;
    private float _idleGain = 1f;
    private int _selectedChannel = -1;
    private int _candidateChannel = -1;
    private int _candidateFrames;
    private int _coherentFrames;

    public AdaptiveNeuralNoiseSampleProvider(
        ISampleProvider source,
        MintProfile runtime,
        NoiseObservationState observation)
    {
        _source = source;
        _runtime = runtime;
        _observation = observation;
        _channels = source.WaveFormat.Channels;

        if (source.WaveFormat.SampleRate != 48000)
            throw new InvalidOperationException("AI Noise Filter expects MINT's 48 kHz internal stream.");
        if (_channels is < 1 or > 2)
            throw new InvalidOperationException("AI Noise Filter supports mono or stereo streams.");

        for (int i = 0; i < FftSize; i++)
        {
            float hann = 0.5f - 0.5f * MathF.Cos(2f * MathF.PI * i / FftSize);
            _window[i] = MathF.Sqrt(Math.Max(0f, hann));
        }

        _states = Enumerable.Range(0, _channels)
            .Select(_ => new ChannelState())
            .ToArray();
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        while (_output.Count < count)
        {
            int request = Math.Max(HopSize * _channels, Math.Min(count, FftSize * _channels * 2));
            EnsureReadScratch(request);
            int read = _source.Read(_readScratch, 0, request);
            if (read <= 0)
                break;

            for (int i = 0; i < read; i++)
                _input.Add(_readScratch[i]);

            ProcessAvailableFrames();
        }

        int written = 0;
        while (written < count && _output.Count > 0)
            buffer[offset + written++] = _output.Dequeue();

        // A live WASAPI graph must not return end-of-stream while the FFT lookahead fills.
        if (written < count)
        {
            Array.Clear(buffer, offset + written, count - written);
            written = count;
        }

        return written;
    }

    private void ProcessAvailableFrames()
    {
        int frameSamples = FftSize * _channels;
        int hopSamples = HopSize * _channels;

        while (_input.Count >= frameSamples)
        {
            FrameContext context = AnalyzeFrame();
            UpdateAutomaticChannelSelection(context);

            for (int channel = 0; channel < _channels; channel++)
            {
                int sourceChannel = _selectedChannel >= 0 ? _selectedChannel : channel;
                ProcessChannel(channel, sourceChannel, context);
            }

            float idleTarget = ComputeIdleTarget(context);
            float openCoefficient = 0.085f;
            float closeCoefficient = 0.0018f + Math.Clamp(_runtime.AiNoiseSensitivity, 0.05f, 1f) * 0.0012f;

            for (int frame = 0; frame < HopSize; frame++)
            {
                float coefficient = idleTarget > _idleGain ? openCoefficient : closeCoefficient;
                _idleGain += (idleTarget - _idleGain) * coefficient;

                for (int channel = 0; channel < _channels; channel++)
                {
                    float value = _states[channel].Overlap[frame] * _idleGain;
                    _output.Enqueue(Math.Clamp(value, -1f, 1f));
                }
            }

            foreach (ChannelState state in _states)
            {
                Array.Copy(state.Overlap, HopSize, state.Overlap, 0, FftSize - HopSize);
                Array.Clear(state.Overlap, FftSize - HopSize, HopSize);
            }

            _input.RemoveRange(0, hopSamples);
        }
    }

    private FrameContext AnalyzeFrame()
    {
        double monoPower = 0;
        double leftPower = 0;
        double rightPower = 0;
        double cross = 0;
        float peak = 0f;
        float previous = 0f;
        double difference = 0;

        for (int i = 0; i < FftSize; i++)
        {
            float left = _input[i * _channels];
            float right = _channels == 2 ? _input[i * _channels + 1] : left;
            float mono = _channels == 2 ? (left + right) * 0.5f : left;

            monoPower += mono * mono;
            leftPower += left * left;
            rightPower += right * right;
            cross += left * right;
            peak = Math.Max(peak, Math.Abs(mono));
            if (i > 0) difference += Math.Abs(mono - previous);
            previous = mono;
        }

        float rms = (float)Math.Sqrt(monoPower / FftSize + Epsilon);
        float peakToRms = peak / Math.Max(rms, 0.000001f);
        float fastTransient = Math.Clamp((float)(difference / FftSize) / Math.Max(rms, 0.002f) * 0.6f, 0f, 1f);

        float observedSpeech = Math.Clamp(_observation.SpeechProbability, 0f, 1f);
        float observedNoise = Math.Clamp(_observation.Noise, 0f, 1f);

        if (!_noiseRmsPrimed)
        {
            // Do not assume the first frame is room tone. Cap the initial estimate so a
            // person speaking immediately after START MINT is not mistaken for the floor.
            _noiseRms = Math.Clamp(rms, 0.0015f, 0.008f);
            _noiseRmsPrimed = true;
        }

        float snrDb = LinearToDb(Math.Max(rms, Epsilon) / Math.Max(_noiseRms, 0.000001f));
        bool instantVoice =
            observedSpeech >= 0.42f ||
            snrDb >= 8.0f ||
            (snrDb >= 5.5f && peakToRms >= 2.0f && observedNoise < 0.72f);

        bool likelyNoiseOnly =
            !instantVoice &&
            observedSpeech < 0.34f &&
            (observedNoise > 0.30f || snrDb < 5.0f);

        if (likelyNoiseOnly)
        {
            float learn = 0.018f + Math.Clamp(_runtime.AiNoiseLearnRate, 0.001f, 0.25f) * 0.55f;
            _noiseRms += (rms - _noiseRms) * Math.Clamp(learn, 0.01f, 0.18f);
        }
        else if (rms < _noiseRms)
        {
            _noiseRms += (rms - _noiseRms) * 0.02f;
        }

        float correlation = 1f;
        float imbalanceDb = 0f;
        int dominantChannel = -1;
        if (_channels == 2)
        {
            correlation = (float)(cross / Math.Sqrt(Math.Max(leftPower * rightPower, Epsilon)));
            float maxPower = (float)Math.Max(leftPower, rightPower);
            float minPower = (float)Math.Max(Math.Min(leftPower, rightPower), Epsilon);
            imbalanceDb = 10f * MathF.Log10(maxPower / minPower);
            dominantChannel = leftPower >= rightPower ? 0 : 1;
        }

        return new FrameContext(
            rms,
            snrDb,
            observedSpeech,
            observedNoise,
            Math.Max(fastTransient, _observation.Transient),
            instantVoice,
            likelyNoiseOnly,
            correlation,
            imbalanceDb,
            dominantChannel);
    }

    private void UpdateAutomaticChannelSelection(FrameContext context)
    {
        if (_channels != 2 ||
            _runtime.AiContentMode is not (MintAiContentMode.Voice or MintAiContentMode.RvcVoice))
        {
            _selectedChannel = -1;
            return;
        }

        bool obviouslyBrokenStereo =
            context.ImbalanceDb >= 8f ||
            (Math.Abs(context.Correlation) < 0.18f && context.ImbalanceDb >= 4.5f);

        if (obviouslyBrokenStereo)
        {
            _coherentFrames = 0;
            if (_candidateChannel == context.DominantChannel)
                _candidateFrames++;
            else
            {
                _candidateChannel = context.DominantChannel;
                _candidateFrames = 1;
            }

            if (_candidateFrames >= 10 && _selectedChannel != _candidateChannel)
            {
                _selectedChannel = _candidateChannel;
                foreach (ChannelState state in _states)
                    state.ResetNoiseEstimator();
            }
            return;
        }

        _candidateFrames = 0;
        _candidateChannel = -1;

        if (_selectedChannel >= 0 &&
            Math.Abs(context.Correlation) >= 0.78f &&
            context.ImbalanceDb <= 2.5f)
        {
            _coherentFrames++;
            if (_coherentFrames >= 80)
            {
                _selectedChannel = -1;
                _coherentFrames = 0;
                foreach (ChannelState state in _states)
                    state.ResetNoiseEstimator();
            }
        }
        else
        {
            _coherentFrames = 0;
        }
    }

    private float ComputeIdleTarget(FrameContext context)
    {
        if (context.InstantVoice)
            return 1f;

        float maxReduction = Math.Clamp(_runtime.AiNoiseMaxReductionDb, 6f, 36f);
        float strength = Math.Clamp(_runtime.AiStrength, 0f, 1f);
        float sensitivity = Math.Clamp(_runtime.AiNoiseSensitivity, 0.05f, 1f);

        // Spectral reduction handles noise under speech. This second stage is an expander
        // only for confirmed non-speech, allowing one node to kill room tone between words
        // without forcing the spectral mask to chew through the voice itself.
        float idleReductionDb = Math.Clamp(
            maxReduction + 12f + sensitivity * 8f,
            18f,
            56f) * (0.62f + strength * 0.38f);

        if (!context.LikelyNoiseOnly)
            idleReductionDb *= 0.45f;

        return DbToLinear(-idleReductionDb);
    }

    private void ProcessChannel(
        int outputChannel,
        int sourceChannel,
        FrameContext context)
    {
        ChannelState state = _states[outputChannel];

        for (int i = 0; i < FftSize; i++)
        {
            state.Real[i] = _input[i * _channels + sourceChannel] * _window[i];
            state.Imag[i] = 0f;
        }

        Fft(state.Real, state.Imag, inverse: false);

        float maxReduction = Math.Clamp(_runtime.AiNoiseMaxReductionDb, 6f, 36f);
        float brainReduction = Math.Clamp(_runtime.AiNoiseReductionDb, 0f, maxReduction);
        float strength = Math.Clamp(_runtime.AiStrength, 0f, 1f);
        float naturalness = Math.Clamp(_runtime.AiNaturalness, 0f, 1f);
        float sensitivity = Math.Clamp(_runtime.AiNoiseSensitivity, 0.05f, 1f);
        float speechProtect = Math.Clamp(_runtime.AiNoiseSpeechProtection, 0f, 1f);
        float learnRate = Math.Clamp(_runtime.AiNoiseLearnRate, 0.001f, 0.25f);

        // The first version obeyed the neural request too literally. V2 treats it as one
        // signal of need while the fast local noise/VAD layer can ask for stronger bounded
        // reduction when the room is clearly noisy.
        float observedNeed = Math.Clamp(
            context.ObservedNoise * 0.72f +
            (1f - context.ObservedSpeech) * 0.18f +
            sensitivity * 0.10f,
            0f,
            1f);
        float brainNeed = maxReduction <= 0f ? 0f : brainReduction / maxReduction;
        float reductionNeed = Math.Max(brainNeed, observedNeed);
        float requestedReduction = Math.Clamp(
            maxReduction * (0.30f + reductionNeed * 0.70f) * (0.58f + strength * 0.42f),
            0f,
            maxReduction);

        float floorGain = DbToLinear(-requestedReduction);
        float oversubtraction = 1.35f + sensitivity * 3.15f;
        int half = FftSize / 2;

        for (int k = 0; k <= half; k++)
        {
            float real = state.Real[k];
            float imag = state.Imag[k];
            float power = real * real + imag * imag + Epsilon;

            if (!state.NoisePrimed)
            {
                state.MinimumPower[k] = power;
                state.NoisePower[k] = context.InstantVoice
                    ? Math.Max(power * 0.08f, Epsilon)
                    : Math.Max(power, Epsilon);
            }

            float minimum = state.MinimumPower[k];
            if (!state.NoisePrimed || power < minimum)
                minimum = power;
            else
                minimum += (power - minimum) * 0.00055f;
            minimum = Math.Max(minimum, Epsilon);
            state.MinimumPower[k] = minimum;

            float noise = state.NoisePower[k];
            bool binLooksLikeNoise = power < noise * (2.4f + sensitivity * 3.2f);

            float update;
            if (context.LikelyNoiseOnly)
                update = Math.Clamp(learnRate * (2.2f + sensitivity * 1.8f), 0.02f, 0.42f);
            else if (binLooksLikeNoise)
                update = Math.Clamp(learnRate * 0.12f, 0.001f, 0.035f);
            else
                update = 0.00035f;

            noise += (power - noise) * update;
            noise = Math.Max(noise, minimum * 0.82f);
            state.NoisePower[k] = Math.Max(noise, Epsilon);

            float estimatedNoise = state.NoisePower[k] * oversubtraction;
            float wiener = Math.Clamp(1f - estimatedNoise / power, 0f, 1f);
            float spectralGain = MathF.Pow(wiener, 0.86f + sensitivity * 0.34f);
            float targetGain = Math.Max(floorGain, spectralGain);

            float frequency = k * 48000f / FftSize;
            bool voiceBand = frequency >= 95f && frequency <= 7600f;
            if (context.InstantVoice && voiceBand)
            {
                // During speech the node may still remove noise, but a protected voice-band
                // floor prevents stacked-denoiser-style hollowing and watery consonants.
                float speechMaxReductionDb =
                    12f +
                    (1f - speechProtect) * 22f +
                    sensitivity * 4f +
                    (1f - naturalness) * 4f;
                targetGain = Math.Max(
                    targetGain,
                    DbToLinear(-Math.Min(requestedReduction, speechMaxReductionDb)));
            }

            float previous = state.PreviousGain[k];
            float attenuationRate = context.LikelyNoiseOnly ? 0.48f : 0.24f;
            float recoveryRate = context.InstantVoice ? 0.48f : 0.28f;
            float rate = targetGain < previous ? attenuationRate : recoveryRate;
            state.PreviousGain[k] = previous + (targetGain - previous) * rate;
        }
        state.NoisePrimed = true;

        // Wider five-bin smoothing knocks down isolated musical-noise pinholes while
        // retaining enough resolution for speech consonants.
        for (int k = 0; k <= half; k++)
        {
            float m2 = state.PreviousGain[Math.Max(0, k - 2)];
            float m1 = state.PreviousGain[Math.Max(0, k - 1)];
            float c = state.PreviousGain[k];
            float p1 = state.PreviousGain[Math.Min(half, k + 1)];
            float p2 = state.PreviousGain[Math.Min(half, k + 2)];
            state.SmoothedGain[k] =
                m2 * 0.08f + m1 * 0.19f + c * 0.46f + p1 * 0.19f + p2 * 0.08f;
        }

        for (int k = 0; k <= half; k++)
        {
            float gain = state.SmoothedGain[k];
            state.Real[k] *= gain;
            state.Imag[k] *= gain;

            if (k > 0 && k < half)
            {
                int mirror = FftSize - k;
                state.Real[mirror] *= gain;
                state.Imag[mirror] *= gain;
            }
        }

        Fft(state.Real, state.Imag, inverse: true);

        for (int i = 0; i < FftSize; i++)
            state.Overlap[i] += state.Real[i] * _window[i];
    }

    private void EnsureReadScratch(int count)
    {
        if (_readScratch.Length < count)
            _readScratch = new float[count];
    }

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);
    private static float LinearToDb(float value) => 20f * MathF.Log10(Math.Max(value, 0.000001f));

    private static void Fft(float[] real, float[] imag, bool inverse)
    {
        int n = real.Length;

        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;

            if (i < j)
            {
                (real[i], real[j]) = (real[j], real[i]);
                (imag[i], imag[j]) = (imag[j], imag[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            float angle = (inverse ? 2f : -2f) * MathF.PI / length;
            float wLenReal = MathF.Cos(angle);
            float wLenImag = MathF.Sin(angle);

            for (int i = 0; i < n; i += length)
            {
                float wReal = 1f;
                float wImag = 0f;
                int half = length >> 1;

                for (int j = 0; j < half; j++)
                {
                    int even = i + j;
                    int odd = even + half;
                    float oddReal = real[odd] * wReal - imag[odd] * wImag;
                    float oddImag = real[odd] * wImag + imag[odd] * wReal;
                    float evenReal = real[even];
                    float evenImag = imag[even];

                    real[even] = evenReal + oddReal;
                    imag[even] = evenImag + oddImag;
                    real[odd] = evenReal - oddReal;
                    imag[odd] = evenImag - oddImag;

                    float nextReal = wReal * wLenReal - wImag * wLenImag;
                    wImag = wReal * wLenImag + wImag * wLenReal;
                    wReal = nextReal;
                }
            }
        }

        if (inverse)
        {
            float scale = 1f / n;
            for (int i = 0; i < n; i++)
            {
                real[i] *= scale;
                imag[i] *= scale;
            }
        }
    }

    private readonly record struct FrameContext(
        float Rms,
        float SnrDb,
        float ObservedSpeech,
        float ObservedNoise,
        float Transient,
        bool InstantVoice,
        bool LikelyNoiseOnly,
        float Correlation,
        float ImbalanceDb,
        int DominantChannel);

    private sealed class ChannelState
    {
        public float[] Real { get; } = new float[FftSize];
        public float[] Imag { get; } = new float[FftSize];
        public float[] NoisePower { get; } = new float[FftSize / 2 + 1];
        public float[] MinimumPower { get; } = new float[FftSize / 2 + 1];
        public float[] PreviousGain { get; } = Enumerable.Repeat(1f, FftSize / 2 + 1).ToArray();
        public float[] SmoothedGain { get; } = Enumerable.Repeat(1f, FftSize / 2 + 1).ToArray();
        public float[] Overlap { get; } = new float[FftSize];
        public bool NoisePrimed { get; set; }

        public void ResetNoiseEstimator()
        {
            Array.Clear(NoisePower);
            Array.Clear(MinimumPower);
            Array.Fill(PreviousGain, 1f);
            Array.Fill(SmoothedGain, 1f);
            NoisePrimed = false;
        }
    }
}
