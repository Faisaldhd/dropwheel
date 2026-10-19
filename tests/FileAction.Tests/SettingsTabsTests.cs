using System.Reflection;
using FileAction.App.UI;
using FileAction.Core.Updates;

namespace FileAction.Tests;

/// <summary>
/// 2.2.0 settings-window acceptance: six tabs, one toggle per new switch, no Czech leftovers, and the
/// update service exposed for the Updates tab. The source is read from the repository (not from the built
/// assembly) so the assertions also run before the WPF window is ever instantiated.
/// </summary>
public class SettingsTabsTests
{
    private static readonly string[] TabKeys =
    [
        "settings.tab.general",
        "settings.tab.wheels",
        "settings.tab.quality",
        "settings.tab.integration",
        "settings.tab.updates",
        "settings.tab.about",
    ];

    /// <summary>
    /// Toggle card headers. Each prefix is matched as its quoted <c>.title</c> key (or the quoted key itself
    /// for a title-less card) so the detail/policy strings do not count as a second toggle.
    /// </summary>
    private static readonly string[] ToggleKeys =
    [
        "settings.company.autostart",
        "settings.company.contextMenu",
        "settings.company.sendTo",
        "settings.company.downloads",
        "settings.general.watch",
        "settings.gesture.pause",
        "settings.results.reveal",
    ];

    [Fact]
    public void Settings_window_uses_the_six_tab_keys()
    {
        var source = SettingsWindowSource();

        foreach (var key in TabKeys)
        {
            Assert.True(
                CountOccurrences(source, $"\"{key}\"") >= 1,
                $"SettingsWindow must use the tab key '{key}'.");
        }
    }

    [Fact]
    public void Each_toggle_bearing_key_is_rendered_exactly_once()
    {
        var source = SettingsWindowSource();

        foreach (var key in ToggleKeys)
        {
            var headers =
                CountOccurrences(source, $"\"{key}.title\"") + CountOccurrences(source, $"\"{key}\"");
            Assert.True(
                headers == 1,
                $"Expected exactly one card/toggle header for '{key}', found {headers} in SettingsWindow.cs.");
        }
    }

    [Fact]
    public void Settings_window_contains_no_czech_text()
    {
        var source = SettingsWindowSource();

        Assert.False(
            source.Contains("Čeština", StringComparison.Ordinal),
            "SettingsWindow.cs still contains the Czech language label 'Čeština'.");
        Assert.False(
            source.Contains("Czech", StringComparison.Ordinal),
            "SettingsWindow.cs still contains 'Czech'; use the keyed English/Arabic values only.");
        Assert.False(
            source.Contains("cs-CZ", StringComparison.Ordinal),
            "SettingsWindow.cs still contains the Czech culture 'cs-CZ'.");
    }

    [Fact]
    public void Settings_window_exposes_the_update_service_for_the_updates_tab()
    {
        var property = typeof(SettingsWindow).GetProperty(
            "Updates", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(property);
        Assert.Equal(typeof(IUpdateService), property!.PropertyType);
        Assert.True(
            property.GetMethod is { IsPublic: true, IsStatic: true },
            "SettingsWindow.Updates must have a public static getter.");
        Assert.True(
            property.SetMethod is { IsPublic: true, IsStatic: true },
            "SettingsWindow.Updates must have a public static setter.");
    }

    private static string SettingsWindowSource()
    {
        var path = Path.Combine(RepoRoot(), "src", "FileAction.App", "UI", "SettingsWindow.cs");
        Assert.True(File.Exists(path), $"SettingsWindow.cs is missing at {path}.");
        return File.ReadAllText(path);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }
}
