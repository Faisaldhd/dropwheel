using FileAction.App;
using FileAction.App.Jobs;
using FileAction.App.UI.Tools;
using FileAction.App.Wheel;
using FileAction.Core;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// F-03 regression: choosing an interactive tool (one that opens a window instead of starting a job)
/// must still remember it as the last operation, so the wheel's repeat-last view can open it again.
/// </summary>
public sealed class WheelRepeatTests
{
    [Fact]
    public void Choosing_an_interactive_tool_remembers_it_for_repeat_last()
    {
        var settings = new AppSettings();
        var jobs = new JobRunner(settings, new ToolLocator());
        var operation = new FakeInteractiveTool("tools.fake-interactive");

        WheelController.Dispatch(
            operation,
            [new FileItem(Path.Combine(Path.GetTempPath(), "repeat-source.png"))],
            null,
            jobs,
            saveSettings: false);

        Assert.Equal(operation.Id, settings.LastOperationId);
        Assert.Null(settings.LastParameters);
        Assert.Equal(1, operation.Opens);
    }

    private sealed class FakeInteractiveTool(string id) : Operation(id, WheelKind.Tools, id, "x"), IInteractiveTool
    {
        public int Opens { get; private set; }

        public override bool AppliesTo(IReadOnlyList<FileItem> items) => true;

        public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
            Task.FromResult(OperationResult.Of([]));

        public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) => Opens++;
    }
}
