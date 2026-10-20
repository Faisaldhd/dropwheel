using System.IO;
using System.Windows;
using System.Windows.Threading;
using FileAction.App.UI;
using FileAction.Core.Lock;
using static FileAction.Core.Loc;

namespace FileAction.App.Shell.Protection;

/// <summary>One unlocked plaintext copy that the panel is watching.</summary>
internal sealed class LockSession : IDisposable
{
    public LockSession(LockSessionRecord record, LockHeader header, byte[] dataKey, bool isDirectory)
    {
        Record = record;
        Header = header;
        DataKey = dataKey;
        IsDirectory = isDirectory;
    }

    public LockSessionRecord Record { get; set; }
    public LockHeader Header { get; }
    public byte[] DataKey { get; }
    public bool IsDirectory { get; }
    public DateTimeOffset LastChange { get; set; }
    public bool Busy { get; set; }
    public FileSystemWatcher? Watcher { get; set; }
    public UnlockedPanel? Panel { get; set; }

    public void Dispose()
    {
        Watcher?.Dispose();
        Watcher = null;
        LockSecrets.Wipe(DataKey);
        Panel?.ClosePanel();
        Panel = null;
    }
}

/// <summary>
/// Opens protected items into a private session folder, watches the program that has them, and re-encrypts
/// the edited content into the same container once the program closes (after the configured grace), the
/// folder window closes, or the idle fallback expires. A failed relock keeps the plaintext and the journal
/// so nothing is ever lost.
/// </summary>
internal sealed class LockSessionManager
{
    private readonly LockService _service;
    private readonly LockJournalStore _journal;
    private readonly ProtectionSettings _settings;
    private readonly ILockHoldProbe _holdProbe = new WindowsHoldProbe();
    private readonly IExplorerProbe _explorerProbe = new ShellExplorerProbe();
    private readonly ILockLauncher _launcher = new ShellLockLauncher();
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, LockSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public LockSessionManager(LockService service, LockJournalStore journal, ProtectionSettings settings)
    {
        _service = service;
        _journal = journal;
        _settings = settings;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => Poll();
    }

    /// <summary>Raised after a successful relock, for the "Locked" toast.</summary>
    public event Action<string, string>? Locked;

    public int OpenCount
    {
        get
        {
            lock (_gate)
                return _sessions.Count;
        }
    }

    /// <summary>Extracts to a session folder, launches the default program and starts watching.</summary>
    public async Task<LockSecretKind> OpenAsync(string containerPath, string secret)
    {
        var existing = _journal.FindByContainer(containerPath);
        if (existing is not null)
        {
            Focus(existing.Id);
            return LockSecretKind.FilePassword;
        }

        var id = Guid.NewGuid().ToString("N");
        var tempRoot = Path.Combine(ProtectionPaths.SessionsFolder, id);
        Directory.CreateDirectory(tempRoot);
        // Extraction can take seconds for a large file; the UI thread stays responsive and the
        // continuation returns to it before any window or journal work happens.
        var extracted = await Task.Run(() => _service.ExtractTo(containerPath, secret, tempRoot));
        var record = new LockSessionRecord(id, LockPaths.Normalize(containerPath), extracted.Path,
            extracted.Read.Meta.Name, extracted.Read.Meta.IsDirectory, DateTimeOffset.Now, LockSessionState.Open, null);
        _journal.Create(record);
        var session = new LockSession(record, extracted.Header, extracted.DataKey, extracted.Read.Meta.IsDirectory)
        {
            LastChange = DateTimeOffset.Now,
        };
        lock (_gate)
            _sessions[id] = session;
        AttachWatcher(session);
        ShowPanel(session);
        _launcher.Open(extracted.Path, session.IsDirectory);
        _timer.Start();
        return extracted.Read.OpenedWith;
    }

    /// <summary>Locks one session immediately; an error is shown and retryable.</summary>
    public void LockNow(string id)
    {
        LockSession? session;
        lock (_gate)
            _sessions.TryGetValue(id, out session);
        if (session is not null)
            _ = RelockAsync(session);
    }

    /// <summary>
    /// Re-encrypts the edited content into the same container, verifies it, removes the plaintext and only
    /// then deletes the journal.
    /// </summary>
    private async Task RelockAsync(LockSession session)
    {
        if (session.Busy)
            return;
        session.Busy = true;
        session.Panel?.SetStatus(UnlockedStatus.Syncing);
        session.Record = session.Record.WithState(LockSessionState.Syncing);
        _journal.Write(session.Record);
        try
        {
            await Task.Run(() => _service.RelockFromSession(session.Record.ContainerPath, session.Header,
                session.DataKey, session.Record.PlaintextPath));
            _journal.Delete(session.Record.Id);
            NotifyLocked(session.Record);
            lock (_gate)
                _sessions.Remove(session.Record.Id);
            session.Dispose();
        }
        catch (Exception ex)
        {
            session.Record = session.Record.WithState(LockSessionState.Error, ex.Message);
            _journal.Write(session.Record);
            session.Panel?.SetError(LockRuntime.Describe(ex));
        }
        finally
        {
            session.Busy = false;
        }
    }

    private void NotifyLocked(LockSessionRecord record) =>
        Locked?.Invoke(T("tool.lock.panel.locked.title"),
            T("tool.lock.panel.locked.body", Bidi.Value(record.OriginalName)));

    /// <summary>The poll: recompute the rules for every session, then relock the ones that are due.</summary>
    private void Poll()
    {
        List<LockSession> sessions;
        lock (_gate)
            sessions = _sessions.Values.ToList();
        if (sessions.Count == 0)
        {
            _timer.Stop();
            return;
        }

        foreach (var session in sessions)
        {
            if (session.Busy || session.Record.State is LockSessionState.Relocked)
                continue;
            try
            {
                var plaintext = session.Record.PlaintextPath;
                var lastChange = session.LastChange;
                try
                {
                    var stamp = session.IsDirectory
                        ? Directory.GetLastWriteTimeUtc(plaintext)
                        : File.GetLastWriteTimeUtc(plaintext);
                    if (stamp > lastChange)
                        lastChange = new DateTimeOffset(stamp, TimeSpan.Zero);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }

                var input = new AutoLockInput(
                    session.IsDirectory,
                    ApplicationHoldsFile: !session.IsDirectory && _holdProbe.HoldsFile(plaintext),
                    ExplorerShowsFolder: session.IsDirectory && _explorerProbe.ShowsFolder(plaintext),
                    AnyFileInFolderHeld: session.IsDirectory && _holdProbe.AnyFileHeld(plaintext),
                    SinceLastChange: DateTimeOffset.Now - lastChange,
                    SinceOpened: DateTimeOffset.Now - session.Record.OpenedUtc,
                    GraceSeconds: _settings.GraceSeconds,
                    IdleFallbackMinutes: _settings.IdleFallbackMinutes);
                if (AutoLockRules.Decide(input) == AutoLockDecision.Lock)
                    _ = RelockAsync(session);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                session.Panel?.SetError(LockRuntime.Describe(ex));
            }
        }
    }

    private void AttachWatcher(LockSession session)
    {
        try
        {
            var path = session.Record.PlaintextPath;
            var directory = session.IsDirectory ? path : Path.GetDirectoryName(path)!;
            if (!Directory.Exists(directory))
                return;
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = session.IsDirectory,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName |
                               NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            void Changed(object? sender, FileSystemEventArgs args)
            {
                session.LastChange = DateTimeOffset.Now;
                session.Panel?.SetStatus(UnlockedStatus.Syncing);
            }
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += (sender, args) =>
            {
                session.LastChange = DateTimeOffset.Now;
                session.Panel?.SetStatus(UnlockedStatus.Syncing);
            };
            session.Watcher = watcher;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Without a watcher the last-write-time fallback still detects changes.
        }
    }

    private void ShowPanel(LockSession session)
    {
        var panel = new UnlockedPanel(session.Record.OriginalName);
        panel.LockNow += () => LockNow(session.Record.Id);
        panel.Retry += () => LockNow(session.Record.Id);
        session.Panel = panel;
        panel.Show();
    }

    private void Focus(string id)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(id, out var session))
                session.Panel?.Activate();
        }
    }

    // ------------------------------------------------------------------ crash recovery

    /// <summary>
    /// Called once at startup. A journal whose plaintext still exists is shown to the user: the edited
    /// content is re-encrypted only after a successful test-open, and the plaintext is never deleted on a
    /// failure or a cancellation.
    /// </summary>
    public void RecoverOnStartup(Window? owner)
    {
        IReadOnlyList<LockSessionRecord> records;
        try
        {
            records = _journal.List();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var record in records)
        {
            var containerExists = File.Exists(record.ContainerPath);
            var plaintextExists = File.Exists(record.PlaintextPath) || Directory.Exists(record.PlaintextPath);
            var action = LockRecoveryPlan.For(containerExists, plaintextExists);
            if (action == LockRecoveryAction.NothingToDo)
            {
                _journal.Delete(record.Id);
                continue;
            }

            var window = new LockRecoveryWindow(record, action) { Owner = owner };
            if (window.ShowDialog() != true)
                continue; // the journal and the plaintext stay for the next start

            var rescuer = LockRuntime.RequireRescuer(owner);
            if (rescuer is null || string.IsNullOrEmpty(rescuer.Password))
                continue;
            var credentials = new LockCredentials
            {
                FilePassword = window.FilePassword,
                RescuerPassword = rescuer.Password,
                RescuerKey = rescuer.Key,
            };
            try
            {
                _service.RecoverSession(record, credentials, _journal,
                    LockRuntime.Icons.FolderIcon);
            }
            catch (Exception ex)
            {
                Dialogs.Show(owner, LockRuntime.Describe(ex), T("tool.lock.recovery.title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>Locks everything still open, used on a clean exit; failures keep their journal entries.</summary>
    public async Task LockAllAsync()
    {
        List<LockSession> sessions;
        lock (_gate)
            sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
            await RelockAsync(session);
    }
}
