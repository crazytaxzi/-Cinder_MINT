using NAudio.CoreAudioApi;
using System.Text;

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
    private static readonly string[] VirtualRoutingMarkers =
    [
        "vb-audio",
        "voicemeeter",
        "virtual cable",
        "virtual audio",
        "mixline"
    ];

    public string DisplayName =>
        Kind == EndpointSourceKind.Capture
            ? $"MIC  •  {Name}"
            : $"LOOPBACK / RVC  •  {Name}";

    /// <summary>
    /// Identifies both sides of a known virtual routing device. For example,
    /// CABLE Input and CABLE Output share the same parenthetical device family.
    /// Physical endpoints deliberately return null so similarly named hardware
    /// is not blocked by an over-eager safety heuristic.
    /// </summary>
    public string? VirtualRoutingFamily
    {
        get
        {
            string lower = Name.ToLowerInvariant();
            if (!VirtualRoutingMarkers.Any(lower.Contains)) return null;

            int open = Name.LastIndexOf('(');
            int close = Name.LastIndexOf(')');
            string family = open >= 0 && close > open
                ? Name[(open + 1)..close]
                : Name;

            return Normalize(family);
        }
    }

    public string RoutingSafetyKey => VirtualRoutingFamily is { Length: > 0 } family
        ? $"virtual:{family}"
        : $"endpoint:{Id}";

    public bool CanReceiveRenderedAudio =>
        Kind == EndpointSourceKind.RenderLoopback || VirtualRoutingFamily is not null;

    public bool ConflictsWithOutput(AudioEndpointChoice output) =>
        string.Equals(Id, output.Id, StringComparison.OrdinalIgnoreCase) ||
        (VirtualRoutingFamily is { Length: > 0 } inputFamily &&
         output.VirtualRoutingFamily is { Length: > 0 } outputFamily &&
         string.Equals(inputFamily, outputFamily, StringComparison.OrdinalIgnoreCase));

    public override string ToString() => DisplayName;

    private static string Normalize(string value)
    {
        var result = new StringBuilder(value.Length);
        bool pendingSpace = false;

        foreach (char character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSpace && result.Length > 0) result.Append(' ');
                result.Append(character);
                pendingSpace = false;
            }
            else
            {
                pendingSpace = true;
            }
        }

        return result.ToString();
    }
}
