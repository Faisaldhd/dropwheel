using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static FileAction.Core.Loc;

namespace FileAction.Core;

/// <summary>What kind of file the metadata window is looking at.</summary>
public enum MetadataKind
{
    /// <summary>Nothing the window can read or write.</summary>
    None,

    /// <summary>A still image: EXIF / IPTC / XMP profiles, camera and GPS tags.</summary>
    Image,

    /// <summary>Audio or video: container tags and chapters, through FFmpeg.</summary>
    Media,

    /// <summary>A PDF: the document info dictionary, through PDFsharp.</summary>
    Pdf,
}

/// <summary>Which block of the field list a value belongs to (file, track, chapter, profile…).</summary>
public enum MetadataGroup
{
    /// <summary>Format-level tags and the file header.</summary>
    File,

    /// <summary>A video stream.</summary>
    Video,

    /// <summary>An audio stream.</summary>
    Audio,

    /// <summary>Basic image properties (size, format).</summary>
    Image,

    Exif,
    Iptc,
    Xmp,

    /// <summary>The PDF info dictionary.</summary>
    Pdf,

    Chapters,

    /// <summary>Streams and blocks the window has no better group for.</summary>
    Other,
}

/// <summary>
/// The group keys the field list uses. They are localization keys, not display text, so the model stays
/// language-independent and the window resolves them when it renders.
/// </summary>
public static class MetadataGroups
{
    public const string FileKey = "tool.metadata.group.file";
    public const string VideoKey = "tool.metadata.group.video";
    public const string AudioKey = "tool.metadata.group.audio";
    public const string ImageKey = "tool.metadata.group.image";
    public const string ExifKey = "tool.metadata.group.exif";
    public const string IptcKey = "tool.metadata.group.iptc";
    public const string XmpKey = "tool.metadata.group.xmp";
    public const string PdfKey = "tool.metadata.group.pdf";
    public const string ChaptersKey = "tool.metadata.group.chapters";
    public const string OtherKey = "tool.metadata.group.other";

    /// <summary>Display order: the file first, then the tracks, then the profile blocks, chapters last.</summary>
    private static readonly string[] Order =
    [
        FileKey, ImageKey, VideoKey, AudioKey, ExifKey, IptcKey, XmpKey, PdfKey, ChaptersKey, OtherKey,
    ];

    /// <summary>The key for a group, so callers never build one from strings.</summary>
    public static string KeyOf(MetadataGroup group) => group switch
    {
        MetadataGroup.File => FileKey,
        MetadataGroup.Video => VideoKey,
        MetadataGroup.Audio => AudioKey,
        MetadataGroup.Image => ImageKey,
        MetadataGroup.Exif => ExifKey,
        MetadataGroup.Iptc => IptcKey,
        MetadataGroup.Xmp => XmpKey,
        MetadataGroup.Pdf => PdfKey,
        MetadataGroup.Chapters => ChaptersKey,
        _ => OtherKey,
    };

    /// <summary>Sort index for a group key; unknown keys sort after every known group.</summary>
    public static int OrderOf(string groupKey)
    {
        var index = Array.IndexOf(Order, groupKey);
        return index < 0 ? Order.Length : index;
    }
}

/// <summary>One row of the metadata list: a technical label, its value and the group it belongs to.</summary>
public sealed record MetadataField(string GroupKey, string Label, string Value);

/// <summary>Which metadata kind a file's format belongs to.</summary>
public static class MetadataModel
{
    public static MetadataKind KindOf(string? format) => Compression.KindOf(format) switch
    {
        MediaKind.Image => MetadataKind.Image,
        MediaKind.Audio or MediaKind.Video => MetadataKind.Media,
        MediaKind.Pdf => MetadataKind.Pdf,
        _ => MetadataKind.None,
    };
}

/// <summary>One rendered group of <see cref="MetadataField"/> values.</summary>
public sealed record MetadataGroupSlice(string GroupKey, IReadOnlyList<MetadataField> Fields);

/// <summary>
/// The metadata read from one file, grouped by file / track / chapter. Search and grouping are pure:
/// the window only renders what these return, so both are covered by unit tests.
/// </summary>
public sealed class MetadataDocument
{
    public MetadataDocument(MetadataKind kind, IReadOnlyList<MetadataField> fields, IReadOnlyList<string>? warnings = null)
    {
        Kind = kind;
        Fields = fields ?? [];
        Warnings = warnings ?? [];
    }

    public MetadataKind Kind { get; }

    public IReadOnlyList<MetadataField> Fields { get; }

    /// <summary>Non-fatal problems found while reading (unreadable profile, damaged tag…).</summary>
    public IReadOnlyList<string> Warnings { get; }

    public int Count => Fields.Count;

    public static MetadataDocument Empty(MetadataKind kind) => new(kind, []);

    /// <summary>Fields whose label, value or group contains <paramref name="query"/>; all of them when it is blank.</summary>
    public IReadOnlyList<MetadataField> Search(string? query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length == 0)
            return Fields;
        return Fields
            .Where(f => f.Label.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || f.Value.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || f.GroupKey.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Search plus grouping, in display order; empty groups are left out.</summary>
    public IReadOnlyList<MetadataGroupSlice> Grouped(string? query) =>
        Search(query)
            .GroupBy(f => f.GroupKey, StringComparer.Ordinal)
            .OrderBy(g => MetadataGroups.OrderOf(g.Key))
            .Select(g => new MetadataGroupSlice(g.Key, g.ToList()))
            .ToList();
}

/// <summary>One chapter start and its title.</summary>
public readonly record struct MetadataChapter(int StartMs, string Title)
{
    public double StartSeconds => StartMs / 1000.0;
}

/// <summary>
/// What the user typed in the editor. A plain mutable class: the window writes into its properties as
/// the boxes change and reads them back when Apply runs.
/// </summary>
public sealed class MetadataEdit
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public string Author { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Keywords { get; set; } = "";
    public string CameraMake { get; set; } = "";
    public string CameraModel { get; set; } = "";
    public string Latitude { get; set; } = "";
    public string Longitude { get; set; } = "";
    public string ChapterText { get; set; } = "";

    /// <summary>Writes a copy without any metadata instead of the values above.</summary>
    public bool RemoveAll { get; set; }

    /// <summary>True when at least one editable value (or the remove switch) would change the file.</summary>
    public bool HasEdits =>
        RemoveAll
        || !string.IsNullOrWhiteSpace(Title)
        || !string.IsNullOrWhiteSpace(Artist)
        || !string.IsNullOrWhiteSpace(Album)
        || !string.IsNullOrWhiteSpace(Author)
        || !string.IsNullOrWhiteSpace(Subject)
        || !string.IsNullOrWhiteSpace(Keywords)
        || !string.IsNullOrWhiteSpace(CameraMake)
        || !string.IsNullOrWhiteSpace(CameraModel)
        || !string.IsNullOrWhiteSpace(Latitude)
        || !string.IsNullOrWhiteSpace(Longitude)
        || !string.IsNullOrWhiteSpace(ChapterText);
}

/// <summary>One validation problem; <see cref="MessageKey"/> is a localization key, not display text.</summary>
public sealed record MetadataIssue(string Field, string MessageKey, IReadOnlyList<string> Arguments)
{
    public static MetadataIssue Of(string field, string messageKey, params string[] arguments) =>
        new(field, messageKey, arguments);

    /// <summary>The localized sentence. Arguments that are themselves localization keys are translated.</summary>
    public string Describe()
    {
        if (Arguments.Count == 0)
            return T(MessageKey);
        var values = Arguments
            .Select(a => a.StartsWith("tool.", StringComparison.Ordinal) ? T(a) : a)
            .Cast<object?>()
            .ToArray();
        return T(MessageKey, values);
    }
}

/// <summary>
/// The rules the editor obeys: which fields a file kind has, the accepted ranges, the chapter format and
/// the text limits. Pure, so every rejection is unit tested without a file or a window.
/// </summary>
public static class MetadataValidation
{
    /// <summary>Longest accepted tag value; FFmpeg and EXIF both handle this comfortably.</summary>
    public const int MaxTextLength = 255;

    /// <summary>Most chapters the sidecar file writes.</summary>
    public const int MaxChapters = 100;

    public static bool SupportsCamera(MetadataKind kind) => kind == MetadataKind.Image;

    public static bool SupportsChapters(MetadataKind kind) => kind == MetadataKind.Media;

    public static bool SupportsTags(MetadataKind kind) => kind is MetadataKind.Media or MetadataKind.Pdf;

    /// <summary>Every problem with the edit; an empty list means Apply can run.</summary>
    public static IReadOnlyList<MetadataIssue> Validate(MetadataKind kind, MetadataEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var issues = new List<MetadataIssue>();
        if (edit.RemoveAll || kind == MetadataKind.None)
            return issues;

        if (kind == MetadataKind.Image)
        {
            CheckText(issues, "cameraMake", edit.CameraMake, "tool.metadata.field.cameraMake");
            CheckText(issues, "cameraModel", edit.CameraModel, "tool.metadata.field.cameraModel");
            if (!string.IsNullOrWhiteSpace(edit.Latitude) && !TryCoordinate(edit.Latitude, latitude: true, out _))
                issues.Add(MetadataIssue.Of("latitude", "tool.metadata.error.latitude"));
            if (!string.IsNullOrWhiteSpace(edit.Longitude) && !TryCoordinate(edit.Longitude, latitude: false, out _))
                issues.Add(MetadataIssue.Of("longitude", "tool.metadata.error.longitude"));
        }

        if (kind == MetadataKind.Media)
        {
            CheckText(issues, "title", edit.Title, "tool.metadata.field.title");
            CheckText(issues, "artist", edit.Artist, "tool.metadata.field.artist");
            CheckText(issues, "album", edit.Album, "tool.metadata.field.album");
            _ = ParseChapters(edit.ChapterText, out var chapterIssues);
            issues.AddRange(chapterIssues);
        }

        if (kind == MetadataKind.Pdf)
        {
            CheckText(issues, "title", edit.Title, "tool.metadata.field.title");
            CheckText(issues, "author", edit.Author, "tool.metadata.field.author");
            CheckText(issues, "subject", edit.Subject, "tool.metadata.field.subject");
            CheckText(issues, "keywords", edit.Keywords, "tool.metadata.field.keywords");
        }

        return issues;
    }

    private static void CheckText(List<MetadataIssue> issues, string field, string? value, string labelKey)
    {
        if (value is not null && value.Trim().Length > MaxTextLength)
            issues.Add(MetadataIssue.Of(field, "tool.metadata.error.text", labelKey));
    }

    /// <summary>Parses a latitude (-90…90) or longitude (-180…180) in decimal degrees.</summary>
    public static bool TryCoordinate(string? text, bool latitude, out double value)
    {
        value = 0;
        var raw = (text ?? string.Empty).Trim().Replace(',', '.');
        if (raw.Length == 0)
            return false;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;
        var limit = latitude ? 90.0 : 180.0;
        if (!double.IsFinite(parsed) || Math.Abs(parsed) > limit)
            return false;
        value = parsed;
        return true;
    }

    /// <summary>Decimal degrees with up to six decimals, Western digits.</summary>
    public static string FormatCoordinate(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Parses "mm:ss Title" lines (an hour part is optional); bad lines become issues.</summary>
    public static IReadOnlyList<MetadataChapter> ParseChapters(string? text, out IReadOnlyList<MetadataIssue> issues)
    {
        var chapters = new List<MetadataChapter>();
        var problems = new List<MetadataIssue>();
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var lastStart = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0)
                continue;

            var separator = line.IndexOfAny(new[] { ' ', '\t' });
            if (separator <= 0
                || !TryParseTime(line[..separator], out var seconds)
                || line[(separator + 1)..].Trim().Length == 0)
            {
                problems.Add(MetadataIssue.Of("chapters", "tool.metadata.error.chapter",
                    (i + 1).ToString(CultureInfo.InvariantCulture)));
                continue;
            }

            var startMs = (int)Math.Round(seconds * 1000);
            if (startMs <= lastStart)
            {
                problems.Add(MetadataIssue.Of("chapters", "tool.metadata.error.chapterOrder"));
                continue;
            }

            lastStart = startMs;
            chapters.Add(new MetadataChapter(startMs, line[(separator + 1)..].Trim()));
        }

        if (chapters.Count > MaxChapters)
        {
            problems.Add(MetadataIssue.Of("chapters", "tool.metadata.error.chapterCount",
                MaxChapters.ToString(CultureInfo.InvariantCulture)));
            chapters = chapters.Take(MaxChapters).ToList();
        }

        issues = problems;
        return chapters;
    }

    /// <summary>Parses "90", "1:30", "01:02:03.5" into seconds.</summary>
    public static bool TryParseTime(string? text, out double seconds)
    {
        seconds = 0;
        var raw = (text ?? string.Empty).Trim().Replace(',', '.');
        if (raw.Length == 0)
            return false;

        var parts = raw.Split(':');
        if (parts.Length is < 1 or > 3)
            return false;

        var total = 0.0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value) || value < 0)
                return false;
            total = total * 60 + value;
        }

        seconds = total;
        return true;
    }

    /// <summary>"0:05", "12:05" or "1:02:03" for a chapter start.</summary>
    public static string FormatChapterTime(int startMs)
    {
        var span = TimeSpan.FromMilliseconds(Math.Max(0, startMs));
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}:{span.Seconds:00}");
    }

    /// <summary>The chapter list as it appears in the editor, one "mm:ss Title" per line.</summary>
    public static string ChaptersToText(IEnumerable<MetadataChapter> chapters) =>
        string.Join("\n", chapters.Select(c => $"{FormatChapterTime(c.StartMs)} {c.Title}"));
}

/// <summary>What FFmpeg reported about one file: its tags and chapters, plus the numbers compress needs.</summary>
public readonly record struct MediaProbe(MetadataDocument Document, double DurationSeconds, int Width, int Height);

/// <summary>
/// The FFmpeg side of metadata editing: reading the <c>-i</c> banner into fields, writing the tag
/// arguments, and building the <c>;FFMETADATA1</c> chapter sidecar. Pure string work — the runner only
/// starts the process.
/// </summary>
public static class MetadataFfmpeg
{
    private static readonly Regex DurationRegex = new(@"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)", RegexOptions.Compiled);
    private static readonly Regex BitrateRegex = new(@"bitrate:\s*(\d+(?:\.\d+)?)\s*kb/s", RegexOptions.Compiled);
    private static readonly Regex ResolutionRegex = new(@"\b(\d{2,5})x(\d{2,5})\b", RegexOptions.Compiled);
    private static readonly Regex FpsRegex = new(@"(\d+(?:\.\d+)?)\s*fps", RegexOptions.Compiled);
    private static readonly Regex HertzRegex = new(@"(\d+)\s*Hz", RegexOptions.Compiled);
    private static readonly Regex ChannelsRegex = new(@"\b(mono|stereo)\b", RegexOptions.Compiled);

    /// <summary>The editable tags as <c>-metadata key=value</c> pairs; empty values are left alone.</summary>
    public static IReadOnlyList<string> TagArgs(MetadataEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var args = new List<string>();
        Add("title", edit.Title);
        Add("artist", edit.Artist);
        Add("album", edit.Album);
        return args;

        void Add(string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            args.Add("-metadata");
            args.Add($"{key}={value.Trim()}");
        }
    }

    /// <summary>
    /// The <c>;FFMETADATA1</c> sidecar FFmpeg reads as a second input: one <c>[CHAPTER]</c> block per
    /// chapter, with millisecond times. The last chapter ends at the file duration.
    /// </summary>
    public static string ChapterFile(IReadOnlyList<MetadataChapter> chapters, double totalSeconds)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        var builder = new StringBuilder(";FFMETADATA1\n");
        for (var i = 0; i < chapters.Count; i++)
        {
            var start = Math.Max(0, chapters[i].StartMs);
            var end = i + 1 < chapters.Count
                ? Math.Max(start + 1, chapters[i + 1].StartMs)
                : Math.Max(start + 1000, (int)Math.Round(Math.Max(0, totalSeconds) * 1000));

            builder.Append("[CHAPTER]\n");
            builder.Append("TIMEBASE=1/1000\n");
            builder.Append("START=").Append(start.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("END=").Append(end.ToString(CultureInfo.InvariantCulture)).Append('\n');
            builder.Append("title=").Append(Escape(chapters[i].Title)).Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The full FFmpeg command line for a metadata copy: <c>-c copy</c>, existing tags kept, the edited
    /// tags overlaid, and either the new chapter sidecar or no chapters at all for "remove all".
    /// </summary>
    public static IReadOnlyList<string> EditArgs(
        MetadataEdit edit,
        string inputPath,
        IReadOnlyList<MetadataChapter> chapters,
        string? chapterPath,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var args = new List<string> { "-hide_banner", "-nostdin", "-y", "-i", inputPath };
        var hasChapters = chapterPath is not null && chapters.Count > 0;
        if (hasChapters)
        {
            args.Add("-i");
            args.Add(chapterPath!);
        }

        if (edit.RemoveAll)
        {
            args.AddRange(["-map", "0", "-map_metadata", "-1", "-map_chapters", "-1"]);
        }
        else
        {
            args.AddRange(["-map", "0", "-map_metadata", "0"]);
            args.AddRange(TagArgs(edit));
            if (hasChapters)
                args.AddRange(["-map_chapters", "1"]);
        }

        args.AddRange(["-c", "copy", outputPath]);
        return args;
    }

    /// <summary>True when the edit changes chapters (so the runner has to write the sidecar).</summary>
    public static bool WritesChapters(MetadataEdit edit, IReadOnlyList<MetadataChapter> chapters) =>
        !edit.RemoveAll && chapters.Count > 0;

    /// <summary>Reads an <c>ffmpeg -i</c> banner into groups: file, video, audio and chapters.</summary>
    public static MediaProbe ParseProbe(string? text)
    {
        var fields = new List<MetadataField>();
        var duration = 0.0;
        var width = 0;
        var height = 0;
        var group = MetadataGroups.FileKey;
        var inMetadata = false;

        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
                continue;
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Input #", StringComparison.Ordinal))
            {
                group = MetadataGroups.FileKey;
                inMetadata = false;
                continue;
            }

            if (trimmed.StartsWith("Stream #", StringComparison.Ordinal))
            {
                inMetadata = false;
                var isVideo = trimmed.Contains(": Video:", StringComparison.Ordinal);
                var isAudio = trimmed.Contains(": Audio:", StringComparison.Ordinal);
                group = isVideo ? MetadataGroups.VideoKey : isAudio ? MetadataGroups.AudioKey : MetadataGroups.OtherKey;

                fields.Add(new MetadataField(group, "codec", DescribeStream(trimmed)));
                if (isVideo)
                {
                    if (ResolutionRegex.Match(trimmed) is { Success: true } size)
                    {
                        width = ParseInt(size.Groups[1].Value);
                        height = ParseInt(size.Groups[2].Value);
                        fields.Add(new MetadataField(group, "resolution", $"{size.Groups[1].Value}x{size.Groups[2].Value}"));
                    }
                    if (FpsRegex.Match(trimmed) is { Success: true } fps)
                        fields.Add(new MetadataField(group, "frame_rate", fps.Groups[1].Value + " fps"));
                }
                else if (isAudio)
                {
                    if (HertzRegex.Match(trimmed) is { Success: true } hertz)
                        fields.Add(new MetadataField(group, "sample_rate", hertz.Groups[1].Value + " Hz"));
                    if (ChannelsRegex.Match(trimmed) is { Success: true } channels)
                        fields.Add(new MetadataField(group, "channels", channels.Groups[1].Value));
                }
                continue;
            }

            if (trimmed.StartsWith("Chapter #", StringComparison.Ordinal))
            {
                inMetadata = false;
                group = MetadataGroups.ChaptersKey;
                fields.Add(new MetadataField(group, "chapter", trimmed));
                continue;
            }

            if (trimmed.StartsWith("Metadata:", StringComparison.Ordinal))
            {
                inMetadata = true;
                continue;
            }

            if (trimmed.StartsWith("Duration:", StringComparison.Ordinal))
            {
                group = MetadataGroups.FileKey;
                inMetadata = false;
                if (DurationRegex.Match(trimmed) is { Success: true } match)
                {
                    duration = ParseInt(match.Groups[1].Value) * 3600
                        + ParseInt(match.Groups[2].Value) * 60
                        + double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                    fields.Add(new MetadataField(group, "duration", Compression.FormatDuration(duration)));
                }
                if (BitrateRegex.Match(trimmed) is { Success: true } bitrate)
                    fields.Add(new MetadataField(group, "bitrate", bitrate.Groups[1].Value + " kb/s"));
                continue;
            }

            if (!inMetadata)
                continue;

            var colon = trimmed.IndexOf(':');
            if (colon <= 0)
                continue;
            var key = trimmed[..colon].Trim();
            var value = trimmed[(colon + 1)..].Trim();
            if (key.Length == 0 || value.Length == 0)
                continue;
            fields.Add(new MetadataField(group, key, value));
        }

        return new MediaProbe(new MetadataDocument(MetadataKind.Media, fields), duration, width, height);
    }

    /// <summary>The codec part of a "Stream #…: Video: h264 …, yuv420p…" line.</summary>
    private static string DescribeStream(string line)
    {
        foreach (var marker in new[] { ": Video: ", ": Audio: ", ": Subtitle: ", ": Data: " })
        {
            var index = line.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                continue;
            var rest = line[(index + marker.Length)..].Trim();
            var comma = rest.IndexOf(',');
            return comma > 0 ? rest[..comma].Trim() : rest;
        }

        return line;
    }

    /// <summary>Escapes the characters the FFMETADATA1 format treats specially.</summary>
    private static string Escape(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                case '=':
                case ';':
                case '#':
                    builder.Append('\\').Append(c);
                    break;
                case '\r':
                case '\n':
                    builder.Append(' ');
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    private static int ParseInt(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
}
