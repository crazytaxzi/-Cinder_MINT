using Cinder.MINT.Models;

namespace Cinder.MINT.Audio.Dsp;

public sealed class DspConfiguration
{
    public required MintProfile Profile { get; init; }
    public bool IsVoice { get; init; }
    public bool IsProgram { get; init; }
    public bool IsMaster { get; init; }

    public bool GateEnabled { get; set; } = true;
    public bool HighPassEnabled { get; set; } = true;
    public bool DeEsserEnabled { get; set; } = true;
    public bool EqEnabled { get; set; } = true;
    public bool RiderEnabled { get; set; } = true;
    public bool CompressorEnabled { get; set; } = true;
    public bool DuckerEnabled { get; set; } = true;
    public bool LimiterEnabled { get; set; } = true;
}
