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

    /// <summary>
    /// The registered tool with this exact id, when it accepts these files. Prefer this over
    /// <see cref="For(IReadOnlyList{FileItem})"/>: several tools accept the same media, so a generic
    /// first-match lookup can open the wrong window.
    /// </summary>
    public static IToolWindowSpec? For(string id, IReadOnlyList<FileItem> items) =>
        Registered.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal) && t.Applies(items));

    /// <summary>First registered tool that accepts these files. Only for callers that have no tool id.</summary>
    public static IToolWindowSpec? For(IReadOnlyList<FileItem> items) =>
        Registered.FirstOrDefault(t => t.Applies(items));
}
