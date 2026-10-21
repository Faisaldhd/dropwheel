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
        var panel = Panel;
        Panel = null;
        if (panel is null)
            return;
        if (panel.Dispatcher.CheckAccess())
            panel.ClosePanel();
        else
            panel.Dispatcher.BeginInvoke(new Action(panel.ClosePanel));
    }

    /// <summary>Panel updates arrive from the watcher's thread pool too; never touch WPF off its dispatcher.</summary>
    public void SetStatus(UnlockedStatus status) => UpdatePanel(panel => panel.SetStatus(status));

    public void SetError(string detail) => UpdatePanel(panel => panel.SetError(detail));

    private void UpdatePanel(Action<UnlockedPanel> update)
    {
        var panel = Panel;
        if (panel is null)
            return;
        if (panel.Dispatcher.CheckAccess())
        {
            update(panel);
            return;
        }
        panel.Dispatcher.BeginInvoke(new Action(() => update(panel)));
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
    private readonly ILockHoldProbe _holdProbe;
    private readonly IExplorerProbe _explorerProbe;
    private readonly ILockLauncher _launcher;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, LockSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _polling;

    public LockSessionManager(LockService service, LockJournalStore journal, ProtectionSettings settings,
        ILockHoldProbe? holdProbe = null, IExplorerProbe? explorerProbe = null, ILockLauncher? launcher = null)
    {
        _service = service;
        _journal = journal;
        _settings = settings;
        _holdProbe = holdProbe ?? new WindowsHoldProbe();
        _explorerProbe = explorerProbe ?? new ShellExplorerProbe();
        _launcher = launcher ?? new ShellLockLauncher();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        // The tick only starts the poll; every blocking probe and every hash runs on a worker thread.
        _timer.Tick += (_, _) => _ = PollSafelyAsync();
    }

    private async Task PollSafelyAsync()
    {
        try
        {
            await PollOnceAsync();
        }
        catch (Exception ex)
        {
            // A poll is background housekeeping: log it, keep the unlocked session and retry next tick.
            FileAction.App.Diagnostics.AppLog.Error("lock poll", ex);
        }
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
        string? treeHash;
        try
        {
            // Extraction and the folder tree hash can take seconds for a large item; both run on the
            // worker, and the continuation returns to the UI thread before any window or journal work.
            (extracted, treeHash) = await Task.Run(() =>
            {
                var outcome = _service.ExtractTo(containerPath, secret, tempRoot, editableCopy: true);
                var hash = outcome.Read.Meta.IsDirectory ? LockArchive.TreeHash(outcome.Path) : null;
                return (outcome, hash);
            });
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
            TreeHash = treeHash,
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
        try
        {
            var wasError = session.Record.State == LockSessionState.Error;
            var plaintextPath = session.Record.PlaintextPath;
            (string? FileHash, string? TreeHash) before;
            try
            {
                // Hashing the whole unlocked file (or walking a folder tree) can take seconds for a big
                // item. It must never run on the dispatcher, or the wheel and every window freeze.
                before = await Task.Run(() => Snapshot(plaintextPath)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                session.Record = session.Record.WithState(LockSessionState.Error, ex.Message);
                _journal.Write(session.Record);
                session.SetError(T("tool.lock.panel.changedSalvage"));
                return;
            }
            if (wasError && !MatchesSnapshot(session.Record, before))
            {
                session.Record = session.Record.WithState(LockSessionState.Error,
                    "The unlocked content changed since the last save. Retry refused so the good container is kept; open the item and salvage the copy.");
                _journal.Write(session.Record);
                session.SetError(T("tool.lock.panel.changedSalvage"));
                return;
            }
            session.SetStatus(UnlockedStatus.Syncing);
            session.Record = session.Record.WithState(LockSessionState.Syncing);
            _journal.Write(session.Record);
            try
            {
                await Task.Run(() => _service.RelockFromSession(session.Record.ContainerPath, session.Header,
                    session.DataKey, session.Record.PlaintextPath,
                    expectedFileHash: wasError ? session.Record.FileHash : null,
                    expectedTreeHash: wasError ? session.Record.TreeHash : null,
                    originalAttributes: session.OriginalAttributes,
                    originalDesktopIniAttributes: session.OriginalDesktopIniAttributes)).ConfigureAwait(true);
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
                session.SetError(LockRuntime.Describe(ex));
            }
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

    /// <summary>
    /// The poll: recompute the rules for every session, then relock the ones that are due. The blocking
    /// probes (a held file, Explorer's shell API, a folder walk) run on a worker thread; only the cheap
    /// decision and the panel updates happen on the dispatcher, so a slow shell can never freeze the wheel.
    /// </summary>
    internal async Task PollOnceAsync()
    {
        // A probe can outlast the two-second tick; skip this tick instead of stacking overlapping probes.
        if (Interlocked.Exchange(ref _polling, 1) == 1)
            return;
        try
        {
            List<LockSession> sessions;
            lock (_gate)
                sessions = _sessions.Values.ToList();
            if (sessions.Count == 0)
            {
                _timer.Stop();
                return;
            }

            var requests = sessions
                .Where(session => !session.Busy && session.Record.State is not LockSessionState.Relocked)
                .Select(session => new AutoLockProbeRequest(session, session.Record.PlaintextPath, session.IsDirectory))
                .ToList();
            if (requests.Count == 0)
                return;

            IReadOnlyList<AutoLockProbe> probes;
            try
            {
                probes = await Task.Run(() => ProbeAll(requests)).ConfigureAwait(true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                           or InvalidOperationException)
            {
                // Fail closed: a probe that could not answer makes this tick wait, never lock blindly.
                return;
            }

            var now = DateTimeOffset.Now;
            foreach (var probe in probes)
            {
                var session = probe.Session;
                if (session.Busy || session.Record.State is LockSessionState.Relocked)
                    continue;
                lock (_gate)
                {
                    if (!_sessions.TryGetValue(session.Record.Id, out var current) ||
                        !ReferenceEquals(current, session))
                        continue;
                }

                try
                {
                    var lastChange = session.LastChange;
                    if (probe.LastWriteUtc is { } stamp && stamp > lastChange)
                        lastChange = stamp;

                    var input = new AutoLockInput(
                        session.IsDirectory,
                        ApplicationHoldsFile: probe.ApplicationHoldsFile,
                        ExplorerShowsFolder: probe.ExplorerShowsFolder,
                        AnyFileInFolderHeld: probe.AnyFileInFolderHeld,
                        SinceLastChange: now - lastChange,
                        SinceOpened: now - session.Record.OpenedUtc,
                        GraceSeconds: _settings.GraceSeconds,
                        IdleFallbackMinutes: _settings.IdleFallbackMinutes);
                    if (AutoLockRules.Decide(input) == AutoLockDecision.Lock)
                        _ = RelockAsync(session);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    session.SetError(LockRuntime.Describe(ex));
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private sealed record AutoLockProbeRequest(LockSession Session, string PlaintextPath, bool IsDirectory);

    private sealed record AutoLockProbe(
        LockSession Session,
        DateTimeOffset? LastWriteUtc,
        bool ApplicationHoldsFile,
        bool ExplorerShowsFolder,
        bool AnyFileInFolderHeld);

    /// <summary>Runs on a worker thread: every call here may block for seconds.</summary>
    private IReadOnlyList<AutoLockProbe> ProbeAll(IReadOnlyList<AutoLockProbeRequest> requests)
    {
        var probes = new List<AutoLockProbe>(requests.Count);
        foreach (var request in requests)
            probes.Add(Probe(request));
        return probes;
    }

    private AutoLockProbe Probe(AutoLockProbeRequest request)
    {
        DateTimeOffset? lastWrite = null;
        try
        {
            var stamp = request.IsDirectory
                ? Directory.GetLastWriteTimeUtc(request.PlaintextPath)
                : File.GetLastWriteTimeUtc(request.PlaintextPath);
            if (stamp != DateTime.MinValue)
                lastWrite = new DateTimeOffset(stamp, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }

        return new AutoLockProbe(
            request.Session,
            lastWrite,
            ApplicationHoldsFile: !request.IsDirectory && _holdProbe.HoldsFile(request.PlaintextPath),
            ExplorerShowsFolder: request.IsDirectory && _explorerProbe.ShowsFolder(request.PlaintextPath),
            AnyFileInFolderHeld: request.IsDirectory && _holdProbe.AnyFileHeld(request.PlaintextPath));
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
                session.SetStatus(UnlockedStatus.Syncing);
            }
            watcher.Changed += Changed;
            watcher.Created += Changed;
            watcher.Deleted += Changed;
            watcher.Renamed += (sender, args) =>
            {
                session.LastChange = DateTimeOffset.Now;
                session.SetStatus(UnlockedStatus.Syncing);
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

        SweepRestoreTemp(owner);
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
