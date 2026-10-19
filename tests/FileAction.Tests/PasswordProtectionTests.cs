using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FileAction.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SharpCompress.Archives;
using SharpCompress.Readers;
using Xunit;

namespace FileAction.Tests;

/// <summary>
/// Acceptance for the Protect / Unlock tool. The tests exercise the pure Core layer with the real libraries
/// used by the app: AES ZIP writing/reading and PDFsharp AES-256. They never start the app.
/// </summary>
public class PasswordProtectionTests
{
    private const string Password = "Correct horse 42!";
    private const string WrongPassword = "Correct horse 43!";

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    [Fact]
    public void Validation_rejects_short_mismatched_and_overlong_passwords()
    {
        Assert.Equal(PasswordProblem.Empty, PasswordRules.Validate(null, null));
        Assert.Equal(PasswordProblem.TooShort, PasswordRules.Validate("short", "short", requireConfirmation: true));
        Assert.Equal(PasswordProblem.Mismatch, PasswordRules.Validate("long enough", "different", requireConfirmation: true));
        Assert.Equal(PasswordProblem.None, PasswordRules.Validate("long enough", "long enough", requireConfirmation: true));
        Assert.Equal(PasswordProblem.TooLong, PasswordRules.Validate(new string('a', PasswordRules.MaximumLength + 1), null));
    }

    [Fact]
    public void Unlock_validation_accepts_any_non_empty_password()
    {
        Assert.Equal(PasswordProblem.None, PasswordRules.ValidateForUnlock("abc"));
        Assert.Equal(PasswordProblem.None, PasswordRules.ValidateForUnlock("  "));
        Assert.Equal(PasswordProblem.Empty, PasswordRules.ValidateForUnlock(""));
        Assert.Equal(PasswordProblem.TooLong, PasswordRules.ValidateForUnlock(new string('a', PasswordRules.MaximumLength + 1)));
    }

    [Fact]
    public void Strength_meter_is_ordered_and_never_strong_for_one_repeated_character()
    {
        Assert.Equal(PasswordStrength.VeryWeak, PasswordStrengthMeter.Evaluate(null));
        Assert.Equal(PasswordStrength.VeryWeak, PasswordStrengthMeter.Evaluate("aaaaaaaaaaaa"));
        Assert.True(PasswordStrengthMeter.Evaluate("Password1!") < PasswordStrengthMeter.Evaluate("Tr0ub4dor&3-very-long"));
        Assert.Equal(PasswordStrength.Strong, PasswordStrengthMeter.Evaluate("Tr0ub4dor&3-very-long"));
    }

    [Fact]
    public void Key_derivation_is_deterministic_and_password_dependent()
    {
        var salt = new byte[16];
        for (var i = 0; i < salt.Length; i++)
            salt[i] = (byte)(i * 7 + 3);

        using var first = ZipAesKeyDerivation.Derive(Password, salt);
        using var second = ZipAesKeyDerivation.Derive(Password, salt);
        using var wrong = ZipAesKeyDerivation.Derive(WrongPassword, salt);

        Assert.Equal(32, first.EncryptionKey.Length);
        Assert.Equal(32, first.AuthenticationKey.Length);
        Assert.Equal(2, first.PasswordVerifier.Length);
        Assert.Equal(first.EncryptionKey, second.EncryptionKey);
        Assert.Equal(first.AuthenticationKey, second.AuthenticationKey);
        Assert.Equal(first.PasswordVerifier, second.PasswordVerifier);
        Assert.NotEqual(first.PasswordVerifier, wrong.PasswordVerifier);
    }

    [Fact]
    public void Protect_defaults_suggest_pdf_for_one_pdf_and_zip_otherwise()
    {
        Assert.Equal(ProtectionOutputFormat.Pdf, PasswordProtectionDefaults.DefaultFormat(1, firstItemIsPdf: true));
        Assert.Equal(ProtectionOutputFormat.Zip, PasswordProtectionDefaults.DefaultFormat(1, firstItemIsPdf: false));
        Assert.Equal(ProtectionOutputFormat.Zip, PasswordProtectionDefaults.DefaultFormat(2, firstItemIsPdf: true));
        Assert.True(PasswordProtectionDefaults.RequirePasswordConfirmation);
        Assert.True(PasswordProtectionDefaults.AllowPrintingByDefault);
        Assert.True(PasswordProtectionDefaults.AllowCopyingByDefault);
        Assert.Equal(256, PasswordProtectionDefaults.ZipAesKeyBits);
    }

    [Fact]
    public void Protect_primary_stays_disabled_until_both_fields_match_and_are_long_enough()
    {
        var form = new PasswordFormState();
        Assert.False(form.CanSubmit);
        Assert.Equal(PasswordMatchStatus.Empty, form.MatchStatus);

        form.Password = "short";
        form.Confirmation = "short";
        Assert.Equal(PasswordMatchStatus.TooShort, form.MatchStatus);
        Assert.False(form.CanSubmit);

        form.Password = "long enough";
        form.Confirmation = "different";
        Assert.Equal(PasswordMatchStatus.Mismatch, form.MatchStatus);
        Assert.False(form.CanSubmit);

        form.Confirmation = "long enough";
        Assert.Equal(PasswordMatchStatus.Match, form.MatchStatus);
        Assert.True(form.CanSubmit);
    }

    [Fact]
    public void Eye_toggle_state_is_hidden_after_reset_and_close()
    {
        var protect = new PasswordFormState { Password = "long enough", Confirmation = "long enough" };
        protect.ToggleReveal();
        Assert.True(protect.Revealed);
        protect.Reset();
        Assert.False(protect.Revealed);
        Assert.Equal(string.Empty, protect.Password);
        Assert.Equal(string.Empty, protect.Confirmation);

        var unlock = new PasswordEntryState { Password = "abc" };
        unlock.ToggleReveal();
        Assert.True(unlock.Revealed);
        Assert.True(unlock.CanSubmit);
        unlock.Reset();
        Assert.False(unlock.Revealed);
        Assert.False(unlock.CanSubmit);
    }

    [Fact]
    public void Capabilities_do_not_pretend_zip_can_hide_names()
    {
        Assert.Equal(256, PasswordProtectionCapabilities.ZipAesKeyBits);
        Assert.False(PasswordProtectionCapabilities.ZipEncryptsEntryNames);
        Assert.Equal(256, PasswordProtectionCapabilities.PdfAesKeyBits);
        Assert.True(PasswordProtectionCapabilities.PdfSupportsOwnerPermissions);
    }

    [Fact]
    public void Zip_round_trips_with_the_correct_password()
    {
        using var scope = new TempScope();
        var source = scope.File("note.txt", "hello from DropWheel");
        var archivePath = Path.Combine(scope.Directory, "note Protected.zip");

        var plan = ProtectedZipPlanBuilder.Collect([source]);
        var result = AesZipWriter.Create(plan, archivePath, Password);

        Assert.Equal(1, result.FileCount);
        using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions { Password = Password });
        var entry = Assert.Single(archive.Entries, item => !item.IsDirectory);
        Assert.True(entry.IsEncrypted);
        using var stream = entry.OpenEntryStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        Assert.Equal("hello from DropWheel", reader.ReadToEnd());
    }

    [Fact]
    public void Zip_rejects_the_wrong_password()
    {
        using var scope = new TempScope();
        var source = scope.File("note.txt", "secret text");
        var archivePath = Path.Combine(scope.Directory, "note Protected.zip");
        var plan = ProtectedZipPlanBuilder.Collect([source]);
        AesZipWriter.Create(plan, archivePath, Password);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions { Password = WrongPassword });
            using var stream = archive.Entries.First(item => !item.IsDirectory).OpenEntryStream();
            using var reader = new StreamReader(stream);
            _ = reader.ReadToEnd();
        });
    }

    [Fact]
    public void Zip_never_overwrites_an_existing_output()
    {
        using var scope = new TempScope();
        var source = scope.File("note.txt", "first");
        var archivePath = scope.File("existing.zip", "keep me");
        var plan = ProtectedZipPlanBuilder.Collect([source]);

        Assert.Throws<IOException>(() => AesZipWriter.Create(plan, archivePath, Password));
        Assert.Equal("keep me", File.ReadAllText(archivePath));
    }

    [Fact]
    public void Plan_walks_nested_folders_in_a_stable_order()
    {
        using var scope = new TempScope();
        var folder = Path.Combine(scope.Directory, "project");
        Directory.CreateDirectory(Path.Combine(folder, "b"));
        Directory.CreateDirectory(Path.Combine(folder, "a"));
        File.WriteAllText(Path.Combine(folder, "root.txt"), "root");
        File.WriteAllText(Path.Combine(folder, "b", "two.txt"), "two");
        File.WriteAllText(Path.Combine(folder, "a", "one.txt"), "one");

        var plan = ProtectedZipPlanBuilder.Collect([folder]);
        var names = plan.Entries.Select(entry => entry.EntryName).ToList();

        Assert.Contains("project/", names);
        Assert.Contains("project/root.txt", names);
        Assert.Contains("project/a/", names);
        Assert.Contains("project/a/one.txt", names);
        Assert.Contains("project/b/two.txt", names);
        Assert.True(names.IndexOf("project/a/one.txt") < names.IndexOf("project/b/two.txt"));
    }

    [Fact]
    public void Empty_folder_is_not_a_fake_protected_archive()
    {
        using var scope = new TempScope();
        var folder = Path.Combine(scope.Directory, "empty");
        Directory.CreateDirectory(folder);
        var plan = ProtectedZipPlanBuilder.Collect([folder]);

        var archivePath = Path.Combine(scope.Directory, "empty Protected.zip");
        var error = Assert.Throws<PasswordProtectionException>(() => AesZipWriter.Create(plan, archivePath, Password));
        Assert.Equal(PasswordProtectionFailure.Empty, error.Failure);
        Assert.False(File.Exists(archivePath));
    }

    [Fact]
    public void Unlock_creates_an_unencrypted_zip_and_never_leaves_a_partial_file()
    {
        using var scope = new TempScope();
        var source = scope.File("note.txt", "hello again");
        var archivePath = Path.Combine(scope.Directory, "note Protected.zip");
        AesZipWriter.Create(ProtectedZipPlanBuilder.Collect([source]), archivePath, Password);

        var unlocked = Path.Combine(scope.Directory, "note Unlocked.zip");
        var result = ZipPasswordUnlocker.Unlock(archivePath, unlocked, Password);
        Assert.Equal(1, result.FileCount);

        using (var archive = ArchiveFactory.OpenArchive(unlocked))
        {
            var entry = Assert.Single(archive.Entries, item => !item.IsDirectory);
            Assert.False(entry.IsEncrypted);
            using var stream = entry.OpenEntryStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Assert.Equal("hello again", reader.ReadToEnd());
        }

        var failedOutput = Path.Combine(scope.Directory, "wrong Unlocked.zip");
        Assert.Throws<PasswordProtectionException>(() =>
            ZipPasswordUnlocker.Unlock(archivePath, failedOutput, WrongPassword));
        Assert.False(File.Exists(failedOutput));
    }

    [Fact]
    public void A_plain_zip_is_reported_as_not_encrypted()
    {
        using var scope = new TempScope();
        var folder = Path.Combine(scope.Directory, "plain");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "a.txt"), "a");

        var plain = Path.Combine(scope.Directory, "plain.zip");
        using (var output = File.Create(plain))
        using (var writer = new SharpCompress.Writers.Zip.ZipWriter(
            output,
            new SharpCompress.Writers.Zip.ZipWriterOptions(SharpCompress.Common.CompressionType.Deflate)))
        {
            using var input = new MemoryStream(Encoding.UTF8.GetBytes("a"));
            writer.Write("a.txt", input, new SharpCompress.Writers.Zip.ZipWriterEntryOptions());
        }

        var unlocked = Path.Combine(scope.Directory, "plain Unlocked.zip");
        var error = Assert.Throws<PasswordProtectionException>(() =>
            ZipPasswordUnlocker.Unlock(plain, unlocked, Password));
        Assert.Equal(PasswordProtectionFailure.NotEncrypted, error.Failure);
        Assert.False(File.Exists(unlocked));
    }

    [Fact]
    public void Pdf_uses_aes_256_and_round_trips_with_the_correct_password()
    {
        using var scope = new TempScope();
        var source = Path.Combine(scope.Directory, "sample.pdf");
        using (var document = new PdfDocument())
        {
            document.AddPage();
            document.Save(source);
        }

        var protectedPath = Path.Combine(scope.Directory, "sample Protected.pdf");
        PdfPasswordProtection.Protect(source, protectedPath, Password, new PdfPermissionPolicy(false, false));

        var bytes = File.ReadAllBytes(protectedPath);
        var text = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/AESV3", text, StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() =>
            PdfReader.Open(protectedPath, WrongPassword, PdfDocumentOpenMode.Modify, args => args.Abort = true));

        using (var document = PdfReader.Open(protectedPath, Password, PdfDocumentOpenMode.Modify, args => args.Abort = true))
            Assert.True(document.SecuritySettings.IsEncrypted);

        var unlockedPath = Path.Combine(scope.Directory, "sample Unlocked.pdf");
        PdfPasswordProtection.Unprotect(protectedPath, unlockedPath, Password);
        using (var document = PdfReader.Open(unlockedPath, PdfDocumentOpenMode.Modify))
            Assert.False(document.SecuritySettings.IsEncrypted);
    }

    [Fact]
    public void New_strings_have_matching_keys_placeholders_and_neutral_values()
    {
        var root = RepoRoot();
        var en = Load(Path.Combine(root, "src", "FileAction.Core", "Strings", "en.json"));
        var ar = Load(Path.Combine(root, "src", "FileAction.Core", "Strings", "ar.json"));

        var newKeys = en.Keys
            .Where(key => key.StartsWith("tool.protect.", StringComparison.Ordinal) ||
                          key.StartsWith("tool.unlock.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(newKeys.Length >= 60, $"only {newKeys.Length} protect/unlock keys");
        Assert.Equal(newKeys, ar.Keys
            .Where(key => key.StartsWith("tool.protect.", StringComparison.Ordinal) ||
                          key.StartsWith("tool.unlock.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal));
        Assert.Contains("tool.protect.status.aes", newKeys);
        Assert.Contains("tool.protect.match.yes", newKeys);
        Assert.Contains("tool.protect.match.no", newKeys);

        foreach (var key in newKeys)
        {
            Assert.True(en.ContainsKey(key), $"en is missing '{key}'");
            Assert.True(ar.ContainsKey(key), $"ar is missing '{key}'");
            Assert.False(string.IsNullOrWhiteSpace(en[key]), $"en '{key}' is empty");
            Assert.False(string.IsNullOrWhiteSpace(ar[key]), $"ar '{key}' is empty");
            Assert.Equal(Placeholders(en[key]), Placeholders(ar[key]));
        }
    }

    private static string[] Placeholders(string value) =>
        Placeholder.Matches(value)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static Dictionary<string, string> Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FileAction.sln")))
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new DirectoryNotFoundException("FileAction.sln not found above the test output folder.");
    }

    private sealed class TempScope : IDisposable
    {
        public TempScope()
        {
            Directory = Path.Combine(Path.GetTempPath(), "dropwheel-protect-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
        }

        public string Directory { get; }

        public string File(string name, string text)
        {
            var path = Path.Combine(Directory, name);
            System.IO.File.WriteAllText(path, text, Encoding.UTF8);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
