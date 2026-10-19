using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// The folder remembered during a drag. It may only ever hold a usable folder seen during the current
/// drag, so a folder crossed before the drag started can never receive the results.
/// </summary>
public class DropFolderMemoryTests
{
    private static void Observe(DropFolderMemory memory, string? folder, bool exists = true, bool writable = true) =>
        memory.Observe(folder, _ => exists, _ => writable);

    [Fact]
    public void Remembers_the_last_usable_folder_of_the_drag()
    {
        var memory = new DropFolderMemory();
        memory.BeginDrag();

        Observe(memory, @"C:\First");
        Observe(memory, @"D:\Second");

        Assert.Equal(@"D:\Second", memory.Remembered);
    }

    [Fact]
    public void Forgets_everything_when_a_new_drag_starts()
    {
        var memory = new DropFolderMemory();
        memory.BeginDrag();
        Observe(memory, @"D:\Old");

        memory.BeginDrag();

        Assert.Null(memory.Remembered);
    }

    [Fact]
    public void Keeps_the_previous_folder_when_the_next_one_is_missing_or_read_only()
    {
        var memory = new DropFolderMemory();
        memory.BeginDrag();
        Observe(memory, @"D:\Good");

        Observe(memory, @"D:\Gone", exists: false);
        Observe(memory, @"D:\ReadOnly", writable: false);

        Assert.Equal(@"D:\Good", memory.Remembered);
    }

    [Fact]
    public void Ignores_empty_observations()
    {
        var memory = new DropFolderMemory();
        memory.BeginDrag();
        Observe(memory, @"D:\Good");

        Observe(memory, null);
        Observe(memory, "   ");

        Assert.Equal(@"D:\Good", memory.Remembered);
    }

    [Fact]
    public void Has_nothing_before_the_first_usable_folder()
    {
        var memory = new DropFolderMemory();
        memory.BeginDrag();

        Observe(memory, @"D:\Missing", exists: false);

        Assert.Null(memory.Remembered);
    }
}
