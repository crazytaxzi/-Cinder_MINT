using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Cinder.MINT.Models;

public enum AudioNodeType
{
    Input,
    Gain,
    NoiseGate,
    HighPass,
    DeEsser,
    Equalizer,
    LevelRider,
    Compressor,
    Ducker,
    Mixer,
    Limiter,
    Output
}

public enum AudioPortDirection
{
    Input,
    Output
}

public enum AudioPortKind
{
    Audio,
    Sidechain
}

public sealed class AudioPortModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; init; }
    public required AudioPortDirection Direction { get; init; }
    public AudioPortKind Kind { get; init; } = AudioPortKind.Audio;
    public bool AllowsMultipleConnections { get; init; }
}

public sealed class AudioNodeModel : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private bool _enabled = true;
    private string _title;
    private string _subtitle;
    private AudioEndpointChoice? _endpoint;
    private string? _savedEndpointId;
    private bool _isVoiceActivitySource;
    private int _latencyMs = 30;

    public AudioNodeModel(AudioNodeType type, string title, string subtitle, Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Type = type;
        _title = title;
        _subtitle = subtitle;
        ConfigurePorts();
    }

    public Guid Id { get; }
    public AudioNodeType Type { get; }
    public MintProfile Profile { get; } = new();
    public ObservableCollection<AudioPortModel> Inputs { get; } = [];
    public ObservableCollection<AudioPortModel> Outputs { get; } = [];

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set
        {
            if (SetField(ref _subtitle, value))
                OnPropertyChanged(nameof(DisplaySubtitle));
        }
    }

    public string DisplaySubtitle => Endpoint?.DisplayName ?? Subtitle;

    public AudioEndpointChoice? Endpoint
    {
        get => _endpoint;
        set
        {
            if (!SetField(ref _endpoint, value)) return;
            SavedEndpointId = value?.Id;
            OnPropertyChanged(nameof(DisplaySubtitle));
        }
    }

    public string? SavedEndpointId
    {
        get => _savedEndpointId;
        set => SetField(ref _savedEndpointId, value);
    }

    public bool IsVoiceActivitySource
    {
        get => _isVoiceActivitySource;
        set => SetField(ref _isVoiceActivitySource, value);
    }

    public int LatencyMs
    {
        get => _latencyMs;
        set => SetField(ref _latencyMs, Math.Clamp(value, 10, 150));
    }

    public double X
    {
        get => _x;
        set => SetField(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => SetField(ref _y, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public bool CanBypass => Type is not AudioNodeType.Input and not AudioNodeType.Output;

    public AudioPortModel? FindInput(string name) =>
        Inputs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    public AudioPortModel? FindOutput(string name) =>
        Outputs.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));

    private void ConfigurePorts()
    {
        switch (Type)
        {
            case AudioNodeType.Input:
                Outputs.Add(Port("OUT", AudioPortDirection.Output));
                break;

            case AudioNodeType.Ducker:
                Inputs.Add(Port("MAIN", AudioPortDirection.Input));
                Inputs.Add(Port("SIDECHAIN", AudioPortDirection.Input, AudioPortKind.Sidechain));
                Outputs.Add(Port("OUT", AudioPortDirection.Output));
                break;

            case AudioNodeType.Mixer:
                Inputs.Add(Port("MIX IN", AudioPortDirection.Input, AudioPortKind.Audio, true));
                Outputs.Add(Port("OUT", AudioPortDirection.Output));
                break;

            case AudioNodeType.Output:
                Inputs.Add(Port("IN", AudioPortDirection.Input));
                break;

            default:
                Inputs.Add(Port("IN", AudioPortDirection.Input));
                Outputs.Add(Port("OUT", AudioPortDirection.Output));
                break;
        }
    }

    private static AudioPortModel Port(
        string name,
        AudioPortDirection direction,
        AudioPortKind kind = AudioPortKind.Audio,
        bool allowsMultiple = false) =>
        new()
        {
            Name = name,
            Direction = direction,
            Kind = kind,
            AllowsMultipleConnections = allowsMultiple
        };

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

public sealed record AudioConnectionModel(
    Guid Id,
    Guid SourceNodeId,
    Guid SourcePortId,
    Guid TargetNodeId,
    Guid TargetPortId);

public sealed class AudioGraphModel
{
    public ObservableCollection<AudioNodeModel> Nodes { get; } = [];
    public ObservableCollection<AudioConnectionModel> Connections { get; } = [];

    public AudioNodeModel AddNode(
        AudioNodeType type,
        double x,
        double y,
        string? title = null,
        Guid? id = null)
    {
        (string defaultTitle, string subtitle) = Defaults(type);
        var node = new AudioNodeModel(type, title ?? defaultTitle, subtitle, id)
        {
            X = x,
            Y = y
        };

        ApplyDefaultProfile(node);
        Nodes.Add(node);
        return node;
    }

    public void RemoveNode(AudioNodeModel node)
    {
        foreach (AudioConnectionModel connection in Connections
                     .Where(x => x.SourceNodeId == node.Id || x.TargetNodeId == node.Id)
                     .ToList())
            Connections.Remove(connection);

        Nodes.Remove(node);
    }

    public bool TryConnect(AudioPortModel first, AudioPortModel second, out string error)
    {
        AudioPortModel sourcePort = first.Direction == AudioPortDirection.Output ? first : second;
        AudioPortModel targetPort = first.Direction == AudioPortDirection.Input ? first : second;

        if (sourcePort.Direction != AudioPortDirection.Output ||
            targetPort.Direction != AudioPortDirection.Input)
        {
            error = "Connect an output socket to an input socket.";
            return false;
        }

        AudioNodeModel? sourceNode = GetNodeForPort(sourcePort.Id);
        AudioNodeModel? targetNode = GetNodeForPort(targetPort.Id);
        if (sourceNode is null || targetNode is null)
        {
            error = "One of those sockets no longer exists.";
            return false;
        }

        if (sourceNode.Id == targetNode.Id)
        {
            error = "A node cannot cable itself.";
            return false;
        }

        if (sourcePort.Kind != AudioPortKind.Audio)
        {
            error = "Only audio outputs can feed the graph.";
            return false;
        }

        if (Connections.Any(x => x.SourcePortId == sourcePort.Id && x.TargetPortId == targetPort.Id))
        {
            error = "Those sockets are already connected.";
            return false;
        }

        if (!targetPort.AllowsMultipleConnections && Connections.Any(x => x.TargetPortId == targetPort.Id))
        {
            error = "That input is already occupied. Right-click the socket to disconnect it first.";
            return false;
        }

        if (WouldCreateCycle(sourceNode.Id, targetNode.Id))
        {
            error = "That cable would create an audio feedback cycle.";
            return false;
        }

        Connections.Add(new AudioConnectionModel(
            Guid.NewGuid(),
            sourceNode.Id,
            sourcePort.Id,
            targetNode.Id,
            targetPort.Id));

        error = string.Empty;
        return true;
    }

    public bool TryConnect(
        Guid sourceNodeId,
        string sourcePortName,
        Guid targetNodeId,
        string targetPortName,
        out string error)
    {
        AudioNodeModel? sourceNode = Nodes.FirstOrDefault(x => x.Id == sourceNodeId);
        AudioNodeModel? targetNode = Nodes.FirstOrDefault(x => x.Id == targetNodeId);
        AudioPortModel? source = sourceNode?.FindOutput(sourcePortName);
        AudioPortModel? target = targetNode?.FindInput(targetPortName);

        if (source is null || target is null)
        {
            error = "Saved cable referenced a missing socket.";
            return false;
        }

        return TryConnect(source, target, out error);
    }

    public void DisconnectPort(Guid portId)
    {
        foreach (AudioConnectionModel connection in Connections
                     .Where(x => x.SourcePortId == portId || x.TargetPortId == portId)
                     .ToList())
            Connections.Remove(connection);
    }

    public void Disconnect(AudioConnectionModel connection) => Connections.Remove(connection);

    public AudioNodeModel? GetNodeForPort(Guid portId) =>
        Nodes.FirstOrDefault(node =>
            node.Inputs.Any(x => x.Id == portId) || node.Outputs.Any(x => x.Id == portId));

    public AudioPortModel? GetPort(Guid portId) =>
        Nodes.SelectMany(x => x.Inputs.Concat(x.Outputs)).FirstOrDefault(x => x.Id == portId);

    public IReadOnlyList<AudioConnectionModel> Incoming(AudioNodeModel node) =>
        Connections.Where(x => x.TargetNodeId == node.Id).ToList();

    public IReadOnlyList<AudioConnectionModel> Incoming(AudioNodeModel node, string portName)
    {
        AudioPortModel? port = node.FindInput(portName);
        return port is null
            ? []
            : Connections.Where(x => x.TargetPortId == port.Id).ToList();
    }

    public IReadOnlyList<AudioConnectionModel> Outgoing(AudioNodeModel node) =>
        Connections.Where(x => x.SourceNodeId == node.Id).ToList();

    public AudioNodeModel? SourceNode(AudioConnectionModel connection) =>
        Nodes.FirstOrDefault(x => x.Id == connection.SourceNodeId);

    public AudioNodeModel? TargetNode(AudioConnectionModel connection) =>
        Nodes.FirstOrDefault(x => x.Id == connection.TargetNodeId);

    public bool Validate(out string error)
    {
        if (!ValidateConnectionIntegrity(out error)) return false;

        if (HasCycle())
        {
            error = "The patch contains an audio cycle. Remove the cable feeding back upstream.";
            return false;
        }

        List<AudioNodeModel> outputs = Nodes
            .Where(x => x.Type == AudioNodeType.Output && Incoming(x).Count > 0)
            .ToList();

        if (outputs.Count == 0)
        {
            error = "Connect at least one signal chain to an OUTPUT node.";
            return false;
        }

        foreach (AudioNodeModel output in outputs)
        {
            if (output.Endpoint is null)
            {
                error = $"Choose an audio endpoint inside the {output.Title} node.";
                return false;
            }
        }

        IGrouping<string, AudioNodeModel>? duplicateOutput = outputs
            .GroupBy(x => x.Endpoint!.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateOutput is not null)
        {
            error = $"Multiple OUTPUT nodes are writing to {duplicateOutput.First().Endpoint!.Name}. Merge those chains first, then use one OUTPUT node.";
            return false;
        }

        HashSet<Guid> activeInputIds = outputs
            .SelectMany(GetUpstreamNodeIds)
            .ToHashSet();

        List<AudioNodeModel> activeInputs = Nodes
            .Where(x => x.Type == AudioNodeType.Input && activeInputIds.Contains(x.Id))
            .ToList();

        foreach (AudioNodeModel input in activeInputs)
        {
            if (input.Endpoint is null)
            {
                error = $"Choose an audio endpoint inside the {input.Title} node.";
                return false;
            }
        }

        IGrouping<string, AudioNodeModel>? duplicateInput = activeInputs
            .GroupBy(x => x.Endpoint!.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateInput is not null)
        {
            error = $"{duplicateInput.First().Endpoint!.Name} is assigned to more than one active INPUT node. Use one INPUT node and split its OUT socket so the source remains explicit and synchronized.";
            return false;
        }

        IGrouping<string, AudioNodeModel>? duplicateVirtualInput = activeInputs
            .Where(x => x.Endpoint!.VirtualRoutingFamily is not null)
            .GroupBy(x => x.Endpoint!.RoutingSafetyKey, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateVirtualInput is not null)
        {
            error = $"Multiple active INPUT nodes belong to the same virtual routing family ({duplicateVirtualInput.First().Endpoint!.Name}). Use a single input node and split it inside MINT.";
            return false;
        }

        var endpointEdges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var endpointLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (AudioNodeModel output in outputs)
        {
            AudioEndpointChoice outputEndpoint = output.Endpoint!;
            string outputKey = outputEndpoint.RoutingSafetyKey;
            endpointLabels[outputKey] = outputEndpoint.Name;

            HashSet<Guid> upstream = GetUpstreamNodeIds(output);
            foreach (AudioNodeModel input in activeInputs.Where(x => upstream.Contains(x.Id)))
            {
                AudioEndpointChoice inputEndpoint = input.Endpoint!;

                if (inputEndpoint.ConflictsWithOutput(outputEndpoint))
                {
                    error = $"{input.Title} can hear the same virtual route used by {output.Title}. MINT blocked the route before it could feed itself.";
                    return false;
                }

                if (!inputEndpoint.CanReceiveRenderedAudio) continue;

                string inputKey = inputEndpoint.RoutingSafetyKey;
                endpointLabels[inputKey] = inputEndpoint.Name;
                if (!endpointEdges.TryGetValue(inputKey, out HashSet<string>? targets))
                {
                    targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    endpointEdges[inputKey] = targets;
                }
                targets.Add(outputKey);
            }
        }

        if (TryFindEndpointCycle(endpointEdges, out List<string> cycle))
        {
            string path = string.Join(" → ", cycle.Select(key => endpointLabels.TryGetValue(key, out string? label) ? label : key));
            error = $"The selected endpoints form an external audio loop: {path}. Use a different virtual endpoint for one leg.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public HashSet<Guid> GetUpstreamNodeIds(AudioNodeModel node)
    {
        var result = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(node.Id);

        while (stack.Count > 0)
        {
            Guid current = stack.Pop();
            foreach (AudioConnectionModel connection in Connections.Where(x => x.TargetNodeId == current))
            {
                if (result.Add(connection.SourceNodeId))
                    stack.Push(connection.SourceNodeId);
            }
        }

        return result;
    }

    private bool ValidateConnectionIntegrity(out string error)
    {
        foreach (AudioConnectionModel connection in Connections)
        {
            AudioNodeModel? sourceNode = SourceNode(connection);
            AudioNodeModel? targetNode = TargetNode(connection);
            AudioPortModel? sourcePort = GetPort(connection.SourcePortId);
            AudioPortModel? targetPort = GetPort(connection.TargetPortId);

            if (sourceNode is null || targetNode is null || sourcePort is null || targetPort is null)
            {
                error = "The patch contains a cable connected to a missing node or socket.";
                return false;
            }

            if (sourcePort.Direction != AudioPortDirection.Output || targetPort.Direction != AudioPortDirection.Input)
            {
                error = "The patch contains a cable connected in the wrong direction.";
                return false;
            }
        }

        foreach (AudioNodeModel node in Nodes)
        {
            foreach (AudioPortModel input in node.Inputs)
            {
                int count = Connections.Count(x => x.TargetPortId == input.Id);
                if (count > 1 && !input.AllowsMultipleConnections)
                {
                    error = $"{node.Title} · {input.Name} has more than one cable. Only MIX BUS inputs may sum audible signals.";
                    return false;
                }
            }

            int audibleInputPorts = node.Inputs
                .Where(port => port.Kind == AudioPortKind.Audio)
                .Count(port => Connections.Any(connection => connection.TargetPortId == port.Id));

            if (node.Type != AudioNodeType.Mixer && audibleInputPorts > 1)
            {
                error = $"{node.Title} is implicitly combining audio. Route those signals through an explicit MIX BUS first.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private bool WouldCreateCycle(Guid sourceNodeId, Guid targetNodeId)
    {
        if (sourceNodeId == targetNodeId) return true;

        var stack = new Stack<Guid>();
        var visited = new HashSet<Guid>();
        stack.Push(targetNodeId);

        while (stack.Count > 0)
        {
            Guid current = stack.Pop();
            if (!visited.Add(current)) continue;
            if (current == sourceNodeId) return true;

            foreach (AudioConnectionModel connection in Connections.Where(x => x.SourceNodeId == current))
                stack.Push(connection.TargetNodeId);
        }

        return false;
    }

    private bool HasCycle()
    {
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();

        bool Visit(Guid nodeId)
        {
            if (visiting.Contains(nodeId)) return true;
            if (!visited.Add(nodeId)) return false;

            visiting.Add(nodeId);
            foreach (AudioConnectionModel connection in Connections.Where(x => x.SourceNodeId == nodeId))
            {
                if (Visit(connection.TargetNodeId)) return true;
            }
            visiting.Remove(nodeId);
            return false;
        }

        return Nodes.Any(node => Visit(node.Id));
    }

    private static bool TryFindEndpointCycle(
        IReadOnlyDictionary<string, HashSet<string>> edges,
        out List<string> cycle)
    {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        bool Visit(string key)
        {
            state[key] = 1;
            path.Add(key);

            if (edges.TryGetValue(key, out HashSet<string>? targets))
            {
                foreach (string target in targets)
                {
                    if (!state.TryGetValue(target, out int targetState))
                    {
                        if (Visit(target)) return true;
                    }
                    else if (targetState == 1)
                    {
                        int start = path.FindIndex(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase));
                        cycle = path.Skip(Math.Max(0, start)).Append(target).ToList();
                        return true;
                    }
                }
            }

            path.RemoveAt(path.Count - 1);
            state[key] = 2;
            return false;
        }

        cycle = [];
        foreach (string key in edges.Keys)
        {
            if (!state.ContainsKey(key) && Visit(key)) return true;
        }

        return false;
    }

    public static AudioGraphModel CreateDefault()
    {
        var graph = new AudioGraphModel();

        AudioNodeModel voice = graph.AddNode(AudioNodeType.Input, 30, 78, "MIC / RVC INPUT");
        voice.IsVoiceActivitySource = true;
        voice.Profile.CopyFrom(MintProfiles.Voice["RVC Cleanup"]);

        AudioNodeModel gate = graph.AddNode(AudioNodeType.NoiseGate, 250, 78);
        AudioNodeModel highPass = graph.AddNode(AudioNodeType.HighPass, 470, 78);
        AudioNodeModel deEsser = graph.AddNode(AudioNodeType.DeEsser, 690, 78);
        AudioNodeModel voiceEq = graph.AddNode(AudioNodeType.Equalizer, 910, 78, "VOICE EQ");
        AudioNodeModel voiceComp = graph.AddNode(AudioNodeType.Compressor, 1130, 78, "VOICE COMP");

        AudioNodeModel program = graph.AddNode(AudioNodeType.Input, 30, 286, "APP / MUSIC INPUT");
        program.Profile.CopyFrom(MintProfiles.Program["Music Safe"]);
        AudioNodeModel rider = graph.AddNode(AudioNodeType.LevelRider, 250, 286);
        AudioNodeModel programEq = graph.AddNode(AudioNodeType.Equalizer, 470, 286, "PROGRAM EQ");
        AudioNodeModel programComp = graph.AddNode(AudioNodeType.Compressor, 690, 286, "PROGRAM COMP");
        AudioNodeModel ducker = graph.AddNode(AudioNodeType.Ducker, 910, 286);

        AudioNodeModel mixer = graph.AddNode(AudioNodeType.Mixer, 1350, 190, "STREAM BUS");
        AudioNodeModel limiter = graph.AddNode(AudioNodeType.Limiter, 1570, 190, "MASTER LIMITER");
        AudioNodeModel output = graph.AddNode(AudioNodeType.Output, 1790, 190, "STREAM OUTPUT");

        Connect(graph, voice, "OUT", gate, "IN");
        Connect(graph, gate, "OUT", highPass, "IN");
        Connect(graph, highPass, "OUT", deEsser, "IN");
        Connect(graph, deEsser, "OUT", voiceEq, "IN");
        Connect(graph, voiceEq, "OUT", voiceComp, "IN");
        Connect(graph, voiceComp, "OUT", mixer, "MIX IN");

        Connect(graph, program, "OUT", rider, "IN");
        Connect(graph, rider, "OUT", programEq, "IN");
        Connect(graph, programEq, "OUT", programComp, "IN");
        Connect(graph, programComp, "OUT", ducker, "MAIN");
        Connect(graph, voiceComp, "OUT", ducker, "SIDECHAIN");
        Connect(graph, ducker, "OUT", mixer, "MIX IN");

        Connect(graph, mixer, "OUT", limiter, "IN");
        Connect(graph, limiter, "OUT", output, "IN");

        return graph;
    }

    private static void Connect(
        AudioGraphModel graph,
        AudioNodeModel source,
        string sourcePort,
        AudioNodeModel target,
        string targetPort)
    {
        if (!graph.TryConnect(source.Id, sourcePort, target.Id, targetPort, out string error))
            throw new InvalidOperationException(error);
    }

    private static (string Title, string Subtitle) Defaults(AudioNodeType type) => type switch
    {
        AudioNodeType.Input => ("AUDIO INPUT", "choose capture or loopback"),
        AudioNodeType.Gain => ("GAIN / TRIM", "input level"),
        AudioNodeType.NoiseGate => ("SMART GATE", "adaptive noise floor"),
        AudioNodeType.HighPass => ("RUMBLE CUT", "high-pass / anti-plosive"),
        AudioNodeType.DeEsser => ("DE-ESSER", "dynamic sibilance"),
        AudioNodeType.Equalizer => ("DYNAMIC EQ", "three-band tone"),
        AudioNodeType.LevelRider => ("LEVEL RIDER", "slow loudness control"),
        AudioNodeType.Compressor => ("COMPRESSOR", "dynamic control"),
        AudioNodeType.Ducker => ("SIDECHAIN DUCKER", "main + sidechain"),
        AudioNodeType.Mixer => ("MIX BUS", "multi-input summing"),
        AudioNodeType.Limiter => ("LIMITER", "protected ceiling"),
        AudioNodeType.Output => ("AUDIO OUTPUT", "choose render endpoint"),
        _ => (type.ToString().ToUpperInvariant(), string.Empty)
    };

    private static void ApplyDefaultProfile(AudioNodeModel node)
    {
        switch (node.Type)
        {
            case AudioNodeType.NoiseGate:
                node.Profile.GateThresholdDb = -52;
                node.Profile.AutoMode = true;
                break;
            case AudioNodeType.HighPass:
                node.Profile.HighPassHz = 75;
                break;
            case AudioNodeType.DeEsser:
                node.Profile.DeEsserAmount = 0.35f;
                node.Profile.AutoMode = true;
                break;
            case AudioNodeType.LevelRider:
                node.Profile.TargetDb = -22;
                node.Profile.AutoMode = true;
                break;
            case AudioNodeType.Compressor:
                node.Profile.Compression = 0.35f;
                break;
            case AudioNodeType.Ducker:
                node.Profile.DuckingDb = -6;
                break;
            case AudioNodeType.Limiter:
                node.Profile.LimiterCeilingDb = -1;
                break;
        }
    }
}
