using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.AI;

/// <summary>
/// Low-latency deterministic spectral suppressor controlled by the per-node neural brain.
/// The neural controller never fabricates waveform samples; it only moves bounded noise
/// reduction, sensitivity, speech-protection, and learning-rate controls on the detached
/// runtime profile.
/// </summary>
internal sealed class AdaptiveNeuralNoiseSampleProvider : ISampleProvider
{
    private const int FftSize = 512;
    private const int HopSize = 256;
    private const float Epsilon = 1e-12f;

    private readonly ISampleProvider _source;
    private readonly MintProfile _runtime;
    private readonly int _channels;
    private readonly float[] _window = new float[FftSize];
    private readonly ChannelState[] _states;
    private readonly List<float> _input = new(FftSize * 8);
    private readonly Queue<float> _output = new(FftSize * 8);
    private float[] _readScratch = new float[FftSize * 4];

    public AdaptiveNeuralNoiseSampleProvider(ISampleProvider source, MintProfile runtime)
    {
        _source = source;
        _runtime = runtime;
        _channels = source.WaveFormat.Channels;

        if (source.WaveFormat.SampleRate != 48000)
            throw new InvalidOperationException("AI Noise Filter expects MINT's 48 kHz internal stream.");
        if (_channels is < 1 or > 2)
            throw new InvalidOperationException("AI Noise Filter supports mono or stereo streams.");

        for (int i = 0; i < FftSize; i++)
        {
            // sqrt-Hann analysis/synthesis pair. With 50% overlap, squared windows
            // sum to unity and preserve level through overlap-add.
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

        // MINT is a live graph. During the initial FFT lookahead, output silence rather
        // than returning 0 and causing WASAPI to treat the stream as ended.
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
            for (int channel = 0; channel < _channels; channel++)
                ProcessChannel(channel);

            for (int frame = 0; frame < HopSize; frame++)
            {
                for (int channel = 0; channel < _channels; channel++)
                {
                    float value = _states[channel].Overlap[frame];
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

    private void ProcessChannel(int channel)
    {
        ChannelState state = _states[channel];

        for (int i = 0; i < FftSize; i++)
        {
            state.Real[i] = _input[i * _channels + channel] * _window[i];
            state.Imag[i] = 0f;
        }

        Fft(state.Real, state.Imag, inverse: false);

        float requestedReduction = Math.Clamp(
            _runtime.AiNoiseReductionDb,
            0f,
            Math.Clamp(_runtime.AiNoiseMaxReductionDb, 6f, 36f));
        float floorGain = DbToLinear(-requestedReduction);
        float sensitivity = Math.Clamp(_runtime.AiNoiseSensitivity, 0.05f, 1f);
        float speechProtect = Math.Clamp(_runtime.AiNoiseSpeechProtection, 0f, 1f);
        float learnRate = Math.Clamp(_runtime.AiNoiseLearnRate, 0.001f, 0.25f);
        float oversubtraction = 0.85f + sensitivity * 1.9f;

        int half = FftSize / 2;
        for (int k = 0; k <= half; k++)
        {
            float real = state.Real[k];
            float imag = state.Imag[k];
            float power = real * real + imag * imag + Epsilon;

            if (!state.NoisePrimed)
                state.NoisePower[k] = Math.Max(power * 0.08f, Epsilon);

            float noise = state.NoisePower[k];
            bool looksLikeNoise = power < noise * (1.6f + sensitivity * 1.8f);
            float update = looksLikeNoise
                ? learnRate
                : learnRate * (0.012f + (1f - speechProtect) * 0.035f);
            noise += (power - noise) * update;
            noise = Math.Max(noise, Epsilon);
            state.NoisePower[k] = noise;

            float estimatedNoise = noise * oversubtraction;
            float cleanPower = Math.Max(power - estimatedNoise, 0f);
            float wiener = cleanPower / power;
            float targetGain = floorGain + (1f - floorGain) * MathF.Pow(wiener, 0.72f);

            // Naturalness/speech protection never disables the denoiser; it simply
            // prevents the mask from chewing deeply into voice harmonics.
            float protectionBlend = speechProtect * 0.16f;
            targetGain += (1f - targetGain) * protectionBlend;

            float previous = state.PreviousGain[k];
            float smoothed = previous + (targetGain - previous) * (targetGain < previous ? 0.32f : 0.18f);
            state.PreviousGain[k] = smoothed;
        }
        state.NoisePrimed = true;

        // Frequency smoothing reduces musical-noise pinholes without blurring the
        // mask enough to dull consonants.
        for (int k = 0; k <= half; k++)
        {
            float left = state.PreviousGain[Math.Max(0, k - 1)];
            float center = state.PreviousGain[k];
            float right = state.PreviousGain[Math.Min(half, k + 1)];
            state.SmoothedGain[k] = left * 0.22f + center * 0.56f + right * 0.22f;
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

    private sealed class ChannelState
    {
        public float[] Real { get; } = new float[FftSize];
        public float[] Imag { get; } = new float[FftSize];
        public float[] NoisePower { get; } = new float[FftSize / 2 + 1];
        public float[] PreviousGain { get; } = Enumerable.Repeat(1f, FftSize / 2 + 1).ToArray();
        public float[] SmoothedGain { get; } = new float[FftSize / 2 + 1];
        public float[] Overlap { get; } = new float[FftSize];
        public bool NoisePrimed { get; set; }
    }
}
