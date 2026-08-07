using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace Cinder.MINT.Models;

public sealed class PatchbayHotkeySettings
{
    public string AddNode { get; set; } = "N";
    public string RemoveHovered { get; set; } = "R";
    public string OpenControls { get; set; } = "Enter";
    public string ToggleBypass { get; set; } = "B";

    public PatchbayHotkeySettings Clone() => new()
    {
        AddNode = AddNode,
        RemoveHovered = RemoveHovered,
        OpenControls = OpenControls,
        ToggleBypass = ToggleBypass
    };
}

public static class PatchbayGesture
{
    private static readonly KeyGestureConverter Converter = new();

    public static bool Matches(KeyEventArgs e, string text)
    {
        if (!TryParse(text, out KeyGesture? gesture) || gesture is null)
            return false;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == gesture.Key && Keyboard.Modifiers == gesture.Modifiers;
    }

    public static bool TryParse(string text, out KeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        try
        {
            object? value = Converter.ConvertFromString(null, CultureInfo.InvariantCulture, text.Trim());
            if (value is not KeyGesture parsed) return false;
            if (IsModifierKey(parsed.Key) || parsed.Key == Key.Escape || parsed.Key == Key.None)
                return false;
            gesture = parsed;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        var gesture = new KeyGesture(key, modifiers);
        return Converter.ConvertToString(null, CultureInfo.InvariantCulture, gesture) ?? key.ToString();
    }

    public static string Normalize(string text, string fallback)
    {
        if (!TryParse(text, out KeyGesture? gesture) || gesture is null)
            return fallback;
        return Format(gesture.Key, gesture.Modifiers);
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;
}

public sealed class PatchbayHotkeyStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public PatchbayHotkeyStore()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cinder MINT");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "patchbay-hotkeys.json");
    }

    public PatchbayHotkeySettings Load()
    {
        PatchbayHotkeySettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<PatchbayHotkeySettings>(File.ReadAllText(_path), _json)
                    ?? new PatchbayHotkeySettings()
                : new PatchbayHotkeySettings();
        }
        catch
        {
            settings = new PatchbayHotkeySettings();
        }

        settings.AddNode = PatchbayGesture.Normalize(settings.AddNode, "N");
        settings.RemoveHovered = PatchbayGesture.Normalize(settings.RemoveHovered, "R");
        settings.OpenControls = PatchbayGesture.Normalize(settings.OpenControls, "Enter");
        settings.ToggleBypass = PatchbayGesture.Normalize(settings.ToggleBypass, "B");
        return settings;
    }

    public void Save(PatchbayHotkeySettings settings)
    {
        settings.AddNode = PatchbayGesture.Normalize(settings.AddNode, "N");
        settings.RemoveHovered = PatchbayGesture.Normalize(settings.RemoveHovered, "R");
        settings.OpenControls = PatchbayGesture.Normalize(settings.OpenControls, "Enter");
        settings.ToggleBypass = PatchbayGesture.Normalize(settings.ToggleBypass, "B");
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, _json));
    }
}
