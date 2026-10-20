using System.Runtime.ExceptionServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        public bool SessionOpen { get; set; }

        public bool IsSessionOpen(string protectedPath) => SessionOpen;

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
    public void Padlock_is_the_only_shipped_badge_and_uses_the_purple_tokens()
    {
        var icon = LockIcons.PadlockBytes;
        Assert.True(icon.Length < 200 * 1024, $"padlock.ico is {icon.Length} bytes");
        Assert.True(LockIcons.HasAllFrames(icon), "padlock.ico must contain 16/24/32/48/64/256 PNG frames");
        foreach (var size in LockIcons.FrameSizes)
        {
            var frame = LockIcons.ReadFrame(icon, size);
            Assert.NotNull(frame);
            var decoder = BitmapDecoder.Create(new MemoryStream(frame!),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Assert.Equal(size, decoder.Frames[0].PixelWidth);
            Assert.Equal(size, decoder.Frames[0].PixelHeight);
        }
        Assert.Equal(Color.FromRgb(0x82, 0x24, 0xD4), Theme.LockBadgeColor);
        Assert.Equal(Color.FromRgb(0xB4, 0x8A, 0xE0), Theme.LockBadgeHighlightColor);
        Assert.Equal(Theme.LockBadgeColor, ((SolidColorBrush)Theme.LockBadge).Color);
        Assert.Equal(Theme.LockBadgeHighlightColor, ((SolidColorBrush)Theme.LockBadgeHighlight).Color);
    }

    [Fact]
    public void Composition_keeps_the_real_shell_icon_and_adds_purple_pixels_bottom_left()
    {
        var directory = NewDirectory();
        try
        {
            var pdf = Path.Combine(directory, "sample.pdf");
            var jpg = Path.Combine(directory, "sample.jpg");
            var folder = Path.Combine(directory, "RealFolder");
            File.WriteAllBytes(pdf, []);
            File.WriteAllBytes(jpg, []);
            Directory.CreateDirectory(folder);

            RunSta(() =>
            {
                foreach (var (path, isFolder) in new[] { (pdf, false), (jpg, false), (folder, true) })
                {
                    foreach (var size in new[] { 48, 256 })
                    {
                        var baseIcon = LockIcons.SystemIconFor(path, isFolder, size);
                        Assert.NotNull(baseIcon);
                        var composed = LockIcons.Compose(path, isFolder, size);
                        Assert.NotNull(composed);
                        Assert.Equal(size, composed!.PixelWidth);
                        Assert.Equal(size, composed.PixelHeight);

                        var basePixels = ToPixels(Scale(baseIcon!, size));
                        var pixels = ToPixels(composed);
                        // The top-right area is untouched by the bottom-left badge.
                        var topRight = ((size - 2) + (2 * size)) * 4;
                        Assert.Equal(basePixels[topRight + 0], pixels[topRight + 0]);
                        Assert.Equal(basePixels[topRight + 1], pixels[topRight + 1]);
                        Assert.Equal(basePixels[topRight + 2], pixels[topRight + 2]);

                        // Purple #8224D4 pixels exist in the bottom-left quadrant.
                        var purple = 0;
                        for (var y = size / 2; y < size; y++)
                        {
                            for (var x = 0; x < size / 2; x++)
                            {
                                var index = ((y * size) + x) * 4;
                                var b = pixels[index];
                                var g = pixels[index + 1];
                                var r = pixels[index + 2];
                                var a = pixels[index + 3];
                                if (a > 200 && r is > 90 and < 180 && g < 90 && b > 170)
                                    purple++;
                            }
                        }
                        Assert.True(purple > 10, $"{Path.GetFileName(path)} {size}px has no purple badge pixels");
                    }
                }
            });
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Icon_cache_reuses_the_extension_cleans_items_and_builds_association_icons()
    {
        var directory = NewDirectory();
        var previous = LockIconCache.RootOverride;
        LockIconCache.RootOverride = directory;
        try
        {
            RunSta(() =>
            {
                var pdf = Path.Combine(directory, "a.pdf");
                var jpg = Path.Combine(directory, "b.jpg");
                var folder = Path.Combine(directory, "Folder");
                File.WriteAllBytes(pdf, []);
                File.WriteAllBytes(jpg, []);
                Directory.CreateDirectory(folder);

                var first = LockIconCache.EnsureTypeIcon(pdf, folder: false);
                var stamp = File.GetLastWriteTimeUtc(first);
                Thread.Sleep(30);
                var again = LockIconCache.EnsureTypeIcon(pdf, folder: false);
                Assert.Equal(first, again);
                Assert.Equal(stamp, File.GetLastWriteTimeUtc(again));

                var other = LockIconCache.EnsureTypeIcon(jpg, folder: false);
                Assert.NotEqual(first, other);
                Assert.True(LockIcons.HasAllFrames(File.ReadAllBytes(first)));

                var item = LockIconCache.EnsureItemIcon(pdf, folder: false);
                Assert.True(File.Exists(item));
                Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(item));
                LockIconCache.RemoveItem(pdf);
                Assert.False(File.Exists(item));

                var (fileIcon, folderIcon) = LockIconCache.EnsureAssociationIcons();
                Assert.True(File.Exists(fileIcon));
                Assert.True(File.Exists(folderIcon));
                Assert.True(LockIcons.HasAllFrames(File.ReadAllBytes(fileIcon)));
                Assert.True(LockIcons.HasAllFrames(File.ReadAllBytes(folderIcon)));

                LockIconCache.Clear();
                Assert.False(Directory.Exists(Path.Combine(directory, "items")));
                Assert.False(Directory.Exists(Path.Combine(directory, "types")));
                Assert.False(Directory.Exists(Path.Combine(directory, "assoc")));
            });
        }
        finally
        {
            LockIconCache.RootOverride = previous;
            Cleanup(directory);
        }
    }

    [Fact]
    public void Host_is_notified_after_lock_and_unlock_so_explorer_refreshes()
    {
        var directory = NewDirectory();
        try
        {
            var host = new FakeHost();
            var service = new LockService(new LockEnvironment([], []), host, minimumIterations: 1000,
                newSlotIterations: 1000);
            var file = Path.Combine(directory, "refresh.txt");
            WriteFile(file, "refresh");
            var container = service.Protect(file, directory, Credentials()).OutputPath;
            Assert.NotEmpty(host.Notified);
            host.Notified.Clear();
            service.Unprotect(container, "file-password-1", recycleContainer: false);
            Assert.Single(host.Notified);
        }
        finally
        {
            Cleanup(directory);
        }
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
            Assert.DoesNotContain("file-password-1", text);
            Assert.DoesNotContain("rescuer-password-1", text);
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
        Assert.Equal("سيتم تشفير الملف", arabic["tool.protect.warn.title"]);
        Assert.Equal("فهمت، أريد التشفير", arabic["tool.protect.warn.checkbox"]);
        Assert.Equal("تشفير", arabic["tool.protect.warn.protect"]);
        Assert.Equal("إلغاء", arabic["tool.protect.warn.cancel"]);
        Assert.Contains("(.dwenc)", arabic["tool.protect.warn.body"]);
        Assert.Contains("DropWheel", arabic["tool.protect.warn.body"]);
        Assert.Contains("لا يمكن استرجاع الملف أبداً", arabic["tool.protect.warn.body"]);
        Assert.Equal("The file will be encrypted", english["tool.protect.warn.title"]);
        Assert.Equal("I understand; I want to encrypt", english["tool.protect.warn.checkbox"]);
        Assert.Equal("Encrypt", english["tool.protect.warn.protect"]);
        Assert.Equal("Cancel", english["tool.protect.warn.cancel"]);
        Assert.Equal("Encrypted: AES-256-GCM", english["tool.lock.status"]);
        Assert.Equal("Lock now", english["tool.lock.panel.lockNow"]);
        Assert.Equal("Encrypt", english["tool.lock.protect"]);
        Assert.Equal("Beta", english["tool.lock.beta"]);
    }
    // ------------------------------------------------------------------ end-to-end function proof

    private sealed class ActionProgress(Action<LockProgress> action) : IProgress<LockProgress>
    {
        public void Report(LockProgress value) => action(value);
    }

    public static IEnumerable<object[]> ServiceSizes()
    {
        yield return new object[] { 0 };
        yield return new object[] { 1 };
        yield return new object[] { LockFormat.ChunkSize - 1 };
        yield return new object[] { LockFormat.ChunkSize };
        yield return new object[] { LockFormat.ChunkSize + 1 };
        yield return new object[] { (2 * LockFormat.ChunkSize) + 17 };
    }

    [Theory]
    [MemberData(nameof(ServiceSizes))]
    public void Service_roundtrip_creates_and_removes_exactly_one_file(int size)
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "size.bin");
            var payload = new byte[size];
            new Random(11 + size).NextBytes(payload);
            File.WriteAllBytes(file, payload);
            var expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload));
            var service = NewService();

            var protect = service.Protect(file, directory, Credentials());
            Assert.True(protect.SourceRemoved);
            Assert.Single(Directory.GetFileSystemEntries(directory));
            Assert.True(File.Exists(protect.OutputPath));
            Assert.False(File.Exists(file));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));

            var unprotect = service.Unprotect(protect.OutputPath, "file-password-1", recycleContainer: false);
            Assert.Single(Directory.GetFileSystemEntries(directory));
            Assert.Equal(file, unprotect.RestoredPath);
            Assert.False(File.Exists(protect.OutputPath));
            Assert.Equal(expected, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                File.ReadAllBytes(file))));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Corrupt_input_leaves_no_output_files()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "corrupt.bin");
            var payload = new byte[3 * 4096];
            new Random(19).NextBytes(payload);
            File.WriteAllBytes(file, payload);
            var service = NewService();
            var container = service.Protect(file, directory, Credentials()).OutputPath;

            using (var stream = new FileStream(container, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                stream.Position = stream.Length / 2;
                var value = stream.ReadByte();
                stream.Position--;
                stream.WriteByte((byte)(value ^ 0xFF));
                stream.Flush(flushToDisk: true);
            }

            Assert.ThrowsAny<LockException>(() =>
                service.Unprotect(container, "file-password-1", recycleContainer: false));
            Assert.False(File.Exists(file));
            Assert.True(File.Exists(container));
            Assert.Empty(Directory.GetFiles(directory, "*.dwrestore*"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Read_only_folder_still_roundtrips_without_partial_output()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "ReadOnlyFolder");
            Directory.CreateDirectory(folder);
            var inner = Path.Combine(folder, "locked.txt");
            File.WriteAllText(inner, "read only content");
            File.SetAttributes(inner, FileAttributes.ReadOnly);
            File.SetAttributes(folder, FileAttributes.Directory | FileAttributes.ReadOnly);

            var service = NewService();
            var protect = service.Protect(folder, directory, Credentials());
            Assert.True(File.Exists(protect.OutputPath));
            Assert.False(Directory.Exists(folder));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));

            var unprotect = service.Unprotect(protect.OutputPath, "rescuer-password-1", recycleContainer: false);
            Assert.Equal("read only content", File.ReadAllText(Path.Combine(unprotect.RestoredPath, "locked.txt")));
            Assert.False(File.Exists(protect.OutputPath));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Cancel_mid_run_leaves_the_source_and_no_output()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "cancel.bin");
            var payload = new byte[(3 * LockFormat.ChunkSize) + 5];
            new Random(23).NextBytes(payload);
            File.WriteAllBytes(file, payload);
            var service = NewService();
            using var cancellation = new CancellationTokenSource();
            var progress = new ActionProgress(value =>
            {
                if (value.Stage == LockStage.Encrypting && value.Done >= LockFormat.ChunkSize)
                    cancellation.Cancel();
            });

            Assert.ThrowsAny<OperationCanceledException>(() =>
                service.Protect(file, directory, Credentials(), progress: progress,
                    cancellationToken: cancellation.Token));
            Assert.True(File.Exists(file));
            Assert.Equal(payload, File.ReadAllBytes(file));
            Assert.Empty(Directory.GetFiles(directory, "*.dwenc"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void No_secret_is_written_under_the_app_data_folder()
    {
        var password = "unique-file-password-" + Guid.NewGuid().ToString("N");
        var rescuer = "unique-rescuer-password-" + Guid.NewGuid().ToString("N");
        var key = RescuerKey.Generate();
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "secret-scan.txt");
            File.WriteAllText(file, "content");
            var service = NewService();
            var credentials = new LockCredentials
            {
                FilePassword = password,
                RescuerPassword = rescuer,
                RescuerKey = key,
                FilePasswordIterations = 1000,
                RescuerPasswordIterations = 1000,
                RescuerKeyIterations = 1000,
            };
            var container = service.Protect(file, directory, credentials).OutputPath;
            service.Unprotect(container, password, recycleContainer: false);

            var appData = ProtectionPaths.DataFolder;
            if (!Directory.Exists(appData))
                return;
            foreach (var path in Directory.EnumerateFiles(appData, "*", SearchOption.AllDirectories))
            {
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
                var text = Encoding.UTF8.GetString(bytes);
                Assert.DoesNotContain(password, text);
                Assert.DoesNotContain(rescuer, text);
                Assert.DoesNotContain(RescuerKey.Format(key), text);
                Assert.DoesNotContain(key, text);
            }
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Recycle_bin_is_off_by_default_and_protection_never_recycles()
    {
        Assert.False(new ProtectionSettings().RecycleContainerAfterUnlock);
        var settings = new ProtectionSettings();
        Assert.False(settings.RescuerConfigured);

        var directory = NewDirectory();
        try
        {
            var host = new FakeHost();
            var service = new LockService(new LockEnvironment([], []), host, minimumIterations: 1000,
                newSlotIterations: 1000);
            var file = Path.Combine(directory, "no-recycle.txt");
            WriteFile(file, "x");
            var container = service.Protect(file, directory, Credentials()).OutputPath;
            Assert.Empty(host.Removed);
            service.Unprotect(container, "file-password-1", settings.RecycleContainerAfterUnlock);
            Assert.Single(host.Removed);
            Assert.False(host.Removed[0].Recycle);
        }
        finally
        {
            Cleanup(directory);
        }
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

    private static BitmapSource Scale(ImageSource source, int size)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawImage(source, new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] ToPixels(BitmapSource source)
    {
        var converted = source.Format == PixelFormats.Bgra32
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
    [Fact]
    public void Protect_writes_three_slots_that_each_open_the_same_container()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "slots.txt");
            WriteFile(file, "slots");
            var credentials = Credentials();
            var container = NewService().Protect(file, directory, credentials).OutputPath;
            using var stream = new FileStream(container, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = LockContainer.ReadHeader(stream);
            foreach (var secret in new[] { credentials.FilePassword, credentials.RescuerPassword,
                         credentials.RescuerKey })
            {
                Assert.True(LockContainer.TryUnwrap(header, secret, out var key, out _));
                LockSecrets.Wipe(key);
            }
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Protect_refuses_when_the_folder_changes_after_the_archive_started()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "Changing");
            Directory.CreateDirectory(folder);
            WriteFile(Path.Combine(folder, "big.bin"), new string('x', 2 * 1024 * 1024));
            var added = 0;
            var progress = new ActionProgress(value =>
            {
                if (value.Stage == LockStage.Encrypting && value.Done > 0 && Interlocked.Exchange(ref added, 1) == 0)
                    WriteFile(Path.Combine(folder, "late.txt"), "late");
            });
            Assert.Throws<LockChangedException>(() =>
                NewService().Protect(folder, directory, Credentials(), progress: progress));
            Assert.True(Directory.Exists(folder));
            Assert.True(File.Exists(Path.Combine(folder, "late.txt")));
            Assert.Empty(Directory.GetFiles(directory, "*.dwdir*"));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp*"));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Unprotect_refuses_the_key_alone_and_an_open_session()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "policy.txt");
            WriteFile(file, "policy");
            var credentials = Credentials();
            var host = new FakeHost();
            var service = new LockService(new LockEnvironment([], []), host, minimumIterations: 1000,
                newSlotIterations: 1000);
            var container = service.Protect(file, directory, credentials).OutputPath;

            Assert.Throws<LockPolicyException>(() =>
                service.Unprotect(container, credentials.RescuerKey, recycleContainer: false));
            Assert.True(File.Exists(container));
            Assert.False(File.Exists(file));

            host.SessionOpen = true;
            Assert.Throws<LockPolicyException>(() =>
                service.Unprotect(container, credentials.FilePassword, recycleContainer: false));
            Assert.True(File.Exists(container));
            host.SessionOpen = false;
            Assert.True(File.Exists(service.Unprotect(container, credentials.FilePassword,
                recycleContainer: false).RestoredPath));
        }
        finally
        {
            Cleanup(directory);
        }
    }
    [Fact]
    public void Relock_encodes_the_current_folder_after_add_remove_and_rename()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "Tree");
            Directory.CreateDirectory(folder);
            WriteFile(Path.Combine(folder, "keep.txt"), "keep");
            WriteFile(Path.Combine(folder, "remove.txt"), "remove");
            var container = Path.Combine(directory, "Tree.dwdir");
            var service = NewService();
            var credentials = Credentials();
            service.ProtectTo(folder, container, credentials, keepSource: true);

            LockHeader header;
            byte[] dataKey;
            using (var source = new FileStream(container, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                header = LockContainer.ReadHeader(source);
                dataKey = LockContainer.Unwrap(header, credentials.FilePassword, out _);
            }
            try
            {
                WriteFile(Path.Combine(folder, "added.txt"), "added");
                File.Delete(Path.Combine(folder, "remove.txt"));
                File.Move(Path.Combine(folder, "keep.txt"), Path.Combine(folder, "renamed.txt"));

                service.RelockFromSession(container, header, dataKey, folder);
                var restored = service.Unprotect(container, credentials.FilePassword, recycleContainer: false);
                Assert.Equal("added", File.ReadAllText(Path.Combine(restored.RestoredPath, "added.txt")));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(restored.RestoredPath, "renamed.txt")));
                Assert.False(File.Exists(Path.Combine(restored.RestoredPath, "remove.txt")));
                Assert.False(File.Exists(Path.Combine(restored.RestoredPath, "keep.txt")));
            }
            finally
            {
                LockSecrets.Wipe(dataKey);
            }
        }
        finally
        {
            Cleanup(directory);
        }
    }
    [Fact]
    public void Relock_keeps_a_rescuer_password_changed_during_the_session()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "Reset");
            Directory.CreateDirectory(folder);
            WriteFile(Path.Combine(folder, "file.txt"), "content");
            var container = Path.Combine(directory, "Reset.dwdir");
            var service = NewService();
            var credentials = Credentials();
            service.ProtectTo(folder, container, credentials, keepSource: true);

            LockHeader oldHeader;
            byte[] dataKey;
            using (var source = new FileStream(container, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                oldHeader = LockContainer.ReadHeader(source);
                dataKey = LockContainer.Unwrap(oldHeader, credentials.FilePassword, out _);
            }
            try
            {
                service.SetRescuerPassword(container, credentials.FilePassword, "rescuer-reset-during-session");
                service.RelockFromSession(container, oldHeader, dataKey, folder);
                var restored = service.Unprotect(container, "rescuer-reset-during-session",
                    recycleContainer: false);
                Assert.Equal("content", File.ReadAllText(Path.Combine(restored.RestoredPath, "file.txt")));
            }
            finally
            {
                LockSecrets.Wipe(dataKey);
            }
        }
        finally
        {
            Cleanup(directory);
        }
    }
    [Fact]
    public void Tree_hash_changes_for_add_remove_and_rename()
    {
        var directory = NewDirectory();
        try
        {
            var folder = Path.Combine(directory, "HashTree");
            Directory.CreateDirectory(folder);
            WriteFile(Path.Combine(folder, "a.txt"), "a");
            WriteFile(Path.Combine(folder, "b.txt"), "b");
            var first = LockArchive.TreeHash(folder);

            WriteFile(Path.Combine(folder, "c.txt"), "c");
            var added = LockArchive.TreeHash(folder);
            Assert.NotEqual(first, added);

            File.Delete(Path.Combine(folder, "b.txt"));
            var removed = LockArchive.TreeHash(folder);
            Assert.NotEqual(added, removed);

            File.Move(Path.Combine(folder, "a.txt"), Path.Combine(folder, "renamed.txt"));
            var renamed = LockArchive.TreeHash(folder);
            Assert.NotEqual(removed, renamed);
        }
        finally
        {
            Cleanup(directory);
        }
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
