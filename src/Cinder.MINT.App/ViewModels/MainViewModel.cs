using Cinder.MINT.Audio;
using Cinder.MINT.Audio.Dsp;
using Cinder.MINT.Models;
using Cinder.MINT.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Cinder.MINT.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioDeviceService _deviceService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AudioEngine _engine;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _watchdogTimer;
    private MintSettings _settings;

    private AudioEndpointChoice? _selectedVoiceSource;
    private AudioEndpointChoice? _selectedProgramSource;
    private AudioEndpointChoice? _selectedOutput;
    private string _selectedVoicePreset = "Natural Broadcast";
    private string _selectedProgramPreset = "Music Safe";
    private string _statusText = "READY — choose sources and output";
    private bool _isRunning;
    private double _voiceMeter;
    private double _programMeter;
    private double _masterMeter;
    private bool _autoStart;
    private int _latencyMs = 30;
    private bool _restartPending;

    public MainViewModel()
    {
        _engine = new AudioEngine(_deviceService);
        _engine.Faulted += OnEngineFaulted;
        _settings = _settingsService.Load();

        VoiceProfile = MintProfiles.Voice["Natural Broadcast"].Clone();
        ProgramProfile = MintProfiles.Program["Music Safe"].Clone();
        MasterProfile = new MintProfile
        {
            Name = "Master",
            AutoMode = false,
            LimiterCeilingDb = -1,
            Compression = 0
        };

        Graph = AudioGraphModel.CreateDefault();
        RefreshDevices();

        SelectedVoicePreset = MintProfiles.Voice.ContainsKey(_settings.VoicePreset)
            ? _settings.VoicePreset
            : "Natural Broadcast";
        SelectedProgramPreset = MintProfiles.Program.ContainsKey(_settings.ProgramPreset)
            ? _settings.ProgramPreset
            : "Music Safe";

        AutoStart = _settings.AutoStart;
        LatencyMs = Math.Clamp(_settings.LatencyMs, 10, 120);

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
        _meterTimer.Start();

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _watchdogTimer.Tick += (_, _) => RunWatchdog();
        _watchdogTimer.Start();
    }

    public ObservableCollection<AudioEndpointChoice> VoiceSources { get; } = [];
    public ObservableCollection<AudioEndpointChoice> ProgramSources { get; } = [];
    public ObservableCollection<AudioEndpointChoice> Outputs { get; } = [];
    public IReadOnlyList<string> VoicePresetNames => MintProfiles.Voice.Keys.ToList();
    public IReadOnlyList<string> ProgramPresetNames => MintProfiles.Program.Keys.ToList();

    public AudioGraphModel Graph { get; }
    public MintProfile VoiceProfile { get; }
    public MintProfile ProgramProfile { get; }
    public MintProfile MasterProfile { get; }

    public AudioEndpointChoice? SelectedVoiceSource
    {
        get => _selectedVoiceSource;
        set { SetField(ref _selectedVoiceSource, value); SaveSettings(); }
    }

    public AudioEndpointChoice? SelectedProgramSource
    {
        get => _selectedProgramSource;
        set { SetField(ref _selectedProgramSource, value); SaveSettings(); }
    }

    public AudioEndpointChoice? SelectedOutput
    {
        get => _selectedOutput;
        set { SetField(ref _selectedOutput, value); SaveSettings(); }
    }

    public string SelectedVoicePreset
    {
        get => _selectedVoicePreset;
        set
        {
            if (!SetField(ref _selectedVoicePreset, value)) return;
            if (MintProfiles.Voice.TryGetValue(value, out MintProfile? profile))
                VoiceProfile.CopyFrom(profile);
            SaveSettings();
        }
    }

    public string SelectedProgramPreset
    {
        get => _selectedProgramPreset;
        set
        {
            if (!SetField(ref _selectedProgramPreset, value)) return;
            if (MintProfiles.Program.TryGetValue(value, out MintProfile? profile))
                ProgramProfile.CopyFrom(profile);
            SaveSettings();
        }
    }

    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public bool IsRunning { get => _isRunning; private set => SetField(ref _isRunning, value); }
    public double VoiceMeter { get => _voiceMeter; private set => SetField(ref _voiceMeter, value); }
    public double ProgramMeter { get => _programMeter; private set => SetField(ref _programMeter, value); }
    public double MasterMeter { get => _masterMeter; private set => SetField(ref _masterMeter, value); }

    public bool AutoStart
    {
        get => _autoStart;
        set { SetField(ref _autoStart, value); SaveSettings(); }
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set { SetField(ref _latencyMs, value); SaveSettings(); }
    }

    public void RefreshDevices()
    {
        string? voiceId = SelectedVoiceSource?.Id ?? _settings.VoiceSourceId;
        string? programId = SelectedProgramSource?.Id ?? _settings.ProgramSourceId;
        string? outputId = SelectedOutput?.Id ?? _settings.OutputId;

        Replace(VoiceSources, _deviceService.GetVoiceSources());
        Replace(ProgramSources, _deviceService.GetProgramSources());
        Replace(Outputs, _deviceService.GetOutputs());

        SelectedVoiceSource = VoiceSources.FirstOrDefault(x => x.Id == voiceId) ?? VoiceSources.FirstOrDefault();
        SelectedProgramSource = ProgramSources.FirstOrDefault(x => x.Id == programId) ?? ProgramSources.FirstOrDefault();
        SelectedOutput = Outputs.FirstOrDefault(x => x.Id == outputId) ?? Outputs.FirstOrDefault();

        StatusText = $"READY — {VoiceSources.Count} voice/RVC sources, {ProgramSources.Count} loopback sources";
    }

    public void Start()
    {
        if (SelectedVoiceSource is null || SelectedProgramSource is null || SelectedOutput is null)
            throw new InvalidOperationException("Choose a voice/RVC source, music/app source, and output.");

        var voiceConfig = new DspConfiguration
        {
            Profile = VoiceProfile,
            IsVoice = true
        };

        var programConfig = new DspConfiguration
        {
            Profile = ProgramProfile,
            IsProgram = true,
            GateEnabled = false,
            HighPassEnabled = false,
            DeEsserEnabled = false
        };

        var masterConfig = new DspConfiguration
        {
            Profile = MasterProfile,
            IsMaster = true,
            GateEnabled = false,
            HighPassEnabled = false,
            DeEsserEnabled = false,
            EqEnabled = false,
            RiderEnabled = false,
            CompressorEnabled = false,
            DuckerEnabled = false,
            LimiterEnabled = true
        };

        ApplyGraphBypass(voiceConfig, programConfig, masterConfig);

        _engine.Start(
            SelectedVoiceSource,
            SelectedProgramSource,
            SelectedOutput,
            voiceConfig,
            programConfig,
            masterConfig,
            LatencyMs);

        IsRunning = true;
        _restartPending = false;
        StatusText = $"LIVE — mastering to {SelectedOutput.Name}";
        SaveSettings();
    }

    public void Stop()
    {
        _engine.Stop();
        IsRunning = false;
        _restartPending = false;
        StatusText = "STOPPED — routing is safe";
    }

    public void ToggleNode(AudioNodeModel node)
    {
        StatusText = node.Enabled
            ? $"{node.Title} enabled — restart MINT to rebuild the lane"
            : $"{node.Title} bypassed — restart MINT to rebuild the lane";
        _restartPending = IsRunning;
    }

    private void ApplyGraphBypass(
        DspConfiguration voice,
        DspConfiguration program,
        DspConfiguration master)
    {
        bool Enabled(AudioNodeType type, int occurrence = 0) =>
            Graph.Nodes.Where(x => x.Type == type).Skip(occurrence).FirstOrDefault()?.Enabled ?? true;

        voice.GateEnabled = Enabled(AudioNodeType.NoiseGate);
        voice.HighPassEnabled = Enabled(AudioNodeType.HighPass);
        voice.DeEsserEnabled = Enabled(AudioNodeType.DeEsser);
        voice.EqEnabled = Enabled(AudioNodeType.Equalizer, 0);
        voice.CompressorEnabled = Enabled(AudioNodeType.Compressor, 0);

        program.RiderEnabled = Enabled(AudioNodeType.LevelRider);
        program.EqEnabled = Enabled(AudioNodeType.Equalizer, 1);
        program.CompressorEnabled = Enabled(AudioNodeType.Compressor, 1);
        program.DuckerEnabled = Enabled(AudioNodeType.Ducker);

        master.LimiterEnabled = Enabled(AudioNodeType.Limiter);
    }

    private void OnEngineFaulted(object? sender, string message)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            IsRunning = false;
            _restartPending = AutoStart;
            StatusText = $"RECOVERING — {message}";
        });
    }

    private void RunWatchdog()
    {
        if (!_restartPending || !AutoStart || IsRunning) return;

        try
        {
            RefreshDevices();
            Start();
            StatusText = "RECOVERED — devices reconnected";
        }
        catch (Exception ex)
        {
            StatusText = $"WAITING FOR DEVICE — {ex.Message}";
        }
    }

    private void UpdateMeters()
    {
        VoiceMeter = MeterPercent(_engine.Levels.VoicePeakDb);
        ProgramMeter = MeterPercent(_engine.Levels.ProgramPeakDb);
        MasterMeter = MeterPercent(_engine.Levels.MasterPeakDb);
    }

    private static double MeterPercent(float db) =>
        Math.Clamp((db + 60f) / 60f * 100f, 0f, 100f);

    private void SaveSettings()
    {
        if (_settings is null) return;

        _settings.VoiceSourceId = SelectedVoiceSource?.Id;
        _settings.ProgramSourceId = SelectedProgramSource?.Id;
        _settings.OutputId = SelectedOutput?.Id;
        _settings.VoicePreset = SelectedVoicePreset;
        _settings.ProgramPreset = SelectedProgramPreset;
        _settings.AutoStart = AutoStart;
        _settings.LatencyMs = LatencyMs;
        _settingsService.Save(_settings);
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items) target.Add(item);
    }

    public void Dispose()
    {
        _meterTimer.Stop();
        _watchdogTimer.Stop();
        _engine.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
