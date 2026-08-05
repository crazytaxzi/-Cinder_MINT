using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace Cinder.MINT.Models;

public enum AudioNodeType
{
    VoiceSource,
    ProgramSource,
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

public sealed class AudioNodeModel : INotifyPropertyChanged
{
    private double _x;
    private double _y;
    private bool _enabled = true;

    public Guid Id { get; init; } = Guid.NewGuid();
    public required AudioNodeType Type { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = string.Empty;

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

    public Rect Bounds => new(X, Y, 156, 72);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public sealed record AudioConnectionModel(Guid SourceId, Guid TargetId);

public sealed class AudioGraphModel
{
    public ObservableCollection<AudioNodeModel> Nodes { get; } = [];
    public ObservableCollection<AudioConnectionModel> Connections { get; } = [];

    public static AudioGraphModel CreateDefault()
    {
        var graph = new AudioGraphModel();

        var voice = Node(AudioNodeType.VoiceSource, "VOICE / RVC", "capture or loopback", 28, 70);
        var gate = Node(AudioNodeType.NoiseGate, "SMART GATE", "adaptive floor", 220, 70);
        var hp = Node(AudioNodeType.HighPass, "RUMBLE CUT", "anti-plosive", 412, 70);
        var deEss = Node(AudioNodeType.DeEsser, "DE-ESSER", "dynamic sibilance", 604, 70);
        var voiceEq = Node(AudioNodeType.Equalizer, "VOICE EQ", "3-band tone", 796, 70);
        var voiceComp = Node(AudioNodeType.Compressor, "VOICE COMP", "level control", 988, 70);

        var music = Node(AudioNodeType.ProgramSource, "MUSIC / APP", "endpoint loopback", 28, 220);
        var rider = Node(AudioNodeType.LevelRider, "LEVEL RIDER", "slow loudness", 220, 220);
        var musicEq = Node(AudioNodeType.Equalizer, "MUSIC EQ", "3-band tone", 412, 220);
        var musicComp = Node(AudioNodeType.Compressor, "MUSIC COMP", "dynamic control", 604, 220);
        var duck = Node(AudioNodeType.Ducker, "MIC DUCKER", "voice sidechain", 796, 220);

        var mixer = Node(AudioNodeType.Mixer, "STREAM BUS", "32-bit float mix", 988, 220);
        var limiter = Node(AudioNodeType.Limiter, "MASTER LIMITER", "-1 dB ceiling", 1180, 145);
        var output = Node(AudioNodeType.Output, "OUTPUT", "VB-Cable / device", 1372, 145);

        foreach (var node in new[] { voice, gate, hp, deEss, voiceEq, voiceComp, music, rider, musicEq, musicComp, duck, mixer, limiter, output })
            graph.Nodes.Add(node);

        graph.Link(voice, gate);
        graph.Link(gate, hp);
        graph.Link(hp, deEss);
        graph.Link(deEss, voiceEq);
        graph.Link(voiceEq, voiceComp);
        graph.Link(voiceComp, mixer);

        graph.Link(music, rider);
        graph.Link(rider, musicEq);
        graph.Link(musicEq, musicComp);
        graph.Link(musicComp, duck);
        graph.Link(duck, mixer);

        graph.Link(mixer, limiter);
        graph.Link(limiter, output);

        return graph;
    }

    private static AudioNodeModel Node(AudioNodeType type, string title, string subtitle, double x, double y) =>
        new()
        {
            Type = type,
            Title = title,
            Subtitle = subtitle,
            X = x,
            Y = y
        };

    private void Link(AudioNodeModel source, AudioNodeModel target) =>
        Connections.Add(new AudioConnectionModel(source.Id, target.Id));
}
