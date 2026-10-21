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
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        if (directory is not null)
        {
            return Directory.Exists(Path.Combine(directory.FullName, "_local"))
                ? Path.Combine(directory.FullName, "_local", "tmp")
                : Path.Combine(directory.FullName, "_audit-ci");
        }
        return Path.Combine(Path.GetTempPath(), "dropwheel-rescuer-flow-tests");
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
