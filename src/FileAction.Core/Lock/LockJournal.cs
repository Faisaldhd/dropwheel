using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FileAction.Core.Lock;

/// <summary>Where a protected file is while it is open for editing.</summary>
public enum LockSessionState
{
    /// <summary>The plaintext is unlocked and the application may still be editing it.</summary>
    Open,

    /// <summary>A save was observed; re-encrypting is due after the grace period.</summary>
    Syncing,

    /// <summary>Re-encrypting failed; the plaintext is kept and the user can retry.</summary>
    Error,

    /// <summary>The content was re-encrypted and the temporary plaintext removed.</summary>
    Relocked,
}

/// <summary>One open session: enough to relock it now, or to recover it after a crash.</summary>
public sealed record LockSessionRecord(
    string Id,
    string ContainerPath,
    string PlaintextPath,
    string OriginalName,
    bool IsDirectory,
    DateTimeOffset OpenedUtc,
    LockSessionState State,
    string? Error)
{
    public LockSessionRecord WithState(LockSessionState state, string? error = null) =>
        this with { State = state, Error = error };
}

/// <summary>
/// A tiny, line-based journal under <c>%LOCALAPPDATA%\DropWheel\sessions</c>. It records only paths and
/// state — never a password, key or data key — so a crash leaves recoverable paths, not secrets. Paths are
/// Base64-encoded so any file name survives one line.
/// </summary>
public sealed class LockJournalStore(string directory)
{
    private const string Format = "1";

    public string Directory { get; } = directory;

    public LockSessionRecord Create(LockSessionRecord record) => Write(record);

    public LockSessionRecord Write(LockSessionRecord record)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = PathFor(record.Id);
        var temp = path + ".tmp";
        var lines = new[]
        {
            "format=" + Format,
            "id=" + record.Id,
            "container=" + Encode(record.ContainerPath),
            "plaintext=" + Encode(record.PlaintextPath),
            "name=" + Encode(record.OriginalName),
            "directory=" + (record.IsDirectory ? "1" : "0"),
            "opened=" + record.OpenedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            "state=" + record.State,
            "error=" + Encode(record.Error ?? string.Empty),
        };
        File.WriteAllLines(temp, lines, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
        return record;
    }

    public void Delete(string id)
    {
        try
        {
            var path = PathFor(id);
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale journal is harmless: recovery checks whether its paths still exist.
        }
    }

    /// <summary>Every readable journal entry, newest first; corrupt entries are ignored.</summary>
    public IReadOnlyList<LockSessionRecord> List()
    {
        if (!System.IO.Directory.Exists(Directory))
            return [];
        var records = new List<LockSessionRecord>();
        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.session"))
        {
            var record = Read(file);
            if (record is not null)
                records.Add(record);
        }
        return records.OrderByDescending(r => r.OpenedUtc).ToList();
    }

    /// <summary>The open session for a container, or null.</summary>
    public LockSessionRecord? FindByContainer(string containerPath)
    {
        var normalized = LockPaths.Normalize(containerPath);
        return List().FirstOrDefault(r =>
            string.Equals(LockPaths.Normalize(r.ContainerPath), normalized, StringComparison.OrdinalIgnoreCase) &&
            r.State is LockSessionState.Open or LockSessionState.Syncing or LockSessionState.Error);
    }

    public bool IsProtectedByOpenSession(string containerPath) => FindByContainer(containerPath) is not null;

    private static LockSessionRecord? Read(string file)
    {
        try
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in File.ReadAllLines(file))
            {
                var equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;
                values[line[..equals]] = line[(equals + 1)..];
            }
            if (!values.TryGetValue("format", out var format) || format != Format)
                return null;
            if (!values.TryGetValue("id", out var id) || string.IsNullOrEmpty(id))
                return null;
            var container = Decode(values.GetValueOrDefault("container"));
            var plaintext = Decode(values.GetValueOrDefault("plaintext"));
            var name = Decode(values.GetValueOrDefault("name"));
            if (string.IsNullOrEmpty(container) || string.IsNullOrEmpty(plaintext))
                return null;
            var directory = values.GetValueOrDefault("directory") == "1";
            var opened = DateTimeOffset.TryParse(values.GetValueOrDefault("opened"),
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow;
            var state = Enum.TryParse<LockSessionState>(values.GetValueOrDefault("state"), out var stateValue)
                ? stateValue
                : LockSessionState.Open;
            var error = Decode(values.GetValueOrDefault("error"));
            return new LockSessionRecord(id, container, plaintext, string.IsNullOrEmpty(name)
                ? LockPaths.DisplayName(container)
                : name, directory, opened, state, string.IsNullOrEmpty(error) ? null : error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private string PathFor(string id) => Path.Combine(Directory, Sanitize(id) + ".session");

    private static string Sanitize(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (var c in id)
            builder.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return builder.Length == 0 ? "session" : builder.ToString();
    }

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }
}

/// <summary>What to do with a journal entry found at startup.</summary>
public enum LockRecoveryAction
{
    /// <summary>The plaintext is gone; the journal is simply deleted.</summary>
    NothingToDo,

    /// <summary>Both sides exist: ask for a secret and re-encrypt the edited plaintext into the container.</summary>
    PromptRelock,

    /// <summary>Only the plaintext survives: never delete it; offer to protect or restore it.</summary>
    PromptSalvage,
}

/// <summary>The crash-recovery decision, kept pure so every combination can be tested.</summary>
public static class LockRecoveryPlan
{
    public static LockRecoveryAction For(bool containerExists, bool plaintextExists)
    {
        if (!plaintextExists)
            return LockRecoveryAction.NothingToDo;
        return containerExists ? LockRecoveryAction.PromptRelock : LockRecoveryAction.PromptSalvage;
    }
}

/// <summary>What the auto-lock poll observed at one moment.</summary>
public sealed record AutoLockInput(
    bool IsDirectory,
    bool ApplicationHoldsFile,
    bool ExplorerShowsFolder,
    bool AnyFileInFolderHeld,
    TimeSpan SinceLastChange,
    TimeSpan SinceOpened,
    int GraceSeconds,
    int IdleFallbackMinutes);

public enum AutoLockDecision
{
    Wait,
    Lock,
}

/// <summary>
/// The rule for locking an unlocked file again: never while the application holds it, never while Explorer
/// shows the folder, never before the grace period has passed since the last change. The 15-minute idle
/// fallback exists only for cases where the process/file probes cannot decide.
/// </summary>
public static class AutoLockRules
{
    public const int MinimumGraceSeconds = 3;
    public const int MaximumGraceSeconds = 60;
    public const int DefaultIdleFallbackMinutes = 15;

    public static AutoLockDecision Decide(AutoLockInput input)
    {
        var grace = Math.Clamp(input.GraceSeconds, MinimumGraceSeconds, MaximumGraceSeconds);
        var idle = TimeSpan.FromMinutes(Math.Max(1, input.IdleFallbackMinutes));

        // A process holding the file is the one definitive signal: locking now would lose its next save.
        if (input.ApplicationHoldsFile)
            return AutoLockDecision.Wait;

        // For a folder, a real held file is equally definitive.
        if (input.IsDirectory && input.AnyFileInFolderHeld)
            return AutoLockDecision.Wait;

        // An Explorer window may be stale; after the idle fallback its opinion no longer blocks locking.
        if (input.IsDirectory && input.ExplorerShowsFolder && input.SinceLastChange < idle)
            return AutoLockDecision.Wait;

        return input.SinceLastChange >= TimeSpan.FromSeconds(grace)
            ? AutoLockDecision.Lock
            : AutoLockDecision.Wait;
    }
}

/// <summary>Paths DropWheel owns for protection metadata, icons and sessions (never inside the user's folders).</summary>
public static class ProtectionPaths
{
    public static string DataFolder
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local))
                local = AppContext.BaseDirectory;
            return Path.Combine(local, "DropWheel");
        }
    }

    public static string SessionsFolder => Path.Combine(DataFolder, "sessions");
    public static string IconsFolder => Path.Combine(DataFolder, "icons");
    public static string SettingsFile => Path.Combine(DataFolder, "protection.json");
}

/// <summary>The Rescuer's long-lived material, kept outside the settings file.</summary>
public sealed record RescuerSecrets(string Key, string? Password);

/// <summary>
/// Where the Rescuer key and password live between sessions. The app implements this with Windows DPAPI;
/// tests use <see cref="InMemoryRescuerVault"/>.
/// </summary>
public interface IRescuerVault
{
    RescuerSecrets? Load();
    void Save(RescuerSecrets secrets);
    void Clear();
}

/// <summary>Process-lifetime storage; used by tests and as the fallback when DPAPI is unavailable.</summary>
public sealed class InMemoryRescuerVault : IRescuerVault
{
    private RescuerSecrets? _secrets;

    public RescuerSecrets? Load() => _secrets;
    public void Save(RescuerSecrets secrets) => _secrets = secrets;
    public void Clear() => _secrets = null;
}

/// <summary>
/// Non-secret protection preferences. Deliberately separate from <c>AppSettings</c>: nothing here is a
/// password or key, and the file can be read by a user without exposing anything.
/// </summary>
public sealed class ProtectionSettings
{
    public bool AutoLockOnClose { get; set; } = true;

    /// <summary>Seconds between "the application closed" and the re-encryption (spec: 3–60, default 8).</summary>
    public int GraceSeconds { get; set; } = 8;

    /// <summary>Also the 15-minute fallback for a session whose probes cannot decide.</summary>
    public int IdleFallbackMinutes { get; set; } = 15;

    /// <summary>Remove the container through the Recycle Bin after a verified unlock.</summary>
    public bool RecycleContainerAfterUnlock { get; set; }

    public bool AssociationsRegistered { get; set; }

    /// <summary>Non-secret marker: a Rescuer was created, so later sessions ask for the password and key.</summary>
    public bool RescuerConfigured { get; set; }

    public static string DefaultPath => ProtectionPaths.SettingsFile;

    public static ProtectionSettings Load() => LoadFrom(DefaultPath);

    public static ProtectionSettings LoadFrom(string path)
    {
        var settings = new ProtectionSettings();
        try
        {
            if (!File.Exists(path))
                return settings;
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return settings;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "AutoLockOnClose" when property.Value.ValueKind is System.Text.Json.JsonValueKind.True
                        or System.Text.Json.JsonValueKind.False:
                        settings.AutoLockOnClose = property.Value.GetBoolean();
                        break;
                    case "GraceSeconds" when property.Value.TryGetInt32(out var grace):
                        settings.GraceSeconds = Math.Clamp(grace, AutoLockRules.MinimumGraceSeconds,
                            AutoLockRules.MaximumGraceSeconds);
                        break;
                    case "IdleFallbackMinutes" when property.Value.TryGetInt32(out var idle):
                        settings.IdleFallbackMinutes = Math.Clamp(idle, 1, 120);
                        break;
                    case "RecycleContainerAfterUnlock" when property.Value.ValueKind is System.Text.Json.JsonValueKind.True
                        or System.Text.Json.JsonValueKind.False:
                        settings.RecycleContainerAfterUnlock = property.Value.GetBoolean();
                        break;
                    case "AssociationsRegistered" when property.Value.ValueKind is System.Text.Json.JsonValueKind.True
                        or System.Text.Json.JsonValueKind.False:
                        settings.AssociationsRegistered = property.Value.GetBoolean();
                        break;
                    case "RescuerConfigured" when property.Value.ValueKind is System.Text.Json.JsonValueKind.True
                        or System.Text.Json.JsonValueKind.False:
                        settings.RescuerConfigured = property.Value.GetBoolean();
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or System.Text.Json.JsonException or ArgumentException)
        {
            return new ProtectionSettings();
        }
        return settings;
    }

    public void Save() => SaveTo(DefaultPath);

    public void SaveTo(string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions
               {
                   Indented = true,
               }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("AutoLockOnClose", AutoLockOnClose);
            writer.WriteNumber("GraceSeconds", Math.Clamp(GraceSeconds, AutoLockRules.MinimumGraceSeconds,
                AutoLockRules.MaximumGraceSeconds));
            writer.WriteNumber("IdleFallbackMinutes", Math.Clamp(IdleFallbackMinutes, 1, 120));
            writer.WriteBoolean("RecycleContainerAfterUnlock", RecycleContainerAfterUnlock);
            writer.WriteBoolean("AssociationsRegistered", AssociationsRegistered);
            writer.WriteBoolean("RescuerConfigured", RescuerConfigured);
            writer.WriteEndObject();
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Creates a random key without ever deriving it from a password.</summary>
    public static string NewRescuerKey() => RescuerKey.Generate();
}
