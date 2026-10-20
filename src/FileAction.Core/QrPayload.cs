using System.Text;

namespace FileAction.Core;

/// <summary>What a decoded QR code turned out to be. Anything that is not a known shape stays text.</summary>
public enum QrPayloadKind
{
    Text,
    Url,
    Email,
    Wifi,
    Contact,
}

/// <summary>One detail row the tool shows below the decoded text.</summary>
/// <param name="LabelKey">Localization key for the row label, always <c>tool.qr.field.*</c>.</param>
/// <param name="Value">Technical value taken from the code, shown exactly as it was encoded.</param>
public sealed record QrField(string LabelKey, string Value);

/// <summary>
/// One decoded QR payload: what it is, the exact text, the link that may be opened and the rows the tool
/// shows next to it. Pure classification and formatting, with no UI and no I/O, so the rules are unit
/// tested without an image. Nothing here opens anything: only <see cref="Url"/> is ever handed to the
/// shell, and only after the user clicks Open.
/// </summary>
public sealed class QrPayload
{
    private const string WifiPrefix = "WIFI:";
    private const string MailToPrefix = "MAILTO:";
    private const string MatmsgPrefix = "MATMSG:";
    private const string VCardBegin = "BEGIN:VCARD";

    private QrPayload(
        QrPayloadKind kind,
        string text,
        string? url,
        string? title,
        IReadOnlyList<QrField> fields)
    {
        Kind = kind;
        Text = text;
        Url = url;
        Title = title;
        Fields = fields;
    }

    public QrPayloadKind Kind { get; }

    /// <summary>The decoded text exactly as the code carried it; copied verbatim by the Copy button.</summary>
    public string Text { get; }

    /// <summary>The absolute http(s) link, or null when the payload must not be opened in a browser.</summary>
    public string? Url { get; }

    /// <summary>Short technical headline (host, address, network name, contact name), or null.</summary>
    public string? Title { get; }

    /// <summary>Detail rows; empty for plain text and for a plain link.</summary>
    public IReadOnlyList<QrField> Fields { get; }

    /// <summary>True when the Open button may hand <see cref="Url"/> to the shell.</summary>
    public bool CanOpen => Url is not null;

    /// <summary>Localization key for the kind badge, e.g. <c>tool.qr.kind.url</c>.</summary>
    public string KindKey => Kind switch
    {
        QrPayloadKind.Url => "tool.qr.kind.url",
        QrPayloadKind.Email => "tool.qr.kind.email",
        QrPayloadKind.Wifi => "tool.qr.kind.wifi",
        QrPayloadKind.Contact => "tool.qr.kind.contact",
        _ => "tool.qr.kind.text",
    };

    /// <summary>Classifies one decoded payload. Never throws and never touches the network.</summary>
    public static QrPayload Parse(string? text)
    {
        var raw = text ?? string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return AsText(raw);

        if (trimmed.StartsWith(WifiPrefix, StringComparison.OrdinalIgnoreCase))
            return ParseWifi(raw, trimmed);

        if (trimmed.Contains(VCardBegin, StringComparison.OrdinalIgnoreCase))
            return ParseVCard(raw, trimmed);

        if (trimmed.StartsWith(MatmsgPrefix, StringComparison.OrdinalIgnoreCase))
            return ParseMatmsg(raw, trimmed);

        if (trimmed.StartsWith(MailToPrefix, StringComparison.OrdinalIgnoreCase))
            return ParseMailto(raw, trimmed);

        // Only http and https reach the Open button; every other scheme stays text.
        if (QrSafety.HttpUrl(trimmed) is { } url)
            return new QrPayload(QrPayloadKind.Url, raw, url, Headline(url), []);

        return AsText(raw);
    }

    private static QrPayload AsText(string raw) => new(QrPayloadKind.Text, raw, null, null, []);

    /// <summary>"host" or "host:port" for a link that <see cref="QrSafety.HttpUrl"/> already accepted.</summary>
    private static string Headline(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            return url;
        return uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
    }

    /// <summary>
    /// <c>WIFI:T:WPA;S:name;P:password;H:true;;</c> — the format Android and iOS write for a network
    /// share. Escapes are <c>\;</c>, <c>\,</c>, <c>\:</c> and <c>\\</c>.
    /// </summary>
    private static QrPayload ParseWifi(string raw, string trimmed)
    {
        string? network = null, security = null, password = null;
        foreach (var segment in SplitEscaped(trimmed[WifiPrefix.Length..], ';'))
        {
            if (segment.Length == 0)
                continue;
            var (key, value) = SplitPair(segment);
            switch (key.ToUpperInvariant())
            {
                case "S": network = Unescape(value); break;
                case "T": security = Unescape(value); break;
                case "P": password = Unescape(value); break;
            }
        }

        var fields = new List<QrField>(3);
        Add(fields, "tool.qr.field.network", network);
        Add(fields, "tool.qr.field.security", security);
        Add(fields, "tool.qr.field.password", password);
        return new QrPayload(QrPayloadKind.Wifi, raw, null, Blank(network), fields);
    }

    /// <summary>Mail app links: <c>mailto:a@b.c?subject=…</c> and the older DoCoMo <c>MATMSG:</c> shape.</summary>
    private static QrPayload ParseMailto(string raw, string trimmed)
    {
        var body = trimmed[MailToPrefix.Length..];
        var queryAt = body.IndexOf('?', StringComparison.Ordinal);
        var address = Decode(queryAt < 0 ? body : body[..queryAt], plusIsSpace: false).Trim();
        var subject = queryAt < 0 ? null : QueryValue(body[(queryAt + 1)..], "subject");
        return Email(raw, address, subject);
    }

    private static QrPayload ParseMatmsg(string raw, string trimmed)
    {
        string? address = null, subject = null;
        foreach (var segment in SplitEscaped(trimmed[MatmsgPrefix.Length..], ';'))
        {
            if (segment.Length == 0)
                continue;
            var (key, value) = SplitPair(segment);
            switch (key.ToUpperInvariant())
            {
                case "TO": address = Unescape(value).Trim(); break;
                case "SUB": subject = Unescape(value); break;
            }
        }
        return Email(raw, address ?? string.Empty, Blank(subject));
    }

    private static QrPayload Email(string raw, string address, string? subject)
    {
        var fields = new List<QrField>(1);
        Add(fields, "tool.qr.field.subject", subject);
        return new QrPayload(QrPayloadKind.Email, raw, null, Blank(address), fields);
    }

    /// <summary>
    /// A minimal vCard reader: FN (or ORG) as the headline, plus organisation, phone, e-mail and one link.
    /// Folded lines are unfolded; other properties are ignored on purpose.
    /// </summary>
    private static QrPayload ParseVCard(string raw, string trimmed)
    {
        var unfolded = trimmed
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n ", string.Empty, StringComparison.Ordinal)
            .Replace("\n\t", string.Empty, StringComparison.Ordinal);
        var lines = unfolded.Split('\n');

        string? name = null, organization = null, phone = null, email = null, link = null;
        for (var index = 1; index < lines.Length; index++) // line 0 is BEGIN:VCARD
        {
            var line = lines[index];
            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
                continue;
            var value = line[(colon + 1)..].Trim();
            if (value.Length == 0)
                continue;

            var property = line[..colon].Split(';')[0].Trim().ToUpperInvariant();
            switch (property)
            {
                case "FN": name ??= value; break;
                case "ORG": organization ??= value.TrimEnd(';'); break;
                case "TEL": phone ??= value; break;
                case "EMAIL": email ??= value; break;
                case "URL": link ??= value; break;
            }
        }

        var fields = new List<QrField>(4);
        Add(fields, "tool.qr.field.organization", organization);
        Add(fields, "tool.qr.field.phone", phone);
        Add(fields, "tool.qr.field.email", email);
        Add(fields, "tool.qr.field.link", link);

        // A contact's own web address may be opened, but only when it is a real http(s) link.
        return new QrPayload(
            QrPayloadKind.Contact,
            raw,
            QrSafety.HttpUrl(link),
            Blank(name) ?? Blank(organization),
            fields);
    }

    private static void Add(List<QrField> fields, string labelKey, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new QrField(labelKey, value));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Splits on an unescaped separator, keeping the backslash escapes for the caller.</summary>
    private static List<string> SplitEscaped(string value, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '\\' && index + 1 < value.Length)
            {
                current.Append(character).Append(value[++index]);
                continue;
            }
            if (character == separator)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        parts.Add(current.ToString());
        return parts;
    }

    /// <summary>Splits <c>KEY:value</c> at the first unescaped colon.</summary>
    private static (string Key, string Value) SplitPair(string segment)
    {
        for (var index = 0; index < segment.Length; index++)
        {
            var character = segment[index];
            if (character == '\\')
            {
                index++;
                continue;
            }
            if (character == ':')
                return (segment[..index].Trim(), segment[(index + 1)..]);
        }
        return (segment.Trim(), string.Empty);
    }

    private static string Unescape(string value)
    {
        if (value.IndexOf('\\') < 0)
            return value;
        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length)
            {
                builder.Append(value[++index]);
                continue;
            }
            builder.Append(value[index]);
        }
        return builder.ToString();
    }

    /// <summary>Parses one query value; a malformed escape yields null rather than an exception.</summary>
    private static string? QueryValue(string query, string name)
    {
        foreach (var pair in query.Split('&'))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = equals < 0 ? pair : pair[..equals];
            if (!Decode(key, plusIsSpace: true).Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            var value = equals < 0 ? string.Empty : Decode(pair[(equals + 1)..], plusIsSpace: true);
            return Blank(value);
        }
        return null;
    }

    private static string Decode(string value, bool plusIsSpace)
    {
        try
        {
            return Uri.UnescapeDataString(plusIsSpace ? value.Replace('+', ' ') : value);
        }
        catch (Exception ex) when (ex is UriFormatException or ArgumentException)
        {
            return value;
        }
    }
}

/// <summary>
/// The one place that decides whether a decoded string may be handed to the shell. Only absolute
/// http/https URLs with a host survive; <c>javascript:</c>, <c>file:</c>, <c>data:</c>, <c>ms-settings:</c>
/// and friends are refused, as are control and bidi characters that could disguise a link.
/// </summary>
public static class QrSafety
{
    /// <summary>Longer payloads are never links (<see cref="Uri"/> caps a URL well below this too).</summary>
    public const int MaxUrlLength = 4096;

    /// <summary>The normalised http(s) URL, or null when this text must not be opened.</summary>
    public static string? HttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        if (text.Length > MaxUrlLength)
            return null;

        foreach (var character in text)
        {
            if (IsRefused(character))
                return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return null;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            return null;
        if (string.IsNullOrEmpty(uri.Host))
            return null;

        return uri.AbsoluteUri;
    }

    /// <summary>True when <see cref="HttpUrl"/> would hand this text to the shell.</summary>
    public static bool CanOpen(string? value) => HttpUrl(value) is not null;

    /// <summary>C0/C1 controls, line breaks and the Unicode bidi overrides used to disguise links.</summary>
    private static bool IsRefused(char character) =>
        character < '\u0020'
        || character == '\u007F'
        || character is >= '\u202A' and <= '\u202E'   // LRE RLE PDF LRO RLO
        || character is '\u200E' or '\u200F'          // LRM RLM
        || character is >= '\u2066' and <= '\u2069';  // LRI RLI FSI PDI
}
