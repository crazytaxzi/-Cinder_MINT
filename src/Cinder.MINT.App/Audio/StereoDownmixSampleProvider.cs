using NAudio.Wave;

namespace Cinder.MINT.Audio;

public sealed class StereoDownmixSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly float[] _sourceBuffer;
    private readonly int _sourceChannels;

    public StereoDownmixSampleProvider(ISampleProvider source)
    {
        if (source.WaveFormat.Channels < 2)
            throw new ArgumentException("Source must have at least two channels.", nameof(source));

        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        _sourceBuffer = new float[8192 * _sourceChannels];
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        int requestedFrames = count / 2;
        int sourceSamplesWanted = Math.Min(requestedFrames * _sourceChannels, _sourceBuffer.Length);
        int sourceRead = _source.Read(_sourceBuffer, 0, sourceSamplesWanted);
        int framesRead = sourceRead / _sourceChannels;

        for (int frame = 0; frame < framesRead; frame++)
        {
            int src = frame * _sourceChannels;
            buffer[offset + frame * 2] = _sourceBuffer[src];
            buffer[offset + frame * 2 + 1] = _sourceBuffer[src + 1];
        }

        return framesRead * 2;
    }
}
