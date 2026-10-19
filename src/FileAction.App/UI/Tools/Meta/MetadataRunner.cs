using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using FileAction.Core;
using FileAction.Core.Backends;
using ImageMagick;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using static FileAction.Core.Loc;

namespace FileAction.App.UI.Tools.Meta;

/// <summary>The metadata read from one file, plus the duration the chapter sidecar needs.</summary>
internal readonly record struct MetadataRead(MetadataDocument Document, double DurationSeconds);

/// <summary>
/// Reads and writes metadata per file kind: EXIF/IPTC/XMP profiles through Magick.NET for images, the
/// <c>-i</c> banner and <c>-c copy</c> through FFmpeg for audio/video, and the info dictionary through
/// PDFsharp for PDFs. Every write goes to a hidden temporary file first, then to a unique name, so the
/// source is never modified and nothing is overwritten.
/// </summary>
internal static class MetadataRunner
{
    public static Task<MetadataRead> ReadAsync(FileItem item, MetadataKind kind, ToolLocator tools, CancellationToken ct) =>
        kind switch
        {
            MetadataKind.Image => Task.Run(() => ReadImage(item), ct),
            MetadataKind.Pdf => Task.Run(() => ReadPdf(item), ct),
            MetadataKind.Media => ReadMediaAsync(item, tools, ct),
            _ => Task.FromResult(new MetadataRead(MetadataDocument.Empty(kind), 0)),
        };

    public static Task<string> WriteAsync(
        FileItem item,
        MetadataKind kind,
        MetadataEdit edit,
        IReadOnlyList<MetadataChapter> chapters,
        double durationSeconds,
        string directory,
        ToolLocator tools,
        CancellationToken ct,
        IProgress<double> progress) =>
        kind switch
        {
            MetadataKind.Image => WriteImageAsync(item, edit, directory, ct, progress),
            MetadataKind.Pdf => WritePdfAsync(item, edit, directory, ct, progress),
            MetadataKind.Media => WriteMediaAsync(item, edit, chapters, durationSeconds, directory, tools, ct),
            _ => throw new ConversionException(T("tool.metadata.error.unsupported")),
        };

    /// <summary>"song Edited metadata" — the base name every write uses.</summary>
    private static string OutputName(FileItem item) => $"{item.BaseName} {T("tool.metadata.suffix")}";

    // ------------------------------------------------------------------ images

    private static MetadataRead ReadImage(FileItem item)
    {
        var fields = new List<MetadataField>();
        var warnings = new List<string>();
        using var image = new MagickImage(item.Path);

        fields.Add(new MetadataField(MetadataGroups.ImageKey, "format", Formats.Display(Formats.KeyOf(item.Path))));
        fields.Add(new MetadataField(MetadataGroups.ImageKey, "width", image.Width.ToString(CultureInfo.InvariantCulture)));
        fields.Add(new MetadataField(MetadataGroups.ImageKey, "height", image.Height.ToString(CultureInfo.InvariantCulture)));
        fields.Add(new MetadataField(MetadataGroups.ImageKey, "size", Compression.FormatSize(LengthOf(item.Path))));

        foreach (var name in image.ProfileNames)
            fields.Add(new MetadataField(MetadataGroups.ImageKey, "profile", name));

        try
        {
            if (image.GetExifProfile() is { } exif)
            {
                foreach (var value in exif.Values)
                {
                    var label = value.Tag.ToString();
                    var editKey = label switch
                    {
                        "Make" => MetadataEditKeys.ExifMake,
                        "Model" => MetadataEditKeys.ExifModel,
                        _ => null,
                    };
                    fields.Add(new MetadataField(MetadataGroups.ExifKey, label, FormatValue(value.GetValue()), editKey));
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add(ex.Message);
        }

        // Camera and location stay editable even when the photo has no such tag yet.
        EnsureEditable(fields, MetadataGroups.ExifKey, "Make", MetadataEditKeys.ExifMake);
        EnsureEditable(fields, MetadataGroups.ExifKey, "Model", MetadataEditKeys.ExifModel);
        EnsureEditable(fields, MetadataGroups.ExifKey, "Latitude", MetadataEditKeys.ExifLatitude);
        EnsureEditable(fields, MetadataGroups.ExifKey, "Longitude", MetadataEditKeys.ExifLongitude);

        try
        {
            if (image.GetIptcProfile() is { } iptc)
            {
                foreach (var value in iptc.Values)
                    fields.Add(new MetadataField(MetadataGroups.IptcKey, value.Tag.ToString(), value.Value ?? string.Empty));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add(ex.Message);
        }

        try
        {
            if (image.GetXmpProfile() is { } xmp)
                AddXmp(fields, xmp.ToXDocument());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add(ex.Message);
        }

        return new MetadataRead(new MetadataDocument(MetadataKind.Image, fields, warnings), 0);
    }

    private static void AddXmp(List<MetadataField> fields, XDocument? document)
    {
        if (document?.Root is null)
            return;
        foreach (var element in document.Root.Descendants())
        {
            if (element.HasElements)
                continue;
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration || string.IsNullOrWhiteSpace(attribute.Value))
                    continue;
                fields.Add(new MetadataField(MetadataGroups.XmpKey, attribute.Name.LocalName, attribute.Value.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(element.Value))
                fields.Add(new MetadataField(MetadataGroups.XmpKey, element.Name.LocalName, element.Value.Trim()));
        }
    }

    private static Task<string> WriteImageAsync(
        FileItem item,
        MetadataEdit edit,
        string directory,
        CancellationToken ct,
        IProgress<double> progress)
    {
        var format = ImageBackend.OutputFormatFor(Formats.Normalize(item.Format));
        return OutputPaths.ProduceAsync(directory, OutputName(item), format, tmp => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var image = new MagickImage(item.Path);
            image.AutoOrient();

            if (edit.RemoveAll)
                image.Strip();
            else
            {
                RemoveCameraTags(image, edit);
                WriteCamera(image, edit);
            }

            ct.ThrowIfCancellationRequested();
            image.Write(tmp, ImageBackend.ToMagickFormat(format));
            progress.Report(1);
        }, ct));
    }

    /// <summary>Writes Make/Model and GPS coordinates into the EXIF profile; other tags are left alone.</summary>
    private static void WriteCamera(MagickImage image, MetadataEdit edit)
    {
        var hasMake = !string.IsNullOrWhiteSpace(edit.CameraMake);
        var hasModel = !string.IsNullOrWhiteSpace(edit.CameraModel);
        var hasLatitude = MetadataValidation.TryCoordinate(edit.Latitude, latitude: true, out var latitude);
        var hasLongitude = MetadataValidation.TryCoordinate(edit.Longitude, latitude: false, out var longitude);
        if (!hasMake && !hasModel && !hasLatitude && !hasLongitude)
            return;

        var profile = image.GetExifProfile() ?? new ExifProfile();
        if (hasMake)
            profile.SetValue(ExifTag.Make, edit.CameraMake.Trim());
        if (hasModel)
            profile.SetValue(ExifTag.Model, edit.CameraModel.Trim());

        if (hasLatitude)
        {
            profile.SetValue(ExifTag.GPSLatitudeRef, latitude >= 0 ? "N" : "S");
            profile.SetValue(ExifTag.GPSLatitude, ToRationals(Math.Abs(latitude)));
        }

        if (hasLongitude)
        {
            profile.SetValue(ExifTag.GPSLongitudeRef, longitude >= 0 ? "E" : "W");
            profile.SetValue(ExifTag.GPSLongitude, ToRationals(Math.Abs(longitude)));
        }

        image.SetProfile(profile);
    }

    /// <summary>Deletes the tags the user removed with the per-field button.</summary>
    private static void RemoveCameraTags(MagickImage image, MetadataEdit edit)
    {
        if (edit.RemovedFields.Count == 0 || image.GetExifProfile() is not { } profile)
            return;

        if (edit.IsRemoved(MetadataEditKeys.ExifMake))
            profile.RemoveValue(ExifTag.Make);
        if (edit.IsRemoved(MetadataEditKeys.ExifModel))
            profile.RemoveValue(ExifTag.Model);
        if (edit.IsRemoved(MetadataEditKeys.ExifLatitude))
        {
            profile.RemoveValue(ExifTag.GPSLatitude);
            profile.RemoveValue(ExifTag.GPSLatitudeRef);
        }

        if (edit.IsRemoved(MetadataEditKeys.ExifLongitude))
        {
            profile.RemoveValue(ExifTag.GPSLongitude);
            profile.RemoveValue(ExifTag.GPSLongitudeRef);
        }

        image.SetProfile(profile);
    }

    /// <summary>Decimal degrees as the EXIF degrees/minutes/seconds triple.</summary>
    private static Rational[] ToRationals(double degrees)
    {
        var whole = (uint)Math.Floor(degrees);
        var minutesTotal = (degrees - whole) * 60;
        var minutes = (uint)Math.Floor(minutesTotal);
        var seconds = (minutesTotal - minutes) * 60;
        return
        [
            new Rational(whole, 1),
            new Rational(minutes, 1),
            new Rational((uint)Math.Round(seconds * 100), 100),
        ];
    }

    // ------------------------------------------------------------------ audio / video

    private static async Task<MetadataRead> ReadMediaAsync(FileItem item, ToolLocator tools, CancellationToken ct)
    {
        var ffmpeg = tools.Require(ExternalTool.Ffmpeg);
        var banner = new StringBuilder();
        // "-i file" with no output always exits non-zero; only its banner is wanted here.
        await ProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", item.Path], ct,
            line =>
            {
                lock (banner)
                    banner.AppendLine(line);
            },
            throwOnError: false).ConfigureAwait(false);

        var probe = MetadataFfmpeg.ParseProbe(banner.ToString());
        return new MetadataRead(probe.Document, probe.DurationSeconds);
    }

    private static async Task<string> WriteMediaAsync(
        FileItem item,
        MetadataEdit edit,
        IReadOnlyList<MetadataChapter> chapters,
        double durationSeconds,
        string directory,
        ToolLocator tools,
        CancellationToken ct)
    {
        var ffmpeg = tools.Require(ExternalTool.Ffmpeg);
        var extension = Formats.Normalize(item.Format);
        var sidecarDirectory = OutputPaths.TempDirectory();
        var sidecar = Path.Combine(sidecarDirectory, "chapters.ffmeta");
        try
        {
            var hasChapters = MetadataFfmpeg.WritesChapters(edit, chapters);
            if (hasChapters)
                await File.WriteAllTextAsync(sidecar, MetadataFfmpeg.ChapterFile(chapters, durationSeconds), new UTF8Encoding(false), ct).ConfigureAwait(false);

            return await OutputPaths.ProduceAsync(directory, OutputName(item), extension, async tmp =>
            {
                var args = MetadataFfmpeg.EditArgs(edit, item.Path, chapters, hasChapters ? sidecar : null, tmp);
                await ProcessRunner.RunAsync(ffmpeg, args, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            OutputPaths.TryDelete(sidecarDirectory);
        }
    }

    // ------------------------------------------------------------------ PDF

    private static MetadataRead ReadPdf(FileItem item)
    {
        var fields = new List<MetadataField>();
        using var document = OpenPdf(item.Path, PdfDocumentOpenMode.Import);
        var info = document.Info;

        Add("title", info.Title, MetadataEditKeys.PdfTitle);
        Add("author", info.Author, MetadataEditKeys.PdfAuthor);
        Add("subject", info.Subject, MetadataEditKeys.PdfSubject);
        Add("keywords", info.Keywords, MetadataEditKeys.PdfKeywords);
        Add("creator", info.Creator, null);
        Add("producer", info.Producer, null);
        fields.Add(new MetadataField(MetadataGroups.PdfKey, "pages", document.PageCount.ToString(CultureInfo.InvariantCulture)));
        fields.Add(new MetadataField(MetadataGroups.PdfKey, "size", Compression.FormatSize(LengthOf(item.Path))));

        // The four info fields stay editable even when the file does not set them yet.
        EnsureEditable(fields, MetadataGroups.PdfKey, "title", MetadataEditKeys.PdfTitle);
        EnsureEditable(fields, MetadataGroups.PdfKey, "author", MetadataEditKeys.PdfAuthor);
        EnsureEditable(fields, MetadataGroups.PdfKey, "subject", MetadataEditKeys.PdfSubject);
        EnsureEditable(fields, MetadataGroups.PdfKey, "keywords", MetadataEditKeys.PdfKeywords);

        return new MetadataRead(new MetadataDocument(MetadataKind.Pdf, fields), 0);

        void Add(string label, string? value, string? editKey)
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields.Add(new MetadataField(MetadataGroups.PdfKey, label, value, editKey));
        }
    }

    private static Task<string> WritePdfAsync(
        FileItem item,
        MetadataEdit edit,
        string directory,
        CancellationToken ct,
        IProgress<double> progress) =>
        OutputPaths.ProduceAsync(directory, OutputName(item), "pdf", tmp => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            using var document = OpenPdf(item.Path, PdfDocumentOpenMode.Modify);
            var info = document.Info;

            if (edit.RemoveAll)
            {
                // Producer is written by the PDF library on save, so only the editable fields are cleared.
                info.Title = string.Empty;
                info.Author = string.Empty;
                info.Subject = string.Empty;
                info.Keywords = string.Empty;
                info.Creator = string.Empty;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(edit.Title))
                    info.Title = edit.Title.Trim();
                if (!string.IsNullOrWhiteSpace(edit.Author))
                    info.Author = edit.Author.Trim();
                if (!string.IsNullOrWhiteSpace(edit.Subject))
                    info.Subject = edit.Subject.Trim();
                if (!string.IsNullOrWhiteSpace(edit.Keywords))
                    info.Keywords = edit.Keywords.Trim();

                // The per-field remove button empties exactly that info entry.
                if (edit.IsRemoved(MetadataEditKeys.PdfTitle))
                    info.Title = string.Empty;
                if (edit.IsRemoved(MetadataEditKeys.PdfAuthor))
                    info.Author = string.Empty;
                if (edit.IsRemoved(MetadataEditKeys.PdfSubject))
                    info.Subject = string.Empty;
                if (edit.IsRemoved(MetadataEditKeys.PdfKeywords))
                    info.Keywords = string.Empty;
            }

            document.Options.CompressContentStreams = true;
            document.Save(tmp);
            progress.Report(1);
        }, ct));

    private static PdfDocument OpenPdf(string path, PdfDocumentOpenMode mode)
    {
        try
        {
            return PdfReader.Open(path, mode);
        }
        catch (PdfReaderException ex)
        {
            throw new ConversionException(T("error.pdf.cannotOpen", Path.GetFileName(path), ex.Message), ex);
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Adds an editable row for a tag the file does not have yet, so it can still be written.</summary>
    private static void EnsureEditable(List<MetadataField> fields, string groupKey, string label, string editKey)
    {
        if (fields.Any(field => string.Equals(field.EditKey, editKey, StringComparison.Ordinal)))
            return;
        fields.Add(new MetadataField(groupKey, label, string.Empty, editKey));
    }

    private static long LengthOf(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>EXIF values are strings, numbers or arrays; arrays are shown comma separated.</summary>
    private static string FormatValue(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string text:
                return text;
            case byte[] bytes:
                return string.Create(CultureInfo.InvariantCulture, $"{bytes.Length} bytes");
            case System.Collections.IEnumerable items:
                return string.Join(", ", items.Cast<object?>().Select(FormatValue));
            case IFormattable formattable:
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;
            default:
                return value.ToString() ?? string.Empty;
        }
    }
}
