using Cinder.MINT.Audio;
using Cinder.MINT.Audio.AI;
using Cinder.MINT.Models;
using Cinder.MINT.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Cinder.MINT.ViewModels;

public sealed record NodePaletteItem(AudioNodeType Type, string Label, MintAiSpecialist? Specialist = null)
{
    public override string ToString() => Label;
}

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioDeviceService _deviceService = new();
    private readonly SettingsService _settingsService = new();
    private readonly AudioEngine _engine;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _watchdogTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly MintSettings _settings;

    private AudioGraphModel _graph;
    private AudioNodeModel? _selectedNode;
    private NodePaletteItem _selectedPaletteItem;
    private string _statusText = "READY — patch sockets, choose endpoints, then start";
    private bool _isRunning;
    private double _voiceMeter;
    private double _programMeter;
    private double _masterMeter;
    private bool _autoStart;
    private bool _restartPending;
    private bool _loading;

    public MainViewModel()
    {
        _engine = new AudioEngine(_deviceService);
        _engine.Faulted += OnEngineFaulted;
        _settings = _settingsService.Load();
        _graph = _settingsService.RestoreGraph(_settings);

        NodePalette =
        [
            new(AudioNodeType.Input, "Audio input"),
            new(AudioNodeType.AiProcessor, "AI · noise filter", MintAiSpecialist.Noise),
            new(AudioNodeType.AiProcessor, "AI · cleanup / RVC repair", MintAiSpecialist.Cleanup),
            new(AudioNodeType.AiProcessor, "AI · tone", MintAiSpecialist.Tone),
            new(AudioNodeType.AiProcessor, "AI · dynamics", MintAiSpecialist.Dynamics),
            new(AudioNodeType.AiProcessor, "AI · loudness", MintAiSpecialist.Loudness),
            new(AudioNodeType.Mixer, "Mix bus"),
            new(AudioNodeType.AiProcessor, "AI · master", MintAiSpecialist.Master),
            new(AudioNodeType.Ducker, "Sidechain ducker"),
            new(AudioNodeType.Output, "Audio output"),
            new(AudioNodeType.Gain, "Manual · gain / trim"),
            new(AudioNodeType.NoiseGate, "Manual · smart gate"),
            new(AudioNodeType.HighPass, "Manual · rumble cut"),
            new(AudioNodeType.DeEsser, "Manual · de-esser"),
            new(AudioNodeType.Equalizer, "Manual · equalizer"),
            new(AudioNodeType.LevelRider, "Manual · level rider"),
            new(AudioNodeType.Compressor, "Manual · compressor"),
            new(AudioNodeType.Limiter, "Manual · limiter")
        ];
        _selectedPaletteItem = NodePalette[1];

        AttachGraph(_graph);
        AutoStart = _settings.AutoStart;
        RefreshDevices();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _meterTimer.Tick += (_, _) => UpdateMetersAndBrains();
        _meterTimer.Start();

        _watchdogTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _watchdogTimer.Tick += (_, _) => RunWatchdog();
        _watchdogTimer.Start();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public ObservableCollection<AudioEndpointChoice> Sources { get; } = [];
    public ObservableCollection<AudioEndpointChoice> Outputs { get; } = [];
    public IReadOnlyList<NodePaletteItem> NodePalette { get; }
    public IReadOnlyList<MintAiSpecialist> AiSpecialists { get; } = Enum.GetValues<MintAiSpecialist>();
    public IReadOnlyList<MintAiContentMode> AiContentModes { get; } = Enum.GetValues<MintAiContentMode>();
    public IReadOnlyList<string> VoicePresetNames => MintProfiles.Voice.Keys.ToList();
    public IReadOnlyList<string> ProgramPresetNames => MintProfiles.Program.Keys.ToList();

    public AudioGraphModel Graph
    {
        get => _graph;
        private set
        {
            if (ReferenceEquals(_graph, value)) return;
            DetachGraph(_graph);
            _graph = value;
            AttachGraph(_graph);
            OnPropertyChanged();
        }
    }

    public AudioNodeModel? SelectedNode
    {
        get => _selectedNode;
        set => SetField(ref _selectedNode, value);
    }

    public NodePaletteItem SelectedPaletteItem
    {
        get => _selectedPaletteItem;
        set => SetField(ref _selectedPaletteItem, value);
    }

    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public bool IsRunning { get => _isRunning; private set => SetField(ref _isRunning, value); }
    public double VoiceMeter { get => _voiceMeter; private set => SetField(ref _voiceMeter, value); }
    public double ProgramMeter { get => _programMeter; private set => SetField(ref _programMeter, value); }
    public double MasterMeter { get => _masterMeter; private set => SetField(ref _masterMeter, value); }

    public bool AutoStart
    {
        get => _autoStart;
        set
        {
            if (!SetField(ref _autoStart, value)) return;
            _settings.AutoStart = value;
            ScheduleSave(false);
        }
    }

    public void RefreshDevices()
    {
        _loading = true;
        try
        {
            Replace(Sources, _deviceService.GetVoiceSources());
            Replace(Outputs, _deviceService.GetOutputs());

            foreach (AudioNodeModel node in Graph.Nodes)
            {
                string? endpointId = node.Endpoint?.Id ?? node.SavedEndpointId;

                if (node.Type == AudioNodeType.Input)
                {
                    AudioEndpointChoice? endpoint = Sources.FirstOrDefault(x => x.Id == endpointId);
                    if (endpoint is null && endpointId is null)
                    {
                        endpoint = node.IsVoiceActivitySource
                            ? Sources.FirstOrDefault(x => x.Kind == EndpointSourceKind.Capture) ?? Sources.FirstOrDefault()
                            : Sources.FirstOrDefault(x => x.Kind == EndpointSourceKind.RenderLoopback) ?? Sources.FirstOrDefault();
                    }
                    node.Endpoint = endpoint;
                    if (endpoint is null) node.SavedEndpointId = endpointId;
                }
                else if (node.Type == AudioNodeType.Output)
                {
                    AudioEndpointChoice? endpoint = Outputs.FirstOrDefault(x => x.Id == endpointId);
                    if (endpoint is null && endpointId is null)
                    {
                        HashSet<string> loopbackIds = Graph.Nodes
                            .Where(x => x.Type == AudioNodeType.Input && x.Endpoint?.Kind == EndpointSourceKind.RenderLoopback)
                            .Select(x => x.Endpoint!.Id)
                            .ToHashSet();
                        endpoint = Outputs.FirstOrDefault(x => !loopbackIds.Contains(x.Id)) ?? Outputs.FirstOrDefault();
                    }
                    node.Endpoint = endpoint;
                    if (endpoint is null) node.SavedEndpointId = endpointId;
                }
            }
        }
        finally
        {
            _loading = false;
        }

        ScheduleSave(false);
        StatusText = $"READY — {Sources.Count} available inputs, {Outputs.Count} available outputs";
    }

    public AudioNodeModel AddSelectedNode()
    {
        int index = Graph.Nodes.Count;
        double x = 70 + (index % 7) * 240;
        double y = 540 + (index / 7) * 160;
        AudioNodeModel node = Graph.AddNode(SelectedPaletteItem.Type, x, y);

        _loading = true;
        try
        {
            if (node.Type == AudioNodeType.Input)
                node.Endpoint = Sources.FirstOrDefault(x => x.Kind == EndpointSourceKind.Capture) ?? Sources.FirstOrDefault();
            else if (node.Type == AudioNodeType.Output)
                node.Endpoint = Outputs.FirstOrDefault();
            else if (node.Type == AudioNodeType.AiProcessor)
            {
                MintAiSpecialist specialist = SelectedPaletteItem.Specialist ?? MintAiSpecialist.Cleanup;
                node.AiSpecialist = specialist;

                if (specialist == MintAiSpecialist.Master)
                {
                    node.Profile.CopyFrom(MintProfiles.Program["Music Safe"]);
                    node.Profile.AiContentMode = MintAiContentMode.Mixed;
                    node.Profile.AiMaxCorrectionDb = 4f;
                }
                else
                {
                    node.Profile.CopyFrom(specialist is MintAiSpecialist.Noise or MintAiSpecialist.Cleanup
                        ? MintProfiles.Voice["RVC Cleanup"]
                        : MintProfiles.Voice["Natural Broadcast"]);
                }

                if (specialist == MintAiSpecialist.Noise)
                {
                    node.Profile.AiNoiseMaxReductionDb = 26f;
                    node.Profile.AiNoiseSensitivity = 0.72f;
                    node.Profile.AiNoiseSpeechProtection = 0.90f;
                }
            }
        }
        finally
        {
            _loading = false;
        }

        SelectedNode = node;
        NotifyGraphChanged($"Added {node.Title}", true);
        return node;
    }

    public void DeleteNode(AudioNodeModel node)
    {
        Graph.RemoveNode(node);
        if (ReferenceEquals(SelectedNode, node)) SelectedNode = null;
        NotifyGraphChanged($"Deleted {node.Title}", true);
    }

    public void ResetGraph()
    {
        Stop();
        _loading = true;
        try
        {
            Graph = AudioGraphModel.CreateDefault();
            SelectedNode = null;
            RefreshDevices();
        }
        finally
        {
            _loading = false;
        }
        NotifyGraphChanged("Restored the AI starter patch", false);
    }

    public void Start()
    {
        _engine.Start(Graph);
        IsRunning = true;
        _restartPending = false;
        int aiCount = Graph.Nodes.Count(x => x.Type == AudioNodeType.AiProcessor && x.Enabled);
        int outputCount = Graph.Nodes.Count(x => x.Type == AudioNodeType.Output && Graph.Incoming(x).Count > 0);
        StatusText = $"LIVE — {aiCount} independent AI brain{(aiCount == 1 ? string.Empty : "s")} controlling {outputCount} output{(outputCount == 1 ? string.Empty : "s")}";
        SaveNow();
    }

    public void Stop()
    {
        _engine.Stop();
        IsRunning = false;
        _restartPending = false;
        foreach (AudioNodeModel node in Graph.Nodes.Where(x => x.IsAiNode))
            node.ApplyAiTelemetry("WAITING", "waiting for audio", "no decision yet", 0f);
        StatusText = "STOPPED — graph editing is safe";
    }

    public void ToggleNode(AudioNodeModel node) =>
        NotifyGraphChanged(node.Enabled ? $"{node.Title} enabled" : $"{node.Title} bypassed", true);

    public void SelectNode(AudioNodeModel? node) => SelectedNode = node;

    public void NotifyGraphChanged(string message, bool requiresRestart)
    {
        if (_loading) return;
        ScheduleSave(requiresRestart);

        if (IsRunning && requiresRestart)
        {
            _restartPending = true;
            StatusText = $"EDITED — {message}; restart MINT to rebuild the patch";
        }
        else
        {
            StatusText = message;
        }
    }

    public void ApplyVoicePreset(string presetName)
    {
        if (SelectedNode is null || !MintProfiles.Voice.TryGetValue(presetName, out MintProfile? profile)) return;
        SelectedNode.Profile.CopyFrom(profile);
        NotifyGraphChanged($"Applied {presetName} to {SelectedNode.Title}", false);
    }

    public void ApplyProgramPreset(string presetName)
    {
        if (SelectedNode is null || !MintProfiles.Program.TryGetValue(presetName, out MintProfile? profile)) return;
        SelectedNode.Profile.CopyFrom(profile);
        NotifyGraphChanged($"Applied {presetName} to {SelectedNode.Title}", false);
    }

    private void AttachGraph(AudioGraphModel graph)
    {
        graph.Nodes.CollectionChanged += GraphNodesChanged;
        graph.Connections.CollectionChanged += GraphConnectionsChanged;
        foreach (AudioNodeModel node in graph.Nodes) AttachNode(node);
    }

    private void DetachGraph(AudioGraphModel graph)
    {
        graph.Nodes.CollectionChanged -= GraphNodesChanged;
        graph.Connections.CollectionChanged -= GraphConnectionsChanged;
        foreach (AudioNodeModel node in graph.Nodes) DetachNode(node);
    }

    private void GraphNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AudioNodeModel node in e.OldItems) DetachNode(node);
        if (e.NewItems is not null)
            foreach (AudioNodeModel node in e.NewItems) AttachNode(node);
        if (!_loading) ScheduleSave(true);
    }

    private void GraphConnectionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_loading) ScheduleSave(true);
    }

    private void AttachNode(AudioNodeModel node)
    {
        node.PropertyChanged += NodePropertyChanged;
        node.Profile.PropertyChanged += ProfilePropertyChanged;
    }

    private void DetachNode(AudioNodeModel node)
    {
        node.PropertyChanged -= NodePropertyChanged;
        node.Profile.PropertyChanged -= ProfilePropertyChanged;
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;

        if (e.PropertyName is nameof(AudioNodeModel.AiState)
            or nameof(AudioNodeModel.AiHeard)
            or nameof(AudioNodeModel.AiAction)
            or nameof(AudioNodeModel.AiConfidence)
            or nameof(AudioNodeModel.AiConfidenceText))
            return;

        bool layoutOnly = e.PropertyName is nameof(AudioNodeModel.X) or nameof(AudioNodeModel.Y);
        bool liveNameOnly = e.PropertyName is nameof(AudioNodeModel.Title) or nameof(AudioNodeModel.Subtitle);
        ScheduleSave(!layoutOnly && !liveNameOnly);

        if (!layoutOnly && !liveNameOnly && IsRunning)
        {
            _restartPending = true;
            StatusText = "EDITED — restart MINT to apply routing/node changes";
        }
    }

    private void ProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;

        // DSP profiles and AI intent are read live by running providers.
        ScheduleSave(false);
        if (IsRunning)
            StatusText = "LIVE TUNE — AI intent / DSP guardrail updated";
    }

    private void ScheduleSave(bool requiresRestart)
    {
        if (_loading) return;
        if (requiresRestart && IsRunning) _restartPending = true;
        if (_saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _settings.AutoStart = AutoStart;
        _settingsService.Save(_settings, Graph);
    }

    private void OnEngineFaulted(object? sender, string message)
    {
        bool feedbackGuard = message.StartsWith(
            "FEEDBACK GUARD",
            StringComparison.OrdinalIgnoreCase);

        App.Current.Dispatcher.BeginInvoke(() =>
        {
            if (feedbackGuard)
            {
                _engine.Stop();
                IsRunning = false;
                _restartPending = false;
                StatusText = $"SAFETY STOP — {message}";
                return;
            }

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
            StatusText = "RECOVERED — devices reconnected and graph rebuilt";
        }
        catch (Exception ex)
        {
            StatusText = $"WAITING — {ex.Message}";
        }
    }

    private void UpdateMetersAndBrains()
    {
        VoiceMeter = MeterPercent(_engine.Levels.VoicePeakDb);
        ProgramMeter = MeterPercent(_engine.Levels.ProgramPeakDb);
        MasterMeter = MeterPercent(_engine.Levels.MasterPeakDb);

        IReadOnlyDictionary<Guid, AiBrainSnapshot> snapshots = _engine.GetAiSnapshots();
        foreach (AudioNodeModel node in Graph.Nodes.Where(x => x.IsAiNode))
        {
            if (snapshots.TryGetValue(node.Id, out AiBrainSnapshot? snapshot))
            {
                node.ApplyAiTelemetry(
                    snapshot.State,
                    snapshot.Heard,
                    snapshot.Action,
                    snapshot.Confidence);
            }
        }
    }

    private static double MeterPercent(float db) =>
        Math.Clamp((db + 60f) / 60f * 100f, 0f, 100f);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (T item in items) target.Add(item);
    }

    public void Dispose()
    {
        _saveTimer.Stop();
        _meterTimer.Stop();
        _watchdogTimer.Stop();
        SaveNow();
        DetachGraph(Graph);
        _engine.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
