using Cinder.MINT.Models;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cinder.MINT.Services;

public sealed class MintSettings
{
    public bool AutoStart { get; set; }
    public AudioGraphSnapshot? Graph { get; set; }

    // Kept for a one-time migration from the original fixed-lane builds.
    public string? VoiceSourceId { get; set; }
    public string? ProgramSourceId { get; set; }
    public string? OutputId { get; set; }
    public string VoicePreset { get; set; } = "Natural Broadcast";
    public string ProgramPreset { get; set; } = "Music Safe";
    public int LatencyMs { get; set; } = 30;
}

public sealed class AudioGraphSnapshot
{
    public List<AudioNodeSnapshot> Nodes { get; set; } = [];
    public List<AudioConnectionSnapshot> Connections { get; set; } = [];
}

public sealed class AudioNodeSnapshot
{
    public Guid Id { get; set; }
    public AudioNodeType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public bool Enabled { get; set; } = true;
    public string? EndpointId { get; set; }
    public bool IsVoiceActivitySource { get; set; }
    public int LatencyMs { get; set; } = 30;
    public MintProfile Profile { get; set; } = new();
}

public sealed class AudioConnectionSnapshot
{
    public Guid SourceNodeId { get; set; }
    public string SourcePort { get; set; } = "OUT";
    public Guid TargetNodeId { get; set; }
    public string TargetPort { get; set; } = "IN";
}

public sealed class SettingsService
{
    private readonly string _path;
    private readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsService()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cinder MINT");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "settings.json");
    }

    public MintSettings Load()
    {
        try
        {
            if (!File.Exists(_path)) return new MintSettings();
            return JsonSerializer.Deserialize<MintSettings>(File.ReadAllText(_path), _options)
                   ?? new MintSettings();
        }
        catch
        {
            return new MintSettings();
        }
    }

    public AudioGraphModel RestoreGraph(MintSettings settings)
    {
        if (settings.Graph is null || settings.Graph.Nodes.Count == 0)
            return RestoreLegacyGraph(settings);

        var graph = new AudioGraphModel();

        foreach (AudioNodeSnapshot snapshot in settings.Graph.Nodes)
        {
            AudioNodeModel node = graph.AddNode(
                snapshot.Type,
                snapshot.X,
                snapshot.Y,
                snapshot.Title,
                snapshot.Id);

            node.Subtitle = snapshot.Subtitle;
            node.Enabled = snapshot.Enabled;
            node.SavedEndpointId = snapshot.EndpointId;
            node.IsVoiceActivitySource = snapshot.IsVoiceActivitySource;
            node.LatencyMs = snapshot.LatencyMs;
            node.Profile.CopyFrom(snapshot.Profile ?? new MintProfile());
        }

        foreach (AudioConnectionSnapshot snapshot in settings.Graph.Connections)
            graph.TryConnect(
                snapshot.SourceNodeId,
                snapshot.SourcePort,
                snapshot.TargetNodeId,
                snapshot.TargetPort,
                out _);

        return graph;
    }

    public void Save(MintSettings settings, AudioGraphModel graph)
    {
        settings.Graph = Snapshot(graph);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _options));
    }

    private static AudioGraphSnapshot Snapshot(AudioGraphModel graph)
    {
        var snapshot = new AudioGraphSnapshot();

        foreach (AudioNodeModel node in graph.Nodes)
        {
            snapshot.Nodes.Add(new AudioNodeSnapshot
            {
                Id = node.Id,
                Type = node.Type,
                Title = node.Title,
                Subtitle = node.Subtitle,
                X = node.X,
                Y = node.Y,
                Enabled = node.Enabled,
                EndpointId = node.Endpoint?.Id ?? node.SavedEndpointId,
                IsVoiceActivitySource = node.IsVoiceActivitySource,
                LatencyMs = node.LatencyMs,
                Profile = node.Profile.Clone()
            });
        }

        foreach (AudioConnectionModel connection in graph.Connections)
        {
            AudioNodeModel? sourceNode = graph.Nodes.FirstOrDefault(x => x.Id == connection.SourceNodeId);
            AudioNodeModel? targetNode = graph.Nodes.FirstOrDefault(x => x.Id == connection.TargetNodeId);
            AudioPortModel? sourcePort = sourceNode?.Outputs.FirstOrDefault(x => x.Id == connection.SourcePortId);
            AudioPortModel? targetPort = targetNode?.Inputs.FirstOrDefault(x => x.Id == connection.TargetPortId);
            if (sourceNode is null || targetNode is null || sourcePort is null || targetPort is null)
                continue;

            snapshot.Connections.Add(new AudioConnectionSnapshot
            {
                SourceNodeId = sourceNode.Id,
                SourcePort = sourcePort.Name,
                TargetNodeId = targetNode.Id,
                TargetPort = targetPort.Name
            });
        }

        return snapshot;
    }

    private static AudioGraphModel RestoreLegacyGraph(MintSettings settings)
    {
        AudioGraphModel graph = AudioGraphModel.CreateDefault();
        List<AudioNodeModel> inputs = graph.Nodes.Where(x => x.Type == AudioNodeType.Input).ToList();
        AudioNodeModel? output = graph.Nodes.FirstOrDefault(x => x.Type == AudioNodeType.Output);

        if (inputs.Count > 0)
        {
            inputs[0].SavedEndpointId = settings.VoiceSourceId;
            if (MintProfiles.Voice.TryGetValue(settings.VoicePreset, out MintProfile? voiceProfile))
                inputs[0].Profile.CopyFrom(voiceProfile);
        }

        if (inputs.Count > 1)
        {
            inputs[1].SavedEndpointId = settings.ProgramSourceId;
            if (MintProfiles.Program.TryGetValue(settings.ProgramPreset, out MintProfile? programProfile))
                inputs[1].Profile.CopyFrom(programProfile);
        }

        if (output is not null)
        {
            output.SavedEndpointId = settings.OutputId;
            output.LatencyMs = Math.Clamp(settings.LatencyMs, 10, 150);
        }

        return graph;
    }
}
