using System.Security.Cryptography;
using System.Text;

namespace FileAction.Core.Lock;

/// <summary>
/// The Rescuer key: 24 characters in six groups of four, drawn from a 32-character alphabet that leaves
/// out the pairs people confuse (I/1, O/0, and L). 24 × 5 bits = 120 bits of entropy, generated once by
/// <see cref="RandomNumberGenerator"/> and shown to the user exactly once.
/// </summary>
public static class RescuerKey
{
    /// <summary>Unambiguous alphabet: no I, L, O, 0 or 1.</summary>
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public const int CharacterCount = 24;
    public const int GroupSize = 4;

    /// <summary>Creates a new key in canonical (no separators) form.</summary>
    public static string Generate()
    {
        var chars = new char[CharacterCount];
        var bytes = new byte[CharacterCount];
        try
        {
            RandomNumberGenerator.Fill(bytes);
            for (var i = 0; i < chars.Length; i++)
                chars[i] = Alphabet[bytes[i] & 31];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
        return new string(chars);
    }

    /// <summary>Groups a canonical key for display: <c>ABCD-EFGH-…</c>.</summary>
    public static string Format(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        if (!TryParse(canonical, out var clean))
            return canonical;
        var text = new StringBuilder(CharacterCount + (CharacterCount / GroupSize) - 1);
        for (var i = 0; i < clean.Length; i++)
        {
            if (i > 0 && i % GroupSize == 0)
                text.Append('-');
            text.Append(clean[i]);
        }
        return text.ToString();
    }

    /// <summary>
    /// Accepts a typed key in any case with spaces, dashes or underscores, and returns the 24-character
    /// canonical form. Everything that is not a key character is rejected rather than guessed.
    /// </summary>
    public static bool TryParse(string? text, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var buffer = new char[CharacterCount];
        var count = 0;
        foreach (var raw in text)
        {
            if (raw is '-' or ' ' or '_' or '\t')
                continue;
            var c = char.ToUpperInvariant(raw);
            if (Alphabet.IndexOf(c) < 0 || count == CharacterCount)
                return false;
            buffer[count++] = c;
        }
        if (count != CharacterCount)
            return false;
        canonical = new string(buffer);
        return true;
    }

    /// <summary>The bytes the key slot derives its wrapping key from (canonical UTF-8).</summary>
    public static byte[] ToSecretBytes(string canonical) => Encoding.UTF8.GetBytes(canonical);
}
