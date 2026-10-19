using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using FileAction.App.Diagnostics;
using FileAction.Core.Updates;

namespace FileAction.App.Updates;

/// <summary>
/// The portable-update helper. The running app starts a second copy of itself with
/// <c>--apply-update &lt;zip&gt; &lt;version&gt; [pid]</c> and exits. That process copies the app to
/// <c>%TEMP%\DropWheel-update-&lt;guid&gt;</c>, restarts itself from there (so the install folder is no longer
/// locked by a running image), waits for the old app to exit, replaces the files and finally starts
/// <c>DropWheel.exe --updated &lt;version&gt;</c> from the install folder.
///
/// The zip only reaches this code through <see cref="IUpdateService.DownloadAsync"/>, which verifies the
/// SHA-256 digest and the ECDSA signature before returning its path; this class never downloads anything and
/// never executes a file from anywhere else.
/// </summary>
internal static class PortableUpdater
{
    internal const string ApplyUpdateSwitch = "--apply-update";

    private const string TargetVariable = "DROPWHEEL_UPDATE_TARGET";
    private const string LauncherPidVariable = "DROPWHEEL_UPDATE_LAUNCHER_PID";
    private const string ExeName = "DropWheel.exe";
    private const string StageFolder = "stage";
    private const string BackupFolder = "backup";

    /// <summary>The helper must give up rather than half-replace when the old app does not exit.</summary>
    private static readonly TimeSpan AppExitTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The launcher only starts the copied helper and then exits; this should take a moment.</summary>
    private static readonly TimeSpan LauncherExitTimeout = TimeSpan.FromSeconds(15);

    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// Starts the helper for the current portable install. Returns null on success, or a clear error the UI
    /// can show. The verified archive path is passed through unchanged.
    /// </summary>
    public static string? StartHelper(string archivePath, string version)
    {
        try
        {
            var exe = Path.Combine(AppContext.BaseDirectory, ExeName);
            if (!File.Exists(exe))
                exe = Environment.ProcessPath ?? "";
            if (!File.Exists(exe))
                return "The updater could not find DropWheel.exe to start itself.";

            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory,
            };
            info.ArgumentList.Add(ApplyUpdateSwitch);
            info.ArgumentList.Add(archivePath);
            info.ArgumentList.Add(version);
            info.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            // Never inherit a helper role from a previous run of this app.
            info.Environment.Remove(TargetVariable);
            info.Environment.Remove(LauncherPidVariable);

            using var helper = Process.Start(info);
            if (helper is null)
                return "The update helper could not be started.";

            AppLog.Info($"update apply: started the portable helper for {version} (pid {helper.Id})");
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException
                                       or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            AppLog.Error("update apply: could not start the portable helper", ex);
            return "The update helper could not be started: " + ex.Message;
        }
    }

    /// <summary>Entry point shared by both helper hops; returns the process exit code.</summary>
    internal static int Run(string[] args)
    {
        if (!TryParse(args, out var archive, out var version, out var appPid, out var error))
        {
            AppLog.Error("update apply: " + error);
            return 2;
        }

        var target = Environment.GetEnvironmentVariable(TargetVariable);
        return string.IsNullOrWhiteSpace(target)
            ? RunLauncher(archive, version, appPid)
            : RunWorker(archive, version, appPid, target);
    }

    private static bool TryParse(
        string[] args, out string archive, out string version, out int appPid, out string error)
    {
        archive = "";
        version = "";
        appPid = 0;
        error = "";

        if (args.Length is < 3 or > 4)
        {
            error = $"expected {ApplyUpdateSwitch} <archive> <version> [pid], got {args.Length} argument(s)";
            return false;
        }

        archive = args[1];
        if (string.IsNullOrWhiteSpace(archive))
        {
            error = "the update archive path is empty";
            return false;
        }

        if (!SemanticVersion.TryParse(args[2], out var parsedVersion))
        {
            error = $"the update version is not a stable version: '{args[2]}'";
            return false;
        }
        version = parsedVersion.ToString(3);

        if (args.Length == 4 &&
            (!int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out appPid) || appPid <= 0))
        {
            error = $"the optional pid argument is not a positive process id: '{args[3]}'";
            return false;
        }

        return true;
    }

    /// <summary>
    /// First hop: copy the app (including the running exe and its runtime files) out of the install folder and
    /// start the copied exe. The copy is required because a running image cannot be overwritten on Windows.
    /// </summary>
    private static int RunLauncher(string archivePath, string version, int appPid)
    {
        var installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
        var tempRoot = Path.Combine(Path.GetTempPath(), "DropWheel-update-" + Guid.NewGuid().ToString("N"));

        try
        {
            if (!File.Exists(archivePath))
                throw new FileNotFoundException("the downloaded update archive is missing", archivePath);
            if (IsInside(tempRoot, installDir))
                throw new InvalidOperationException("the temporary update folder is inside the install folder");

            Directory.CreateDirectory(tempRoot);
            CopyApplication(installDir, tempRoot);

            // Copy the running exe explicitly as well; CopyApplication may have skipped it after an earlier
            // update left the folder in an unexpected state.
            var self = Path.Combine(installDir, ExeName);
            if (!File.Exists(self))
                self = Environment.ProcessPath ?? self;
            if (!File.Exists(self))
                throw new FileNotFoundException("the running DropWheel.exe was not found", self);
            File.Copy(self, Path.Combine(tempRoot, ExeName), true);

            var worker = Path.Combine(tempRoot, ExeName);
            if (!File.Exists(worker))
                throw new FileNotFoundException("the update helper copy is missing", worker);

            var info = new ProcessStartInfo(worker)
            {
                UseShellExecute = false,
                WorkingDirectory = tempRoot,
            };
            info.ArgumentList.Add(ApplyUpdateSwitch);
            info.ArgumentList.Add(archivePath);
            info.ArgumentList.Add(version);
            if (appPid > 0)
                info.ArgumentList.Add(appPid.ToString(CultureInfo.InvariantCulture));
            info.Environment[TargetVariable] = installDir;
            info.Environment[LauncherPidVariable] = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

            using var process = Process.Start(info);
            if (process is null)
                throw new InvalidOperationException("the copied helper did not start");

            AppLog.Info($"update apply: copied the app to {tempRoot} and started the helper (pid {process.Id})");
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error($"update apply: could not prepare the portable helper in {tempRoot}", ex);
            TryDeleteDirectory(tempRoot);
            return 1;
        }
    }

    /// <summary>
    /// Second hop: wait for the old app and the launcher, unpack the verified zip, back up and replace the
    /// app files while keeping user data, then restart DropWheel. Any failure restores the backup.
    /// </summary>
    private static int RunWorker(string archivePath, string version, int appPid, string target)
    {
        string installDir;
        try
        {
            installDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            AppLog.Error($"update apply: invalid install folder '{target}'", ex);
            return 1;
        }

        var log = new UpdateLog(installDir);
        var tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));

        try
        {
            log.Info($"running helper for {version} in {installDir} (app pid " +
                     $"{(appPid > 0 ? appPid.ToString(CultureInfo.InvariantCulture) : "not passed")})");

            if (!Directory.Exists(installDir))
            {
                log.Error("failed: the install folder no longer exists");
                return 1;
            }
            if (tempRoot.Equals(installDir, PathComparison) || IsInside(tempRoot, installDir))
            {
                // A stray DROPWHEEL_UPDATE_TARGET must never make the installed exe replace itself in place.
                log.Error("failed: the update helper is not running from its temporary copy; nothing was replaced");
                return 1;
            }

            if (!WaitForExit(appPid, AppExitTimeout, log, "the running DropWheel"))
                return 1;

            var launcherPid = ReadPid(LauncherPidVariable);
            if (!WaitForExit(launcherPid, LauncherExitTimeout, log, "the update launcher"))
                return 1;

            var stage = Path.Combine(tempRoot, StageFolder);
            var backup = Path.Combine(tempRoot, BackupFolder);
            Directory.CreateDirectory(stage);

            log.Info($"extracting {archivePath} ...");
            ZipFile.ExtractToDirectory(archivePath, stage);

            if (!File.Exists(Path.Combine(stage, ExeName)))
                throw new InvalidDataException($"the update package does not contain {ExeName} at its root");

            var plan = BuildPlan(stage, installDir, backup);
            if (plan.Count == 0)
                throw new InvalidDataException("the update package contains no files to install");

            BackUp(plan);
            log.Info($"replacing {plan.Count} file(s) ...");

            var touched = new List<Replacement>(plan.Count);
            try
            {
                foreach (var item in plan)
                {
                    touched.Add(item);
                    Replace(item);
                }

                var relaunched = Relaunch(installDir, version);

                // The worker itself lives here; only the extracted and backup files can go.
                TryDeleteDirectory(stage);
                TryDeleteDirectory(backup);

                log.Info($"success; relaunched {relaunched}");
                return 0;
            }
            catch (Exception ex)
            {
                var rollback = RollBack(touched);
                log.Error(rollback is null
                    ? $"failed: {ex.Message}; the previous version was restored"
                    : $"failed: {ex.Message}; restoring the previous version failed: {rollback}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            log.Error($"failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Copies every app file except the user data that must survive an update. The runtime files are part of
    /// the copy too: the published app is a folder, not a single-file exe, and the copied apphost resolves its
    /// runtime next to itself.
    /// </summary>
    private static void CopyApplication(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (IsUserDataFile(name))
                continue;
            File.Copy(file, Path.Combine(destination, name), true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name.Equals("licenses", PathComparison) || name.Equals("tools", PathComparison))
                continue;
            CopyApplication(directory, Path.Combine(destination, name));
        }
    }

    private static bool IsUserDataFile(string name) =>
        name.Equals("settings.json", PathComparison)
        || name.Equals("portable.txt", PathComparison)
        || name.Equals("log.txt", PathComparison);

    /// <summary>The files the update will replace, with the main exe last so a failure leaves it recoverable.</summary>
    private static List<Replacement> BuildPlan(string stage, string installDir, string backupDir)
    {
        var installRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDir));
        var plan = new List<Replacement>();

        foreach (var source in Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stage, source);
            if (IsProtected(relative))
                continue;

            var target = Path.GetFullPath(Path.Combine(installRoot, relative));
            if (!IsInside(target, installRoot))
                throw new InvalidDataException($"the update package tries to write outside the install folder: {relative}");

            plan.Add(new Replacement(relative, source, target, Path.Combine(backupDir, relative)));
        }

        return plan
            .OrderBy(item => item.Relative.Equals(ExeName, PathComparison) ? 1 : 0)
            .ToList();
    }

    /// <summary>The paths an update must never touch: settings, the portable marker, the log and licences.</summary>
    private static bool IsProtected(string relative)
    {
        var normalized = relative.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var separator = normalized.IndexOf(Path.DirectorySeparatorChar);
        var first = separator < 0 ? normalized : normalized[..separator];

        return first.Equals("settings.json", PathComparison)
            || first.Equals("portable.txt", PathComparison)
            || first.Equals("log.txt", PathComparison)
            || first.Equals("licenses", PathComparison);
    }

    private static void BackUp(List<Replacement> plan)
    {
        foreach (var item in plan)
        {
            item.Existed = File.Exists(item.Target);
            if (!item.Existed)
                continue;

            var folder = Path.GetDirectoryName(item.Backup);
            if (folder is not null)
                Directory.CreateDirectory(folder);
            File.Copy(item.Target, item.Backup, true);
        }
    }

    private static void Replace(Replacement item)
    {
        var folder = Path.GetDirectoryName(item.Target);
        if (folder is not null)
            Directory.CreateDirectory(folder);

        if (File.Exists(item.Target))
        {
            var attributes = File.GetAttributes(item.Target);
            if ((attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(item.Target, attributes & ~FileAttributes.ReadOnly);
        }

        File.Copy(item.Source, item.Target, true);
    }

    /// <summary>Restores every file that was touched; returns null when the rollback fully succeeded.</summary>
    private static string? RollBack(List<Replacement> touched)
    {
        var errors = new List<string>();
        for (var i = touched.Count - 1; i >= 0; i--)
        {
            var item = touched[i];
            try
            {
                if (item.Existed)
                    File.Copy(item.Backup, item.Target, true);
                else if (File.Exists(item.Target))
                    File.Delete(item.Target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{item.Relative}: {ex.Message}");
            }
        }

        return errors.Count == 0 ? null : string.Join("; ", errors);
    }

    private static string Relaunch(string installDir, string version)
    {
        var exe = Path.Combine(installDir, ExeName);
        if (!File.Exists(exe))
            throw new FileNotFoundException("the updated DropWheel.exe is missing", exe);

        // Never hand a non-PE file to CreateProcess: on Windows that can surface a blocking "this app
        // can't run" dialog (or a WER prompt) instead of a clean exception, which would leave the update
        // half-applied. Rejecting it here lets the caller roll back deterministically.
        var header = new byte[2];
        using (var stream = File.OpenRead(exe))
        {
            if (stream.Read(header, 0, header.Length) != header.Length || header[0] != (byte)'M' || header[1] != (byte)'Z')
                throw new InvalidDataException("the updated DropWheel.exe is not a Windows executable");
        }

        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = installDir,
        };
        info.ArgumentList.Add("--updated");
        info.ArgumentList.Add(version);

        // The relaunched app must look like a normal start, not like the helper that started it.
        info.Environment.Remove(TargetVariable);
        info.Environment.Remove(LauncherPidVariable);

        using var process = Process.Start(info);
        if (process is null)
            throw new InvalidOperationException("the updated DropWheel.exe did not start");

        return $"{exe} --updated {version} (pid {process.Id})";
    }

    /// <summary>Waits for one process; a process that is already gone counts as exited.</summary>
    private static bool WaitForExit(int pid, TimeSpan timeout, UpdateLog log, string what)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
            return true;

        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
                return true;

            log.Info($"waiting up to {timeout.TotalSeconds:0} s for {what} (pid {pid}) to exit ...");
            if (process.WaitForExit((int)timeout.TotalMilliseconds))
                return true;

            log.Error($"failed: {what} (pid {pid}) is still running after {timeout.TotalSeconds:0} s; nothing was replaced");
            return false;
        }
        catch (ArgumentException)
        {
            // No such process: it exited before we could open it.
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or NotSupportedException)
        {
            log.Error($"failed: could not wait for {what} (pid {pid}): {ex.Message}; nothing was replaced");
            return false;
        }
    }

    private static int ReadPid(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0
            ? pid
            : 0;
    }

    private static bool IsInside(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, PathComparison);

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leftovers in %TEMP% are harmless; the update result is what matters.
        }
    }

    private sealed record Replacement(string Relative, string Source, string Target, string Backup)
    {
        /// <summary>True when the target existed before the update and therefore has a backup.</summary>
        public bool Existed { get; set; }
    }

    /// <summary>
    /// Writes the helper's result to the app log and to the install folder's <c>log.txt</c>; the latter is
    /// appended, never replaced, and gives the user (and CI) one place with a clear success/failure line.
    /// </summary>
    private sealed class UpdateLog(string installDir)
    {
        public void Info(string message) => Write("INFO", message);

        public void Error(string message) => Write("ERROR", message);

        private void Write(string level, string message)
        {
            var line = $"update apply: {message}";
            if (level == "ERROR")
                AppLog.Error(line);
            else
                AppLog.Info(line);

            try
            {
                File.AppendAllText(
                    Path.Combine(installDir, "log.txt"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level,-5} {line}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                           or NotSupportedException or ArgumentException)
            {
                // Diagnostics must never break the update.
            }
        }
    }
}
