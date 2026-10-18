using System.Text.Json;
using System.Text.Json.Serialization;
using FileAction.Core;

namespace FileAction.Linux;

/// <summary>Preferences in ~/.config/dropwheel/settings.json (same names as on Windows).</summary>
public sealed class LinuxSettings
{

    /// <summary>$HOME; unlike GetFolderPath it doesn't come back empty when the folder is missing.</summary>
    public static string Home => Environment.GetEnvironmentVariable("HOME") is { Length: > 0 } home
        ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string Folder => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg ? xdg : Path.Combine(Home, ".config"),
        "dropwheel");

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Null = follow the system language.</summary>
    public Language? Language { get; set; }

    /// <summary>Empty = save results next to the original file.</summary>
    public string? OutputDirectory { get; set; }

    public Settings Quality { get; set; } = new();
    public List<string> HiddenOperations { get; set; } = [];
    public List<string> FavoriteOperations { get; set; } = [];
    public string? LastOperationId { get; set; }
    public Dictionary<string, string>? LastParameters { get; set; }

    /// <summary>Tesseract languages for OCR, e.g. "ces+eng"; empty = pick from what is installed.</summary>
    public string? OcrLanguages { get; set; }

    public static LinuxSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize(File.ReadAllText(FilePath), LinuxSettingsJson.Default.LinuxSettings) ?? new LinuxSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new LinuxSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, LinuxSettingsJson.Default.LinuxSettings));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Source-generated JSON for the settings file (works in the trimmed build without reflection).</summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(LinuxSettings))]
internal sealed partial class LinuxSettingsJson : JsonSerializerContext;
