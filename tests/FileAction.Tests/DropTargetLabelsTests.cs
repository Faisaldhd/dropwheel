using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The promise the wheel makes before the drop. It must agree with the rules used when the operation
/// starts: a usable folder that differs from the source, the remembered folder, a refused folder, or
/// "next to the file".
/// </summary>
public class DropTargetLabelsTests
{
    private static DropTargetLabel Choose(
        string? source,
        string? cursor,
        string? remembered = null,
        bool exists = true,
        bool writable = true) =>
        DropTargetLabels.Choose(source, cursor, remembered, _ => exists, _ => writable);

    [Fact]
    public void Names_the_folder_under_the_cursor()
    {
        var label = Choose(@"C:\Work", @"D:\Finished");

        Assert.Equal(DropTargetLabelKind.Folder, label.Kind);
        Assert.Equal(@"D:\Finished", label.Folder);
        Assert.Equal("Finished", label.FolderName);
    }

    [Fact]
    public void Says_next_to_the_file_when_the_cursor_folder_is_the_source()
    {
        var label = Choose(@"C:\Work", @"c:\work\");

        Assert.Equal(DropTargetLabelKind.NextToFile, label.Kind);
        Assert.Null(label.Folder);
    }

    [Fact]
    public void Falls_back_to_the_remembered_folder()
    {
        var label = Choose(@"C:\Work", cursor: null, remembered: @"D:\Crossed");

        Assert.Equal(DropTargetLabelKind.Folder, label.Kind);
        Assert.Equal(@"D:\Crossed", label.Folder);
        Assert.Equal("Crossed", label.FolderName);
    }

    [Fact]
    public void Prefers_the_cursor_folder_over_the_remembered_one()
    {
        var label = Choose(@"C:\Work", @"D:\Now", @"D:\Earlier");

        Assert.Equal(@"D:\Now", label.Folder);
    }

    [Fact]
    public void Warns_when_the_folder_refuses_a_write()
    {
        var label = Choose(@"C:\Work", @"D:\ReadOnly", writable: false);

        Assert.Equal(DropTargetLabelKind.Blocked, label.Kind);
        Assert.Equal(@"D:\ReadOnly", label.Folder);
    }

    [Fact]
    public void Warns_when_the_remembered_folder_refuses_a_write()
    {
        var label = Choose(@"C:\Work", cursor: null, remembered: @"D:\ReadOnly", writable: false);

        Assert.Equal(DropTargetLabelKind.Blocked, label.Kind);
        Assert.Equal(@"D:\ReadOnly", label.Folder);
    }

    [Fact]
    public void Says_next_to_the_file_when_nothing_was_detected()
    {
        var label = Choose(@"C:\Work", cursor: null, remembered: null);

        Assert.Equal(DropTargetLabelKind.NextToFile, label.Kind);
        Assert.Null(label.FolderName);
    }

    [Fact]
    public void Names_a_usable_folder_even_when_the_source_is_unknown()
    {
        var label = Choose(source: null, cursor: @"D:\Target");

        Assert.Equal(DropTargetLabelKind.Folder, label.Kind);
        Assert.Equal("Target", label.FolderName);
    }
}
