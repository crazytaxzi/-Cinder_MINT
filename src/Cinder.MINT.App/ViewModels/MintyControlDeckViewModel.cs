using Cinder.MINT.Models;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cinder.MINT.ViewModels;

/// <summary>
/// Friendly control surface over the exact same graph MintyBay edits.
/// There is deliberately no second audio configuration model here.
/// </summary>
public sealed class MintyControlDeckViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MainViewModel _main;
    private string _selectedUseCase;
    private AudioGraphModel? _wiredGraph;

    public MintyControlDeckViewModel(MainViewModel main)
    {
        _main = main;
        _selectedUseCase = DetectUseCase();
        _main.PropertyChanged += MainPropertyChanged;
        WireGraph(_main.Graph);
    }

    public ObservableCollection<AudioEndpointChoice> Sources => _main.Sources;
    public ObservableCollection<AudioEndpointChoice> Outputs => _main.Outputs;

    public IReadOnlyList<string> UseCases { get; } =
    [
        "RVC Live",
        "RVC Polish",
        "Podcast Natural",
        "Podcast Studio",
        "Streaming",
        "Voice Chat",
        "Raw Rescue",
        "Custom"
    ];

    public string StatusText => _main.StatusText;
    public bool IsRunning => _main.IsRunning;
    public string StartStopLabel => IsRunning ? "STOP" : "GO LIVE";
    public string LiveStateLabel => IsRunning ? "LIVE" : "READY";
    public double InputMeter => _main.VoiceMeter;
    public double OutputMeter => _main.MasterMeter;

    public AudioEndpointChoice? InputDevice
    {
        get => PrimaryInputNode?.Endpoint;
        set
        {
            AudioNodeModel? node = PrimaryInputNode;
            if (node is null || ReferenceEquals(node.Endpoint, value)) return;
            node.Endpoint = value;
            _main.NotifyGraphChanged($"Input changed to {value?.DisplayName ?? "none"}", true);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RouteSummary));
        }
    }

    public AudioEndpointChoice? OutputDevice
    {
        get => PrimaryOutputNode?.Endpoint;
        set
        {
            AudioNodeModel? node = PrimaryOutputNode;
            if (node is null || ReferenceEquals(node.Endpoint, value)) return;
            node.Endpoint = value;
            _main.NotifyGraphChanged($"Output changed to {value?.DisplayName ?? "none"}", true);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RouteSummary));
        }
    }

    public string SelectedUseCase
    {
        get => _selectedUseCase;
        set
        {
            if (string.Equals(_selectedUseCase, value, StringComparison.Ordinal)) return;
            _selectedUseCase = value;
            OnPropertyChanged();
            ApplyUseCase(value);
        }
    }

    public double OverallTreatment
    {
        get
        {
            AudioNodeModel[] nodes = VoiceAiNodes().Where(x => x.AiSpecialist != MintAiSpecialist.Noise).ToArray();
            return nodes.Length == 0 ? 0 : nodes.Average(x => x.Profile.AiStrength) * 100.0;
        }
        set
        {
            float amount = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
            foreach (AudioNodeModel node in VoiceAiNodes().Where(x => x.AiSpecialist != MintAiSpecialist.Noise))
                node.Profile.AiStrength = amount;
            MarkCustom("Overall treatment updated");
            OnPropertyChanged();
            NotifyTreatmentProperties();
        }
    }

    public double Naturalness
    {
        get
        {
            AudioNodeModel[] nodes = VoiceAiNodes().ToArray();
            return nodes.Length == 0 ? 0 : nodes.Average(x => x.Profile.AiNaturalness) * 100.0;
        }
        set
        {
            float amount = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
            foreach (AudioNodeModel node in VoiceAiNodes())
                node.Profile.AiNaturalness = amount;
            MarkCustom("Naturalness updated");
            OnPropertyChanged();
        }
    }

    public double NoiseRemoval
    {
        get => GetSpecialist(MintAiSpecialist.Noise)?.Profile.AiStrength * 100.0 ?? 0;
        set
        {
            AudioNodeModel? node = GetSpecialist(MintAiSpecialist.Noise);
            if (node is null) return;
            node.Profile.AiStrength = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
            MarkCustom("Noise removal updated");
            OnPropertyChanged();
        }
    }

    public double VoiceCleanup
    {
        get => GetSpecialist(MintAiSpecialist.Cleanup)?.Profile.AiStrength * 100.0 ?? 0;
        set => SetSpecialistStrength(MintAiSpecialist.Cleanup, value, nameof(VoiceCleanup));
    }

    public double TonePolish
    {
        get => GetSpecialist(MintAiSpecialist.Tone)?.Profile.AiStrength * 100.0 ?? 0;
        set => SetSpecialistStrength(MintAiSpecialist.Tone, value, nameof(TonePolish));
    }

    public double Dynamics
    {
        get => GetSpecialist(MintAiSpecialist.Dynamics)?.Profile.AiStrength * 100.0 ?? 0;
        set => SetSpecialistStrength(MintAiSpecialist.Dynamics, value, nameof(Dynamics));
    }

    public double OutputConsistency
    {
        get => GetSpecialist(MintAiSpecialist.Loudness)?.Profile.AiStrength * 100.0 ?? 0;
        set => SetSpecialistStrength(MintAiSpecialist.Loudness, value, nameof(OutputConsistency));
    }

    public double MaxNoiseReduction
    {
        get => GetSpecialist(MintAiSpecialist.Noise)?.Profile.AiNoiseMaxReductionDb ?? 0;
        set
        {
            AudioNodeModel? node = GetSpecialist(MintAiSpecialist.Noise);
            if (node is null) return;
            node.Profile.AiNoiseMaxReductionDb = (float)value;
            MarkCustom("Noise ceiling updated");
            OnPropertyChanged();
        }
    }

    public double VoiceProtection
    {
        get => GetSpecialist(MintAiSpecialist.Noise)?.Profile.AiNoiseSpeechProtection * 100.0 ?? 0;
        set
        {
            AudioNodeModel? node = GetSpecialist(MintAiSpecialist.Noise);
            if (node is null) return;
            node.Profile.AiNoiseSpeechProtection = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
            MarkCustom("Voice protection updated");
            OnPropertyChanged();
        }
    }

    public string NoiseHeard => GetSpecialist(MintAiSpecialist.Noise)?.AiHeard ?? "waiting for audio";
    public string NoiseDecision => GetSpecialist(MintAiSpecialist.Noise)?.AiAction ?? "no decision yet";
    public string NoiseConfidence => GetSpecialist(MintAiSpecialist.Noise)?.AiConfidenceText ?? "0% confidence";

    public string RouteSummary =>
        $"{InputDevice?.DisplayName ?? "Choose input"}  →  MintyFilter  →  {OutputDevice?.DisplayName ?? "Choose output"}";

    public string GraphSummary
    {
        get
        {
            int brains = _main.Graph.Nodes.Count(x => x.Type == AudioNodeType.AiProcessor && x.Enabled);
            int nodes = _main.Graph.Nodes.Count;
            int cables = _main.Graph.Connections.Count;
            return $"{brains} AI brains • {nodes} nodes • {cables} cables • same live graph as MintyBay";
        }
    }

    public string BackendSummary => "WASAPI backend active in this preview • MME / WDM-KS / ASIO adapter layer is planned";

    public void StartOrStop()
    {
        if (_main.IsRunning) _main.Stop();
        else _main.Start();
        NotifyRuntime();
    }

    public void RefreshDevices()
    {
        _main.RefreshDevices();
        OnPropertyChanged(nameof(InputDevice));
        OnPropertyChanged(nameof(OutputDevice));
        OnPropertyChanged(nameof(RouteSummary));
    }

    public void RestoreStarterGraph()
    {
        _main.ResetGraph();
        _selectedUseCase = DetectUseCase();
        OnPropertyChanged(nameof(SelectedUseCase));
        NotifyAll();
    }

    private AudioNodeModel? PrimaryInputNode =>
        _main.Graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.Input && x.IsVoiceActivitySource)
        ?? _main.Graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.Input);

    private AudioNodeModel? PrimaryOutputNode =>
        _main.Graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.Output && _main.Graph.Incoming(x).Count > 0)
        ?? _main.Graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.Output);

    private AudioNodeModel? GetSpecialist(MintAiSpecialist specialist) =>
        _main.Graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.AiProcessor && x.AiSpecialist == specialist);

    private IEnumerable<AudioNodeModel> VoiceAiNodes() =>
        _main.Graph.Nodes.Where(x =>
            x.Type == AudioNodeType.AiProcessor &&
            x.AiSpecialist != MintAiSpecialist.Master);

    private void SetSpecialistStrength(MintAiSpecialist specialist, double value, string propertyName)
    {
        AudioNodeModel? node = GetSpecialist(specialist);
        if (node is null) return;
        node.Profile.AiStrength = (float)Math.Clamp(value / 100.0, 0.0, 1.0);
        MarkCustom($"{specialist} treatment updated");
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(OverallTreatment));
    }

    private void ApplyUseCase(string name)
    {
        if (name == "Custom") return;

        string preset = name switch
        {
            "Streaming" => "Streaming Strong",
            "Raw Rescue" => "Raw Rescue",
            "RVC Live" or "RVC Polish" => "RVC Cleanup",
            _ => "Natural Broadcast"
        };

        if (!MintProfiles.Voice.TryGetValue(preset, out MintProfile? source)) return;

        foreach (AudioNodeModel node in VoiceAiNodes())
            node.Profile.CopyFrom(source);

        AudioNodeModel? noise = GetSpecialist(MintAiSpecialist.Noise);
        AudioNodeModel? cleanup = GetSpecialist(MintAiSpecialist.Cleanup);
        AudioNodeModel? tone = GetSpecialist(MintAiSpecialist.Tone);
        AudioNodeModel? dynamics = GetSpecialist(MintAiSpecialist.Dynamics);
        AudioNodeModel? loudness = GetSpecialist(MintAiSpecialist.Loudness);
        AudioNodeModel? master = GetSpecialist(MintAiSpecialist.Master);

        switch (name)
        {
            case "RVC Live":
                SetMode(MintAiContentMode.RvcVoice, 20);
                Tune(noise, .82f, .92f); Tune(cleanup, .74f, .90f); Tune(tone, .56f, .94f);
                Tune(dynamics, .52f, .94f); Tune(loudness, .64f, .92f); Tune(master, .44f, .95f);
                TuneNoise(noise, 30, .82f, .96f);
                break;
            case "RVC Polish":
                SetMode(MintAiContentMode.RvcVoice, 30);
                Tune(noise, .76f, .90f); Tune(cleanup, .88f, .84f); Tune(tone, .76f, .86f);
                Tune(dynamics, .68f, .86f); Tune(loudness, .74f, .88f); Tune(master, .58f, .90f);
                TuneNoise(noise, 30, .80f, .92f);
                break;
            case "Podcast Natural":
                SetMode(MintAiContentMode.Voice, 30);
                Tune(noise, .70f, .94f); Tune(cleanup, .62f, .94f); Tune(tone, .62f, .95f);
                Tune(dynamics, .56f, .94f); Tune(loudness, .68f, .93f); Tune(master, .50f, .95f);
                TuneNoise(noise, 26, .72f, .95f);
                break;
            case "Podcast Studio":
                SetMode(MintAiContentMode.Voice, 35);
                Tune(noise, .78f, .88f); Tune(cleanup, .76f, .88f); Tune(tone, .78f, .88f);
                Tune(dynamics, .74f, .86f); Tune(loudness, .84f, .88f); Tune(master, .72f, .90f);
                TuneNoise(noise, 30, .80f, .92f);
                if (loudness is not null) loudness.Profile.AiTargetLoudnessDb = -16.5f;
                if (master is not null) master.Profile.AiTargetLoudnessDb = -16.5f;
                break;
            case "Streaming":
                SetMode(MintAiContentMode.Voice, 25);
                Tune(noise, .88f, .78f); Tune(cleanup, .84f, .76f); Tune(tone, .74f, .78f);
                Tune(dynamics, .82f, .76f); Tune(loudness, .90f, .76f); Tune(master, .76f, .80f);
                TuneNoise(noise, 34, .90f, .90f);
                break;
            case "Voice Chat":
                SetMode(MintAiContentMode.Voice, 15);
                Tune(noise, .64f, .96f); Tune(cleanup, .56f, .96f); Tune(tone, .46f, .97f);
                Tune(dynamics, .42f, .97f); Tune(loudness, .56f, .95f); Tune(master, .38f, .97f);
                TuneNoise(noise, 24, .68f, .97f);
                break;
            case "Raw Rescue":
                SetMode(MintAiContentMode.Voice, 40);
                Tune(noise, .98f, .58f); Tune(cleanup, .96f, .58f); Tune(tone, .88f, .62f);
                Tune(dynamics, .90f, .58f); Tune(loudness, .92f, .62f); Tune(master, .84f, .66f);
                TuneNoise(noise, 36, .98f, .84f);
                break;
        }

        _main.NotifyGraphChanged($"Use case changed to {name}", false);
        NotifyAll();
    }

    private void SetMode(MintAiContentMode mode, int latencyMs)
    {
        foreach (AudioNodeModel node in VoiceAiNodes()) node.Profile.AiContentMode = mode;
        AudioNodeModel? input = PrimaryInputNode;
        AudioNodeModel? output = PrimaryOutputNode;
        if (input is not null) input.LatencyMs = latencyMs;
        if (output is not null) output.LatencyMs = latencyMs;
    }

    private static void Tune(AudioNodeModel? node, float strength, float naturalness)
    {
        if (node is null) return;
        node.Profile.AiStrength = strength;
        node.Profile.AiNaturalness = naturalness;
    }

    private static void TuneNoise(AudioNodeModel? node, float maxReductionDb, float sensitivity, float protection)
    {
        if (node is null) return;
        node.Profile.AiNoiseMaxReductionDb = maxReductionDb;
        node.Profile.AiNoiseSensitivity = sensitivity;
        node.Profile.AiNoiseSpeechProtection = protection;
    }

    private string DetectUseCase()
    {
        AudioNodeModel? cleanup = _main.Graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.AiProcessor && x.AiSpecialist == MintAiSpecialist.Cleanup);
        if (cleanup is null) return "Custom";
        if (cleanup.Profile.Name.Equals("Raw Rescue", StringComparison.OrdinalIgnoreCase)) return "Raw Rescue";
        if (cleanup.Profile.Name.Equals("Streaming Strong", StringComparison.OrdinalIgnoreCase)) return "Streaming";
        if (cleanup.Profile.AiContentMode == MintAiContentMode.RvcVoice) return "RVC Live";
        if (cleanup.Profile.Name.Equals("Natural Broadcast", StringComparison.OrdinalIgnoreCase)) return "Podcast Natural";
        return "Custom";
    }

    private void MarkCustom(string message)
    {
        if (_selectedUseCase != "Custom")
        {
            _selectedUseCase = "Custom";
            OnPropertyChanged(nameof(SelectedUseCase));
        }
        _main.NotifyGraphChanged(message, false);
    }

    private void MainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Graph))
            WireGraph(_main.Graph);

        if (e.PropertyName is nameof(MainViewModel.StatusText)
            or nameof(MainViewModel.IsRunning)
            or nameof(MainViewModel.VoiceMeter)
            or nameof(MainViewModel.MasterMeter)
            or nameof(MainViewModel.Graph))
            NotifyRuntime();
    }

    private void WireGraph(AudioGraphModel graph)
    {
        if (_wiredGraph is not null)
        {
            _wiredGraph.Nodes.CollectionChanged -= NodesChanged;
            foreach (AudioNodeModel node in _wiredGraph.Nodes) UnwireNode(node);
        }

        _wiredGraph = graph;
        _wiredGraph.Nodes.CollectionChanged += NodesChanged;
        foreach (AudioNodeModel node in _wiredGraph.Nodes) WireNode(node);
        NotifyAll();
    }

    private void NodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AudioNodeModel node in e.OldItems) UnwireNode(node);
        if (e.NewItems is not null)
            foreach (AudioNodeModel node in e.NewItems) WireNode(node);
        NotifyAll();
    }

    private void WireNode(AudioNodeModel node)
    {
        node.PropertyChanged += NodePropertyChanged;
        node.Profile.PropertyChanged += ProfilePropertyChanged;
    }

    private void UnwireNode(AudioNodeModel node)
    {
        node.PropertyChanged -= NodePropertyChanged;
        node.Profile.PropertyChanged -= ProfilePropertyChanged;
    }

    private void NodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AudioNodeModel.Endpoint)
            or nameof(AudioNodeModel.AiHeard)
            or nameof(AudioNodeModel.AiAction)
            or nameof(AudioNodeModel.AiConfidence)
            or nameof(AudioNodeModel.Enabled))
            NotifyAll();
    }

    private void ProfilePropertyChanged(object? sender, PropertyChangedEventArgs e) => NotifyTreatmentProperties();

    private void NotifyRuntime()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StartStopLabel));
        OnPropertyChanged(nameof(LiveStateLabel));
        OnPropertyChanged(nameof(InputMeter));
        OnPropertyChanged(nameof(OutputMeter));
        OnPropertyChanged(nameof(NoiseHeard));
        OnPropertyChanged(nameof(NoiseDecision));
        OnPropertyChanged(nameof(NoiseConfidence));
    }

    private void NotifyTreatmentProperties()
    {
        OnPropertyChanged(nameof(OverallTreatment));
        OnPropertyChanged(nameof(Naturalness));
        OnPropertyChanged(nameof(NoiseRemoval));
        OnPropertyChanged(nameof(VoiceCleanup));
        OnPropertyChanged(nameof(TonePolish));
        OnPropertyChanged(nameof(Dynamics));
        OnPropertyChanged(nameof(OutputConsistency));
        OnPropertyChanged(nameof(MaxNoiseReduction));
        OnPropertyChanged(nameof(VoiceProtection));
    }

    private void NotifyAll()
    {
        NotifyRuntime();
        NotifyTreatmentProperties();
        OnPropertyChanged(nameof(InputDevice));
        OnPropertyChanged(nameof(OutputDevice));
        OnPropertyChanged(nameof(RouteSummary));
        OnPropertyChanged(nameof(GraphSummary));
        OnPropertyChanged(nameof(BackendSummary));
    }

    public void Dispose()
    {
        _main.PropertyChanged -= MainPropertyChanged;
        if (_wiredGraph is null) return;
        _wiredGraph.Nodes.CollectionChanged -= NodesChanged;
        foreach (AudioNodeModel node in _wiredGraph.Nodes) UnwireNode(node);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
