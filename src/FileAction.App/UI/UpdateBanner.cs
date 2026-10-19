using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FileAction.Core.Updates;

namespace FileAction.App.UI;

/// <summary>
/// Stub of the violet "Update available" banner; the update-ui agent replaces this file. It lives inside the
/// Settings Updates tab, so the Settings window can embed it without owning the update UI.
/// </summary>
internal sealed class UpdateBanner : Border
{
    /// <summary>Builds the banner for a settings page. A null service means "updates unavailable in this build".</summary>
    public static UpdateBanner Create(IUpdateService? service, AppSettings settings, Action? changed = null) => new();

    /// <summary>Re-renders the banner after a state change or a live language switch.</summary>
    public void Refresh()
    {
    }

    /// <summary>Relocalizes every open banner.</summary>
    public static void RelocalizeAll()
    {
    }
}
