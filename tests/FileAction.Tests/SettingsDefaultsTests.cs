using System.Text.Json;
using FileAction.App;

namespace FileAction.Tests;

/// <summary>
/// 2.2.0 settings defaults and the on-disk contract: update checks are on by default, the theme follows
/// Windows, gestures/helper downloads stay opt-in, and the JSON round-trip keeps the new members. The legacy
/// language migration is covered too because old settings files must never lose other preferences.
/// </summary>
public class SettingsDefaultsTests
{
    [Fact]
    public void New_settings_use_the_2_2_0_defaults()
    {
        var settings = new AppSettings();

        Assert.True(settings.CheckForUpdates);
        Assert.Equal(ThemeMode.System, settings.Theme);
        Assert.False(settings.WatchGestures);
        Assert.False(settings.AutoDownloadTools);
    }

    [Fact]
    public void Json_round_trip_preserves_theme_and_update_choice()
    {
        var settings = new AppSettings
        {
            Theme = ThemeMode.Dark,
            CheckForUpdates = false,
            Language = UiLanguage.Arabic,
        };

        var json = JsonSerializer.Serialize(settings, SettingsJson.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.Equal(ThemeMode.Dark, restored!.Theme);
        Assert.False(restored.CheckForUpdates);
        Assert.Equal(UiLanguage.Arabic, restored.Language);
    }

    [Fact]
    public void Json_round_trip_keeps_the_defaults()
    {        var json = JsonSerializer.Serialize(new AppSettings(), SettingsJson.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.True(restored!.CheckForUpdates);
        Assert.Equal(ThemeMode.System, restored.Theme);
        Assert.False(restored.WatchGestures);
        Assert.False(restored.AutoDownloadTools);
    }

    [Theory]
    [InlineData("Czech", "System")]
    [InlineData("English", "English")]
    [InlineData("Arabic", "Arabic")]
    [InlineData("cs-CZ", "System")]
    [InlineData("", "System")]
    [InlineData("klingon", "System")]
    public void Legacy_language_values_migrate(string stored, string expected)
    {
        var source = "{\"Language\":\"" + stored + "\",\"CheckForUpdates\":false}";

        var migrated = AppSettings.MigrateLanguage(source);

        using var document = JsonDocument.Parse(migrated);
        Assert.Equal(expected, document.RootElement.GetProperty("Language").GetString());
        Assert.False(document.RootElement.GetProperty("CheckForUpdates").GetBoolean());
    }

    [Fact]
    public void Legacy_null_language_becomes_System()
    {
        var migrated = AppSettings.MigrateLanguage("{\"Language\":null}");

        using var document = JsonDocument.Parse(migrated);
        Assert.Equal("System", document.RootElement.GetProperty("Language").GetString());
    }

    [Fact]
    public void Migration_leaves_settings_without_a_language_alone()
    {
        const string source = "{\"CheckForUpdates\":true}";

        Assert.Equal(source, AppSettings.MigrateLanguage(source));
    }

    [Fact]
    public void Animations_are_on_by_default_and_survive_a_round_trip()
    {
        Assert.True(new AppSettings().Animations, "2.4.0 motion must be on by default");

        var json = JsonSerializer.Serialize(new AppSettings { Animations = false }, SettingsJson.Default.AppSettings);
        var restored = JsonSerializer.Deserialize(json, SettingsJson.Default.AppSettings);

        Assert.NotNull(restored);
        Assert.False(restored!.Animations);
    }
}
