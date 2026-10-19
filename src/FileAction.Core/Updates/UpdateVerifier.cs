using System.Security.Cryptography;
using System.Text;

namespace FileAction.Core.Updates;

/// <summary>
/// Verifies a downloaded update against both trust anchors: SHA-256 (GitHub asset <c>digest</c> and
/// <c>SHA256SUMS.txt</c>) and the ECDSA P-256 <c>.sig</c> over the file's SHA-256, using the public key
/// embedded in <see cref="UpdateKey"/>. Replaced by the update-core agent; this stub keeps every other
/// branch compiling while that work happens.
/// </summary>
public static class UpdateVerifier
{
    /// <summary>Lower-case SHA-256 of a file.</summary>
    public static string Sha256Hex(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>True when the file's SHA-256 equals <paramref name="expectedHex"/> (case-insensitive).</summary>
    public static bool VerifyDigest(string filePath, string? expectedHex) =>
        !string.IsNullOrWhiteSpace(expectedHex) &&
        string.Equals(Sha256Hex(filePath), expectedHex.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="signatureBase64"/> is a valid ECDSA P-256 signature over the file's SHA-256.</summary>
    public static bool VerifySignature(string filePath, string? signatureBase64, string? publicKeyBase64 = null)
    {
        if (string.IsNullOrWhiteSpace(signatureBase64))
            return false;
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(publicKeyBase64 ?? UpdateKey.PublicKeyBase64), out _);
            var hash = SHA256.HashData(File.ReadAllBytes(filePath));
            return key.VerifyHash(hash, Convert.FromBase64String(signatureBase64));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Full check: optional digest, optional sums-file entry, and a required valid signature. Every supplied
    /// digest source must match; a missing or bad signature always fails.
    /// </summary>
    public static bool Verify(
        string filePath, string? digestHex, string? sumsText, string? signatureBase64, string? publicKeyBase64 = null)
    {
        if (!VerifySignature(filePath, signatureBase64, publicKeyBase64))
            return false;
        if (!string.IsNullOrWhiteSpace(digestHex) && !VerifyDigest(filePath, digestHex))
            return false;
        if (!string.IsNullOrWhiteSpace(sumsText) &&
            !Sha256Sums.Matches(sumsText, Path.GetFileName(filePath), Sha256Hex(filePath)))
            return false;
        return true;
    }
}
