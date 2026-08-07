using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cinder.MINT.Models;

public enum MintAiSpecialist
{
    Cleanup,
    Noise,
    Tone,
    Dynamics,
    Loudness,
    Master
}

public enum MintAiContentMode
{
    Auto,
    Voice,
    RvcVoice,
    Music,
    Mixed
}

public sealed class MintProfile : INotifyPropertyChanged
{
    private string _name = "Custom";
    private bool _autoMode = true;
    private float _inputGainDb;
    private float _gateThresholdDb = -48;
    private float _gateReleaseMs = 110;
    private float _highPassHz = 75;
    private float _deEsserAmount = 0.35f;
    private float _lowGainDb;
    private float _midGainDb;
    private float _highGainDb;
    private float _targetDb = -20;
    private float _riderSpeedMs = 1200;
    private float _compression = 0.35f;
    private float _compressorAttackMs = 8;
    private float _compressorReleaseMs = 160;
    private float _duckingDb = -6;
    private float _duckerThresholdDb = -38;
    private float _duckerAttackMs = 35;
    private float _duckerReleaseMs = 550;
    private float _limiterCeilingDb = -1;
    private float _limiterReleaseMs = 80;

    private MintAiContentMode _aiContentMode = MintAiContentMode.Auto;
    private float _aiStrength = 0.72f;
    private float _aiNaturalness = 0.82f;
    private float _aiMaxCorrectionDb = 6f;
    private float _aiPreserveTransients = 0.76f;
    private float _aiConsistency = 0.72f;
    private float _aiTargetLoudnessDb = -18f;
    private float _aiAdaptation = 0.55f;
    private float _aiNoiseMaxReductionDb = 24f;
    private float _aiNoiseReductionDb = 10f;
    private float _aiNoiseSensitivity = 0.68f;
    private float _aiNoiseSpeechProtection = 0.86f;
    private float _aiNoiseLearnRate = 0.035f;

    public string Name { get => _name; set => SetField(ref _name, value); }
    public bool AutoMode { get => _autoMode; set => SetField(ref _autoMode, value); }
    public float InputGainDb { get => _inputGainDb; set => SetField(ref _inputGainDb, value); }
    public float GateThresholdDb { get => _gateThresholdDb; set => SetField(ref _gateThresholdDb, value); }
    public float GateReleaseMs { get => _gateReleaseMs; set => SetField(ref _gateReleaseMs, value); }
    public float HighPassHz { get => _highPassHz; set => SetField(ref _highPassHz, value); }
    public float DeEsserAmount { get => _deEsserAmount; set => SetField(ref _deEsserAmount, value); }
    public float LowGainDb { get => _lowGainDb; set => SetField(ref _lowGainDb, value); }
    public float MidGainDb { get => _midGainDb; set => SetField(ref _midGainDb, value); }
    public float HighGainDb { get => _highGainDb; set => SetField(ref _highGainDb, value); }
    public float TargetDb { get => _targetDb; set => SetField(ref _targetDb, value); }
    public float RiderSpeedMs { get => _riderSpeedMs; set => SetField(ref _riderSpeedMs, value); }
    public float Compression { get => _compression; set => SetField(ref _compression, value); }
    public float CompressorAttackMs { get => _compressorAttackMs; set => SetField(ref _compressorAttackMs, value); }
    public float CompressorReleaseMs { get => _compressorReleaseMs; set => SetField(ref _compressorReleaseMs, value); }
    public float DuckingDb { get => _duckingDb; set => SetField(ref _duckingDb, value); }
    public float DuckerThresholdDb { get => _duckerThresholdDb; set => SetField(ref _duckerThresholdDb, value); }
    public float DuckerAttackMs { get => _duckerAttackMs; set => SetField(ref _duckerAttackMs, value); }
    public float DuckerReleaseMs { get => _duckerReleaseMs; set => SetField(ref _duckerReleaseMs, value); }
    public float LimiterCeilingDb { get => _limiterCeilingDb; set => SetField(ref _limiterCeilingDb, value); }
    public float LimiterReleaseMs { get => _limiterReleaseMs; set => SetField(ref _limiterReleaseMs, value); }

    public MintAiContentMode AiContentMode { get => _aiContentMode; set => SetField(ref _aiContentMode, value); }
    public float AiStrength { get => _aiStrength; set => SetField(ref _aiStrength, Math.Clamp(value, 0f, 1f)); }
    public float AiNaturalness { get => _aiNaturalness; set => SetField(ref _aiNaturalness, Math.Clamp(value, 0f, 1f)); }
    public float AiMaxCorrectionDb { get => _aiMaxCorrectionDb; set => SetField(ref _aiMaxCorrectionDb, Math.Clamp(value, 1f, 12f)); }
    public float AiPreserveTransients { get => _aiPreserveTransients; set => SetField(ref _aiPreserveTransients, Math.Clamp(value, 0f, 1f)); }
    public float AiConsistency { get => _aiConsistency; set => SetField(ref _aiConsistency, Math.Clamp(value, 0f, 1f)); }
    public float AiTargetLoudnessDb { get => _aiTargetLoudnessDb; set => SetField(ref _aiTargetLoudnessDb, Math.Clamp(value, -30f, -12f)); }
    public float AiAdaptation { get => _aiAdaptation; set => SetField(ref _aiAdaptation, Math.Clamp(value, 0f, 1f)); }
    public float AiNoiseMaxReductionDb { get => _aiNoiseMaxReductionDb; set => SetField(ref _aiNoiseMaxReductionDb, Math.Clamp(value, 6f, 36f)); }
    public float AiNoiseReductionDb { get => _aiNoiseReductionDb; set => SetField(ref _aiNoiseReductionDb, Math.Clamp(value, 0f, 36f)); }
    public float AiNoiseSensitivity { get => _aiNoiseSensitivity; set => SetField(ref _aiNoiseSensitivity, Math.Clamp(value, 0.05f, 1f)); }
    public float AiNoiseSpeechProtection { get => _aiNoiseSpeechProtection; set => SetField(ref _aiNoiseSpeechProtection, Math.Clamp(value, 0f, 1f)); }
    public float AiNoiseLearnRate { get => _aiNoiseLearnRate; set => SetField(ref _aiNoiseLearnRate, Math.Clamp(value, 0.001f, 0.25f)); }

    public MintProfile Clone()
    {
        // Value-only clone: never copy PropertyChanged subscribers into realtime state.
        var clone = new MintProfile();
        clone.CopyFrom(this);
        return clone;
    }

    public void CopyFrom(MintProfile source)
    {
        Name = source.Name;
        AutoMode = source.AutoMode;
        InputGainDb = source.InputGainDb;
        GateThresholdDb = source.GateThresholdDb;
        GateReleaseMs = source.GateReleaseMs;
        HighPassHz = source.HighPassHz;
        DeEsserAmount = source.DeEsserAmount;
        LowGainDb = source.LowGainDb;
        MidGainDb = source.MidGainDb;
        HighGainDb = source.HighGainDb;
        TargetDb = source.TargetDb;
        RiderSpeedMs = source.RiderSpeedMs;
        Compression = source.Compression;
        CompressorAttackMs = source.CompressorAttackMs;
        CompressorReleaseMs = source.CompressorReleaseMs;
        DuckingDb = source.DuckingDb;
        DuckerThresholdDb = source.DuckerThresholdDb;
        DuckerAttackMs = source.DuckerAttackMs;
        DuckerReleaseMs = source.DuckerReleaseMs;
        LimiterCeilingDb = source.LimiterCeilingDb;
        LimiterReleaseMs = source.LimiterReleaseMs;
        AiContentMode = source.AiContentMode;
        AiStrength = source.AiStrength;
        AiNaturalness = source.AiNaturalness;
        AiMaxCorrectionDb = source.AiMaxCorrectionDb;
        AiPreserveTransients = source.AiPreserveTransients;
        AiConsistency = source.AiConsistency;
        AiTargetLoudnessDb = source.AiTargetLoudnessDb;
        AiAdaptation = source.AiAdaptation;
        AiNoiseMaxReductionDb = source.AiNoiseMaxReductionDb;
        AiNoiseReductionDb = source.AiNoiseReductionDb;
        AiNoiseSensitivity = source.AiNoiseSensitivity;
        AiNoiseSpeechProtection = source.AiNoiseSpeechProtection;
        AiNoiseLearnRate = source.AiNoiseLearnRate;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public static class MintProfiles
{
    public static IReadOnlyDictionary<string, MintProfile> Voice { get; } =
        new Dictionary<string, MintProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Natural Broadcast"] = new()
            {
                Name = "Natural Broadcast", AutoMode = true,
                AiContentMode = MintAiContentMode.Voice, AiStrength = 0.62f, AiNaturalness = 0.90f,
                AiMaxCorrectionDb = 5f, AiPreserveTransients = 0.82f, AiConsistency = 0.68f,
                AiTargetLoudnessDb = -18f, GateThresholdDb = -52, GateReleaseMs = 135,
                HighPassHz = 72, DeEsserAmount = 0.28f, TargetDb = -19, RiderSpeedMs = 850,
                Compression = 0.32f, CompressorAttackMs = 10, CompressorReleaseMs = 180
            },
            ["RVC Cleanup"] = new()
            {
                Name = "RVC Cleanup", AutoMode = true,
                AiContentMode = MintAiContentMode.RvcVoice, AiStrength = 0.78f, AiNaturalness = 0.88f,
                AiMaxCorrectionDb = 6f, AiPreserveTransients = 0.80f, AiConsistency = 0.76f,
                AiTargetLoudnessDb = -18f, GateThresholdDb = -56, GateReleaseMs = 155,
                HighPassHz = 68, DeEsserAmount = 0.48f, TargetDb = -18, RiderSpeedMs = 750,
                Compression = 0.42f, CompressorAttackMs = 7, CompressorReleaseMs = 145
            },
            ["Streaming Strong"] = new()
            {
                Name = "Streaming Strong", AutoMode = true,
                AiContentMode = MintAiContentMode.Voice, AiStrength = 0.84f, AiNaturalness = 0.70f,
                AiMaxCorrectionDb = 7f, AiPreserveTransients = 0.68f, AiConsistency = 0.86f,
                AiTargetLoudnessDb = -17f, GateThresholdDb = -46, GateReleaseMs = 115,
                HighPassHz = 82, DeEsserAmount = 0.40f, TargetDb = -17, RiderSpeedMs = 650,
                Compression = 0.55f, CompressorAttackMs = 6, CompressorReleaseMs = 130
            },
            ["Raw Rescue"] = new()
            {
                Name = "Raw Rescue", AutoMode = true,
                AiContentMode = MintAiContentMode.Voice, AiStrength = 0.94f, AiNaturalness = 0.55f,
                AiMaxCorrectionDb = 9f, AiPreserveTransients = 0.58f, AiConsistency = 0.88f,
                AiTargetLoudnessDb = -17f, InputGainDb = 3, GateThresholdDb = -44, GateReleaseMs = 170,
                HighPassHz = 90, DeEsserAmount = 0.55f, TargetDb = -17, RiderSpeedMs = 600,
                Compression = 0.65f, CompressorAttackMs = 5, CompressorReleaseMs = 120
            }
        };

    public static IReadOnlyDictionary<string, MintProfile> Program { get; } =
        new Dictionary<string, MintProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Music Safe"] = new()
            {
                Name = "Music Safe", AutoMode = true,
                AiContentMode = MintAiContentMode.Music, AiStrength = 0.52f, AiNaturalness = 0.95f,
                AiMaxCorrectionDb = 4f, AiPreserveTransients = 0.94f, AiConsistency = 0.62f,
                AiTargetLoudnessDb = -21f, TargetDb = -22, RiderSpeedMs = 1400,
                Compression = 0.22f, CompressorAttackMs = 18, CompressorReleaseMs = 260, DuckingDb = -6
            },
            ["Game + Music"] = new()
            {
                Name = "Game + Music", AutoMode = true,
                AiContentMode = MintAiContentMode.Mixed, AiStrength = 0.65f, AiNaturalness = 0.86f,
                AiMaxCorrectionDb = 5f, AiPreserveTransients = 0.84f, AiConsistency = 0.72f,
                AiTargetLoudnessDb = -20f, TargetDb = -20, RiderSpeedMs = 1100,
                Compression = 0.30f, DuckingDb = -7
            },
            ["Background Bed"] = new()
            {
                Name = "Background Bed", AutoMode = true,
                AiContentMode = MintAiContentMode.Music, AiStrength = 0.70f, AiNaturalness = 0.86f,
                AiMaxCorrectionDb = 5f, AiPreserveTransients = 0.75f, AiConsistency = 0.86f,
                AiTargetLoudnessDb = -24f, TargetDb = -25, RiderSpeedMs = 1700,
                Compression = 0.38f, DuckingDb = -9
            },
            ["Punchy"] = new()
            {
                Name = "Punchy", AutoMode = true,
                AiContentMode = MintAiContentMode.Music, AiStrength = 0.74f, AiNaturalness = 0.64f,
                AiMaxCorrectionDb = 6f, AiPreserveTransients = 0.88f, AiConsistency = 0.74f,
                AiTargetLoudnessDb = -19f, TargetDb = -19, RiderSpeedMs = 900,
                Compression = 0.48f, CompressorAttackMs = 12, CompressorReleaseMs = 190, DuckingDb = -5
            }
        };
}
