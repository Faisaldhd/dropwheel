namespace FileAction.App.UI.Tools;

/// <summary>
/// Every tool window size in one place, so the defaults are easy to tune without touching the tools.
/// Sizes are DIPs; the window clamps them to the monitor work area (95% wide, 90% high).
/// </summary>
internal static class ToolSizes
{
    /// <summary>Used by any tool that does not declare its own size.</summary>
    public static readonly (double Width, double Height) Default = (500, 745);

    public static readonly (double Width, double Height) Crop = (520, 760);

    public static (double Width, double Height) For(string toolId) => toolId switch
    {
        "tools.crop" => Crop,
        _ => Default,
    };
}
