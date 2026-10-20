using FileAction.App.UI.Tools;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// RC-01: a tool window must not close while its Apply action is still writing. Closing mid-Apply would
/// orphan the only owner of the write and can leave a partial/corrupt file at the final output name.
/// </summary>
public class ToolCloseDuringApplyTests
{
    [Fact]
    public void Apply_state_blocks_close_until_end_and_rejects_reentry()
    {
        var state = new ToolApplyState();
        Assert.False(state.BlocksClose);

        Assert.True(state.Begin());
        Assert.True(state.BlocksClose);
        Assert.False(state.Begin()); // no second writer while one is running

        state.End();
        Assert.False(state.BlocksClose);
        Assert.True(state.Begin());
    }

    [Fact]
    public void ToolWindow_defers_closing_while_apply_is_running_and_closes_after_it_ends()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "ToolWindow.cs"));

        var closing = Between(source, "protected override void OnClosing", "protected override void OnClosed");
        Assert.Contains("_applyState.BlocksClose", closing, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true", closing, StringComparison.Ordinal);

        var apply = Between(source, "private async Task ApplyAsync()", "private readonly record struct WorkArea");
        Assert.Contains("_applyState.Begin()", apply, StringComparison.Ordinal);
        Assert.Contains("_applyState.End()", apply, StringComparison.Ordinal);
        Assert.True(
            apply.LastIndexOf("_applyState.End()", StringComparison.Ordinal) <
            apply.LastIndexOf("Close();", StringComparison.Ordinal),
            "ApplyAsync must end the apply state before closing the window");
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"missing source marker '{start}'");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"missing source marker '{end}' after '{start}'");
        return text[from..to];
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
