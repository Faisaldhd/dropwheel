using System.Windows;
using FileAction.Core.Updates;

namespace FileAction.App.Updates;

/// <summary>Result of downloading and applying one verified update.</summary>
public sealed record UpdateApplyResult(bool Success, string? Error);

/// <summary>
/// Stub of the installer/portable applier; the update-apply agent replaces this file. The UI calls
/// <see cref="DownloadAndApplyAsync"/> after the user clicks Update, and <see cref="TryRun"/> handles the
/// command-line entry points (<c>--apply-update</c>, <c>--updated</c>) from <c>Program.Main</c>.
/// </summary>
internal static class UpdateApplier
{
    /// <summary>Handles update-related command lines before the single-instance check. False = keep starting normally.</summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        return false;
    }

    /// <summary>Downloads, verifies, applies the update and exits the app. Never runs an unverified file.</summary>
    public static Task<UpdateApplyResult> DownloadAndApplyAsync(
        IUpdateService service, UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.FromResult(new UpdateApplyResult(false, null));

    /// <summary>Shows "DropWheel updated to x.y.z" with a What's new link after a successful restart.</summary>
    public static void AnnounceUpdated(string version)
    {
    }
}
