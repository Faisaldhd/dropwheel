using FileAction.App;
using FileAction.App.Shell;

namespace FileAction.Tests;

/// <summary>
/// R-20 follow-up: the policy value parser and the two machine-policy gates. Only <c>ParsePolicyValue</c> is
/// exercised directly; the gates are checked with defaults that short-circuit before the registry is read, so
/// no HKLM write (or read on the happy path) is needed.
/// </summary>
public class PolicyValueTests2
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, true)]
    [InlineData(-1, true)]
    [InlineData(1L, true)]
    [InlineData(0L, false)]
    [InlineData("1", true)]
    [InlineData("1 ", true)]
    [InlineData(" true ", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("Yes", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("no", false)]
    [InlineData("garbage", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void ParsePolicyValue_accepts_only_documented_truthy_values(object? value, bool expected) =>
        Assert.Equal(expected, Policy.ParsePolicyValue(value));
}

public class PolicyGateTests2
{
    [Fact]
    public void Default_settings_keep_hooks_and_downloads_off()
    {
        // WatchGestures and AutoDownloadTools are false, so both checks short-circuit before reading HKLM.
        var settings = new AppSettings();

        Assert.False(Policy.HooksAllowed(settings, Array.Empty<string>()));
        Assert.False(Policy.HooksAllowed(settings, ["--no-hooks"]));
        Assert.False(Policy.DownloadsAllowed(settings));
    }

    [Fact]
    public void No_hooks_argument_never_enables_hooks_by_itself()
    {
        var settings = new AppSettings { WatchGestures = false };

        Assert.False(Policy.HooksAllowed(settings, ["--no-hooks"]));
    }
}
