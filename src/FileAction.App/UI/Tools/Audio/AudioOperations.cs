using FileAction.Core;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Audio;

/// <summary>
/// The three audio tools as operations on the Tools wheel. Each one opens its floating window instead of
/// starting a job: the user picks channels/ranges/frames before anything is written.
/// </summary>
internal static class AudioOperations
{
    /// <summary>Every audio tool operation, for the wheel's operation list.</summary>
    public static IReadOnlyList<Operation> Build() =>
    [
        new AudioChannelsOperation(),
        new AudioBleepOperation(),
        new AudioVisualizerOperation(),
    ];
}

/// <summary>"Channels" on the Tools wheel.</summary>
internal sealed class AudioChannelsOperation : Operation, IInteractiveTool
{
    public AudioChannelsOperation()
        : base("tools.audiochannels", WheelKind.Tools,
            T("tool.audiochannels.label"), Glyphs.Equalizer, T("tool.audiochannels.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AudioChannelsTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(AudioChannelsTool.Instance, items, targets).Show();
}

/// <summary>"Bleep" on the Tools wheel.</summary>
internal sealed class AudioBleepOperation : Operation, IInteractiveTool
{
    public AudioBleepOperation()
        : base("tools.audiobleep", WheelKind.Tools,
            T("tool.audiobleep.label"), Glyphs.Mute, T("tool.audiobleep.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AudioBleepTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(AudioBleepTool.Instance, items, targets).Show();
}

/// <summary>"Visualizer" on the Tools wheel.</summary>
internal sealed class AudioVisualizerOperation : Operation, IInteractiveTool
{
    public AudioVisualizerOperation()
        : base("tools.audiovisualizer", WheelKind.Tools,
            T("tool.audiovisualizer.label"), Glyphs.Play, T("tool.audiovisualizer.subtitle"))
    {
    }

    public override bool AppliesTo(IReadOnlyList<FileItem> items) => AudioVisualizerTool.Instance.Applies(items);

    /// <summary>Never used: the wheel opens the tool window before a job is created.</summary>
    public override Task<OperationResult> ExecuteAsync(IReadOnlyList<FileItem> items, OperationContext ctx) =>
        Task.FromResult(OperationResult.Of(Array.Empty<string>()));

    public void Open(IReadOnlyList<FileItem> items, DropTargets? targets) =>
        new ToolWindow(AudioVisualizerTool.Instance, items, targets).Show();
}
