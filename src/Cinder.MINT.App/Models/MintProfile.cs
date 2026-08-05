using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cinder.MINT.Models;

public sealed class MintProfile : INotifyPropertyChanged
{
    private string _name = "Custom";
    private bool _autoMode = true;
    private float _inputGainDb;
    private float _gateThresholdDb = -48;
    private float _highPassHz = 75;
    private float _deEsserAmount = 0.35f;
    private float _lowGainDb;
    private float _midGainDb;
    private float _highGainDb;
    private float _targetDb = -20;
    private float _compression = 0.35f;
    private float _duckingDb = -6;
    private float _limiterCeilingDb = -1;

    public string Name { get => _name; set => SetField(ref _name, value); }
    public bool AutoMode { get => _autoMode; set => SetField(ref _autoMode, value); }
    public float InputGainDb { get => _inputGainDb; set => SetField(ref _inputGainDb, value); }
    public float GateThresholdDb { get => _gateThresholdDb; set => SetField(ref _gateThresholdDb, value); }
    public float HighPassHz { get => _highPassHz; set => SetField(ref _highPassHz, value); }
    public float DeEsserAmount { get => _deEsserAmount; set => SetField(ref _deEsserAmount, value); }
    public float LowGainDb { get => _lowGainDb; set => SetField(ref _lowGainDb, value); }
    public float MidGainDb { get => _midGainDb; set => SetField(ref _midGainDb, value); }
    public float HighGainDb { get => _highGainDb; set => SetField(ref _highGainDb, value); }
    public float TargetDb { get => _targetDb; set => SetField(ref _targetDb, value); }
    public float Compression { get => _compression; set => SetField(ref _compression, value); }
    public float DuckingDb { get => _duckingDb; set => SetField(ref _duckingDb, value); }
    public float LimiterCeilingDb { get => _limiterCeilingDb; set => SetField(ref _limiterCeilingDb, value); }

    public MintProfile Clone() => (MintProfile)MemberwiseClone();

    public void CopyFrom(MintProfile source)
    {
        Name = source.Name;
        AutoMode = source.AutoMode;
        InputGainDb = source.InputGainDb;
        GateThresholdDb = source.GateThresholdDb;
        HighPassHz = source.HighPassHz;
        DeEsserAmount = source.DeEsserAmount;
        LowGainDb = source.LowGainDb;
        MidGainDb = source.MidGainDb;
        HighGainDb = source.HighGainDb;
        TargetDb = source.TargetDb;
        Compression = source.Compression;
        DuckingDb = source.DuckingDb;
        LimiterCeilingDb = source.LimiterCeilingDb;
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
                HighPassHz = 72,
                DeEsserAmount = 0.28f,
                LowGainDb = 0.5f,
                MidGainDb = -0.5f,
                HighGainDb = 0.7f,
                TargetDb = -19,
                Compression = 0.32f
            },
            ["RVC Cleanup"] = new()
            {
                Name = "RVC Cleanup",
                AutoMode = true,
                GateThresholdDb = -56,
                HighPassHz = 68,
                DeEsserAmount = 0.48f,
                LowGainDb = -0.8f,
                MidGainDb = -1.2f,
                HighGainDb = -0.6f,
                TargetDb = -18,
                Compression = 0.42f
            },
            ["Streaming Strong"] = new()
            {
                Name = "Streaming Strong",
                AutoMode = true,
                GateThresholdDb = -46,
                HighPassHz = 82,
                DeEsserAmount = 0.40f,
                LowGainDb = 0.8f,
                MidGainDb = 0.3f,
                HighGainDb = 1.2f,
                TargetDb = -17,
                Compression = 0.55f
            },
            ["Raw Rescue"] = new()
            {
                Name = "Raw Rescue",
                AutoMode = true,
                InputGainDb = 3,
                GateThresholdDb = -44,
                HighPassHz = 90,
                DeEsserAmount = 0.55f,
                LowGainDb = -1.5f,
                MidGainDb = -2,
                HighGainDb = 1,
                TargetDb = -17,
                Compression = 0.65f
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
                Compression = 0.22f,
                DuckingDb = -6,
                LimiterCeilingDb = -1
            },
            ["Game + Music"] = new()
            {
                Name = "Game + Music",
                AutoMode = true,
                TargetDb = -20,
                Compression = 0.30f,
                LowGainDb = -0.5f,
                MidGainDb = -0.8f,
                HighGainDb = -0.4f,
                DuckingDb = -7
            },
            ["Background Bed"] = new()
            {
                Name = "Background Bed",
                AutoMode = true,
                TargetDb = -25,
                Compression = 0.38f,
                MidGainDb = -1.5f,
                DuckingDb = -9
            },
            ["Punchy"] = new()
            {
                Name = "Punchy",
                AutoMode = false,
                TargetDb = -19,
                Compression = 0.48f,
                LowGainDb = 1,
                MidGainDb = 0.5f,
                HighGainDb = 0.7f,
                DuckingDb = -5
            }
        };
}
