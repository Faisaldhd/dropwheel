using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FileAction.App.Input;
using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App;

/// <summary>The UI language the user picked; <see cref="UiLanguage.System"/> follows the Windows display language.</summary>
public enum UiLanguage
{
    System,
    English,
    Arabic,
}

/// <summary>2.2.0: app theme. <see cref="ThemeMode.System"/> follows the Windows app mode.</summary>
public enum ThemeMode
{
    System,
    Dark,
    Light,
}

public enum HotkeyPreset
{
    None,
    CtrlAltSpace,
    CtrlShiftSpace,
    CtrlAltF,
    CtrlAltW,
}

/// <summary>User preferences, stored as JSON in %APPDATA%\DropWheel\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>
    /// Settings folder: next to the exe for a portable build (a <c>portable.txt</c> marker), otherwise
    /// <c>%APPDATA%\DropWheel</c>. Nothing is written outside the folder on a portable build.
    /// </summary>
    public static string Folder { get; } = ResolveFolder();

    private static string ResolveFolder()
    {
        try
        {
            var exeFolder = AppContext.BaseDirectory;
            if (File.Exists(Path.Combine(exeFolder, "portable.txt")))
                return exeFolder;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DropWheel");
    }

    private static string FilePath => Path.Combine(Folder, "settings.json");

    public ModifierKey ConvertKey { get; set; } = ModifierKey.Shift;
    public ModifierKey ToolsKey { get; set; } = ModifierKey.Ctrl;
    public ModifierKey ActionsKey { get; set; } = ModifierKey.Alt;
    public HotkeyPreset Hotkey { get; set; } = HotkeyPreset.CtrlAltSpace;

    /// <summary>System = follow the Windows display language.</summary>
    public UiLanguage Language { get; set; } = UiLanguage.System;

    /// <summary>System / Dark / Light; applies to every window.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;

    /// <summary>
    /// 2.2.0: check GitHub once a day for a newer release (it only checks; downloading always needs a click).
    /// Default ON, as the spec requires.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Empty = save results next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// R-20: company-safe by default. When false no low-level keyboard/mouse hooks are installed at all; the
    /// wheel opens from the tray, the Explorer context menu or the shelf window.
    /// </summary>
    public bool WatchGestures { get; set; }

    /// <summary>
    /// R-20: company-safe by default. When false DropWheel never downloads a helper tool; it only uses what is
    /// already on PATH, installed by winget, or chosen by the user.
    /// </summary>
    public bool AutoDownloadTools { get; set; }

    public bool RevealResult { get; set; }
    public bool Paused { get; set; }
    public bool FirstRunDone { get; set; }
    public string? LastCopyFolder { get; set; }
    public string? LastMoveFolder { get; set; }

    public Settings Quality { get; set; } = new();

    /// <summary>Operation ids the user removed from the wheels.</summary>
    public List<string> HiddenOperations { get; set; } = [];

    /// <summary>Operation ids that always come first on their wheel, in this order.</summary>
    public List<string> FavoriteOperations { get; set; } = [];

    /// <summary>The last operation run, for "repeat last" (with the values it asked for, e.g. trim times).</summary>
    public string? LastOperationId { get; set; }

    public Dictionary<string, string>? LastParameters { get; set; }


    public WheelKind? WheelFor(ModifierKey key)
    {
        if (key == ModifierKey.None)
            return null;
        if (key == ConvertKey)
            return WheelKind.Convert;
        if (key == ToolsKey)
            return WheelKind.Tools;
        if (key == ActionsKey)
            return WheelKind.Actions;
        return null;
    }

    public ModifierKey KeyFor(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => ConvertKey,
        WheelKind.Tools => ToolsKey,
        _ => ActionsKey,
    };

    public static AppSettings Load() => LoadFrom(FilePath);

    /// <summary>Loads one settings file; also used by the self-test to exercise the real file path.</summary>
    internal static AppSettings LoadFrom(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize(MigrateLanguage(json), SettingsJson.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Broken settings file: start with defaults.
        }
        return new AppSettings();
    }

    /// <summary>
    /// Older builds stored <c>Language</c> as a nullable English/Czech enum. The model is now
    /// System/English/Arabic, and an unknown, legacy or null value means "follow Windows". Only that
    /// member is rewritten, so an old settings file never loses the other preferences.
    /// </summary>
    internal static string MigrateLanguage(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject root || !root.TryGetPropertyValue("Language", out var language))
                return json;
            root["Language"] = JsonValue.Create(ReadLanguage(language).ToString());
            return root.ToJsonString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            // Keep the original text; normal loading below decides whether it is usable.
            return json;
        }
    }

    private static UiLanguage ReadLanguage(JsonNode? language)
    {
        if (language is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            return UiLanguage.System;
        return text.Trim().ToLowerInvariant() switch
        {
            "system" => UiLanguage.System,
            "english" => UiLanguage.English,
            "arabic" => UiLanguage.Arabic,
            _ => UiLanguage.System,
        };
    }

    public void Save() => SaveTo(FilePath);

    /// <summary>Saves one settings file; also used by the self-test to exercise the real file path.</summary>
    internal void SaveTo(string path)
    {
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, SettingsJson.Default.AppSettings));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal; settings simply won't persist.
        }
    }

    public static string KeyName(ModifierKey key) => key switch
    {
        ModifierKey.Shift => "Shift",
        ModifierKey.Ctrl => "Ctrl",
        ModifierKey.Alt => "Alt",
        _ => "—",
    };

    public static string WheelName(WheelKind wheel) => wheel switch
    {
        WheelKind.Convert => T("wheel.convert"),
        WheelKind.Tools => T("wheel.tools"),
        _ => T("wheel.actions"),
    };

    public static string HotkeyName(HotkeyPreset preset) => preset switch
    {
        HotkeyPreset.CtrlAltSpace => T("hotkey.ctrlAltSpace"),
        HotkeyPreset.CtrlShiftSpace => T("hotkey.ctrlShiftSpace"),
        HotkeyPreset.CtrlAltF => "Ctrl + Alt + F",
        HotkeyPreset.CtrlAltW => "Ctrl + Alt + W",
        _ => T("hotkey.off"),
    };
}

/// <summary>Source-generated JSON for the settings file (works in the trimmed build without reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
