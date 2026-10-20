using System.Text.Json;
using FileAction.App;
using FileAction.Core;

namespace FileAction.Tests;

/// <summary>
/// UP-01: a settings file that cannot be read must never be destroyed silently. The original is copied to
/// <c>settings.bad-&lt;timestamp&gt;.json</c> before the defaults are used, a first run stays silent, and a
/// failed save leaves no <c>.tmp</c> orphan behind.
/// </summary>
/// <summary>
/// The recovery tests read the process-wide `last load kept a backup` flag, so they must not run beside
/// another test that loads settings.
/// </summary>
[CollectionDefinition(nameof(SettingsRecovery), DisableParallelization = true)]
public sealed class SettingsRecovery;

[Collection(nameof(SettingsRecovery))]
public class SettingsRecoveryTests
{
    /// <summary>Corrupt JSON: the original bytes survive in a backup and the caller gets defaults.</summary>
    [Fact]
    public void A_corrupt_settings_file_is_kept_before_the_defaults_are_used()
    {
        var folder = NewFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            const string original = "{ this is not json";
            File.WriteAllText(path, original);

            var loaded = AppSettings.LoadFrom(path);

            Assert.Equal(new AppSettings().Hotkey, loaded.Hotkey);
            Assert.Equal(UiLanguage.System, loaded.Language);

            var backup = AppSettings.RecoveryBackupPath;
            Assert.NotNull(backup);
            Assert.Equal("settings.bad-", Path.GetFileName(backup)![.."settings.bad-".Length]);
            Assert.EndsWith(".json", backup, StringComparison.Ordinal);
            Assert.Equal(original, File.ReadAllText(backup));
            Assert.Equal(original, File.ReadAllText(path));

            // The next save writes a fresh, valid file and leaves the kept copy untouched.
            loaded.SaveTo(path);

            Assert.Equal(original, File.ReadAllText(backup));
            var rewritten = File.ReadAllText(path);
            Assert.DoesNotContain("not json", rewritten, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(rewritten);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);

            // And that file loads cleanly: no second backup, no notice.
            AppSettings.LoadFrom(path);
            Assert.Null(AppSettings.RecoveryBackupPath);
        }
        finally
        {
            Delete(folder);
        }
    }

    /// <summary>A second unreadable file in the same second must not overwrite the first backup.</summary>
    [Fact]
    public void A_second_unreadable_file_never_overwrites_the_first_backup()
    {
        var folder = NewFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            File.WriteAllText(path, "{ first failure");
            AppSettings.LoadFrom(path);
            var first = AppSettings.RecoveryBackupPath;
            Assert.NotNull(first);

            File.WriteAllText(path, "{ second failure");
            AppSettings.LoadFrom(path);
            var second = AppSettings.RecoveryBackupPath;
            Assert.NotNull(second);

            Assert.NotEqual(first, second);
            Assert.Equal("{ first failure", File.ReadAllText(first));
            Assert.Equal("{ second failure", File.ReadAllText(second!));
        }
        finally
        {
            Delete(folder);
        }
    }

    /// <summary>First run: a missing file is silent and never creates a backup.</summary>
    [Fact]
    public void A_missing_settings_file_stays_silent()
    {
        var folder = NewFolder();
        try
        {
            var loaded = AppSettings.LoadFrom(Path.Combine(folder, "settings.json"));

            Assert.Equal(new AppSettings().Hotkey, loaded.Hotkey);
            Assert.Null(AppSettings.RecoveryBackupPath);
            Assert.Null(AppSettings.ConsumeRecoveryBackup());
            Assert.Empty(Directory.GetFiles(folder, "settings.bad-*.json"));
        }
        finally
        {
            Delete(folder);
        }
    }

    /// <summary>A readable file keeps the user's choices and is never copied aside.</summary>
    [Fact]
    public void A_readable_settings_file_is_not_backed_up()
    {
        var folder = NewFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            new AppSettings { Hotkey = HotkeyPreset.CtrlAltW, Paused = true }.SaveTo(path);

            var loaded = AppSettings.LoadFrom(path);

            Assert.Equal(HotkeyPreset.CtrlAltW, loaded.Hotkey);
            Assert.True(loaded.Paused);
            Assert.Null(AppSettings.RecoveryBackupPath);
            Assert.Empty(Directory.GetFiles(folder, "settings.bad-*.json"));
        }
        finally
        {
            Delete(folder);
        }
    }

    /// <summary>A save that cannot land reports nothing to the UI and leaves no <c>.tmp</c> orphan.</summary>
    [Fact]
    public void A_failed_save_leaves_no_temporary_file_behind()
    {
        var folder = NewFolder();
        try
        {
            var path = Path.Combine(folder, "settings.json");
            // The destination is a directory, so the final move cannot succeed.
            Directory.CreateDirectory(path);

            var exception = Record.Exception(() => new AppSettings().SaveTo(path));

            Assert.Null(exception);
            Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
            Assert.True(Directory.Exists(path));
        }
        finally
        {
            Delete(folder);
        }
    }

    /// <summary>The one-time notice is localized in both shipped tables and names the kept copy.</summary>
    [Fact]
    public void The_recovery_notice_exists_in_both_languages()
    {
        foreach (var key in new[] { "settings.recovered.title", "settings.recovered.body" })
        {
            Assert.True(Loc.HasKey(key, Language.English), key);
            Assert.True(Loc.HasKey(key, Language.Arabic), key);
            Assert.NotEqual(Loc.Table(Language.English)[key], Loc.Table(Language.Arabic)[key]);
        }

        Assert.Contains("{0}", Loc.Table(Language.English)["settings.recovered.body"], StringComparison.Ordinal);
        Assert.Contains("{0}", Loc.Table(Language.Arabic)["settings.recovered.body"], StringComparison.Ordinal);
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "DropWheel-up01-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Delete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp cleanup is best effort.
        }
    }
}
