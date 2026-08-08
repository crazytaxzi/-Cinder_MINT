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

public readonly record struct PatchbayShortcut(Key Key, ModifierKeys Modifiers);

public static class PatchbayGesture
{
    public static bool Matches(KeyEventArgs e, string text)
    {
        if (!TryParse(text, out PatchbayShortcut gesture))
            return false;

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        return key == gesture.Key && Keyboard.Modifiers == gesture.Modifiers;
    }

    public static bool TryParse(string text, out PatchbayShortcut gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string cleaned = text.Trim();

        // preview.3 could persist strings such as "None+N". Treat the old
        // sentinel as no modifier and normalize it away on the next load/save.
        while (cleaned.StartsWith("None+", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[5..];

        string[] parts = cleaned.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        ModifierKeys modifiers = ModifierKeys.None;
        Key? key = null;

        foreach (string rawPart in parts)
        {
            string part = rawPart.Trim();
            if (part.Length == 0)
                continue;

            if (part.Equals("None", StringComparison.OrdinalIgnoreCase))
                continue;

            if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Control;
                continue;
            }

            if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Alt;
                continue;
            }

            if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Shift;
                continue;
            }

            if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModifierKeys.Windows;
                continue;
            }

            if (key is not null || !TryParseKey(part, out Key parsedKey))
                return false;

            key = parsedKey;
        }

        if (key is null || key == Key.None || key == Key.Escape || IsModifierKey(key.Value))
            return false;

        gesture = new PatchbayShortcut(key.Value, modifiers);
        return true;
    }

    public static string Format(Key key, ModifierKeys modifiers)
    {
        if (key == Key.None || key == Key.Escape || IsModifierKey(key))
            return string.Empty;

        var parts = new List<string>(5);
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        parts.Add(FormatKey(key));
        return string.Join('+', parts);
    }

    public static string Normalize(string text, string fallback)
    {
        if (!TryParse(text, out PatchbayShortcut gesture))
            return fallback;
        return Format(gesture.Key, gesture.Modifiers);
    }

    public static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin;

    private static bool TryParseKey(string text, out Key key)
    {
        key = Key.None;
        string token = text.Trim();

        if (token.Length == 1 && char.IsDigit(token[0]))
            token = $"D{token[0]}";
        else if (token.Equals("Enter", StringComparison.OrdinalIgnoreCase))
            token = nameof(Key.Return);
        else if (token.Equals("Backspace", StringComparison.OrdinalIgnoreCase))
            token = nameof(Key.Back);
        else if (token.Equals("Del", StringComparison.OrdinalIgnoreCase))
            token = nameof(Key.Delete);
        else if (token.Equals("PgUp", StringComparison.OrdinalIgnoreCase))
            token = nameof(Key.PageUp);
        else if (token.Equals("PgDn", StringComparison.OrdinalIgnoreCase))
            token = nameof(Key.PageDown);
        else if (token.Equals("Spacebar", StringComparison.OrdinalIgnoreCase))
            token = nameof(Key.Space);

        return Enum.TryParse(token, ignoreCase: true, out key);
    }

    private static string FormatKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
            return ((int)key - (int)Key.D0).ToString();

        return key switch
        {
            Key.Return => "Enter",
            Key.Back => "Backspace",
            _ => key.ToString()
        };
    }
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

        string oldAdd = settings.AddNode;
        string oldRemove = settings.RemoveHovered;
        string oldOpen = settings.OpenControls;
        string oldBypass = settings.ToggleBypass;

        settings.AddNode = PatchbayGesture.Normalize(settings.AddNode, "N");
        settings.RemoveHovered = PatchbayGesture.Normalize(settings.RemoveHovered, "R");
        settings.OpenControls = PatchbayGesture.Normalize(settings.OpenControls, "Enter");
        settings.ToggleBypass = PatchbayGesture.Normalize(settings.ToggleBypass, "B");

        bool migrated = !string.Equals(oldAdd, settings.AddNode, StringComparison.Ordinal) ||
                        !string.Equals(oldRemove, settings.RemoveHovered, StringComparison.Ordinal) ||
                        !string.Equals(oldOpen, settings.OpenControls, StringComparison.Ordinal) ||
                        !string.Equals(oldBypass, settings.ToggleBypass, StringComparison.Ordinal);

        if (migrated)
        {
            try
            {
                File.WriteAllText(_path, JsonSerializer.Serialize(settings, _json));
            }
            catch
            {
                // Runtime settings are already repaired; persistence can fail harmlessly.
            }
        }

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
