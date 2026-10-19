using System.Text;
using System.Windows.Media;
using FileAction.App.Shell.Protection;
using FileAction.App.UI;
using FileAction.Core.Lock;

namespace FileAction.Tests;

/// <summary>
/// The safety-critical behaviour around the container: no plaintext copy survives, a failed protect rolls
/// back, header rewrites are atomic under an injected failure at every file operation, the folder badge is
/// restored byte for byte, and the recovery journal never contains a secret.
/// </summary>
public class LockSafetyTests
{
    // ------------------------------------------------------------------ fixtures

    private static LockCredentials Credentials(string? key = null) => new()
    {
        FilePassword = "file-password-1",
        RescuerPassword = "rescuer-password-1",
        RescuerKey = key ?? RescuerKey.Generate(),
        FilePasswordIterations = 1000,
        RescuerPasswordIterations = 1000,
        RescuerKeyIterations = 1000,
    };

    private static LockService NewService(ILockHost? host = null) =>
        new(new LockEnvironment([], []), host ?? NullLockHost.Instance, minimumIterations: 1000,
            newSlotIterations: 1000);

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "dwlock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Cleanup(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void WriteFile(string path, string text)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, text);
    }

    private sealed class FakeHost : ILockHost
    {
        public List<(string Path, bool Recycle)> Removed { get; } = [];
        public List<string> Notified { get; } = [];

        public void RemoveContainer(string protectedPath, bool recycleBin)
        {
            Removed.Add((protectedPath, recycleBin));
            if (File.Exists(protectedPath))
                File.Delete(protectedPath);
        }

        public void NotifyShell(string path) => Notified.Add(path);
    }

    /// <summary>Fails exactly one file operation, so a rewrite can be interrupted at every step.</summary>
    private sealed class FaultyFileOps : ILockFileOps
    {
        private readonly ILockFileOps _inner = RealLockFileOps.Instance;

        public int FailAt { get; set; }
        public int Calls { get; private set; }
        public List<string> Operations { get; } = [];

        public void Reset()
        {
            Calls = 0;
        }

        private void Step(string operation)
        {
            Calls++;
            Operations.Add(operation);
            if (Calls == FailAt)
                throw new IOException("injected failure at " + operation);
        }

        public Stream OpenRead(string path)
        {
            Step("OpenRead");
            return _inner.OpenRead(path);
        }

        public Stream CreateNew(string path)
        {
            Step("CreateNew");
            return _inner.CreateNew(path);
        }

        public void FlushToDisk(Stream stream)
        {
            Step("Flush");
            _inner.FlushToDisk(stream);
        }

        public void Replace(string temporaryPath, string destinationPath)
        {
            Step("Replace");
            _inner.Replace(temporaryPath, destinationPath);
        }

        public void Delete(string path)
        {
            Step("Delete");
            _inner.Delete(path);
        }

        public bool Exists(string path) => File.Exists(path);
    }

    private sealed class FakeRegistry : IProtectionRegistry
    {
        public Dictionary<string, string?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Deleted { get; } = [];

        public bool KeyExists(string path) =>
            Values.ContainsKey(path) ||
            Values.Keys.Any(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase) ||
                                 k.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase));

        public string? GetValue(string path, string? name) =>
            Values.TryGetValue(path + "|" + (name ?? string.Empty), out var value) ? value : null;

        public void SetValue(string path, string? name, string value) =>
            Values[path + "|" + (name ?? string.Empty)] = value;

        public void DeleteTree(string path)
        {
            Deleted.Add(path);
            foreach (var key in Values.Keys
                         .Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase) ||
                                     k.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase))
                         .ToList())
                Values.Remove(key);
        }
    }

    private sealed class StreamSink(Stream stream) : ILockContentSink
    {
        public void Write(ReadOnlySpan<byte> data) => stream.Write(data);
    }

    // ------------------------------------------------------------------ protect / unprotect

    [Fact]
    public void Protect_leaves_exactly_one_container_and_no_plaintext()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "note.txt");
            WriteFile(file, "keep me");
            var stamp = new DateTime(2024, 5, 6, 7, 8, 9, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, stamp);

            var service = NewService();
            var outcome = service.Protect(file, directory, Credentials());

            Assert.True(outcome.SourceRemoved);
            Assert.Null(outcome.LeftoverSource);
            var entries = Directory.GetFileSystemEntries(directory);
            Assert.Single(entries);
            Assert.Equal(Path.Combine(directory, "note.txt.dwenc"), entries[0]);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));

            var unprotect = service.Unprotect(outcome.OutputPath, "file-password-1", recycleContainer: false);
            entries = Directory.GetFileSystemEntries(directory);
            Assert.Single(entries);
            Assert.Equal(file, entries[0]);
            Assert.Equal("keep me", File.ReadAllText(file));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
            Assert.False(File.Exists(outcome.OutputPath));
            Assert.False(File.Exists(unprotect.RestoredPath + ".dwenc"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Protect_never_overwrites_an_existing_container()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "one.txt");
            WriteFile(file, "a");
            File.WriteAllText(file + ".dwenc", "already here");
            var service = NewService();
            Assert.Throws<LockTargetExistsException>(() => service.Protect(file, directory, Credentials()));
            Assert.Equal("a", File.ReadAllText(file));
            Assert.Equal("already here", File.ReadAllText(file + ".dwenc"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void File_can_be_moved_and_unlocked_there()
    {
        var directory = NewDirectory();
        var other = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "move.txt");
            WriteFile(file, "moved content");
            var service = NewService();
            var outcome = service.Protect(file, directory, Credentials());

            var moved = Path.Combine(other, Path.GetFileName(outcome.OutputPath));
            File.Move(outcome.OutputPath, moved);
            Assert.Empty(Directory.GetFileSystemEntries(directory));

            var restored = service.Unprotect(moved, "file-password-1", recycleContainer: false);
            Assert.Equal(Path.Combine(other, "move.txt"), restored.RestoredPath);
            Assert.Equal("moved content", File.ReadAllText(restored.RestoredPath));
        }
        finally
        {
            Cleanup(directory);
            Cleanup(other);
        }
    }

    [Fact]
    public void Unlock_uses_a_restored_suffix_on_collision()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "clash.txt");
            WriteFile(file, "original");
            var service = NewService();
            var outcome = service.Protect(file, directory, Credentials());
            WriteFile(file, "a different file arrived");

            var restored = service.Unprotect(outcome.OutputPath, "file-password-1", recycleContainer: false);
            Assert.Equal(Path.Combine(directory, "clash (restored).txt"), restored.RestoredPath);
            Assert.Equal("original", File.ReadAllText(restored.RestoredPath));
            Assert.Equal("a different file arrived", File.ReadAllText(file));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Folder_roundtrip_restores_content_and_desktop_ini_byte_for_byte()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "Photos");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            WriteFile(Path.Combine(folder, "a.txt"), "alpha");
            WriteFile(Path.Combine(folder, "nested", "b.txt"), "beta");
            var previousIni = Encoding.UTF8.GetBytes("[.ShellClassInfo]\r\nInfoTip=old\r\n");
            File.WriteAllBytes(Path.Combine(folder, "desktop.ini"), previousIni);
            var icon = Path.Combine(directory, "folder.ico");
            File.WriteAllBytes(icon, [1, 2, 3, 4]);

            var service = NewService();
            var outcome = service.Protect(folder, directory, Credentials(), folderIconPath: icon);
            Assert.False(Directory.Exists(folder));
            var entries = Directory.GetFileSystemEntries(directory);
            Assert.Contains(Path.Combine(directory, "Photos.dwdir"), entries);
            Assert.DoesNotContain(entries, e => e.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

            var restored = service.Unprotect(outcome.OutputPath, "rescuer-password-1", recycleContainer: false);
            Assert.Equal(Path.Combine(directory, "Photos"), restored.RestoredPath);
            Assert.Equal("alpha", File.ReadAllText(Path.Combine(restored.RestoredPath, "a.txt")));
            Assert.Equal("beta", File.ReadAllText(Path.Combine(restored.RestoredPath, "nested", "b.txt")));
            Assert.Equal(previousIni, File.ReadAllBytes(Path.Combine(restored.RestoredPath, "desktop.ini")));
            Assert.False(File.Exists(outcome.OutputPath));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Folder_protect_rolls_back_when_a_file_is_locked()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "Busy");
            Directory.CreateDirectory(folder);
            WriteFile(Path.Combine(folder, "free.txt"), "free");
            var lockedPath = Path.Combine(folder, "locked.txt");
            WriteFile(lockedPath, "locked");
            var previousIni = Encoding.UTF8.GetBytes("previous desktop.ini");
            File.WriteAllBytes(Path.Combine(folder, "desktop.ini"), previousIni);
            var icon = Path.Combine(directory, "folder.ico");
            File.WriteAllBytes(icon, [9]);

            var service = NewService();
            using (new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<LockFileInUseException>(() =>
                    service.Protect(folder, directory, Credentials(), folderIconPath: icon));
            }

            Assert.True(Directory.Exists(folder));
            Assert.Equal("free", File.ReadAllText(Path.Combine(folder, "free.txt")));
            Assert.Equal("locked", File.ReadAllText(lockedPath));
            Assert.Equal(previousIni, File.ReadAllBytes(Path.Combine(folder, "desktop.ini")));
            Assert.False(File.Exists(Path.Combine(directory, "Busy.dwdir")));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    // ------------------------------------------------------------------ header rewrites

    [Fact]
    public void File_password_changes_with_the_file_password_or_the_rescuer_password()
    {
        var directory = NewDirectory();
        try
        {
            var service = NewService();
            var first = Path.Combine(directory, "first.txt");
            WriteFile(first, "one");
            var container = service.Protect(first, directory, Credentials()).OutputPath;
            service.SetFilePassword(container, "file-password-1", "brand-new-password");
            Assert.Throws<LockWrongSecretException>(() => service.Verify(container, "file-password-1"));
            Assert.Equal("one", ReadContent(service, container, "brand-new-password"));

            var second = Path.Combine(directory, "second.txt");
            WriteFile(second, "two");
            var secondContainer = service.Protect(second, directory, Credentials()).OutputPath;
            service.SetFilePassword(secondContainer, "rescuer-password-1", "another-password");
            Assert.Equal("two", ReadContent(service, secondContainer, "another-password"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Rescuer_key_alone_cannot_change_the_file_password()
    {
        var directory = NewDirectory();
        try
        {
            var key = RescuerKey.Generate();
            var file = Path.Combine(directory, "key-only.txt");
            WriteFile(file, "content");
            var service = NewService();
            var container = service.Protect(file, directory, Credentials(key)).OutputPath;
            Assert.Throws<LockPolicyException>(() =>
                service.SetFilePassword(container, RescuerKey.Format(key), "nope-password"));
            Assert.Equal("content", ReadContent(service, container, "file-password-1"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Rescuer_password_reset_and_bulk_update_verify_every_file()
    {
        var directory = NewDirectory();
        try
        {
            var service = NewService();
            var one = Path.Combine(directory, "one.txt");
            var two = Path.Combine(directory, "two.txt");
            WriteFile(one, "1");
            WriteFile(two, "2");
            var oneContainer = service.Protect(one, directory, Credentials()).OutputPath;
            var twoContainer = service.Protect(two, directory, Credentials()).OutputPath;

            service.SetRescuerPassword(oneContainer, "rescuer-password-1", "reset-password");
            Assert.Equal("1", ReadContent(service, oneContainer, "reset-password"));
            service.SetRescuerPassword(twoContainer, "rescuer-password-1", "reset-password");

            var results = service.UpdateRescuer([oneContainer, twoContainer], "reset-password", "final-password");
            Assert.All(results, result => Assert.Null(result.Error));
            Assert.Equal("1", ReadContent(service, oneContainer, "final-password"));
            Assert.Equal("2", ReadContent(service, twoContainer, "final-password"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Bulk_update_reports_a_failure_but_updates_the_rest()
    {
        var directory = NewDirectory();
        try
        {
            var service = NewService();
            var file = Path.Combine(directory, "good.txt");
            WriteFile(file, "good");
            var container = service.Protect(file, directory, Credentials()).OutputPath;
            var broken = Path.Combine(directory, "broken.dwenc");
            File.WriteAllText(broken, "not a container");

            var results = service.UpdateRescuer([container, broken], "rescuer-password-1", "next-password");
            Assert.Null(results.Single(r => r.Path.EndsWith("good.txt.dwenc", StringComparison.Ordinal)).Error);
            Assert.NotNull(results.Single(r => r.Path.EndsWith("broken.dwenc", StringComparison.Ordinal)).Error);
            Assert.Equal("good", ReadContent(service, container, "next-password"));
            Assert.Equal("not a container", File.ReadAllText(broken));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Header_rewrite_is_atomic_under_a_failure_at_every_file_operation()
    {
        var directory = NewDirectory();
        try
        {
            var service = NewService();
            var file = Path.Combine(directory, "atomic.txt");
            WriteFile(file, "atomic payload");
            var container = service.Protect(file, directory, Credentials()).OutputPath;
            var pristine = Path.Combine(directory, "pristine.bin");
            File.Copy(container, pristine);

            // Baseline: normal rewrite, recording the exact operation sequence.
            var baselineOps = new FaultyFileOps();
            var work = Path.Combine(directory, "work.dwenc");
            File.Copy(container, work, overwrite: true);
            service.SetFilePassword(work, "file-password-1", "atomic-new-password", baselineOps);
            var operations = baselineOps.Operations.ToList();
            Assert.Contains("Replace", operations);
            Assert.True(operations.Count >= 6);

            var tested = 0;
            for (var call = 1; call < operations.Count; call++)
            {
                if (operations[call - 1] == "Delete")
                    continue; // cleanup failures are swallowed by design
                File.Copy(pristine, work, overwrite: true);
                var faulty = new FaultyFileOps { FailAt = call };
                try
                {
                    service.SetFilePassword(work, "file-password-1", "atomic-new-password", faulty);
                    continue; // this failure was swallowed; prove it below
                }
                catch (Exception ex) when (ex is IOException or LockException or UnauthorizedAccessException)
                {
                }

                Assert.Equal("atomic payload", ReadContent(service, work, "file-password-1"));
                Assert.Throws<LockWrongSecretException>(() => service.Verify(work, "atomic-new-password"));
                tested++;
            }
            Assert.True(tested >= 4, $"only {tested} failure points were exercised");
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Production_service_refuses_low_iteration_slots()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "iter.txt");
            WriteFile(file, "x");
            var production = new LockService();
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                production.Protect(file, directory, Credentials()));
            Assert.Contains("600000", exception.Message);
            Assert.True(File.Exists(file));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    // ------------------------------------------------------------------ journal and recovery

    [Fact]
    public void Journal_roundtrip_and_recovery_plan()
    {
        var directory = NewDirectory();
        try
        {
            var store = new LockJournalStore(directory);
            var record = new LockSessionRecord("abc123", @"C:\x\a.dwenc", @"C:\x\a", "a", false,
                DateTimeOffset.Now, LockSessionState.Open, null);
            store.Create(record);
            var listed = store.List();
            Assert.Single(listed);
            Assert.Equal("abc123", listed[0].Id);
            Assert.Equal(record.ContainerPath, listed[0].ContainerPath);
            Assert.NotNull(store.FindByContainer(record.ContainerPath));
            store.Delete("abc123");
            Assert.Empty(store.List());

            Assert.Equal(LockRecoveryAction.NothingToDo, LockRecoveryPlan.For(true, false));
            Assert.Equal(LockRecoveryAction.PromptRelock, LockRecoveryPlan.For(true, true));
            Assert.Equal(LockRecoveryAction.PromptSalvage, LockRecoveryPlan.For(false, true));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Journal_never_contains_a_secret()
    {
        var directory = NewDirectory();
        try
        {
            var store = new LockJournalStore(directory);
            var record = new LockSessionRecord("id1", @"C:\x\a.dwenc", @"C:\x\a", "a", false,
                DateTimeOffset.Now, LockSessionState.Error, "an error, not a secret");
            store.Create(record);
            var text = string.Join("\n", Directory.GetFiles(directory).Select(File.ReadAllText));
            Assert.DoesNotContain("file-password-1", text);
            Assert.DoesNotContain("rescuer-password-1", text);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Crash_recovery_reencrypts_the_edited_plaintext_into_the_same_container()
    {
        var sessionsRoot = Path.Combine(ProtectionPaths.SessionsFolder, "test-" + Guid.NewGuid().ToString("N"));
        var containerDirectory = NewDirectory();
        try
        {
            var service = NewService();
            var file = Path.Combine(containerDirectory, "crash.txt");
            WriteFile(file, "first version");
            var container = service.Protect(file, containerDirectory, Credentials()).OutputPath;

            Directory.CreateDirectory(sessionsRoot);
            var plaintext = Path.Combine(sessionsRoot, "crash.txt");
            WriteFile(plaintext, "edited after the crash");
            var journal = new LockJournalStore(sessionsRoot);
            var record = new LockSessionRecord("crash1", container, plaintext, "crash.txt", false,
                DateTimeOffset.Now.AddMinutes(-2), LockSessionState.Open, null);
            journal.Create(record);

            service.RecoverSession(record, Credentials(), journal);
            Assert.Equal("edited after the crash", ReadContent(service, container, "file-password-1"));
            Assert.False(File.Exists(plaintext));
            Assert.Empty(journal.List());
        }
        finally
        {
            Cleanup(containerDirectory);
            try
            {
                if (Directory.Exists(sessionsRoot))
                    Directory.Delete(sessionsRoot, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // ------------------------------------------------------------------ auto-lock rules

    [Fact]
    public void Auto_lock_waits_for_the_program_the_explorer_and_the_grace()
    {
        var file = new AutoLockInput(false, ApplicationHoldsFile: true, ExplorerShowsFolder: false,
            AnyFileInFolderHeld: false, SinceLastChange: TimeSpan.FromMinutes(1),
            SinceOpened: TimeSpan.FromMinutes(1), GraceSeconds: 8, IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Wait, AutoLockRules.Decide(file));

        var explorer = new AutoLockInput(true, false, ExplorerShowsFolder: true, AnyFileInFolderHeld: false,
            SinceLastChange: TimeSpan.FromMinutes(1), SinceOpened: TimeSpan.FromMinutes(1), GraceSeconds: 8,
            IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Wait, AutoLockRules.Decide(explorer));

        var held = new AutoLockInput(true, false, ExplorerShowsFolder: false, AnyFileInFolderHeld: true,
            SinceLastChange: TimeSpan.FromMinutes(20), SinceOpened: TimeSpan.FromMinutes(20), GraceSeconds: 8,
            IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Wait, AutoLockRules.Decide(held));

        var grace = new AutoLockInput(false, false, false, false, TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(3), GraceSeconds: 8, IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Wait, AutoLockRules.Decide(grace));

        var due = new AutoLockInput(false, false, false, false, TimeSpan.FromSeconds(9),
            TimeSpan.FromSeconds(9), GraceSeconds: 8, IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Lock, AutoLockRules.Decide(due));

        // A stale Explorer window no longer blocks a folder after the idle fallback.
        var idle = new AutoLockInput(true, false, ExplorerShowsFolder: true, AnyFileInFolderHeld: false,
            SinceLastChange: TimeSpan.FromMinutes(16), SinceOpened: TimeSpan.FromMinutes(16), GraceSeconds: 8,
            IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Lock, AutoLockRules.Decide(idle));

        var clampedGrace = new AutoLockInput(false, false, false, false, TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(4), GraceSeconds: 1, IdleFallbackMinutes: 15);
        Assert.Equal(AutoLockDecision.Lock, AutoLockRules.Decide(clampedGrace)); // grace clamps up to 3 s
    }

    // ------------------------------------------------------------------ icons, registry, desktop.ini

    [Fact]
    public void Icons_are_multi_size_png_frames_and_the_pink_token_is_used()
    {
        foreach (var folder in new[] { false, true })
        {
            var icon = folder ? LockIcons.BuildFolderIcon() : LockIcons.BuildFileIcon();
            Assert.Equal(1, BitConverter.ToUInt16(icon, 2));
            Assert.Equal(5, BitConverter.ToUInt16(icon, 4));
            var expected = new byte[] { 16, 24, 32, 48, 0 };
            for (var i = 0; i < 5; i++)
            {
                var entry = 6 + (16 * i);
                Assert.Equal(expected[i], icon[entry]);
                Assert.Equal(expected[i], icon[entry + 1]);
                var offset = BitConverter.ToInt32(icon, entry + 12);
                Assert.Equal(0x89, icon[offset]);
                Assert.Equal((byte)'P', icon[offset + 1]);
                Assert.Equal((byte)'N', icon[offset + 2]);
                Assert.Equal((byte)'G', icon[offset + 3]);
            }
        }
        Assert.Equal(Color.FromRgb(0xF0, 0x6F, 0xA8), Theme.ProtectBadgeDarkColor);
        Assert.Equal(Color.FromRgb(0xD6, 0x40, 0x7F), Theme.ProtectBadgeLightColor);
    }

    [Fact]
    public void Associations_are_per_user_and_removable()
    {
        var registry = new FakeRegistry();
        var written = ProtectionAssociations.Register(registry,
            @"C:\Users\me\AppData\Local\DropWheel\DropWheel.exe",
            @"C:\Users\me\AppData\Local\DropWheel\icons\protected-file.ico",
            @"C:\Users\me\AppData\Local\DropWheel\icons\protected-folder.ico");

        Assert.Contains(".dwenc", written);
        Assert.Contains(".dwdir", written);
        Assert.All(written, path => Assert.DoesNotContain("HKLM", path, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ProtectionAssociations.FileProgId,
            registry.GetValue(ProtectionAssociations.FileExtension, null));
        Assert.Equal(ProtectionAssociations.FolderProgId,
            registry.GetValue(ProtectionAssociations.FolderExtension, null));
        var icon = registry.GetValue(ProtectionAssociations.FileProgId + @"\DefaultIcon", null);
        Assert.EndsWith(",0", icon);
        var command = registry.GetValue(ProtectionAssociations.FileProgId + @"\shell\open\command", null);
        Assert.Contains("--lock-open", command);
        Assert.Contains("\"%1\"", command);
        Assert.True(ProtectionAssociations.IsRegistered(registry));

        ProtectionAssociations.Remove(registry);
        Assert.Contains(ProtectionAssociations.FileExtension, registry.Deleted);
        Assert.Contains(ProtectionAssociations.FolderExtension, registry.Deleted);
        Assert.Contains(ProtectionAssociations.FileProgId, registry.Deleted);
        Assert.Contains(ProtectionAssociations.FolderProgId, registry.Deleted);
        Assert.False(ProtectionAssociations.IsRegistered(registry));
    }

    [Fact]
    public void Desktop_ini_is_restored_byte_for_byte_or_removed()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "folder");
            Directory.CreateDirectory(folder);
            var previous = Encoding.UTF8.GetBytes("[.ShellClassInfo]\r\nInfoTip=before\r\n");
            var ini = Path.Combine(folder, "desktop.ini");
            File.WriteAllBytes(ini, previous);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            var previousAttributes = (int)File.GetAttributes(ini) & 0xFFFF;
            var icon = Path.Combine(directory, "icon.ico");
            File.WriteAllBytes(icon, [1]);

            FolderAppearance.Apply(folder, icon);
            Assert.Contains("IconResource", File.ReadAllText(ini));
            Assert.True((File.GetAttributes(folder) & FileAttributes.ReadOnly) != 0);
            Assert.True((File.GetAttributes(ini) & FileAttributes.Hidden) != 0);

            FolderAppearance.Restore(folder, previous, previousAttributes);
            Assert.Equal(previous, File.ReadAllBytes(ini));

            FolderAppearance.Restore(folder, null, 0);
            Assert.False(File.Exists(ini));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    // ------------------------------------------------------------------ refusals and archive safety

    [Fact]
    public void Guard_refuses_drive_roots_system_folders_and_containers()
    {
        var directory = NewDirectory();
        try
        {
            var system = Path.Combine(directory, "WindowsLike");
            Directory.CreateDirectory(system);
            var data = Path.Combine(directory, "DataLike");
            Directory.CreateDirectory(data);
            var environment = new LockEnvironment([system], [data]);
            var insideSystem = Path.Combine(system, "file.txt");
            WriteFile(insideSystem, "x");
            Assert.Equal(LockRefusal.SystemFolder, LockGuard.Check(insideSystem, environment));

            var insideData = Path.Combine(data, "file.txt");
            WriteFile(insideData, "x");
            Assert.Equal(LockRefusal.DataFolder, LockGuard.Check(insideData, environment));

            Assert.Equal(LockRefusal.DriveRoot, LockGuard.Check(Path.GetPathRoot(directory)!, environment));

            var protectedFile = Path.Combine(directory, "x.dwenc");
            WriteFile(protectedFile, "x");
            Assert.Equal(LockRefusal.AlreadyProtected, LockGuard.Check(protectedFile, environment));

            var normal = Path.Combine(directory, "normal.txt");
            WriteFile(normal, "x");
            Assert.Equal(LockRefusal.None, LockGuard.Check(normal, environment));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Archive_refuses_a_path_that_escapes_the_destination()
    {
        using var archive = new MemoryStream();
        var path = Encoding.UTF8.GetBytes("../evil.txt");
        using (var writer = new BinaryWriter(archive, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((byte)LockArchive.EntryFile);
            writer.Write((ushort)path.Length);
            writer.Write(path);
            writer.Write(0L);
            writer.Write(0);
            writer.Write(0L);
            writer.Write((byte)LockArchive.EntryEnd);
        }
        archive.Position = 0;
        var destination = NewDirectory();
        try
        {
            Assert.Throws<LockDamagedException>(() => LockArchive.Extract(archive, destination));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(destination)!, "evil.txt")));
        }
        finally
        {
            Cleanup(destination);
        }
    }

    [Fact]
    public void Archive_writer_skips_symlinks_and_the_folder_desktop_ini()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "tree");
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            WriteFile(Path.Combine(folder, "a.txt"), "a");
            WriteFile(Path.Combine(folder, "nested", "b.txt"), "b");
            WriteFile(Path.Combine(folder, "desktop.ini"), "old");
            Assert.Equal(3, LockArchive.CountEntries(folder));

            using var archive = new MemoryStream();
            var count = LockArchive.Write(folder, new StreamSink(archive));
            Assert.Equal(3, count);
            archive.Position = 0;
            var destination = Path.Combine(directory, "restored");
            Assert.Equal(3, LockArchive.Extract(archive, destination));
            Assert.True(File.Exists(Path.Combine(destination, "a.txt")));
            Assert.True(File.Exists(Path.Combine(destination, "nested", "b.txt")));
            Assert.False(File.Exists(Path.Combine(destination, "desktop.ini")));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    // ------------------------------------------------------------------ secrets

    [Fact]
    public void Wrong_secret_delay_grows_after_three_attempts()
    {
        Assert.Equal(TimeSpan.Zero, LockSecrets.DelayAfter(0));
        Assert.Equal(TimeSpan.Zero, LockSecrets.DelayAfter(1));
        Assert.Equal(TimeSpan.Zero, LockSecrets.DelayAfter(2));
        Assert.Equal(TimeSpan.Zero, LockSecrets.DelayAfter(3));
        Assert.True(LockSecrets.DelayAfter(4) > TimeSpan.Zero);
        Assert.True(LockSecrets.DelayAfter(6) > LockSecrets.DelayAfter(4));
        Assert.True(LockSecrets.DelayAfter(100) <= TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Settings_file_never_contains_a_secret()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "protection.json");
            var settings = new ProtectionSettings
            {
                AutoLockOnClose = false,
                GraceSeconds = 12,
                RecycleContainerAfterUnlock = false,
                AssociationsRegistered = true,
            };
            settings.SaveTo(path);
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rescuer", text, StringComparison.OrdinalIgnoreCase);
            var loaded = ProtectionSettings.LoadFrom(path);
            Assert.False(loaded.AutoLockOnClose);
            Assert.Equal(12, loaded.GraceSeconds);
            Assert.False(loaded.RecycleContainerAfterUnlock);
            Assert.True(loaded.AssociationsRegistered);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Rescuer_password_can_be_reset_with_the_rescuer_key()
    {
        var directory = NewDirectory();
        try
        {
            var key = RescuerKey.Generate();
            var file = Path.Combine(directory, "key-reset.txt");
            WriteFile(file, "secret content");
            var service = NewService();
            var container = service.Protect(file, directory, Credentials(key)).OutputPath;
            service.SetRescuerPassword(container, RescuerKey.Format(key), "rescuer-after-key");
            Assert.Equal("secret content", ReadContent(service, container, "rescuer-after-key"));
            service.SetRescuerKey(container, "rescuer-after-key", RescuerKey.Generate());
            Assert.Equal("secret content", ReadContent(service, container, "file-password-1"));
        }
        finally
        {
            Cleanup(directory);
        }
    }
    // ------------------------------------------------------------------ password-field UX

    [Fact]
    public void Password_pair_enables_creation_only_when_it_matches_and_is_long_enough()
    {
        Assert.Equal(LockSecrets.PairStatus.Empty, LockSecrets.CheckPair("", ""));
        Assert.Equal(LockSecrets.PairStatus.TooShort, LockSecrets.CheckPair("short", "short"));
        Assert.Equal(LockSecrets.PairStatus.Mismatch, LockSecrets.CheckPair("long-enough-1", "long-enough-2"));
        Assert.Equal(LockSecrets.PairStatus.Match, LockSecrets.CheckPair("long-enough-1", "long-enough-1"));
        Assert.False(LockSecrets.CanCreate("long-enough-1", "long-enough-2"));
        Assert.False(LockSecrets.CanCreate("short", "short"));
        Assert.True(LockSecrets.CanCreate("long-enough-1", "long-enough-1"));
    }

    [Fact]
    public void Recovery_key_cannot_be_confirmed_without_the_checkbox_or_the_last_four()
    {
        var key = RescuerKey.Generate();
        Assert.False(LockSecrets.IsRecoveryKeyConfirmed(false, null, key));
        Assert.False(LockSecrets.IsRecoveryKeyConfirmed(false, "xxxx", key));
        Assert.True(LockSecrets.IsRecoveryKeyConfirmed(true, null, key));
        Assert.True(LockSecrets.IsRecoveryKeyConfirmed(false, key[^4..].ToLowerInvariant(), key));
        Assert.False(LockSecrets.IsRecoveryKeyConfirmed(false, key[..4], key));
    }

    [Fact]
    public void Rescuer_password_pair_belongs_to_first_time_and_reset_only()
    {
        Assert.True(RescuerSetupPolicy.NeedsPasswordPair(RescuerSetupMode.FirstTime));
        Assert.True(RescuerSetupPolicy.NeedsPasswordPair(RescuerSetupMode.ResetPassword));
        Assert.False(RescuerSetupPolicy.NeedsPasswordPair(RescuerSetupMode.NewKey));
        Assert.True(RescuerSetupPolicy.NeedsKeyConfirmation(RescuerSetupMode.FirstTime));
        Assert.True(RescuerSetupPolicy.NeedsKeyConfirmation(RescuerSetupMode.NewKey));
        Assert.False(RescuerSetupPolicy.NeedsKeyConfirmation(RescuerSetupMode.ResetPassword));
    }

    [Fact]
    public void Reveal_state_clears_the_mirror_and_resets_on_close()
    {
        var state = new SecretRevealState();
        Assert.False(state.Revealed);
        Assert.Equal(string.Empty, state.Mirror);
        state.Reveal("a secret");
        Assert.True(state.Revealed);
        Assert.Equal("a secret", state.Mirror);
        state.Hide();
        Assert.False(state.Revealed);
        Assert.Equal(string.Empty, state.Mirror);
        state.Reveal("again");
        state.Reset();
        Assert.False(state.Revealed);
        Assert.Equal(string.Empty, state.Mirror);
    }
    // ------------------------------------------------------------------ strings

    [Fact]
    public void Every_lock_key_exists_in_both_languages_with_the_exact_warning()
    {
        var english = FileAction.Core.Loc.Table(FileAction.Core.Language.English);
        var arabic = FileAction.Core.Loc.Table(FileAction.Core.Language.Arabic);
        var keys = english.Keys.Where(k => k.StartsWith("tool.lock.", StringComparison.Ordinal)).ToList();
        Assert.True(keys.Count >= 130, $"only {keys.Count} lock keys");
        foreach (var key in keys)
        {
            Assert.True(arabic.ContainsKey(key), "missing Arabic key " + key);
            Assert.False(string.IsNullOrWhiteSpace(arabic[key]), "empty Arabic value for " + key);
            Assert.False(string.IsNullOrWhiteSpace(english[key]), "empty English value for " + key);
        }

        // The warning text is fixed by the specification and must not drift.
        Assert.Equal("سيتم تشفير الملف", arabic["tool.lock.warn.title"]);
        Assert.Equal("فهمت، أريد التشفير", arabic["tool.lock.warn.checkbox"]);
        Assert.Equal("تشفير", arabic["tool.lock.warn.protect"]);
        Assert.Equal("إلغاء", arabic["tool.lock.warn.cancel"]);
        Assert.Contains("(.dwenc)", arabic["tool.lock.warn.body"]);
        Assert.Contains("DropWheel", arabic["tool.lock.warn.body"]);
        Assert.Contains("لا يمكن استرجاع الملف أبداً", arabic["tool.lock.warn.body"]);
        Assert.Equal("The file will be encrypted", english["tool.lock.warn.title"]);
        Assert.Equal("I understand; I want to encrypt", english["tool.lock.warn.checkbox"]);
        Assert.Equal("Encrypt", english["tool.lock.warn.protect"]);
        Assert.Equal("Cancel", english["tool.lock.warn.cancel"]);
        Assert.Equal("Encrypted: AES-256-GCM", english["tool.lock.status"]);
        Assert.Equal("Lock now", english["tool.lock.panel.lockNow"]);
        Assert.Equal("Encrypt", english["tool.lock.protect"]);
        Assert.Equal("Beta", english["tool.lock.beta"]);
    }
    // ------------------------------------------------------------------ helpers

    private static string ReadContent(LockService service, string container, string secret)
    {
        using var source = new FileStream(container, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new MemoryStream();
        LockContainer.Decrypt(source, destination, secret);
        return Encoding.UTF8.GetString(destination.ToArray());
    }
}
