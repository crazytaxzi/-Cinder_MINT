using System.Text.Json;

namespace Cinder.MINT.Services;

public sealed class MintSettings
{
    public string? VoiceSourceId { get; set; }
    public string? ProgramSourceId { get; set; }
    public string? OutputId { get; set; }
    public string VoicePreset { get; set; } = "Natural Broadcast";
    public string ProgramPreset { get; set; } = "Music Safe";
    public bool AutoStart { get; set; }
    public int LatencyMs { get; set; } = 30;
}

public sealed class SettingsService
{
    private readonly string _path;

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
            return JsonSerializer.Deserialize<MintSettings>(File.ReadAllText(_path))
                   ?? new MintSettings();
        }
        catch
        {
            return new MintSettings();
        }
    }

    public void Save(MintSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, options));
    }
}
