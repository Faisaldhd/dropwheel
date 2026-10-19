using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileAction.App.Diagnostics;
using FileAction.Core.Updates;

namespace FileAction.App.Updates;

/// <summary>Result of downloading and applying one verified update.</summary>
public sealed record UpdateApplyResult(bool Success, string? Error);

/// <summary>
/// Applies a verified update. <see cref="TryRun"/> is the portable helper entry point called from
/// <c>Program.Main</c> before the single-instance check; <see cref="DownloadAndApplyAsync"/> is the UI entry
/// point; <see cref="AnnounceUpdated"/> is called by the app on an <c>--updated</c> start.
/// </summary>
internal static class UpdateApplier
{
    private const string ApplyUpdateSwitch = "--apply-update";

    /// <summary>
    /// Handles the update helper command line before the single-instance check. Returns false for every
    /// normal start, including <c>--updated</c>, so only the small <c>--apply-update</c> helper path is
    /// consumed here. The helper is a windowless process: its exit code is the result.
    /// </summary>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || !string.Equals(args[0], ApplyUpdateSwitch, StringComparison.Ordinal))
            return false;

        try
        {
            exitCode = PortableUpdater.Run(args);
        }
        catch (Exception ex)
        {
            // The helper has no window: a failure must not become a silent exit code 0 or a WER dialog.
            AppLog.Error("update apply failed", ex);
            exitCode = 1;
        }

        return true;
    }

    /// <summary>
    /// Downloads, verifies and applies the update, then asks the app to exit. The only trust boundary is
    /// <see cref="IUpdateService.DownloadAsync"/>: it verifies the digest and the signature and returns a path
    /// only for a file that passed both, so nothing here ever runs a file from another source.
    /// </summary>
    public static async Task<UpdateApplyResult> DownloadAndApplyAsync(
        IUpdateService service, UpdateInfo info, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(info);

        var verified = await service.DownloadAsync(info, progress, cancellationToken).ConfigureAwait(false);
        if (verified is null)
            return new UpdateApplyResult(false, service.LastError ?? "Update could not be verified");
        if (!File.Exists(verified))
            return new UpdateApplyResult(false, "The downloaded update file is missing.");

        var version = info.Version.ToString(3);
        var error = service.IsPortable
            ? PortableUpdater.StartHelper(verified, version)
            : SetupUpdater.Start(verified);
        if (error is not null)
            return new UpdateApplyResult(false, error);

        RequestExit();
        return new UpdateApplyResult(true, null);
    }

    /// <summary>Shows "DropWheel updated to x.y.z" with a What's new link after a successful restart.</summary>
    public static void AnnounceUpdated(string version)
    {
        try
        {
            var app = Application.Current;
            if (app is not null && !app.Dispatcher.CheckAccess())
            {
                app.Dispatcher.BeginInvoke(new Action(() => AnnounceUpdated(version)));
                return;
            }

            UpdatedWindow.Show(version);
        }
        catch (Exception ex)
        {
            // A missed "updated" message must never keep the app from starting.
            AppLog.Error("could not show the update-finished message", ex);
        }
    }

    /// <summary>
    /// Shuts the app down only after the caller has returned its result, so the UI can show a failure or
    /// finish its own state change before the dispatcher stops.
    /// </summary>
    private static void RequestExit()
    {
        var app = Application.Current;
        if (app is null)
            return;

        try
        {
            app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    app.Shutdown();
                }
                catch (InvalidOperationException)
                {
                    // Already shutting down.
                }
            }));
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is already shutting down; the app is exiting either way.
        }
    }
}
