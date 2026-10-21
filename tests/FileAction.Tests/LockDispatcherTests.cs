using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using FileAction.App.Shell.Protection;
using FileAction.App.UI.Tools.Lock;
using FileAction.Core;
using FileAction.Core.Lock;

namespace FileAction.Tests;

/// <summary>
/// The Lock tool must stay invisible to the dispatcher while it watches a session: the file probes and the
/// snapshot hash are what used to freeze the wheel for seconds after the opened program closed. The source
/// guards below pin the two call sites that cannot be observed from a unit test, and the runtime tests use
/// fake probes to prove the poll actually runs them on a worker and still relocks a closed program.
/// </summary>
public class LockDispatcherTests
{
    private static LockCredentials Credentials() => new()
    {
        FilePassword = "file-password-1",
        RescuerPassword = "rescuer-password-1",
        RescuerKey = RescuerKey.Generate(),
        FilePasswordIterations = 1000,
        RescuerPasswordIterations = 1000,
        RescuerKeyIterations = 1000,
    };

    [Fact]
    public void Poll_probes_on_a_worker_and_relocks_once_the_program_is_gone()
    {
        var root = NewDirectory();
        try
        {
            var service = new LockService(
                new LockEnvironment([], [], RestoreTempFolder: Path.Combine(root, "restore")),
                minimumIterations: 1000, newSlotIterations: 1000);
            var source = Path.Combine(root, "note.txt");
            File.WriteAllText(source, "auto lock");
            var credentials = Credentials();
            var container = service.Protect(source, root, credentials).OutputPath;
            var extracted = service.ExtractTo(container, credentials.FilePassword, Path.Combine(root, "plain"));
            // The probe folds the file's last-write time into the grace calculation; make the restored copy
            // look like it was opened and left alone a while ago.
            File.SetLastWriteTimeUtc(extracted.Path, DateTime.UtcNow.AddMinutes(-5));
            var journal = new LockJournalStore(Path.Combine(root, "sessions"));
            var record = new LockSessionRecord("auto1", container, extracted.Path, "note.txt", false,
                DateTimeOffset.Now.AddMinutes(-1), LockSessionState.Open, null);
            journal.Create(record);

            var hold = new RecordingHoldProbe();
            var manager = new LockSessionManager(service, journal,
                new ProtectionSettings { GraceSeconds = 3 }, hold, new SilentExplorerProbe());
            AddSession(manager, new LockSession(record, extracted.Header, extracted.DataKey, false, 0, 0)
            {
                LastChange = DateTimeOffset.Now.AddMinutes(-1),
            });

            var caller = RunPollOnDedicatedThread(manager);

            Assert.True(hold.ThreadId != 0, "the hold probe was never called");
            Assert.NotEqual(caller, hold.ThreadId);
            var relocked = WaitUntil(() => journal.List().Count == 0, TimeSpan.FromSeconds(30));
            var remaining = journal.List();
            Assert.True(relocked,
                "the session was not relocked: " + string.Join(", ", remaining.Select(record =>
                    record.State + ":" + record.Error)) + " plaintext=" + File.Exists(extracted.Path));
            Assert.False(File.Exists(extracted.Path));
            Assert.Equal("auto lock", ContentOf(service, container, credentials.FilePassword));
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public void Unlock_gate_labels_the_primary_action_open_and_follows_the_secret()
    {
        RunSta(() =>
        {
            var gate = new ActionGateGrid(ActionGateGrid.UnlockLabelKey);
            Assert.Equal("tool.lock.unlock.open", gate.PrimaryLabelKey);
            Assert.Equal("Open", FileAction.Core.Loc.T(gate.PrimaryLabelKey));
            Assert.False(gate.CanApply);

            var changed = 0;
            gate.CanApplyChanged += () => changed++;
            gate.SetCanApply(true);
            Assert.True(gate.CanApply);
            Assert.Equal(1, changed);
            gate.SetCanApply(true);
            Assert.Equal(1, changed);
            gate.SetCanApply(false);
            Assert.False(gate.CanApply);
            Assert.Equal(2, changed);
        });
    }

    [Fact]
    public void Lock_source_keeps_hashing_and_probes_off_the_dispatcher()
    {
        var sessions = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "Shell", "Protection", "LockSessions.cs"));
        var relock = Between(sessions, "private async Task RelockAsync", "private static (string? FileHash");
        Assert.Contains("await Task.Run(() => Snapshot", relock, StringComparison.Ordinal);
        Assert.DoesNotContain("before = Snapshot(", relock, StringComparison.Ordinal);

        var poll = Between(sessions, "internal async Task PollOnceAsync",
            "private sealed record AutoLockProbeRequest");
        Assert.Contains("await Task.Run(() => ProbeAll", poll, StringComparison.Ordinal);
        Assert.DoesNotContain("_holdProbe.HoldsFile", poll, StringComparison.Ordinal);
        Assert.DoesNotContain("_explorerProbe.ShowsFolder", poll, StringComparison.Ordinal);

        var recovery = Between(sessions, "public void RecoverOnStartup", "private static void SweepSessionFolders");
        Assert.Contains("SweepRestoreTemp(owner);", recovery, StringComparison.Ordinal);

        var view = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "FileAction.App", "UI", "Tools", "Lock", "LockTool.cs"));
        var unlock = Between(view, "internal sealed class LockUnlockView",
            "internal sealed class ChangeFilePasswordWindow");
        Assert.Contains("new(ActionGateGrid.UnlockLabelKey)", unlock, StringComparison.Ordinal);
        Assert.Contains("SetCanApply(!string.IsNullOrEmpty(_secret.Text))", unlock, StringComparison.Ordinal);
        Assert.Contains("_secret.Changed", unlock, StringComparison.Ordinal);
        Assert.Contains("_root.Loaded", unlock, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ fixtures

    private sealed class RecordingHoldProbe : ILockHoldProbe
    {
        public int ThreadId { get; private set; }

        public bool HoldsFile(string path)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            return false;
        }

        public bool AnyFileHeld(string folder)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            return false;
        }
    }

    private sealed class SilentExplorerProbe : IExplorerProbe
    {
        public bool ShowsFolder(string folder) => false;
    }

    private static void AddSession(LockSessionManager manager, LockSession session)
    {
        var field = typeof(LockSessionManager).GetField("_sessions",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var sessions = (Dictionary<string, LockSession>)field!.GetValue(manager)!;
        sessions[session.Record.Id] = session;
    }

    /// <summary>
    /// Polls from a dedicated thread that stays blocked for the whole probe, so the probe can never borrow
    /// the caller's thread id: the recorded id proves the shell/file probes ran on a worker.
    /// </summary>
    private static int RunPollOnDedicatedThread(LockSessionManager manager)
    {
        var caller = 0;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            caller = Environment.CurrentManagedThreadId;
            try
            {
                manager.PollOnceAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
            Name = "lock-poll-test",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "the poll did not finish");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
        return caller;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            Thread.Sleep(25);
        }
        return condition();
    }

    private static string ContentOf(LockService service, string container, string secret)
    {
        using var source = new FileStream(container, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new MemoryStream();
        LockContainer.Decrypt(source, destination, secret);
        return Encoding.UTF8.GetString(destination.ToArray());
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dwlock-dispatcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"missing source marker '{start}'");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"missing source marker '{end}'");
        return text[from..to];
    }
}
