using Cinder.MINT.Audio.Dsp;
using Cinder.MINT.Models;
using NAudio.Wave;

namespace Cinder.MINT.Audio.AI;

/// <summary>
/// Plain realtime-only observation state shared by the feature tap and denoiser.
/// It deliberately owns no WPF/model objects, events, or dispatcher state.
/// </summary>
internal sealed class NoiseObservationState
{
    private float _speechProbability;
    private float _noise;
    private float _loudness;
    private float _transient;

    public float SpeechProbability => Volatile.Read(ref _speechProbability);
    public float Noise => Volatile.Read(ref _noise);
    public float Loudness => Volatile.Read(ref _loudness);
    public float Transient => Volatile.Read(ref _transient);

    public void Update(AiFeatureFrame frame)
    {
        Volatile.Write(ref _speechProbability, frame.SpeechProbability);
        Volatile.Write(ref _noise, frame.Noise);
        Volatile.Write(ref _loudness, frame.Loudness);
        Volatile.Write(ref _transient, frame.Transient);
    }
}

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
        // Graph/profile objects are UI-owned. The realtime path receives detached
        // value copies only; never retain AudioNodeModel or its event subscribers.
        _intentProfile = DetachedCopy(node.Profile);
        _runtimeProfile = DetachedCopy(_intentProfile);
        _session = runtime.GetOrCreate(node.Id, node.AiSpecialist);

        NoiseObservationState? noiseObservation =
            node.AiSpecialist == MintAiSpecialist.Noise ? new NoiseObservationState() : null;

        var tap = new AiFeatureTapSampleProvider(
            source,
            () => _intentProfile.AiContentMode,
            frame =>
            {
                noiseObservation?.Update(frame);
                _session.Evaluate(frame, _intentProfile, _runtimeProfile);
            });

        if (node.AiSpecialist == MintAiSpecialist.Noise)
        {
            _output = new AdaptiveNeuralNoiseSampleProvider(
                tap,
                _runtimeProfile,
                noiseObservation!);
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
