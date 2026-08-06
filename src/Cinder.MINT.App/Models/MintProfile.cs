using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cinder.MINT.Models;

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

    public MintProfile Clone() => (MintProfile)MemberwiseClone();

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
                Name = "Natural Broadcast",
                AutoMode = true,
                GateThresholdDb = -52,
                GateReleaseMs = 135,
                HighPassHz = 72,
                DeEsserAmount = 0.28f,
                LowGainDb = 0.5f,
                MidGainDb = -0.5f,
                HighGainDb = 0.7f,
                TargetDb = -19,
                RiderSpeedMs = 850,
                Compression = 0.32f,
                CompressorAttackMs = 10,
                CompressorReleaseMs = 180
            },
            ["RVC Cleanup"] = new()
            {
                Name = "RVC Cleanup",
                AutoMode = true,
                GateThresholdDb = -56,
                GateReleaseMs = 155,
                HighPassHz = 68,
                DeEsserAmount = 0.48f,
                LowGainDb = -0.8f,
                MidGainDb = -1.2f,
                HighGainDb = -0.6f,
                TargetDb = -18,
                RiderSpeedMs = 750,
                Compression = 0.42f,
                CompressorAttackMs = 7,
                CompressorReleaseMs = 145
            },
            ["Streaming Strong"] = new()
            {
                Name = "Streaming Strong",
                AutoMode = true,
                GateThresholdDb = -46,
                GateReleaseMs = 115,
                HighPassHz = 82,
                DeEsserAmount = 0.40f,
                LowGainDb = 0.8f,
                MidGainDb = 0.3f,
                HighGainDb = 1.2f,
                TargetDb = -17,
                RiderSpeedMs = 650,
                Compression = 0.55f,
                CompressorAttackMs = 6,
                CompressorReleaseMs = 130
            },
            ["Raw Rescue"] = new()
            {
                Name = "Raw Rescue",
                AutoMode = true,
                InputGainDb = 3,
                GateThresholdDb = -44,
                GateReleaseMs = 170,
                HighPassHz = 90,
                DeEsserAmount = 0.55f,
                LowGainDb = -1.5f,
                MidGainDb = -2,
                HighGainDb = 1,
                TargetDb = -17,
                RiderSpeedMs = 600,
                Compression = 0.65f,
                CompressorAttackMs = 5,
                CompressorReleaseMs = 120
            }
        };

    public static IReadOnlyDictionary<string, MintProfile> Program { get; } =
        new Dictionary<string, MintProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["Music Safe"] = new()
            {
                Name = "Music Safe",
                AutoMode = true,
                TargetDb = -22,
                RiderSpeedMs = 1400,
                Compression = 0.22f,
                CompressorAttackMs = 18,
                CompressorReleaseMs = 260,
                DuckingDb = -6,
                DuckerThresholdDb = -38,
                DuckerAttackMs = 35,
                DuckerReleaseMs = 550,
                LimiterCeilingDb = -1
            },
            ["Game + Music"] = new()
            {
                Name = "Game + Music",
                AutoMode = true,
                TargetDb = -20,
                RiderSpeedMs = 1100,
                Compression = 0.30f,
                LowGainDb = -0.5f,
                MidGainDb = -0.8f,
                HighGainDb = -0.4f,
                DuckingDb = -7,
                DuckerThresholdDb = -40,
                DuckerAttackMs = 28,
                DuckerReleaseMs = 480
            },
            ["Background Bed"] = new()
            {
                Name = "Background Bed",
                AutoMode = true,
                TargetDb = -25,
                RiderSpeedMs = 1700,
                Compression = 0.38f,
                MidGainDb = -1.5f,
                DuckingDb = -9,
                DuckerThresholdDb = -42,
                DuckerAttackMs = 45,
                DuckerReleaseMs = 700
            },
            ["Punchy"] = new()
            {
                Name = "Punchy",
                AutoMode = false,
                TargetDb = -19,
                RiderSpeedMs = 900,
                Compression = 0.48f,
                CompressorAttackMs = 12,
                CompressorReleaseMs = 190,
                LowGainDb = 1,
                MidGainDb = 0.5f,
                HighGainDb = 0.7f,
                DuckingDb = -5
            }
        };
}
