using Cinder.MINT.Audio;
using Cinder.MINT.Models;
using Cinder.MINT.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Cinder.MINT.ViewModels;

public sealed record NodePaletteItem(AudioNodeType Type, string Label)
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
            new(AudioNodeType.Gain, "Gain / trim"),
            new(AudioNodeType.NoiseGate, "Smart gate"),
            new(AudioNodeType.HighPass, "Rumble cut"),
            new(AudioNodeType.DeEsser, "De-esser"),
            new(AudioNodeType.Equalizer, "Equalizer"),
            new(AudioNodeType.LevelRider, "Level rider"),
            new(AudioNodeType.Compressor, "Compressor"),
            new(AudioNodeType.Ducker, "Sidechain ducker"),
            new(AudioNodeType.Mixer, "Mix bus"),
            new(AudioNodeType.Limiter, "Limiter"),
            new(AudioNodeType.Output, "Audio output")
        ];
        _selectedPaletteItem = NodePalette[0];

        AttachGraph(_graph);
        AutoStart = _settings.AutoStart;
        RefreshDevices();

        _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _meterTimer.Tick += (_, _) => UpdateMeters();
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
        double x = 70 + (index % 7) * 220;
        double y = 520 + (index / 7) * 150;
        AudioNodeModel node = Graph.AddNode(SelectedPaletteItem.Type, x, y);

        _loading = true;
        try
        {
            if (node.Type == AudioNodeType.Input)
                node.Endpoint = Sources.FirstOrDefault(x => x.Kind == EndpointSourceKind.Capture) ?? Sources.FirstOrDefault();
            else if (node.Type == AudioNodeType.Output)
                node.Endpoint = Outputs.FirstOrDefault();
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
        NotifyGraphChanged("Restored the starter patch", false);
    }

    public void Start()
    {
        _engine.Start(Graph);
        IsRunning = true;
        _restartPending = false;
        int outputCount = Graph.Nodes.Count(x => x.Type == AudioNodeType.Output && Graph.Incoming(x).Count > 0);
        StatusText = $"LIVE — running {Graph.Nodes.Count} nodes into {outputCount} output{(outputCount == 1 ? string.Empty : "s")}";
        SaveNow();
    }

    public void Stop()
    {
        _engine.Stop();
        IsRunning = false;
        _restartPending = false;
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
        NotifyGraphChanged($"Applied {presetName} to {SelectedNode.Title}", true);
    }

    public void ApplyProgramPreset(string presetName)
    {
        if (SelectedNode is null || !MintProfiles.Program.TryGetValue(presetName, out MintProfile? profile)) return;
        SelectedNode.Profile.CopyFrom(profile);
        NotifyGraphChanged($"Applied {presetName} to {SelectedNode.Title}", true);
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
        bool layoutOnly = e.PropertyName is nameof(AudioNodeModel.X) or nameof(AudioNodeModel.Y);
        ScheduleSave(!layoutOnly);
        if (!layoutOnly && IsRunning)
        {
            _restartPending = true;
            StatusText = "EDITED — restart MINT to apply node changes";
        }
    }

    private void ProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_loading) return;
        ScheduleSave(true);
        if (IsRunning)
        {
            _restartPending = true;
            StatusText = "EDITED — restart MINT to apply processor changes";
        }
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
            StatusText = "RECOVERED — devices reconnected and graph rebuilt";
        }
        catch (Exception ex)
        {
            StatusText = $"WAITING — {ex.Message}";
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
