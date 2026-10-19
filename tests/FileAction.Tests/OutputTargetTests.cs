using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The folder a drop writes into. Pure logic: existence and writability are injected, so these cases
/// pin the rules (use it, keep it, refuse it) without a UI, a shell window or a real disk.
/// </summary>
public class OutputTargetTests
{
    private static OutputTargetResult Resolve(string source, string? candidate, bool exists = true, bool writable = true) =>
        OutputTarget.Resolve(source, candidate, _ => exists, _ => writable);

    [Fact]
    public void Uses_a_writable_folder_that_differs_from_the_source_folder()
    {
        var result = Resolve(@"C:\work\report.docx", @"D:\Finished");

        Assert.Equal(OutputTargetStatus.UseDetectedFolder, result.Status);
        Assert.True(result.UsesDetectedFolder);
        Assert.Equal(@"D:\Finished", result.Directory);
    }

    [Fact]
    public void Keeps_next_to_the_file_when_the_folder_is_the_source_folder()
    {
        // Same folder, different casing and a trailing separator: still the source folder.
        var result = Resolve(@"C:\work\report.docx", @"c:\WORK\");

        Assert.Equal(OutputTargetStatus.SameAsSource, result.Status);
        Assert.Null(result.Directory);
    }

    [Fact]
    public void Falls_back_when_the_folder_no_longer_exists()
    {
        var result = Resolve(@"C:\work\report.docx", @"D:\Gone", exists: false);

        Assert.Equal(OutputTargetStatus.Missing, result.Status);
        Assert.Null(result.Directory);
    }

    [Fact]
    public void Falls_back_when_the_folder_refuses_a_write()
    {
        var result = Resolve(@"C:\work\report.docx", @"D:\ReadOnly", writable: false);

        Assert.Equal(OutputTargetStatus.NotWritable, result.Status);
        Assert.Null(result.Directory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_when_nothing_was_detected(string? candidate)
    {
        var result = Resolve(@"C:\work\report.docx", candidate);

        Assert.Equal(OutputTargetStatus.NoFolderDetected, result.Status);
        Assert.Null(result.Directory);
    }

    [Fact]
    public void Drops_a_trailing_separator_but_keeps_a_drive_root()
    {
        Assert.Equal(@"D:\Finished", Resolve(@"C:\work\a.txt", @"D:\Finished\").Directory);
        Assert.Equal(@"D:\", Resolve(@"C:\work\a.txt", @"D:\").Directory);
    }

    [Fact]
    public void Never_writes_into_the_source_folder_even_when_it_is_detected_again()
    {
        // A file on the drive root: its folder is the root itself.
        var result = Resolve(@"C:\a.txt", @"C:\");

        Assert.Equal(OutputTargetStatus.SameAsSource, result.Status);
        Assert.Null(result.Directory);
    }
}
