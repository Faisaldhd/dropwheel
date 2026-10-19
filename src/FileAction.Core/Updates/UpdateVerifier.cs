using System.Security.Cryptography;

namespace FileAction.Core.Updates;

/// <summary>
/// Verifies a downloaded update against both trust anchors: SHA-256 (the GitHub asset <c>digest</c> field and
/// <c>SHA256SUMS.txt</c>) and the ECDSA P-256 <c>.sig</c> over the file's SHA-256, using the public key
/// embedded in <see cref="UpdateKey"/>. Every method is defensive: bad base64, bad keys, missing files and
/// malformed digests return false instead of throwing.
/// </summary>
public static class UpdateVerifier
{
    /// <summary>Lower-case SHA-256 of a file.</summary>
    public static string Sha256Hex(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    /// <summary>True when the file's SHA-256 equals <paramref name="expectedHex"/> (case-insensitive).</summary>
    public static bool VerifyDigest(string filePath, string? expectedHex)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !TryNormalizeHex(expectedHex, out var expected))
            return false;

        try
        {
            return string.Equals(Sha256Hex(filePath), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// True when <paramref name="signatureBase64"/> is a valid ECDSA P-256 signature over the file's
    /// SHA-256. Accepts the IEEE P1363 (r||s) and the DER/RFC 3279 encodings so release signing is not tied
    /// to one tool; a wrong key, a tampered file or garbage input returns false.
    /// </summary>
    public static bool VerifySignature(string filePath, string? signatureBase64, string? publicKeyBase64 = null)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(signatureBase64))
            return false;

        try
        {
            var signature = Convert.FromBase64String(signatureBase64.Trim());
            var keyBytes = Convert.FromBase64String((publicKeyBase64 ?? UpdateKey.PublicKeyBase64).Trim());
            if (signature.Length == 0 || keyBytes.Length == 0)
                return false;

            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(keyBytes, out _);

            using var stream = File.OpenRead(filePath);
            var hash = SHA256.HashData(stream);
            return VerifyHash(key, hash, signature);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }
    }

    /// <summary>
    /// Full check: a required valid signature, and every supplied digest source must match. A null/empty
    /// digest or sums text means "that source was not supplied" (the caller decides whether it is required);
    /// a supplied but malformed digest is a mismatch, never a pass.
    /// </summary>
    public static bool Verify(
        string filePath,
        string? digestHex,
        string? sumsText,
        string? signatureBase64,
        string? publicKeyBase64 = null)
    {
        if (!VerifySignature(filePath, signatureBase64, publicKeyBase64))
            return false;

        try
        {
            var actual = Sha256Hex(filePath);

            if (!string.IsNullOrWhiteSpace(digestHex))
            {
                if (!TryNormalizeHex(digestHex, out var expected) ||
                    !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            if (!string.IsNullOrWhiteSpace(sumsText) &&
                !Sha256Sums.Matches(sumsText, Path.GetFileName(filePath), actual))
                return false;

            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }
    }

    private static bool VerifyHash(ECDsa key, byte[] hash, byte[] signature)
    {
        // .NET's SignHash writes IEEE P1363 (r||s); OpenSSL's `dgst -sign` writes DER. Both are valid
        // ECDSA encodings of the same signature, so accept either instead of pinning the release script.
        try
        {
            if (key.VerifyHash(hash, signature))
                return true;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            // Not a P1363 signature; try the DER form below.
        }

        try
        {
            return key.VerifyHash(hash, signature, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }
    }

    private static bool TryNormalizeHex(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            text = text[7..].Trim();
        if (text.Length != 64)
            return false;

        foreach (var c in text)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        normalized = text.ToLowerInvariant();
        return true;
    }

    private static bool IsRecoverable(Exception ex) =>
        ex is CryptographicException
            or FormatException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException;
}
