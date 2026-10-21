using FileAction.App;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The website screenshot mode is a process entry point, so only its argument recognition is unit
/// tested here: actually rendering needs a desktop session and is covered by the CI screenshots job.
/// </summary>
public class ScreenshotModeTests
{
    [Theory]
    [InlineData(new[] { "--screenshots", @"C:\shots" }, true, @"C:\shots")]
    [InlineData(new[] { "--screenshots" }, true, "screenshots")]
    [InlineData(new[] { "--screenshots", "--background" }, true, "screenshots")]
    [InlineData(new[] { "--screenshots", "--self-test", "out" }, true, "screenshots")]
    [InlineData(new[] { "--self-test", "out" }, false, "screenshots")]
    [InlineData(new[] { "--diagnose" }, false, "screenshots")]
    [InlineData(new string[0], false, "screenshots")]
    public void Screenshots_flag_is_recognized_as_the_first_argument(string[] args, bool recognized, string expected)
    {
        var actual = SelfTest.TryScreenshots(args, out var directory);

        Assert.Equal(recognized, actual);
        Assert.Equal(expected, directory);
    }

    [Fact]
    public void The_screenshots_flag_is_not_treated_as_a_path()
    {
        Assert.True(SelfTest.TryScreenshots(["--screenshots", "--settings"], out var directory));
        Assert.Equal("screenshots", directory);
    }

    [Fact]
    public void A_later_screenshots_flag_does_not_start_the_mode()
    {
        Assert.False(SelfTest.TryScreenshots(["--background", "--screenshots", "out"], out _));
    }
}
