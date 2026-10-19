using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Which shell window belongs to the tab under the cursor. These are the rules that matter on Windows 11,
/// where every tab of one Explorer window is listed separately but shares the same top-level HWND.
/// </summary>
public class ShellFolderSelectionTests
{
    private static readonly ShellFolderCandidate A = new(@"C:\A", 0x11);
    private static readonly ShellFolderCandidate B = new(@"D:\B", 0x22);
    private static readonly ShellFolderCandidate C = new(@"E:\C", 0x33);

    [Fact]
    public void Picks_the_tab_window_that_is_under_the_cursor()
    {
        var pick = ShellFolderSelection.Pick([A, B, C], tabWindow: 0x22, rootWindow: 0x99);

        Assert.Equal(1, pick);
    }

    [Fact]
    public void Prefers_the_tab_over_a_window_handle_that_matches_the_root()
    {
        var pick = ShellFolderSelection.Pick([A, B], tabWindow: 0x22, rootWindow: 0x11);

        Assert.Equal(1, pick);
    }

    [Fact]
    public void Uses_the_root_window_when_explorer_has_no_tabs()
    {
        var pick = ShellFolderSelection.Pick([A, B], tabWindow: 0, rootWindow: 0x22);

        Assert.Equal(1, pick);
    }

    [Fact]
    public void Uses_the_only_candidate_when_no_handle_matches()
    {
        var pick = ShellFolderSelection.Pick([A], tabWindow: 0x77, rootWindow: 0x88);

        Assert.Equal(0, pick);
    }

    [Fact]
    public void Refuses_to_guess_between_several_unmatched_windows()
    {
        var pick = ShellFolderSelection.Pick([A, B], tabWindow: 0x77, rootWindow: 0x88);

        Assert.Equal(ShellFolderSelection.None, pick);
    }

    [Fact]
    public void Returns_none_when_explorer_lists_nothing()
    {
        var pick = ShellFolderSelection.Pick([], tabWindow: 0x11, rootWindow: 0x11);

        Assert.Equal(ShellFolderSelection.None, pick);
    }
}
