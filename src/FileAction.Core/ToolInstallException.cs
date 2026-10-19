namespace FileAction.Core;

/// <summary>
/// R-16: downloading a helper tool failed. The old code reported every <see cref="UnauthorizedAccessException"/>
/// as "cannot write to the target folder", which sent the user to the wrong setting: the failure is in the
/// tools folder, and the message must name the tool, the exact path and the OS error.
/// </summary>
public sealed class ToolInstallException : Exception
{
    public ToolInstallException(ExternalTool tool, string path, Exception inner)
        : base(inner.Message, inner)
    {
        Tool = tool;
        Path = path;
    }

    public ExternalTool Tool { get; }

    /// <summary>The folder DropWheel tried to install the tool into.</summary>
    public string Path { get; }

    /// <summary>A complete, user-facing explanation with the real cause.</summary>
    public string Describe() => Loc.T("tool.installFailed", ToolLocator.DisplayName(Tool), Path, Reason());

    /// <summary>The OS error in plain words ("access denied" for a blocked folder).</summary>
    public string Reason() => InnerException switch
    {
        UnauthorizedAccessException => Loc.T("tool.installAccessDenied"),
        IOException io => io.Message,
        _ => InnerException?.Message ?? Message,
    };
}
