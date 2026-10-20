using System.Security.Cryptography;
using System.Text;

namespace FileAction.Core.Lock;

/// <summary>
/// Secret hygiene shared by the lock code: UTF-8 conversion that the caller zeroes, fixed-time hash
/// comparison, and the delayed-retry rule for wrong passwords. Nothing here writes a secret to a file,
/// a log, a setting or an exception message.
/// </summary>
public static class LockSecrets
{
    /// <summary>UTF-8 bytes of a password. The caller must zero the returned buffer in a finally block.</summary>
    public static byte[] ToBytes(string secret) => Encoding.UTF8.GetBytes(secret);

    /// <summary>True when two digests are equal without an early exit.</summary>
    public static bool SameHash(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        left.Length == right.Length && CryptographicOperations.FixedTimeEquals(left, right);

    /// <summary>Wipes a buffer; null is fine so callers can use it unconditionally.</summary>
    public static void Wipe(byte[]? buffer)
    {
        if (buffer is not null)
            CryptographicOperations.ZeroMemory(buffer);
    }

    /// <summary>Wipes several buffers, ignoring nulls.</summary>
    public static void WipeAll(params byte[]?[] buffers)
    {
        foreach (var buffer in buffers)
            Wipe(buffer);
    }

    /// <summary>
    /// The delay that follows the nth consecutive wrong secret. The first three attempts are immediate so
    /// a typo is not punished; after that the wait doubles to a ceiling of eight seconds. The rule is pure
    /// and lives here so the unlock window and the tests share one implementation.
    /// </summary>
    public static TimeSpan DelayAfter(int consecutiveFailures)
    {
        if (consecutiveFailures <= 3)
            return TimeSpan.Zero;
        var step = Math.Min(consecutiveFailures - 3, 5);
        return TimeSpan.FromMilliseconds(Math.Min(8000, 400 * (1 << step)));
    }

    /// <summary>
    /// A passphrase score from 0 to 4 for the strength meter: length, character classes and repetition.
    /// It is advice, not a gate; the only hard rule is the minimum length enforced by the caller.
    /// </summary>
    public static int Strength(string? password)
    {
        if (string.IsNullOrEmpty(password))
            return 0;
        var score = 0;
        if (password.Length >= 8)
            score++;
        if (password.Length >= 14)
            score++;
        var classes = 0;
        bool lower = false, upper = false, digit = false, symbol = false;
        foreach (var c in password)
        {
            if (char.IsLower(c) && !lower)
            {
                lower = true;
                classes++;
            }
            else if (char.IsUpper(c) && !upper)
            {
                upper = true;
                classes++;
            }
            else if (char.IsDigit(c) && !digit)
            {
                digit = true;
                classes++;
            }
            else if (!char.IsLetterOrDigit(c) && !symbol)
            {
                symbol = true;
                classes++;
            }
        }
        if (classes >= 3)
            score++;
        if (classes >= 4 && password.Length >= 12)
            score++;
        if (score > 4)
            score = 4;
        if (IsRepetitive(password))
            score = Math.Max(0, score - 1);
        return score;
    }

    /// <summary>True for "aaaaaaaa" or "abcabcabc", which look long but carry little variety.</summary>
    private static bool IsRepetitive(string password)
    {
        if (password.Length < 4)
            return true;
        var distinct = password.Distinct().Count();
        if (distinct <= 2)
            return true;
        for (var period = 1; period <= password.Length / 2; period++)
        {
            if (password.Length % period != 0)
                continue;
            var repeated = true;
            for (var i = period; i < password.Length && repeated; i++)
                repeated = password[i] == password[i % period];
            if (repeated)
                return true;
        }
        return false;
    }

    /// <summary>The state of a "password + confirm" pair, shared by every create dialog.</summary>
    public enum PairStatus
    {
        Empty,
        TooShort,
        Mismatch,
        Match,
    }

    /// <summary>
    /// One rule for every create flow: both fields are filled, the password is at least eight characters,
    /// and the two entries match exactly. The primary button stays disabled until this is
    /// <see cref="PairStatus.Match"/>.
    /// </summary>
    public static PairStatus CheckPair(string? password, string? confirm)
    {
        if (string.IsNullOrEmpty(password) && string.IsNullOrEmpty(confirm))
            return PairStatus.Empty;
        if (!IsAcceptablePassword(password))
            return PairStatus.TooShort;
        return string.Equals(password, confirm, StringComparison.Ordinal) ? PairStatus.Match : PairStatus.Mismatch;
    }

    /// <summary>True only for a complete, long-enough, matching pair.</summary>
    public static bool CanCreate(string? password, string? confirm) =>
        CheckPair(password, confirm) == PairStatus.Match;

    /// <summary>
    /// The one-time Rescuer key may be acknowledged by the "I saved the key" checkbox or by typing the
    /// last four characters. Nothing else counts, so the key screen cannot be dismissed by accident.
    /// </summary>
    public static bool IsRecoveryKeyConfirmed(bool savedCheckbox, string? typedLastFour, string canonicalKey)
    {
        if (savedCheckbox)
            return true;
        if (string.IsNullOrWhiteSpace(typedLastFour) || string.IsNullOrEmpty(canonicalKey) ||
            canonicalKey.Length < 4)
            return false;
        var typed = new string(typedLastFour
            .Where(c => char.IsLetterOrDigit(c))
            .Select(char.ToUpperInvariant)
            .ToArray());
        return typed.Length == 4 &&
               typed == canonicalKey[^4..].ToUpperInvariant();
    }
    /// <summary>The shortest accepted password. Longer is better, but eight characters is the floor.</summary>
    public const int MinimumPasswordLength = 8;

    public static bool IsAcceptablePassword(string? password) =>
        !string.IsNullOrEmpty(password) && password.Length >= MinimumPasswordLength;
}
