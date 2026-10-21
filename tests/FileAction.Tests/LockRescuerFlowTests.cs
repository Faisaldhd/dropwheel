using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FileAction.App.Shell.Protection;
using FileAction.Core.Lock;

namespace FileAction.Tests;

/// <summary>
/// The final Rescuer flow: DPAPI persistence (with the in-memory fallback), provisional credentials for
/// the very first Lock, adopting the real pair after setup, and the "key must have opened a file" gate on
/// rotation. Everything writes inside the checkout's scratch area, never the profile.
/// </summary>
[Collection(nameof(LanguageSwitching))]
public class LockRescuerFlowTests
{
    // ------------------------------------------------------------------ fixtures

    private static LockCredentials Credentials(RescuerSecrets rescuer) => new()
    {
        FilePassword = "file-password-1",
        RescuerPassword = rescuer.Password!,
        RescuerKey = rescuer.Key,
        FilePasswordIterations = 1000,
        RescuerPasswordIterations = 1000,
        RescuerKeyIterations = 1000,
    };

    private static LockService NewService() =>
        new(new LockEnvironment([], []), NullLockHost.Instance, minimumIterations: 1000,
            newSlotIterations: 1000);

    private static string NewDirectory()
    {
        var path = Path.Combine(ScratchRoot(), "rescuer-flow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ScratchRoot()
    {
        var root = FindRepoRoot();
        if (root is null)
            return Path.Combine(Path.GetTempPath(), "dropwheel-rescuer-flow-tests");
        return Directory.Exists(Path.Combine(root, "_local"))
            ? Path.Combine(root, "_local", "tmp")
            : Path.Combine(root, "_audit-ci");
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

    private static bool ContainsUtf8(ReadOnlySpan<byte> haystack, string needle)
    {
        var bytes = Encoding.UTF8.GetBytes(needle);
        return haystack.IndexOf(bytes) >= 0;
    }

    private sealed class XorProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) =>
            plaintext.Select(value => (byte)(value ^ 0x5A)).ToArray();

        public byte[]? Unprotect(byte[] protectedBytes) => Protect(protectedBytes);
    }

    private sealed class FailingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) =>
            throw new CryptographicException("DPAPI is unavailable for this test.");

        public byte[]? Unprotect(byte[] protectedBytes) => null;
    }

    private static string? FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName;
    }

    private static string RepoRoot() =>
        FindRepoRoot() ?? throw new DirectoryNotFoundException(
            "FileAction.sln was not found above the test output folder.");

    private static string Source(params string[] relative)
    {
        var path = RepoRoot();
        foreach (var part in relative)
            path = Path.Combine(path, part);
        return File.ReadAllText(path);
    }

    private static string Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"missing source marker '{start}'");
        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        Assert.True(to > from, $"missing source marker '{end}' after '{start}'");
        return text[from..to];
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    /// <summary>
    /// Swaps in a deterministic vault/preferences pair for the process-wide runtime, then restores it. The
    /// runtime initializes first so the injected objects are not replaced by the real ones on first use.
    /// </summary>
    private static void WithRuntime(IRescuerVault vault, ProtectionSettings settings, Action action)
    {
        ArgumentNullException.ThrowIfNull(vault);
        ArgumentNullException.ThrowIfNull(settings);
        LockRuntime.Initialize();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var vaultField = typeof(LockRuntime).GetField("<Vault>k__BackingField", flags)
            ?? throw new InvalidOperationException("LockRuntime.Vault backing field was not found.");
        var settingsField = typeof(LockRuntime).GetField("<Settings>k__BackingField", flags)
            ?? throw new InvalidOperationException("LockRuntime.Settings backing field was not found.");
        var previousVault = vaultField.GetValue(null);
        var previousSettings = settingsField.GetValue(null);
        vaultField.SetValue(null, vault);
        settingsField.SetValue(null, settings);
        try
        {
            action();
        }
        finally
        {
            vaultField.SetValue(null, previousVault);
            settingsField.SetValue(null, previousSettings);
        }
    }

    // ------------------------------------------------------------------ persistence

    [Fact]
    public void Dpapi_vault_roundtrips_the_pair_and_the_key_used_flag()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "protection", "rescuer.dat");
            var key = RescuerKey.Generate();
            var vault = new DpapiRescuerVault(path, DpapiSecretProtector.Instance);
            vault.Save(new RescuerSecrets(key, "rescuer-password-1", KeyUsed: true));
            Assert.True(vault.Persistent, "the real DPAPI protector must make the vault persistent");
            Assert.True(File.Exists(path));

            var reopened = new DpapiRescuerVault(path, DpapiSecretProtector.Instance).Load();
            Assert.NotNull(reopened);
            Assert.Equal(key, reopened!.Key);
            Assert.Equal("rescuer-password-1", reopened.Password);
            Assert.True(reopened.KeyUsed);

            var raw = File.ReadAllBytes(path);
            Assert.False(ContainsUtf8(raw, key), "the Rescuer key must never appear in the DPAPI file");
            Assert.False(ContainsUtf8(raw, "rescuer-password-1"),
                "the Rescuer password must never appear in the DPAPI file");
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Vault_falls_back_to_memory_and_never_writes_plaintext_when_dpapi_fails()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "protection", "rescuer.dat");
            var key = RescuerKey.Generate();
            var vault = new DpapiRescuerVault(path, new FailingProtector());
            vault.Save(new RescuerSecrets(key, "rescuer-password-1", KeyUsed: true));
            Assert.False(vault.Persistent);
            Assert.False(File.Exists(path), "a failed DPAPI save must not write a plaintext fallback");

            // The same process still has the pair in memory for this run.
            var memory = vault.Load();
            Assert.NotNull(memory);
            Assert.Equal(key, memory!.Key);
            Assert.True(memory.KeyUsed);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Vault_blob_is_not_plaintext_with_a_test_protector()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "protection", "rescuer.dat");
            var key = RescuerKey.Generate();
            var vault = new DpapiRescuerVault(path, new XorProtector());
            vault.Save(new RescuerSecrets(key, "rescuer-password-1", false));
            Assert.True(vault.Persistent);
            var raw = File.ReadAllBytes(path);
            Assert.False(ContainsUtf8(raw, key));
            Assert.False(ContainsUtf8(raw, "rescuer-password-1"));
            var loaded = new DpapiRescuerVault(path, new XorProtector()).Load();
            Assert.Equal(key, loaded!.Key);
            Assert.False(loaded.KeyUsed);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Vault_codec_reads_the_old_shape_and_keeps_the_flag()
    {
        var key = RescuerKey.Generate();
        var encoded = RescuerVaultCodec.Encode(new RescuerSecrets(key, "rescuer-password-1", true));
        var decoded = RescuerVaultCodec.TryDecode(encoded);
        Assert.NotNull(decoded);
        Assert.Equal(key, decoded!.Key);
        Assert.Equal("rescuer-password-1", decoded.Password);
        Assert.True(decoded.KeyUsed);

        // Version 1 (2.4.0) had no keyUsed line; it must read back as untouched.
        var old = Encoding.UTF8.GetBytes(
            "version=1\nkey=" + key + "\npassword=" +
            Convert.ToBase64String(Encoding.UTF8.GetBytes("old-password")) + "\n");
        var legacy = RescuerVaultCodec.TryDecode(old);
        Assert.NotNull(legacy);
        Assert.Equal(key, legacy!.Key);
        Assert.Equal("old-password", legacy.Password);
        Assert.False(legacy.KeyUsed);
    }

    [Fact]
    public void A_corrupt_or_foreign_dpapi_blob_falls_back_to_the_manual_prompt_without_crashing()
    {
        var directory = NewDirectory();
        try
        {
            var path = Path.Combine(directory, "protection", "rescuer.dat");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Neither a valid DPAPI blob nor one sealed by this Windows user: CryptUnprotectData rejects it.
            File.WriteAllBytes(path, RandomNumberGenerator.GetBytes(256));

            var vault = new DpapiRescuerVault(path, DpapiSecretProtector.Instance);
            Exception? failure = null;
            RescuerSecrets? loaded = null;
            try
            {
                loaded = vault.Load();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            Assert.Null(failure);
            Assert.Null(loaded);
            Assert.False(vault.Persistent);

            var settings = new ProtectionSettings { RescuerConfigured = true };
            WithRuntime(vault, settings, () =>
                Assert.Null(LockRuntime.TryGetRescuerCredentials()));

            // The marker says a Rescuer exists but the vault is unreadable, so the runtime takes the manual
            // prompt path instead of crashing or silently starting the first-time flow.
            var runtime = Source("src", "FileAction.App", "Shell", "Protection", "LockRuntime.cs");
            var require = Between(runtime, "public static RescuerSecrets? RequireRescuer",
                "public static IReadOnlyList<string> FindVerifierContainers");
            Assert.Contains("if (!Settings.RescuerConfigured)", require, StringComparison.Ordinal);
            Assert.Contains("new RescuerSessionWindow", require, StringComparison.Ordinal);
            Assert.Contains("prompt.ShowDialog() != true", require, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    // ------------------------------------------------------------------ policy

    [Fact]
    public void New_key_is_disabled_until_the_current_key_has_opened_a_file()
    {
        var key = RescuerKey.Generate();
        var unused = new RescuerSecrets(key, "rescuer-password-1", false);
        Assert.False(RescuerPolicy.CanCreateNewKey(unused));
        Assert.False(RescuerPolicy.CanCreateNewKey(new RescuerSecrets(key, null, true)));
        Assert.False(RescuerPolicy.CanCreateNewKey(null));
        Assert.True(RescuerPolicy.CanCreateNewKey(unused with { KeyUsed = true }));

        var rotated = RescuerPolicy.CreateNewKey(unused with { KeyUsed = true });
        Assert.NotEqual(key, rotated.Key);
        Assert.False(rotated.KeyUsed);
        Assert.Equal("rescuer-password-1", rotated.Password);
    }

    [Fact]
    public void Rescuer_proof_accepts_the_password_or_the_grouped_key_and_nothing_else()
    {
        var key = RescuerKey.Generate();
        var stored = new RescuerSecrets(key, "rescuer-password-1", false);
        Assert.True(RescuerPolicy.MatchesPassword(stored, "rescuer-password-1"));
        Assert.False(RescuerPolicy.MatchesPassword(stored, "rescuer-password-2"));
        Assert.True(RescuerPolicy.MatchesKey(stored, RescuerKey.Format(key)));
        Assert.True(RescuerPolicy.MatchesKey(stored, key.ToLowerInvariant()));
        Assert.False(RescuerPolicy.MatchesKey(stored, RescuerKey.Generate()));

        Assert.True(RescuerPolicy.TryMatch(stored, "rescuer-password-1", out var passwordKind));
        Assert.Equal(LockSecretKind.RescuerPassword, passwordKind);
        Assert.True(RescuerPolicy.TryMatch(stored, RescuerKey.Format(key), out var keyKind));
        Assert.Equal(LockSecretKind.RescuerKey, keyKind);
        Assert.False(RescuerPolicy.TryMatch(stored, "wrong", out _));
    }

    [Fact]
    public void New_key_generation_is_disabled_until_use_and_then_asks_for_the_current_password()
    {
        var key = RescuerKey.Generate();
        var unused = new RescuerSecrets(key, "rescuer-password-1", false);
        Assert.False(RescuerPolicy.CanCreateNewKey(unused));
        Assert.True(RescuerPolicy.CanCreateNewKey(unused with { KeyUsed = true }));

        // The current password is proved before the replacement key page is produced.
        Assert.True(RescuerPolicy.TryMatch(unused, "rescuer-password-1", out var kind));
        Assert.Equal(LockSecretKind.RescuerPassword, kind);
        Assert.False(RescuerPolicy.MatchesPassword(unused, "not-the-password"));
        var rotated = RescuerPolicy.CreateNewKey(unused with { KeyUsed = true });
        Assert.NotEqual(key, rotated.Key);
        Assert.Equal("rescuer-password-1", rotated.Password);
        Assert.False(rotated.KeyUsed);
        Assert.False(RescuerPolicy.CanCreateNewKey(rotated));

        var settings = Source("src", "FileAction.App", "Shell", "Protection", "LockSettingsWindow.cs");
        var refresh = Between(settings, "private void RefreshState()", "private void BeginFirstTimeSetup()");
        Assert.Contains("var canRotate = ready && RescuerPolicy.CanCreateNewKey(stored);", refresh,
            StringComparison.Ordinal);
        Assert.Contains("_newKey.IsEnabled = canRotate;", refresh, StringComparison.Ordinal);
        Assert.Contains("tool.lock.settings.newKeyNeedsUse", refresh, StringComparison.Ordinal);

        var create = Between(settings, "private void CreateNewKey()", "private void UpdateProtectedFiles()");
        Assert.Contains("if (!RescuerPolicy.CanCreateNewKey(stored))", create, StringComparison.Ordinal);
        Assert.Contains("SecretPromptWindow(T(\"tool.lock.settings.newKeyPassword\")", create,
            StringComparison.Ordinal);
        Assert.Contains("RescuerPolicy.MatchesPassword(stored, value)", create, StringComparison.Ordinal);
        Assert.Contains("new RescuerSetupWindow(RescuerSetupMode.NewKey, verifiedPassword: stored.Password)",
            create, StringComparison.Ordinal);
    }

    [Fact]
    public void Opening_with_the_rescuer_key_marks_it_used_but_the_rescuer_password_does_not()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "key-used.txt");
            File.WriteAllText(file, "key used");
            var rescuer = new RescuerSecrets(RescuerKey.Generate(), "rescuer-password-1", false);
            var service = NewService();
            var container = service.Protect(file, directory, Credentials(rescuer)).OutputPath;

            Assert.Equal(LockSecretKind.RescuerPassword,
                service.Verify(container, rescuer.Password!).OpenedWith);
            Assert.Equal(LockSecretKind.RescuerKey,
                service.Verify(container, RescuerKey.Format(rescuer.Key)).OpenedWith);

            var vault = new InMemoryRescuerVault();
            vault.Save(rescuer);
            var settings = new ProtectionSettings { RescuerConfigured = true };
            WithRuntime(vault, settings, () =>
            {
                LockRuntime.NoteRescuerKeyUsed(rescuer.Password!);
                Assert.False(vault.Load()!.KeyUsed, "the Rescuer password must not count as key use");
                LockRuntime.NoteRescuerKeyUsed(RescuerKey.Format(rescuer.Key));
                Assert.True(vault.Load()!.KeyUsed, "the current key that opened a file is used");
            });

            // The session manager is the only caller that can know which door opened the container.
            var sessions = Source("src", "FileAction.App", "Shell", "Protection", "LockSessions.cs");
            var open = Between(sessions, "public async Task<LockSecretKind> OpenAsync", "public void LockNow");
            Assert.Contains("if (extracted.Read.OpenedWith == LockSecretKind.RescuerKey)", open,
                StringComparison.Ordinal);
            Assert.Contains("LockRuntime.NoteRescuerKeyUsed(secret);", open, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void An_old_key_that_opens_an_older_container_does_not_unlock_new_key_generation()
    {
        var directory = NewDirectory();
        try
        {
            var oldRescuer = new RescuerSecrets(RescuerKey.Generate(), "old-rescuer-password", false);
            var current = new RescuerSecrets(RescuerKey.Generate(), "current-rescuer-password", false);
            var service = NewService();

            var oldFile = Path.Combine(directory, "old.txt");
            File.WriteAllText(oldFile, "old");
            var oldContainer = service.Protect(oldFile, directory, Credentials(oldRescuer)).OutputPath;

            var currentFile = Path.Combine(directory, "current.txt");
            File.WriteAllText(currentFile, "current");
            var currentContainer = service.Protect(currentFile, directory, Credentials(current)).OutputPath;

            // The old key really does open the older container; that success is exactly what must not
            // unlock rotation for the current, still-unused key.
            Assert.Equal(LockSecretKind.RescuerKey,
                service.Verify(oldContainer, RescuerKey.Format(oldRescuer.Key)).OpenedWith);

            var vault = new InMemoryRescuerVault();
            vault.Save(current);
            var settings = new ProtectionSettings { RescuerConfigured = true };
            WithRuntime(vault, settings, () =>
            {
                LockRuntime.NoteRescuerKeyUsed(RescuerKey.Format(oldRescuer.Key));
                var stored = vault.Load()!;
                Assert.Equal(current.Key, stored.Key);
                Assert.False(stored.KeyUsed);
                Assert.False(RescuerPolicy.CanCreateNewKey(stored));

                Assert.Equal(LockSecretKind.RescuerKey,
                    service.Verify(currentContainer, RescuerKey.Format(current.Key)).OpenedWith);
                LockRuntime.NoteRescuerKeyUsed(RescuerKey.Format(current.Key));
                Assert.True(vault.Load()!.KeyUsed);
                Assert.True(RescuerPolicy.CanCreateNewKey(vault.Load()!));
            });
        }
        finally
        {
            Cleanup(directory);
        }
    }

    // ------------------------------------------------------------------ first Lock adoption

    [Fact]
    public void First_lock_adopts_the_real_rescuer_without_touching_the_file_password_or_payload()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "first-lock.txt");
            File.WriteAllText(file, "first lock content");
            var provisional = RescuerPolicy.CreateProvisional();
            var service = NewService();
            var container = service.Protect(file, directory, Credentials(provisional)).OutputPath;
            Assert.False(File.Exists(file));

            var real = new RescuerSecrets(RescuerKey.Generate(), "real-rescuer-password", false);
            service.AdoptRescuer(container, provisional.Password!, real);

            // Both Rescuer doors now open the file, the rescuers' password is the stored one, and the
            // provisional pair is dead.
            var byPassword = service.Verify(container, real.Password!);
            Assert.Equal(LockSecretKind.RescuerPassword, byPassword.OpenedWith);
            var byKey = service.Verify(container, RescuerKey.Format(real.Key));
            Assert.Equal(LockSecretKind.RescuerKey, byKey.OpenedWith);
            Assert.Throws<LockWrongSecretException>(() => service.Verify(container, provisional.Password!));

            // The file password is unchanged and the payload is still exactly the original.
            var restored = service.Unprotect(container, "file-password-1", recycleContainer: false);
            Assert.Equal("first lock content", File.ReadAllText(restored.RestoredPath));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Failed_rescuer_adoption_leaves_the_provisional_doors_working()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "adopt-failure.txt");
            File.WriteAllText(file, "keep me");
            var provisional = RescuerPolicy.CreateProvisional();
            var service = NewService();
            var container = service.Protect(file, directory, Credentials(provisional)).OutputPath;

            var real = new RescuerSecrets(RescuerKey.Generate(), "real-rescuer-password", false);
            Assert.Throws<LockWrongSecretException>(() =>
                service.AdoptRescuer(container, "not-the-provisional-password", real));

            var stillWorks = service.Verify(container, provisional.Password!);
            Assert.Equal(LockSecretKind.RescuerPassword, stillWorks.OpenedWith);
            Assert.Throws<LockWrongSecretException>(() => service.Verify(container, real.Password!));
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void Cancelling_the_first_time_setup_leaves_the_file_password_as_the_only_usable_door()
    {
        var directory = NewDirectory();
        try
        {
            var file = Path.Combine(directory, "cancelled-setup.txt");
            File.WriteAllText(file, "cancel keeps this");
            var provisional = RescuerPolicy.CreateProvisional();
            var service = NewService();
            var container = service.Protect(file, directory, Credentials(provisional)).OutputPath;

            // "Cancel" means Protection settings never calls AdoptRescuer and never saves a real pair. The
            // provisional password was random and was never shown or stored, so from the user's side the
            // file password is the only usable door.
            var vault = new InMemoryRescuerVault();
            var settingsWithoutRescuer = new ProtectionSettings();
            WithRuntime(vault, settingsWithoutRescuer, () =>
            {
                Assert.Null(LockRuntime.TryGetRescuerCredentials());
                Assert.False(settingsWithoutRescuer.RescuerConfigured);
            });
            Assert.Equal(LockSecretKind.FilePassword,
                service.Verify(container, "file-password-1").OpenedWith);
            Assert.Throws<LockWrongSecretException>(() =>
                service.Verify(container, RescuerPolicy.CreateProvisional().Password!));

            var restored = service.Unprotect(container, "file-password-1", recycleContainer: false);
            Assert.Equal("cancel keeps this", File.ReadAllText(restored.RestoredPath));

            var settings = Source("src", "FileAction.App", "Shell", "Protection", "LockSettingsWindow.cs");
            var begin = Between(settings, "private void BeginFirstTimeSetup()",
                "private void AdoptPendingContainers");
            var cancelled = Between(begin, "if (setup.ShowDialog() != true || setup.Result is null)",
                "RefreshState();");
            Assert.Contains("setupSkipped", cancelled, StringComparison.Ordinal);
            Assert.Contains("return;", cancelled, StringComparison.Ordinal);
            Assert.DoesNotContain("AdoptPendingContainers", cancelled, StringComparison.Ordinal);
        }
        finally
        {
            Cleanup(directory);
        }
    }

    [Fact]
    public void A_complete_stored_rescuer_is_reused_without_a_prompt_or_protection_settings()
    {
        var stored = new RescuerSecrets(RescuerKey.Generate(), "stored-rescuer-password", false);
        var vault = new InMemoryRescuerVault();
        vault.Save(stored);
        var settings = new ProtectionSettings { RescuerConfigured = true };

        RescuerSecrets? seen = null;
        Exception? failure = null;
        WithRuntime(vault, settings, () =>
        {
            Assert.Equal(stored, LockRuntime.TryGetRescuerCredentials());
            // An MTA thread cannot construct or show a WPF window; returning here proves the complete-vault
            // path never opens a prompt.
            var thread = new Thread(() =>
            {
                try
                {
                    seen = LockRuntime.RequireRescuer(null);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            thread.Join();
        });

        Assert.Null(failure);
        Assert.Equal(stored, seen);

        // The later-Lock branch reuses the stored pair directly; Protection settings is only reachable from
        // the first-Lock branch.
        var tool = Source("src", "FileAction.App", "UI", "Tools", "Lock", "LockTool.cs");
        var storedCase = Between(tool, "if (stored is not null)",
            "else if (LockRuntime.Settings.RescuerConfigured)");
        Assert.Contains("rescuer = stored;", storedCase, StringComparison.Ordinal);
        Assert.DoesNotContain("RequireRescuer", storedCase, StringComparison.Ordinal);
        Assert.DoesNotContain("firstLock", storedCase, StringComparison.Ordinal);
        Assert.Equal(1, Count(tool, "firstLock = true;"));
        var setup = Between(tool, "if (firstLock && outputs.Count > 0)", "if (failures.Count > 0)");
        Assert.Contains("ShowFirstRescuerSetup", setup, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ labels

    [Fact]
    public void Rescuer_credentials_dialog_has_the_exact_final_labels()
    {
        var previous = FileAction.Core.Loc.Current;
        try
        {
            FileAction.Core.Loc.Current = FileAction.Core.Language.English;
            Assert.Equal("Rescuer credentials", FileAction.Core.Loc.T("tool.lock.rescuer.sessionTitle"));
            Assert.Equal("Rescuer password", FileAction.Core.Loc.T("tool.lock.rescuer.password"));
            Assert.Equal("Rescuer key (24 characters)", FileAction.Core.Loc.T("tool.lock.rescuer.sessionKey"));

            FileAction.Core.Loc.Current = FileAction.Core.Language.Arabic;
            Assert.Equal("بيانات المنقذ", FileAction.Core.Loc.T("tool.lock.rescuer.sessionTitle"));
            Assert.Equal("كلمة سر المنقذ", FileAction.Core.Loc.T("tool.lock.rescuer.password"));
            Assert.Equal("مفتاح المنقذ (24 حرفاً)", FileAction.Core.Loc.T("tool.lock.rescuer.sessionKey"));
        }
        finally
        {
            FileAction.Core.Loc.Current = previous;
        }
    }
}
