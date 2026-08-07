using Cinder.MINT.Audio.Dsp;
using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.AI;

internal sealed class AiControlledSampleProvider : ISampleProvider
{
    private readonly AudioNodeModel _node;
    private readonly MintProfile _runtimeProfile;
    private readonly AiBrainSession _session;
    private readonly MintDspSampleProvider _dsp;

    public AiControlledSampleProvider(
        ISampleProvider source,
        AudioNodeModel node,
        MintAiRuntime runtime,
        AudioLevelState levels)
    {
        _node = node;
        _runtimeProfile = node.Profile.Clone();
        _session = runtime.GetOrCreate(node.Id, node.AiSpecialist);

        var tap = new AiFeatureTapSampleProvider(
            source,
            () => _node.Profile.AiContentMode,
            frame => _session.Evaluate(frame, _node.Profile, _runtimeProfile));

        DspConfiguration configuration = ConfigurationFor(node.AiSpecialist, _runtimeProfile);
        _dsp = new MintDspSampleProvider(tap, configuration, levels);
    }

    public WaveFormat WaveFormat => _dsp.WaveFormat;

    public int Read(float[] buffer, int offset, int count) =>
        _dsp.Read(buffer, offset, count);

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
        }

        return config;
    }
}
