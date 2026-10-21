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
    public LockSession(LockSessionRecord record, LockHeader header, byte[] dataKey, bool isDirectory,
        int originalAttributes, int originalDesktopIniAttributes)
    {
        Record = record;
        Header = header;
        DataKey = dataKey;
        IsDirectory = isDirectory;
        OriginalAttributes = originalAttributes;
        OriginalDesktopIniAttributes = originalDesktopIniAttributes;
    }

    public LockSessionRecord Record { get; set; }
    public LockHeader Header { get; }
    public byte[] DataKey { get; }
    public bool IsDirectory { get; }

    /// <summary>Attributes the original item had before the editable copy cleared ReadOnly.</summary>
    public int OriginalAttributes { get; }

    public int OriginalDesktopIniAttributes { get; }
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
        // Journal before extraction: a crash during a long extraction leaves a clear Extracting entry that
        // startup can discard, never an untracked partial plaintext.
        var provisional = new LockSessionRecord(id, LockPaths.Normalize(containerPath), tempRoot,
            LockPaths.DisplayName(containerPath), false, DateTimeOffset.Now, LockSessionState.Extracting, null);
        _journal.Create(provisional);
        ExtractOutcome extracted;
        try
        {
            // Extraction can take seconds for a large file; the UI thread stays responsive and the
            // continuation returns to it before any window or journal work happens.
            // The session copy is disposable: clear ReadOnly so the editor can save. The attributes captured
            // before that change are carried to RelockFromSession so the container keeps the original meaning.
            extracted = await Task.Run(() => _service.ExtractTo(containerPath, secret, tempRoot,
                editableCopy: true));
        }
        catch
        {
            var cleaned = true;
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                cleaned = false;
                FileAction.App.Diagnostics.AppLog.Error("lock: extraction cleanup failed", cleanup);
            }
            // Never lose the pointer to a folder that could not be deleted: keep the Extracting journal
            // so the startup sweep can offer it again.
            if (cleaned)
                _journal.Delete(id);
            throw;
        }
        var record = provisional with
        {
            PlaintextPath = extracted.Path,
            OriginalName = extracted.Read.Meta.Name,
            IsDirectory = extracted.Read.Meta.IsDirectory,
            State = LockSessionState.Open,
            EntryCount = extracted.Read.Meta.EntryCount,
            TreeHash = extracted.Read.Meta.IsDirectory ? LockArchive.TreeHash(extracted.Path) : null,
            FileHash = extracted.Read.Meta.IsDirectory
                ? null
                : Convert.ToHexString(extracted.Read.Digest.Sha256),
        };
        _journal.Write(record);
        var session = new LockSession(record, extracted.Header, extracted.DataKey, extracted.Read.Meta.IsDirectory,
            extracted.Read.Meta.Attributes, extracted.Read.Meta.PreviousDesktopIniAttributes)
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
        var wasError = session.Record.State == LockSessionState.Error;
        (string? FileHash, string? TreeHash) before;
        try
        {
            before = Snapshot(session.Record.PlaintextPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            session.Record = session.Record.WithState(LockSessionState.Error, ex.Message);
            _journal.Write(session.Record);
            session.Panel?.SetError(T("tool.lock.panel.changedSalvage"));
            session.Busy = false;
            return;
        }
        if (wasError && !MatchesSnapshot(session.Record, before))
        {
            session.Record = session.Record.WithState(LockSessionState.Error,
                "The unlocked content changed since the last save. Retry refused so the good container is kept; open the item and salvage the copy.");
            _journal.Write(session.Record);
            session.Panel?.SetError(T("tool.lock.panel.changedSalvage"));
            session.Busy = false;
            return;
        }
        session.Panel?.SetStatus(UnlockedStatus.Syncing);
        session.Record = session.Record.WithState(LockSessionState.Syncing);
        _journal.Write(session.Record);
        try
        {
            await Task.Run(() => _service.RelockFromSession(session.Record.ContainerPath, session.Header,
                session.DataKey, session.Record.PlaintextPath,
                expectedFileHash: wasError ? session.Record.FileHash : null,
                expectedTreeHash: wasError ? session.Record.TreeHash : null,
                originalAttributes: session.OriginalAttributes,
                originalDesktopIniAttributes: session.OriginalDesktopIniAttributes));
            _journal.Delete(session.Record.Id);
            NotifyLocked(session.Record);
            lock (_gate)
                _sessions.Remove(session.Record.Id);
            session.Dispose();
        }
        catch (Exception ex)
        {
            session.Record = session.Record.WithState(LockSessionState.Error, ex.Message) with
            {
                FileHash = before.FileHash ?? session.Record.FileHash,
                TreeHash = before.TreeHash ?? session.Record.TreeHash,
            };
            _journal.Write(session.Record);
            session.Panel?.SetError(LockRuntime.Describe(ex));
        }
        finally
        {
            session.Busy = false;
        }
    }

    private static (string? FileHash, string? TreeHash) Snapshot(string path)
    {
        if (Directory.Exists(path))
            return (null, LockArchive.TreeHash(path));
        if (File.Exists(path))
            return (LockArchive.FileHash(path), null);
        return (null, null);
    }

    private static bool MatchesSnapshot(LockSessionRecord record, (string? FileHash, string? TreeHash) current)
    {
        if (record.IsDirectory)
            return record.TreeHash is null ||
                   string.Equals(record.TreeHash, current.TreeHash, StringComparison.Ordinal);
        return record.FileHash is null ||
               string.Equals(record.FileHash, current.FileHash, StringComparison.Ordinal);
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
            Dialogs.Show(owner, ex.Message, T("tool.lock.recovery.title"), MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SweepSessionFolders(records, owner);
        foreach (var record in records)
        {
            if (record.State == LockSessionState.Extracting)
            {
                var discard = Dialogs.Show(owner, T("tool.lock.recovery.incompleteBody", Bidi.Value(record.OriginalName)),
                    T("tool.lock.recovery.incompleteTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (discard == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (Directory.Exists(record.PlaintextPath))
                            Directory.Delete(record.PlaintextPath, recursive: true);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        Dialogs.Show(owner, ex.Message, T("tool.lock.recovery.title"), MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        continue;
                    }
                    _journal.Delete(record.Id);
                }
                continue;
            }

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

            // The journal names a real container whose Rescuer slots can prove the typed secret.
            var rescuer = LockRuntime.RequireRescuer(owner, record.ContainerPath);
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

    /// <summary>Prompts to remove session folders with no journal entry (orphans from a crash).</summary>
    private static void SweepSessionFolders(IReadOnlyList<LockSessionRecord> records, Window? owner)
    {
        try
        {
            if (!Directory.Exists(ProtectionPaths.SessionsFolder))
                return;
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in records)
            {
                var plaintext = Path.GetFullPath(record.PlaintextPath);
                known.Add(plaintext);
                if (Path.GetDirectoryName(plaintext) is { } parent)
                    known.Add(parent);
                known.Add(Path.Combine(ProtectionPaths.SessionsFolder, record.Id));
            }
            var orphans = Directory.EnumerateDirectories(ProtectionPaths.SessionsFolder)
                .Where(path => !known.Contains(Path.GetFullPath(path)) &&
                               !known.Contains(Path.GetFileName(path)))
                .ToList();
            if (orphans.Count == 0)
                return;
            var discard = Dialogs.Show(owner, T("tool.lock.recovery.orphanBody", orphans.Count),
                T("tool.lock.recovery.orphanTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (discard != MessageBoxResult.Yes)
                return;
            foreach (var path in orphans)
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Show(owner, ex.Message, T("tool.lock.recovery.title"), MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    /// <summary>Deletes leftover .dwrestore staging from an interrupted extraction after an explicit prompt.</summary>
    private void SweepRestoreTemp(Window? owner)
    {
        var folder = _service.RestoreTempFolder;
        try
        {
            if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
                return;
            var discard = Dialogs.Show(owner, T("tool.lock.recovery.tempBody"), T("tool.lock.recovery.tempTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (discard == MessageBoxResult.Yes)
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Dialogs.Show(owner, ex.Message, T("tool.lock.recovery.title"), MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
    /// <summary>
    /// Synchronous clean-exit relock: does not touch the dispatcher or the panel, so it cannot deadlock the
    /// UI thread. Failures write the journal and are recovered at next start.
    /// </summary>
    public void LockAllNow()
    {
        List<LockSession> sessions;
        lock (_gate)
            sessions = _sessions.Values.ToList();
        foreach (var session in sessions)
        {
            try
            {
                _service.RelockFromSession(session.Record.ContainerPath, session.Header, session.DataKey,
                    session.Record.PlaintextPath, originalAttributes: session.OriginalAttributes,
                    originalDesktopIniAttributes: session.OriginalDesktopIniAttributes);
                _journal.Delete(session.Record.Id);
                lock (_gate)
                    _sessions.Remove(session.Record.Id);
                session.Dispose();
            }
            catch (Exception ex)
            {
                session.Record = session.Record.WithState(LockSessionState.Error, ex.Message);
                _journal.Write(session.Record);
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
