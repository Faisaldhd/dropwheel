using FileAction.App;
using FileAction.App.Shell;

namespace FileAction.Tests;

public class PolicyParsingTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("0", false)]
    [InlineData("garbage", false)]
    public void ParsePolicyValue_matches_the_documented_values(object? value, bool expected) =>
        Assert.Equal(expected, Policy.ParsePolicyValue(value));
}

public class CompanySafeDefaultsTests
{
    [Fact]
    public void New_settings_disable_hooks_and_downloads()
    {
        var settings = new AppSettings();
        Assert.False(settings.WatchGestures);
        Assert.False(settings.AutoDownloadTools);
    }

    [Fact]
    public void Hooks_and_downloads_need_an_explicit_opt_in()
    {
        // No registry access: with default settings both checks short-circuit before the policy is read.
        var settings = new AppSettings();
        Assert.False(Policy.HooksAllowed(settings, Array.Empty<string>()));
        Assert.False(Policy.DownloadsAllowed(settings));
    }
}
