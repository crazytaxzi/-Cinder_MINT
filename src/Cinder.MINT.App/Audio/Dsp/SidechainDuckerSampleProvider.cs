using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.Dsp;

public sealed class SidechainDuckerSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _main;
    private readonly ISampleProvider _sidechain;
    private readonly MintProfile _profile;
    private float[] _sidechainBuffer = [];
    private float _envelope;
    private float _gain = 1f;

    public SidechainDuckerSampleProvider(
        ISampleProvider main,
        ISampleProvider sidechain,
        MintProfile profile)
    {
        if (main.WaveFormat.SampleRate != sidechain.WaveFormat.SampleRate ||
            main.WaveFormat.Channels != sidechain.WaveFormat.Channels)
            throw new InvalidOperationException("Main and sidechain signals must share the same engine format.");

        _main = main;
        _sidechain = sidechain;
        _profile = profile;
    }

    public WaveFormat WaveFormat => _main.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _main.Read(buffer, offset, count);
        if (read <= 0) return read;

        if (_sidechainBuffer.Length < read)
            _sidechainBuffer = new float[read];

        Array.Clear(_sidechainBuffer, 0, read);
        int sidechainRead = _sidechain.Read(_sidechainBuffer, 0, read);

        int sampleRate = WaveFormat.SampleRate;
        float detectorAttack = Coefficient(0.008f, sampleRate);
        float detectorRelease = Coefficient(0.12f, sampleRate);
        float gainAttack = Coefficient(Math.Clamp(_profile.DuckerAttackMs, 1f, 500f) / 1000f, sampleRate);
        float gainRelease = Coefficient(Math.Clamp(_profile.DuckerReleaseMs, 20f, 3000f) / 1000f, sampleRate);
        float threshold = DbToLinear(Math.Clamp(_profile.DuckerThresholdDb, -72f, -6f));
        float duckGain = DbToLinear(Math.Clamp(_profile.DuckingDb, -30f, 0f));

        for (int i = 0; i < read; i++)
        {
            float sidechain = i < sidechainRead ? Math.Abs(_sidechainBuffer[i]) : 0f;
            float detectorCoefficient = sidechain > _envelope ? detectorAttack : detectorRelease;
            _envelope = detectorCoefficient * _envelope + (1f - detectorCoefficient) * sidechain;

            float desired = _envelope >= threshold ? duckGain : 1f;
            float gainCoefficient = desired < _gain ? gainAttack : gainRelease;
            _gain = gainCoefficient * _gain + (1f - gainCoefficient) * desired;
            buffer[offset + i] *= _gain;
        }

        return read;
    }

    private static float Coefficient(float seconds, int sampleRate) =>
        MathF.Exp(-1f / Math.Max(1f, seconds * sampleRate));

    private static float DbToLinear(float db) => MathF.Pow(10f, db / 20f);
}
