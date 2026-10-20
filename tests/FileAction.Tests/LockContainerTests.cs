using System.Text;
using FileAction.Core.Lock;

namespace FileAction.Tests;

/// <summary>
/// The protected-file container on its own: every size crosses the frame boundary, each of the three
/// secrets opens it, and any change to the header, ciphertext, tag or length is refused.
/// </summary>
public class LockContainerTests
{
    private static LockCredentials Credentials(string? key = null) => new()
    {
        FilePassword = "file-password-1",
        RescuerPassword = "rescuer-password-1",
        RescuerKey = key ?? RescuerKey.Generate(),
        FilePasswordIterations = 1000,
        RescuerPasswordIterations = 1000,
        RescuerKeyIterations = 1000,
    };

    private static LockMeta Meta(int size, string name = "sample.bin") => new()
    {
        Name = name,
        Size = size,
        CreationTimeUtcTicks = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks,
        LastWriteTimeUtcTicks = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks,
        LastAccessTimeUtcTicks = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc).Ticks,
        Attributes = 0x20,
    };

    private static byte[] Seal(byte[] plain, out LockCredentials credentials, string name = "sample.bin")
    {
        credentials = Credentials();
        using var source = new MemoryStream(plain);
        using var destination = new MemoryStream();
        LockContainer.Encrypt(source, destination, credentials, Meta(plain.Length, name));
        return destination.ToArray();
    }

    private static byte[] Open(byte[] container, string secret, out LockReadResult result)
    {
        using var source = new MemoryStream(container);
        using var destination = new MemoryStream();
        result = LockContainer.Decrypt(source, destination, secret);
        return destination.ToArray();
    }

    public static IEnumerable<object[]> Sizes()
    {
        yield return new object[] { 0 };
        yield return new object[] { 1 };
        yield return new object[] { LockFormat.ChunkSize - 1 };
        yield return new object[] { LockFormat.ChunkSize };
        yield return new object[] { LockFormat.ChunkSize + 1 };
        yield return new object[] { (3 * LockFormat.ChunkSize) + 123 };
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void Roundtrip_at_every_frame_boundary(int size)
    {
        var plain = new byte[size];
        new Random(42 + size).NextBytes(plain);
        var container = Seal(plain, out var credentials);
        var opened = Open(container, credentials.FilePassword, out var result);

        Assert.Equal(plain, opened);
        Assert.Equal(size, result.Digest.Length);
        Assert.Equal("sample.bin", result.Meta.Name);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(plain)),
            Convert.ToHexString(result.Digest.Sha256));
        Assert.False(result.Meta.IsDirectory);
    }

    [Fact]
    public void Every_one_of_the_three_secrets_opens_the_file()
    {
        var plain = Encoding.UTF8.GetBytes("the same content through three doors");
        var key = RescuerKey.Generate();
        var credentials = Credentials(key);
        using var source = new MemoryStream(plain);
        using var destination = new MemoryStream();
        LockContainer.Encrypt(source, destination, credentials, Meta(plain.Length));

        Assert.Equal(plain, Open(destination.ToArray(), credentials.FilePassword, out var byFile));
        Assert.Equal(LockSecretKind.FilePassword, byFile.OpenedWith);
        Assert.Equal(plain, Open(destination.ToArray(), credentials.RescuerPassword, out var byRescuer));
        Assert.Equal(LockSecretKind.RescuerPassword, byRescuer.OpenedWith);
        Assert.Equal(plain, Open(destination.ToArray(), RescuerKey.Format(credentials.RescuerKey), out var byKey));
        Assert.Equal(LockSecretKind.RescuerKey, byKey.OpenedWith);
    }

    [Fact]
    public void Wrong_secret_is_generic_and_reveals_nothing()
    {
        var container = Seal([1, 2, 3], out var credentials);
        var wrong = "definitely-not-the-password";
        var exception = Assert.Throws<LockWrongSecretException>(() => Open(container, wrong, out _));
        Assert.DoesNotContain(wrong, exception.Message);
        Assert.DoesNotContain("rescuer", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("file password", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tampered_tag_is_damaged()
    {
        var container = Seal(Encoding.UTF8.GetBytes("tamper me"), out var credentials);
        container[^1] ^= 0x01; // the footer's GCM tag
        Assert.Throws<LockDamagedException>(() => Open(container, credentials.FilePassword, out _));
    }

    [Fact]
    public void Tampered_ciphertext_is_damaged()
    {
        var container = Seal(new byte[4096], out var credentials);
        container[LockFormat.HeaderLength + 120] ^= 0x01; // inside the first content frame
        Assert.Throws<LockDamagedException>(() => Open(container, credentials.FilePassword, out _));
    }

    [Fact]
    public void Tampered_header_mac_is_damaged()
    {
        var container = Seal(Encoding.UTF8.GetBytes("header"), out var credentials);
        container[LockFormat.HeaderLength - 1] ^= 0x01; // last byte of the header MAC
        Assert.Throws<LockDamagedException>(() => Open(container, credentials.FilePassword, out _));
    }

    [Fact]
    public void Tampered_file_id_cannot_be_opened_with_any_secret()
    {
        var container = Seal(Encoding.UTF8.GetBytes("header"), out var credentials);
        container[16] ^= 0x01; // the file id, bound into every slot's AAD
        Assert.ThrowsAny<LockException>(() => Open(container, credentials.FilePassword, out _));
        Assert.ThrowsAny<LockException>(() => Open(container, credentials.RescuerPassword, out _));
        Assert.ThrowsAny<LockException>(() => Open(container, credentials.RescuerKey, out _));
    }

    [Fact]
    public void Truncated_file_is_refused()
    {
        var container = Seal(Encoding.UTF8.GetBytes(new string('x', 5000)), out var credentials);
        var cut = container[..(LockFormat.HeaderLength + 10)];
        Assert.Throws<LockTruncatedException>(() => Open(cut, credentials.FilePassword, out _));
    }

    [Fact]
    public void Trailing_bytes_are_refused()
    {
        var container = Seal(Encoding.UTF8.GetBytes("done"), out var credentials);
        var padded = container.Concat(new byte[] { 0 }).ToArray();
        Assert.Throws<LockDamagedException>(() => Open(padded, credentials.FilePassword, out _));
    }

    [Fact]
    public void Header_is_not_protected_file_when_magic_is_missing()
    {
        var garbage = new byte[LockFormat.HeaderLength + 64];
        new Random(1).NextBytes(garbage);
        Assert.Throws<LockNotProtectedException>(() => Open(garbage, "whatever", out _));
    }

    [Fact]
    public void Wrong_length_footer_is_refused()
    {
        var plain = Encoding.UTF8.GetBytes("content");
        var container = Seal(plain, out var credentials);
        // Replace the last four bytes of the footer's SHA-256; the tag then fails.
        container[^5] ^= 0x7F;
        Assert.Throws<LockDamagedException>(() => Open(container, credentials.FilePassword, out _));
    }

    [Fact]
    public void Metadata_rejects_a_traversal_name()
    {
        var meta = Meta(1, "..");
        var bytes = meta.Serialize();
        Assert.Throws<LockDamagedException>(() => LockMeta.Deserialize(bytes));
    }

    [Fact]
    public void Production_iterations_meet_the_required_floor()
    {
        Assert.True(LockFormat.MinimumIterations >= 600_000);
        Assert.True(LockFormat.DefaultIterations >= LockFormat.MinimumIterations);
        var credentials = Credentials();
        Assert.Equal(1000, credentials.FilePasswordIterations); // tests may lower it explicitly
    }

    [Fact]
    public void Five_hundred_megabytes_streams_with_flat_memory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "dwlock-flat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var sourcePath = Path.Combine(directory, "large.bin");
        var containerPath = Path.Combine(directory, "large.dwenc");
        const long size = 500L * 1024 * 1024;
        try
        {
            var buffer = new byte[1024 * 1024];
            new Random(7).NextBytes(buffer);
            using (var file = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       1024 * 1024, FileOptions.SequentialScan))
            {
                for (long written = 0; written < size; written += buffer.Length)
                    file.Write(buffer, 0, buffer.Length);
            }

            var credentials = Credentials();
            var meta = Meta(0) with { Size = size };
            GC.Collect();
            GC.WaitForPendingFinalizers();
            var before = GC.GetTotalMemory(true);
            LockProtectResult protect;
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       1024 * 1024, FileOptions.SequentialScan))
            using (var destination = new FileStream(containerPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 1024 * 1024, FileOptions.SequentialScan))
            {
                protect = LockContainer.Encrypt(source, destination, credentials, meta);
                destination.Flush(flushToDisk: true);
            }
            Assert.Equal(size, protect.Digest.Length);

            using (var container = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                       1024 * 1024, FileOptions.SequentialScan))
            {
                var read = LockContainer.Decrypt(container, null, credentials.FilePassword);
                Assert.Equal(size, read.Digest.Length);
                Assert.Equal(Convert.ToHexString(protect.Digest.Sha256),
                    Convert.ToHexString(read.Digest.Sha256));
            }

            var after = GC.GetTotalMemory(true);
            Assert.True(after - before < 96L * 1024 * 1024,
                $"live managed memory grew by {(after - before) / (1024 * 1024)} MB");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
