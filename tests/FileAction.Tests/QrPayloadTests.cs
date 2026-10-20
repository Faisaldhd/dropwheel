using FileAction.App.UI.Tools.Qr;
using FileAction.Core;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace FileAction.Tests;

/// <summary>
/// 2.4.0 QR reader: what a decoded payload is classified as, which links may ever reach the shell, the
/// detail rows the window shows, and the one-shot decoder wiring. Everything below is pure data — no
/// window, no file and no network.
/// </summary>
public class QrPayloadTests
{
    /// <summary>The tool's identity and the inputs it opens for (one image, never a folder or a PDF).</summary>
    [Fact]
    public void The_tool_opens_for_one_image_only()
    {
        var tool = QrTool.Instance;

        Assert.Equal("tools.qr", tool.Id);
        Assert.Equal("tool.qr.title", tool.TitleKey);
        Assert.True(tool.Layout.ChromeHeight > 0);
        Assert.True(tool.Applies([new FileItem("photo.png")]));
        Assert.True(tool.Applies([new FileItem(@"C:\shots\code.JPG")]));
        Assert.False(tool.Applies([new FileItem("photo.png"), new FileItem("other.png")]));
        Assert.False(tool.Applies([new FileItem("report.pdf")]));
        Assert.False(tool.Applies([]));

        var english = Loc.Table(Language.English)[tool.TitleKey];
        var arabic = Loc.Table(Language.Arabic)[tool.TitleKey];
        Assert.NotEqual(english, arabic);
    }
    [Theory]
    [InlineData("http://example.com/path?x=1", "http://example.com/path?x=1")]
    [InlineData("https://example.com", "https://example.com/")]
    [InlineData("  https://example.com/a  ", "https://example.com/a")]
    public void Web_links_are_openable(string text, string expected)
    {
        var payload = QrPayload.Parse(text);

        Assert.Equal(QrPayloadKind.Url, payload.Kind);
        Assert.True(payload.CanOpen);
        Assert.Equal(expected, payload.Url);
        Assert.Equal("example.com", payload.Title);
        Assert.Empty(payload.Fields);
        Assert.Equal(text, payload.Text);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ms-settings:privacy")]
    [InlineData("shell:startup")]
    [InlineData("tel:+123456")]
    [InlineData("ftp://example.com/file.txt")]
    public void Non_web_schemes_never_become_links(string text)
    {
        var payload = QrPayload.Parse(text);

        Assert.Equal(QrPayloadKind.Text, payload.Kind);
        Assert.False(payload.CanOpen);
        Assert.Null(payload.Url);
        Assert.Equal(text, payload.Text);
    }

    [Theory]
    [InlineData("http://example.com/\r\nInjected: 1")]
    [InlineData("http://example.com/\thack")]
    [InlineData("https://exa\u202Emple.com/")]
    [InlineData("https://example.com/\u2066evil\u2069")]
    [InlineData("//example.com/path")]
    [InlineData("example.com/path")]
    [InlineData("http://")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Disguised_or_relative_values_are_not_links(string? text)
    {
        Assert.False(QrSafety.CanOpen(text));
        Assert.Null(QrSafety.HttpUrl(text));
    }

    [Theory]
    [InlineData("HTTP://EXAMPLE.COM/Path", "http://example.com/Path")]
    [InlineData("HTTPS://example.com/Path", "https://example.com/Path")]
    public void The_scheme_and_host_are_normalised(string text, string expected)
    {
        Assert.True(QrSafety.CanOpen(text));
        Assert.Equal(expected, QrSafety.HttpUrl(text));
    }

    [Fact]
    public void A_non_default_port_stays_in_the_headline()
    {
        var payload = QrPayload.Parse("http://localhost:8080/health");

        Assert.Equal("localhost:8080", payload.Title);
    }

    [Fact]
    public void Plain_text_stays_text()
    {
        var payload = QrPayload.Parse("Hello, wheel!");

        Assert.Equal(QrPayloadKind.Text, payload.Kind);
        Assert.Equal("Hello, wheel!", payload.Text);
        Assert.Null(payload.Url);
        Assert.Null(payload.Title);
        Assert.Empty(payload.Fields);
        Assert.False(payload.CanOpen);
        Assert.Equal("tool.qr.kind.text", payload.KindKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n")]
    public void Missing_text_is_an_empty_payload(string? text)
    {
        var payload = QrPayload.Parse(text);

        Assert.Equal(QrPayloadKind.Text, payload.Kind);
        Assert.False(payload.CanOpen);
        Assert.Equal(text ?? string.Empty, payload.Text);
    }

    [Fact]
    public void Mailto_is_an_email_with_its_subject()
    {
        var payload = QrPayload.Parse("mailto:hello@example.com?subject=Drop%20Wheel%20rocks&body=Hi");

        Assert.Equal(QrPayloadKind.Email, payload.Kind);
        Assert.Equal("tool.qr.kind.email", payload.KindKey);
        Assert.Equal("hello@example.com", payload.Title);
        var field = Assert.Single(payload.Fields);
        Assert.Equal("tool.qr.field.subject", field.LabelKey);
        Assert.Equal("Drop Wheel rocks", field.Value);
        Assert.False(payload.CanOpen);
    }

    [Fact]
    public void Matmsg_is_an_email_too()
    {
        var payload = QrPayload.Parse("MATMSG:TO:team@example.com;SUB:Order 42;BODY:Hello;;");

        Assert.Equal(QrPayloadKind.Email, payload.Kind);
        Assert.Equal("team@example.com", payload.Title);
        Assert.Equal("Order 42", Assert.Single(payload.Fields).Value);
    }

    [Fact]
    public void Wifi_shows_network_security_and_password()
    {
        var payload = QrPayload.Parse(@"WIFI:T:WPA;S:DropWheel-Guest;P:p@ss\;word;;");

        Assert.Equal(QrPayloadKind.Wifi, payload.Kind);
        Assert.Equal("tool.qr.kind.wifi", payload.KindKey);
        Assert.Equal("DropWheel-Guest", payload.Title);
        Assert.Equal(
            new[] { "tool.qr.field.network", "tool.qr.field.security", "tool.qr.field.password" },
            payload.Fields.Select(field => field.LabelKey));
        Assert.Equal(new[] { "DropWheel-Guest", "WPA", @"p@ss;word" }, payload.Fields.Select(field => field.Value));
        Assert.False(payload.CanOpen);
    }

    [Fact]
    public void Wifi_keeps_escaped_backslashes()
    {
        var payload = QrPayload.Parse(@"WIFI:T:WEP;S:A\\B;P:x;;");

        Assert.Equal(@"A\B", payload.Fields.First(field => field.LabelKey == "tool.qr.field.network").Value);
    }

    [Fact]
    public void A_wifi_code_without_a_network_name_still_shows_its_details()
    {
        var payload = QrPayload.Parse("WIFI:T:nopass;S:;;");

        Assert.Equal(QrPayloadKind.Wifi, payload.Kind);
        Assert.Null(payload.Title);
        Assert.Equal("nopass", payload.Fields.Single().Value);
    }

    [Fact]
    public void Vcard_shows_the_contact_details()
    {
        var payload = QrPayload.Parse(
            "BEGIN:VCARD\nVERSION:3.0\nFN:Faisal Saeed\nORG:DropWheel\nTEL:+966500000000\n"
            + "EMAIL:faisal@example.com\nURL:https://example.com/me\nEND:VCARD");

        Assert.Equal(QrPayloadKind.Contact, payload.Kind);
        Assert.Equal("tool.qr.kind.contact", payload.KindKey);
        Assert.Equal("Faisal Saeed", payload.Title);
        Assert.Equal(
            new[]
            {
                "tool.qr.field.organization",
                "tool.qr.field.phone",
                "tool.qr.field.email",
                "tool.qr.field.link",
            },
            payload.Fields.Select(field => field.LabelKey));
        Assert.Equal("https://example.com/me", payload.Url);
        Assert.True(payload.CanOpen);
    }

    [Fact]
    public void Vcard_folded_lines_are_unfolded()
    {
        var payload = QrPayload.Parse("BEGIN:VCARD\r\nFN:A very long\r\n  name\r\nEND:VCARD");

        Assert.Equal("A very long name", payload.Title);
    }

    [Fact]
    public void A_contact_without_a_web_address_is_not_openable()
    {
        var payload = QrPayload.Parse("BEGIN:VCARD\nFN:Someone\nTEL:123\nEND:VCARD");

        Assert.Equal(QrPayloadKind.Contact, payload.Kind);
        Assert.False(payload.CanOpen);
        Assert.Equal("tool.qr.field.phone", Assert.Single(payload.Fields).LabelKey);
    }

    /// <summary>Every label the window can render must be a real string in both shipped tables.</summary>
    [Fact]
    public void Every_kind_and_field_label_exists_in_both_languages()
    {
        var samples = new (QrPayloadKind Kind, string Text)[]
        {
            (QrPayloadKind.Text, "plain text"),
            (QrPayloadKind.Url, "https://example.com"),
            (QrPayloadKind.Email, "mailto:a@example.com?subject=hi"),
            (QrPayloadKind.Wifi, "WIFI:T:WPA;S:net;P:pw;;"),
            (QrPayloadKind.Contact, "BEGIN:VCARD\nFN:X\nORG:Y\nTEL:1\nEMAIL:e@x.test\nURL:https://x.test\nEND:VCARD"),
        };

        foreach (var (kind, text) in samples)
        {
            var payload = QrPayload.Parse(text);
            Assert.Equal(kind, payload.Kind);
            Assert.StartsWith("tool.qr.", payload.KindKey);
            Assert.True(Loc.HasKey(payload.KindKey, Language.English), payload.KindKey);
            Assert.True(Loc.HasKey(payload.KindKey, Language.Arabic), payload.KindKey);
        }

        var labels = samples
            .SelectMany(sample => QrPayload.Parse(sample.Text).Fields)
            .Select(field => field.LabelKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.NotEmpty(labels);
        foreach (var label in labels)
        {
            Assert.StartsWith("tool.qr.field.", label);
            Assert.True(Loc.HasKey(label, Language.English), label);
            Assert.True(Loc.HasKey(label, Language.Arabic), label);
        }
    }

    /// <summary>The decoder wiring: a generated code is read back, and a blank image is "no code".</summary>
    [Fact]
    public void Decoder_reads_a_generated_qr_code()
    {
        const string text = "https://example.com/dropwheel?q=2.4.0";
        var matrix = new QRCodeWriter().encode(text, BarcodeFormat.QR_CODE, 320, 320);

        Assert.Equal(text, QrDecoder.Decode(ToBgra32(matrix), matrix.Width, matrix.Height));
    }

    [Fact]
    public void Decoder_reports_no_code_for_a_blank_image()
    {
        const int side = 160;
        var pixels = new byte[side * side * 4];
        for (var index = 0; index < pixels.Length; index++)
            pixels[index] = 0xFF;

        Assert.Null(QrDecoder.Decode(pixels, side, side));
    }

    [Fact]
    public void Decoder_refuses_a_buffer_that_is_too_small()
    {
        Assert.Null(QrDecoder.Decode(new byte[16], 100, 100));
        Assert.Null(QrDecoder.Decode(new byte[16], 0, 0));
    }

    /// <summary>Black modules become black pixels; the quiet zone around the code is white.</summary>
    private static byte[] ToBgra32(BitMatrix matrix)
    {
        var pixels = new byte[matrix.Width * matrix.Height * 4];
        for (var y = 0; y < matrix.Height; y++)
        {
            for (var x = 0; x < matrix.Width; x++)
            {
                var value = (byte)(matrix[x, y] ? 0x00 : 0xFF);
                var offset = ((y * matrix.Width) + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 0xFF;
            }
        }
        return pixels;
    }
}
