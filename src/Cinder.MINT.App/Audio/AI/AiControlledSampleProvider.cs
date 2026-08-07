using Cinder.MINT.Audio.Dsp;
using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.AI;

internal sealed class AiControlledSampleProvider : ISampleProvider
{
    private readonly MintProfile _intentProfile;
    private readonly MintProfile _runtimeProfile;
    private readonly AiBrainSession _session;
    private readonly ISampleProvider _output;

    public AiControlledSampleProvider(
        ISampleProvider source,
        AudioNodeModel node,
        MintAiRuntime runtime,
        AudioLevelState levels)
    {
        // The graph/profile objects are UI-owned and have PropertyChanged subscribers.
        // Copy only their values before the realtime callback is built. The audio thread
        // must never dereference a live WPF-bound node/profile object.
        _intentProfile = DetachedCopy(node.Profile);
        _runtimeProfile = DetachedCopy(_intentProfile);
        _session = runtime.GetOrCreate(node.Id, node.AiSpecialist);

        var tap = new AiFeatureTapSampleProvider(
            source,
            () => _intentProfile.AiContentMode,
            frame => _session.Evaluate(frame, _intentProfile, _runtimeProfile));

        if (node.AiSpecialist == MintAiSpecialist.Noise)
        {
            _output = new AdaptiveNeuralNoiseSampleProvider(tap, _runtimeProfile);
            return;
        }

        DspConfiguration configuration = ConfigurationFor(node.AiSpecialist, _runtimeProfile);
        _output = new MintDspSampleProvider(tap, configuration, levels);
    }

    public WaveFormat WaveFormat => _output.WaveFormat;

    public int Read(float[] buffer, int offset, int count) =>
        _output.Read(buffer, offset, count);

    private static MintProfile DetachedCopy(MintProfile source)
    {
        var copy = new MintProfile();
        copy.CopyFrom(source);
        return copy;
    }

    private static DspConfiguration ConfigurationFor(
        MintAiSpecialist specialist,
        MintProfile profile)
    {
        var config = new DspConfiguration
        {
            Profile = profile,
            IsVoice = specialist == MintAiSpecialist.Cleanup,
            IsProgram = specialist == MintAiSpecialist.Loudness,
            IsMaster = specialist == MintAiSpecialist.Master
        };

        switch (specialist)
        {
            case MintAiSpecialist.Cleanup:
                config.GateEnabled = true;
                config.HighPassEnabled = true;
                config.DeEsserEnabled = true;
                config.EqEnabled = true;
                break;

            case MintAiSpecialist.Tone:
                config.EqEnabled = true;
                break;

            case MintAiSpecialist.Dynamics:
                config.CompressorEnabled = true;
                break;

            case MintAiSpecialist.Loudness:
                config.RiderEnabled = true;
                break;

            case MintAiSpecialist.Master:
                config.EqEnabled = true;
                config.CompressorEnabled = true;
                config.LimiterEnabled = true;
                break;

            case MintAiSpecialist.Noise:
                // Noise is handled by AdaptiveNeuralNoiseSampleProvider above.
                break;
        }

        return config;
    }
}
