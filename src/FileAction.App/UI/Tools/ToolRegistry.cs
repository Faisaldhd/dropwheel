using FileAction.Core;

namespace FileAction.App.UI.Tools;

/// <summary>
/// The tools the app knows about. Each tool registers itself from its own file (a module initializer),
/// so later tools can be added without editing a shared list.
/// </summary>
internal static class ToolRegistry
{
    private static readonly List<IToolWindowSpec> Registered = [];

    public static IReadOnlyList<IToolWindowSpec> All => Registered;

    public static void Register(IToolWindowSpec spec)
    {
        if (Registered.All(t => !string.Equals(t.Id, spec.Id, StringComparison.Ordinal)))
            Registered.Add(spec);
    }

    /// <summary>The tool that can open for these files, or null.</summary>
    public static IToolWindowSpec? For(IReadOnlyList<FileItem> items) =>
        Registered.FirstOrDefault(t => t.Applies(items));
}
