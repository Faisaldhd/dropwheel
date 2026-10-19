using FileAction.App;
using FileAction.App.Shell;

namespace FileAction.Tests;

/// <summary>
/// R-20 acceptance: a fresh install is company-safe. Global input hooks and helper-tool downloads are
/// opt-in, and the command-line <c>--no-hooks</c> switch wins even when the user enabled gestures.
/// </summary>
public class CompanyDefaultsTests
{
    [Fact]
    public void Fresh_settings_disable_hooks_and_downloads()
    {
        var settings = new AppSettings();

        Assert.False(settings.WatchGestures);
        Assert.False(settings.AutoDownloadTools);
    }

    [Fact]
    public void Default_settings_do_not_allow_hooks_or_downloads()
    {
        // With WatchGestures false both checks short-circuit before the machine policy is read.
        var settings = new AppSettings();

        Assert.False(Policy.HooksAllowed(settings, Array.Empty<string>()));
        Assert.False(Policy.DownloadsAllowed(settings));
    }

    [Fact]
    public void No_hooks_argument_beats_the_gesture_setting()
    {
        var settings = new AppSettings { WatchGestures = true };

        Assert.False(Policy.HooksAllowed(settings, ["--no-hooks"]));
    }
}
