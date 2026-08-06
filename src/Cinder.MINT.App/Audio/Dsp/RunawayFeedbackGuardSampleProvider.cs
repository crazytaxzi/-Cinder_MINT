using NAudio.Wave;

namespace Cinder.MINT.Audio.Dsp;

/// <summary>
/// Last-resort protection against a sustained full-scale feedback tone.
/// It does not replace routing validation; it mutes an output if a loop is
/// introduced later by third-party routing software while MINT is already live.
/// </summary>
public sealed class RunawayFeedbackGuardSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action _tripped;
    private readonly int _tripFrames;
    private int _hotFrames;
    private bool _isTripped;

    public RunawayFeedbackGuardSampleProvider(ISampleProvider source, Action tripped)
    {
        _source = source;
        _tripped = tripped;
        _tripFrames = (int)(source.WaveFormat.SampleRate * 1.25);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read <= 0) return read;

        if (_isTripped)
        {
            Array.Clear(buffer, offset, read);
            return read;
        }

        float peak = 0f;
        double sumSquares = 0;
        for (int i = 0; i < read; i++)
        {
            float sample = buffer[offset + i];
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += sample * sample;
        }

        float rms = (float)Math.Sqrt(sumSquares / read);
        int frames = Math.Max(1, read / Math.Max(1, WaveFormat.Channels));

        // Feedback after a limiter is usually a sustained near-ceiling tone.
        // Loud music may touch the ceiling, but rarely holds this much RMS for
        // more than a full second without relief.
        if (peak >= 0.86f && rms >= 0.48f)
            _hotFrames += frames;
        else
            _hotFrames = Math.Max(0, _hotFrames - frames * 2);

        if (_hotFrames >= _tripFrames)
        {
            _isTripped = true;
            Array.Clear(buffer, offset, read);
            _tripped();
        }

        return read;
    }
}
