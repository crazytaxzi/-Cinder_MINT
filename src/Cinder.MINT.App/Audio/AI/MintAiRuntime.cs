using Cinder.MINT.Models;
using System.Collections.Concurrent;

namespace Cinder.MINT.Audio.AI;

public readonly record struct AiFeatureFrame(
    float Loudness,
    float Peak,
    float Crest,
    float LowEnergy,
    float MidEnergy,
    float HighEnergy,
    float Sibilance,
    float Transient,
    float Noise,
    float Harshness,
    float Metallicity,
    float SpeechProbability)
{
    public float[] ToArray() =>
    [
        Loudness, Peak, Crest, LowEnergy, MidEnergy, HighEnergy,
        Sibilance, Transient, Noise, Harshness, Metallicity, SpeechProbability
    ];
}

public sealed record AiBrainSnapshot(
    Guid NodeId,
    MintAiSpecialist Specialist,
    string State,
    string Heard,
    string Action,
    float Confidence,
    DateTime UpdatedUtc);

internal sealed class NeuralModel
{
    private readonly int _inputCount;
    private readonly int _hiddenCount;
    private readonly int _outputCount;
    private readonly float[] _w1;
    private readonly float[] _b1;
    private readonly float[] _w2;
    private readonly float[] _b2;

    public NeuralModel(
        int inputCount,
        int hiddenCount,
        int outputCount,
        float[] w1,
        float[] b1,
        float[] w2,
        float[] b2)
    {
        _inputCount = inputCount;
        _hiddenCount = hiddenCount;
        _outputCount = outputCount;
        _w1 = w1;
        _b1 = b1;
        _w2 = w2;
        _b2 = b2;

        if (_w1.Length != inputCount * hiddenCount ||
            _b1.Length != hiddenCount ||
            _w2.Length != hiddenCount * outputCount ||
            _b2.Length != outputCount)
            throw new ArgumentException("Invalid MINT neural model dimensions.");
    }

    public float[] Predict(ReadOnlySpan<float> input)
    {
        if (input.Length != _inputCount)
            throw new ArgumentException("Unexpected feature count.", nameof(input));

        Span<float> hidden = stackalloc float[_hiddenCount];
        for (int h = 0; h < _hiddenCount; h++)
        {
            float sum = _b1[h];
            for (int i = 0; i < _inputCount; i++)
                sum += input[i] * _w1[i * _hiddenCount + h];
            hidden[h] = MathF.Tanh(sum);
        }

        var output = new float[_outputCount];
        for (int o = 0; o < _outputCount; o++)
        {
            float sum = _b2[o];
            for (int h = 0; h < _hiddenCount; h++)
                sum += hidden[h] * _w2[h * _outputCount + o];
            output[o] = sum;
        }

        return output;
    }
}

public sealed class MintAiRuntime
{
    private readonly ConcurrentDictionary<Guid, AiBrainSession> _sessions = new();

    public AiBrainSession GetOrCreate(Guid nodeId, MintAiSpecialist specialist)
    {
        return _sessions.AddOrUpdate(
            nodeId,
            _ => new AiBrainSession(nodeId, specialist, ModelFor(specialist)),
            (_, existing) => existing.Specialist == specialist
                ? existing
                : new AiBrainSession(nodeId, specialist, ModelFor(specialist)));
    }

    public IReadOnlyDictionary<Guid, AiBrainSnapshot> GetSnapshots()
    {
        return _sessions
            .Select(pair => pair.Value.TryGetSnapshot(out AiBrainSnapshot? snapshot)
                ? new KeyValuePair<Guid, AiBrainSnapshot?>(pair.Key, snapshot)
                : new KeyValuePair<Guid, AiBrainSnapshot?>(pair.Key, null))
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!);
    }

    public void Reset() => _sessions.Clear();

    private static NeuralModel ModelFor(MintAiSpecialist specialist) => specialist switch
    {
        MintAiSpecialist.Cleanup => MintAiModels.Cleanup,
        MintAiSpecialist.Noise => MintAiModels.Noise,
        MintAiSpecialist.Tone => MintAiModels.Tone,
        MintAiSpecialist.Dynamics => MintAiModels.Dynamics,
        MintAiSpecialist.Loudness => MintAiModels.Loudness,
        MintAiSpecialist.Master => MintAiModels.Master,
        _ => MintAiModels.Cleanup
    };
}

public sealed class AiBrainSession
{
    private readonly Guid _nodeId;
    private readonly NeuralModel _model;
    private readonly float[] _history = new float[12];
    private readonly object _snapshotLock = new();
    private AiBrainSnapshot? _snapshot;
    private bool _historyPrimed;

    internal AiBrainSession(Guid nodeId, MintAiSpecialist specialist, NeuralModel model)
    {
        _nodeId = nodeId;
        Specialist = specialist;
        _model = model;
    }

    public MintAiSpecialist Specialist { get; }

    public void Evaluate(AiFeatureFrame frame, MintProfile intent, MintProfile runtime)
    {
        float[] current = frame.ToArray();
        float adaptation = 0.03f + Math.Clamp(intent.AiAdaptation, 0f, 1f) * 0.17f;

        if (!_historyPrimed)
        {
            Array.Copy(current, _history, current.Length);
            _historyPrimed = true;
        }
        else
        {
            for (int i = 0; i < _history.Length; i++)
                _history[i] += (current[i] - _history[i]) * adaptation;
        }

        Span<float> blended = stackalloc float[12];
        for (int i = 0; i < blended.Length; i++)
            blended[i] = Math.Clamp(current[i] * 0.72f + _history[i] * 0.28f, 0f, 1f);

        float[] prediction = _model.Predict(blended);
        float strength = Math.Clamp(intent.AiStrength, 0f, 1f);
        float natural = Math.Clamp(intent.AiNaturalness, 0f, 1f);
        float maxCorrection = Math.Clamp(intent.AiMaxCorrectionDb, 1f, 12f);
        float preserve = Math.Clamp(intent.AiPreserveTransients, 0f, 1f);
        float consistency = Math.Clamp(intent.AiConsistency, 0f, 1f);
        float smoothing = 0.08f + adaptation * 0.55f;

        string action = Specialist switch
        {
            MintAiSpecialist.Cleanup => ApplyCleanup(prediction, intent, runtime, strength, natural, maxCorrection, smoothing),
            MintAiSpecialist.Noise => ApplyNoise(prediction, intent, runtime, strength, natural, smoothing),
            MintAiSpecialist.Tone => ApplyTone(prediction, runtime, strength, natural, maxCorrection, smoothing),
            MintAiSpecialist.Dynamics => ApplyDynamics(prediction, runtime, strength, natural, preserve, consistency, smoothing),
            MintAiSpecialist.Loudness => ApplyLoudness(prediction, intent, runtime, strength, consistency, smoothing),
            MintAiSpecialist.Master => ApplyMaster(prediction, runtime, strength, natural, maxCorrection, preserve, smoothing),
            _ => "holding current parameters"
        };

        string heard = Describe(frame);
        float confidence = Math.Clamp(
            0.30f +
            frame.Loudness * 0.25f +
            Math.Max(frame.SpeechProbability, 0.35f) * 0.20f +
            (1f - frame.Noise * 0.45f) * 0.25f,
            0f,
            1f);

        lock (_snapshotLock)
        {
            _snapshot = new AiBrainSnapshot(
                _nodeId,
                Specialist,
                "LEARNING / CONTROLLING",
                heard,
                action,
                confidence,
                DateTime.UtcNow);
        }
    }

    public bool TryGetSnapshot(out AiBrainSnapshot? snapshot)
    {
        lock (_snapshotLock)
        {
            snapshot = _snapshot;
            return snapshot is not null;
        }
    }

    private static string ApplyNoise(
        float[] y,
        MintProfile intent,
        MintProfile p,
        float strength,
        float natural,
        float smoothing)
    {
        float severity = Clamp01(y[0]);
        float depth = Clamp01(y[1]);
        float speech = Clamp01(y[2]);
        float learning = Clamp01(y[3]);

        float maxReduction = Math.Clamp(intent.AiNoiseMaxReductionDb, 6f, 36f);
        float reductionTarget = Math.Clamp(
            maxReduction * severity * strength * (0.86f + (1f - natural) * 0.18f),
            0f,
            maxReduction);
        float sensitivityTarget = Math.Clamp(
            intent.AiNoiseSensitivity + (depth - 0.5f) * 0.34f,
            0.05f,
            1f);
        float protectionTarget = Math.Clamp(
            Math.Max(intent.AiNoiseSpeechProtection * 0.82f, speech * (0.64f + natural * 0.32f)),
            0f,
            1f);
        float learningTarget = Math.Clamp(
            Lerp(0.004f, 0.16f, learning) * Lerp(0.72f, 1.18f, intent.AiAdaptation),
            0.001f,
            0.25f);

        p.AiNoiseReductionDb = Smooth(p.AiNoiseReductionDb, reductionTarget, smoothing);
        p.AiNoiseSensitivity = Smooth(p.AiNoiseSensitivity, sensitivityTarget, smoothing);
        p.AiNoiseSpeechProtection = Smooth(p.AiNoiseSpeechProtection, protectionTarget, smoothing);
        p.AiNoiseLearnRate = Smooth(p.AiNoiseLearnRate, learningTarget, smoothing);

        return $"noise cut {p.AiNoiseReductionDb:F1} dB • sensitivity {p.AiNoiseSensitivity:P0} • voice protect {p.AiNoiseSpeechProtection:P0}";
    }

    private static string ApplyCleanup(
        float[] y,
        MintProfile intent,
        MintProfile p,
        float strength,
        float natural,
        float maxCorrection,
        float smoothing)
    {
        float gate = Clamp01(y[0]);
        float plosive = Clamp01(y[1]);
        float deEss = Clamp01(y[2]);
        float artifact = Clamp01(y[3]);

        float voiceBias = intent.AiContentMode == MintAiContentMode.RvcVoice ? 1.12f : 1f;
        float gateTarget = Lerp(-62f, -38f, gate * strength * voiceBias);
        float hpTarget = Lerp(45f, 175f, plosive * strength);
        float deEssTarget = Math.Clamp(0.05f + deEss * strength * (0.95f - natural * 0.25f), 0f, 0.95f);
        float artifactCut = -artifact * strength * maxCorrection * (0.52f - natural * 0.16f);

        p.GateThresholdDb = Smooth(p.GateThresholdDb, gateTarget, smoothing);
        p.HighPassHz = Smooth(p.HighPassHz, hpTarget, smoothing);
        p.DeEsserAmount = Smooth(p.DeEsserAmount, deEssTarget, smoothing);
        p.HighGainDb = Smooth(p.HighGainDb, artifactCut, smoothing);
        p.MidGainDb = Smooth(p.MidGainDb, artifactCut * 0.32f, smoothing);

        return $"gate {p.GateThresholdDb:F0} dB • HP {p.HighPassHz:F0} Hz • de-ess {p.DeEsserAmount:P0} • artifact {p.HighGainDb:F1} dB";
    }

    private static string ApplyTone(
        float[] y,
        MintProfile p,
        float strength,
        float natural,
        float maxCorrection,
        float smoothing)
    {
        float range = maxCorrection * strength * (0.55f + (1f - natural) * 0.45f);
        float low = Math.Clamp(y[0], -1f, 1f) * range;
        float mid = Math.Clamp(y[1], -1f, 1f) * range;
        float high = Math.Clamp(y[2], -1f, 1f) * range;

        p.LowGainDb = Smooth(p.LowGainDb, low, smoothing);
        p.MidGainDb = Smooth(p.MidGainDb, mid, smoothing);
        p.HighGainDb = Smooth(p.HighGainDb, high, smoothing);

        return $"tone L {p.LowGainDb:+0.0;-0.0;0.0} • M {p.MidGainDb:+0.0;-0.0;0.0} • H {p.HighGainDb:+0.0;-0.0;0.0} dB";
    }

    private static string ApplyDynamics(
        float[] y,
        MintProfile p,
        float strength,
        float natural,
        float preserve,
        float consistency,
        float smoothing)
    {
        float compression = Clamp01(y[0]);
        float attackShape = Clamp01(y[1]);
        float releaseShape = Clamp01(y[2]);
        float grNeed = Clamp01(y[3]);

        float compTarget = Math.Clamp(
            compression * strength * (0.55f + consistency * 0.65f) * (1.06f - natural * 0.18f),
            0f,
            0.92f);

        float attack = Lerp(3f, 42f, Math.Clamp(attackShape * (0.45f + preserve * 0.75f), 0f, 1f));
        float release = Lerp(75f, 480f, releaseShape);
        compTarget *= 0.85f + grNeed * 0.25f;

        p.Compression = Smooth(p.Compression, compTarget, smoothing);
        p.CompressorAttackMs = Smooth(p.CompressorAttackMs, attack, smoothing);
        p.CompressorReleaseMs = Smooth(p.CompressorReleaseMs, release, smoothing);

        return $"compression {p.Compression:P0} • attack {p.CompressorAttackMs:F0} ms • release {p.CompressorReleaseMs:F0} ms";
    }

    private static string ApplyLoudness(
        float[] y,
        MintProfile intent,
        MintProfile p,
        float strength,
        float consistency,
        float smoothing)
    {
        float gainDirection = Math.Clamp(y[0], -1f, 1f);
        float riderNeed = Clamp01(y[1]);
        float riderSlow = Clamp01(y[2]);
        float peakCaution = Clamp01(y[3]);

        float gainRange = Math.Clamp(intent.AiMaxCorrectionDb, 1f, 12f) * 0.55f;
        float trim = gainDirection * gainRange * strength;
        float target = Math.Clamp(intent.AiTargetLoudnessDb - peakCaution * 1.2f, -30f, -12f);
        float speed = Lerp(450f, 2500f, riderSlow);
        speed *= Lerp(1.25f, 0.72f, consistency);
        speed *= Lerp(1.10f, 0.84f, riderNeed);

        p.InputGainDb = Smooth(p.InputGainDb, trim, smoothing);
        p.TargetDb = Smooth(p.TargetDb, target, smoothing);
        p.RiderSpeedMs = Smooth(p.RiderSpeedMs, speed, smoothing);

        return $"trim {p.InputGainDb:+0.0;-0.0;0.0} dB • target {p.TargetDb:F1} dB • ride {p.RiderSpeedMs:F0} ms";
    }

    private static string ApplyMaster(
        float[] y,
        MintProfile p,
        float strength,
        float natural,
        float maxCorrection,
        float preserve,
        float smoothing)
    {
        float pressure = Clamp01(y[0]);
        float compression = Clamp01(y[1]);
        float mid = Math.Clamp(y[2], -1f, 1f);
        float high = Math.Clamp(y[3], -1f, 1f);

        float eqRange = Math.Min(maxCorrection, 4f) * strength * (0.42f + (1f - natural) * 0.25f);
        p.MidGainDb = Smooth(p.MidGainDb, mid * eqRange, smoothing);
        p.HighGainDb = Smooth(p.HighGainDb, high * eqRange, smoothing);
        p.Compression = Smooth(
            p.Compression,
            compression * strength * (0.42f + (1f - preserve) * 0.28f),
            smoothing);
        p.CompressorAttackMs = Smooth(p.CompressorAttackMs, Lerp(8f, 35f, preserve), smoothing);
        p.CompressorReleaseMs = Smooth(p.CompressorReleaseMs, Lerp(120f, 360f, 1f - pressure), smoothing);
        p.LimiterCeilingDb = Smooth(p.LimiterCeilingDb, Lerp(-0.8f, -1.8f, pressure), smoothing);
        p.LimiterReleaseMs = Smooth(p.LimiterReleaseMs, Lerp(55f, 180f, pressure), smoothing);

        return $"master comp {p.Compression:P0} • EQ {p.MidGainDb:+0.0;-0.0;0.0}/{p.HighGainDb:+0.0;-0.0;0.0} dB • ceiling {p.LimiterCeilingDb:F1}";
    }

    private static string Describe(AiFeatureFrame f)
    {
        List<string> items = [];

        if (f.Noise > 0.58f) items.Add("noise high");
        else if (f.Noise > 0.35f) items.Add("noise moderate");

        if (f.Sibilance > 0.58f) items.Add("sibilance high");
        if (f.Metallicity > 0.55f) items.Add("metallic texture");
        if (f.Harshness > 0.58f) items.Add("upper-mid harshness");
        if (f.Transient > 0.67f) items.Add("strong transients");
        if (f.LowEnergy > 0.56f) items.Add("low-end buildup");
        if (f.Crest > 0.68f) items.Add("wide dynamics");

        if (items.Count == 0)
            items.Add(f.SpeechProbability > 0.6f ? "stable voice" : "balanced program");

        return string.Join(" • ", items.Take(3));
    }

    private static float Clamp01(float value) => Math.Clamp(value, 0f, 1f);
    private static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);
    private static float Smooth(float current, float target, float amount) => current + (target - current) * Math.Clamp(amount, 0.01f, 0.35f);
}
