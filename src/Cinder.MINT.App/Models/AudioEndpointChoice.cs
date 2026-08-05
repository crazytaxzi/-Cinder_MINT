using NAudio.CoreAudioApi;

namespace Cinder.MINT.Models;

public enum EndpointSourceKind
{
    Capture,
    RenderLoopback
}

public sealed record AudioEndpointChoice(
    string Id,
    string Name,
    EndpointSourceKind Kind,
    DataFlow DataFlow)
{
    public string DisplayName =>
        Kind == EndpointSourceKind.Capture
            ? $"MIC  •  {Name}"
            : $"LOOPBACK / RVC  •  {Name}";

    public override string ToString() => DisplayName;
}
