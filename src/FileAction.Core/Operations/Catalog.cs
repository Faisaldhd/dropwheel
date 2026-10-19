using System.Globalization;
using FileAction.Core.Backends;
using static FileAction.Core.Operations.Helpers;
using static FileAction.Core.Loc;

namespace FileAction.Core.Operations;

/// <summary>Every conversion and tool the wheels can offer. The app adds its own file actions.</summary>
public static class Catalog
{
    public static IReadOnlyList<Operation> Build()
    {
        var ops = new List<Operation>();
        AddImages(ops);
        AddAudio(ops);
        AddVideo(ops);
        AddPdf(ops);
        AddDocuments(ops);
        AddEpub(ops);
        AddSlides(ops);
        AddSpreadsheets(ops);
        AddSubtitles(ops);
        AddArchives(ops);
        return ops;
    }

    /// <summary>Operations for a wheel that apply to the dropped items and whose tools are installed.</summary>
    public static IReadOnlyList<Operation> For(IEnumerable<Operation> all, WheelKind wheel, IReadOnlyList<FileItem> items, ToolLocator tools) =>
        all.Where(o => o.Wheel == wheel && o.AppliesTo(items) && o.IsAvailable(tools, items)).ToList();

    // ---------------------------------------------------------------- images

    /// <summary>
    /// Image targets the packaged writer can actually encode. HEIC is readable (Magick.NET maps it), but
    /// its bundled libheif has a HEVC decoder and an AV1 encoder only — no HEVC encoder — so writing HEIC
    /// always fails. The target stays declared but is never offered, rather than producing a broken file.
    /// </summary>
    private static bool CanEncodeImage(string format) =>
        !string.Equals(format, "heic", StringComparison.OrdinalIgnoreCase);

    private static void AddImages(List<Operation> ops)
    {
        foreach (var target in Formats.ImageOutputs)
        {
            ops.Add(new PerFileOperation($"image.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Photo, Formats.Images,
                async (item, ctx, _) => [await Produce(item, ctx, target, tmp => ImageBackend.ConvertAsync(item.Path, tmp, target, ctx.Settings, ctx.CancellationToken))],
                targetFormat: target, subtitle: target == "pdf" ? T("catalog.file.document") : null)
            {
                AvailableFor = (_, _) => CanEncodeImage(target),
            });
        }
        ops.Add(new PerFileOperation("gif.to.mp4", WheelKind.Convert, "MP4", Glyphs.Video, ["gif"],
            async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, "mp4", ctx, p)], subtitle: T("catalog.video.subtitle"))
        { Requires = [ExternalTool.Ffmpeg] });
        ops.Add(new PerFileOperation("gif.to.webm", WheelKind.Convert, "WEBM", Glyphs.Video, ["gif"],
            async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, "webm", ctx, p)], subtitle: T("catalog.video.subtitle"))
        { Requires = [ExternalTool.Ffmpeg] });

        ImageTool(ops, "image.compress", T("catalog.image.compress.label"), Glyphs.Shrink, T("catalog.image.compress.suffix"),
            (item, ctx, tmp) => ImageBackend.CompressAsync(item.Path, tmp, ctx.Settings.CompressQuality, ctx.CancellationToken));
        ImageEdit(ops, "image.half", T("catalog.image.half.label"), Glyphs.Shrink, "50 %", T("catalog.image.half.suffix"), img => ImageBackend.ResizePercent(img, 50));
        ImageEdit(ops, "image.max1920", T("catalog.image.max1920.label"), Glyphs.Shrink, T("catalog.image.max1920.subtitle"), "1920", img => ImageBackend.ResizeMax(img, 1920));
        ImageEdit(ops, "image.rotate.right", T("catalog.image.rotate.right.label"), Glyphs.Rotate, T("catalog.image.rotate.right.subtitle"), T("catalog.image.rotate.suffix"), img => img.Rotate(90));
        ImageEdit(ops, "image.rotate.left", T("catalog.image.rotate.left.label"), Glyphs.Rotate, T("catalog.image.rotate.left.subtitle"), T("catalog.image.rotate.suffix"), img => img.Rotate(-90));
        ImageEdit(ops, "image.flip", T("catalog.image.flip.label"), Glyphs.Flip, null, T("catalog.image.flip.suffix"), img => img.Flop());
        ImageEdit(ops, "image.square", T("catalog.image.square.label"), Glyphs.Crop, T("catalog.image.square.subtitle"), T("catalog.image.square.suffix"), ImageBackend.CropSquare);
        ImageEdit(ops, "image.grayscale", T("catalog.image.grayscale.label"), Glyphs.Color, null, T("catalog.image.grayscale.suffix"), ImageBackend.Grayscale);
        ImageEdit(ops, "image.strip", T("catalog.image.strip.label"), Glyphs.Clear, "GPS, EXIF", T("catalog.image.strip.suffix"), img => img.Strip());
        ops.Add(new BatchOperation("image.merge.pdf", WheelKind.Tools, T("catalog.image.mergePdf.label"), Glyphs.Merge,
            BatchOperation.AllOf(Formats.Images, min: 2),
            async (items, ctx) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(items[0].Path), items[0].BaseName, "pdf",
                tmp => ImageBackend.ImagesToPdfAsync(items.Select(i => i.Path).OrderBy(p => p, NaturalComparer.Instance).ToList(), tmp, ctx.Settings.JpegQuality, ctx.CancellationToken))]));
    }

    private static void ImageTool(List<Operation> ops, string id, string label, string glyph, string suffix, Func<FileItem, OperationContext, string, Task> run, string? subtitle = null) =>
        ops.Add(new PerFileOperation(id, WheelKind.Tools, label, glyph, Formats.Images,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({suffix})",
                ImageBackend.OutputFormatFor(item.Format), tmp => run(item, ctx, tmp))], subtitle: subtitle)
        { Preview = PreviewKind.Image });

    private static void ImageEdit(List<Operation> ops, string id, string label, string glyph, string? subtitle, string suffix, Action<ImageMagick.MagickImage> edit) =>
        ImageTool(ops, id, label, glyph, suffix, (item, ctx, tmp) => ImageBackend.EditAsync(item.Path, tmp, edit, ctx.Settings.JpegQuality, ctx.CancellationToken), subtitle);

    // ---------------------------------------------------------------- audio

    private static void AddAudio(List<Operation> ops)
    {
        foreach (var target in Formats.AudioOutputs)
        {
            ops.Add(new PerFileOperation($"audio.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Audio, Formats.Audio,
                async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, target, ctx, p)], targetFormat: target)
            { Requires = [ExternalTool.Ffmpeg] });
        }

        Ffmpeg(ops, "audio.compress", T("catalog.audio.compress.label"), Glyphs.Shrink, Formats.Audio, T("catalog.audio.compress.suffix"),
            (item, ctx) => ["-vn", "-c:a", "libmp3lame", "-b:a", "128k"], outputFormat: _ => "mp3", subtitle: "MP3 128k");
        Ffmpeg(ops, "audio.normalize", T("catalog.audio.normalize.label"), Glyphs.Equalizer, Formats.Audio, T("catalog.audio.normalize.suffix"),
            (item, ctx) => ["-af", "loudnorm=I=-16:TP=-1.5:LRA=11", .. FfmpegBackend.CodecArgs(FfmpegBackend.SafeContainer(item.Format), ctx.Settings)],
            subtitle: T("catalog.audio.normalize.subtitle"));
        ops.Add(Trim("audio.trim", Formats.Audio));
        ops.Add(new BatchOperation("audio.concat", WheelKind.Tools, T("catalog.audio.concat.label"), Glyphs.Merge, BatchOperation.AllOf(Formats.Audio, min: 2),
            async (items, ctx) =>
            {
                var sorted = items.OrderBy(i => i.Path, NaturalComparer.Instance).ToList();
                var format = FfmpegBackend.SafeContainer(sorted[0].Format);
                var graph = string.Concat(Enumerable.Range(0, sorted.Count).Select(i => $"[{i}:a]")) + $"concat=n={sorted.Count}:v=0:a=1[a]";
                return [await OutputPaths.ProduceAsync(ctx.OutputDirFor(sorted[0].Path), $"{sorted[0].BaseName} ({T("catalog.audio.concat.suffix")})", format, tmp =>
                    FfmpegBackend.RunAsync(ctx, sorted.Select(i => i.Path).ToList(),
                        ["-filter_complex", graph, "-map", "[a]", .. FfmpegBackend.CodecArgs(format, ctx.Settings)], tmp, ctx.Progress))];
            })
        { Requires = [ExternalTool.Ffmpeg] });
    }

    // ---------------------------------------------------------------- video

    private static void AddVideo(List<Operation> ops)
    {
        foreach (var target in Formats.VideoOutputs)
        {
            ops.Add(new PerFileOperation($"video.to.{target}", WheelKind.Convert, Formats.Display(target), target == "mp3" ? Glyphs.Audio : Glyphs.Video, Formats.Video,
                async (item, ctx, p) => [await FfmpegBackend.ConvertAsync(item, target, ctx, p)], targetFormat: target,
                subtitle: target switch { "mp3" => T("catalog.video.toMp3.subtitle"), "gif" => T("catalog.video.toGif.subtitle"), _ => null })
            { Requires = [ExternalTool.Ffmpeg] });
        }

        Ffmpeg(ops, "video.compress", T("catalog.video.compress.label"), Glyphs.Shrink, Formats.Video, T("catalog.video.compress.suffix"),
            (item, ctx) => H264(ctx.Settings.CompressVideoCrf, "slow", null, "128k"), outputFormat: _ => "mp4", subtitle: "H.264");
        Ffmpeg(ops, "video.720p", "720p", Glyphs.Shrink, Formats.Video, "720p",
            (item, ctx) => H264(ctx.Settings.VideoCrf, "medium", "scale=-2:'min(720,ih)'", "160k"), outputFormat: _ => "mp4", subtitle: T("catalog.video.720p.subtitle"));
        Ffmpeg(ops, "video.mute", T("catalog.video.mute.label"), Glyphs.Mute, Formats.Video, T("catalog.video.mute.suffix"),
            (item, ctx) => FfmpegBackend.SafeContainer(item.Format) == item.Format
                ? ["-map", "0:v", "-c:v", "copy", "-an"]
                : [.. H264(ctx.Settings.VideoCrf, "medium", null, null), "-an"],
            subtitle: T("catalog.video.mute.subtitle"));
        ops.Add(Trim("video.trim", Formats.Video));
        Ffmpeg(ops, "video.rotate", T("catalog.video.rotate.label"), Glyphs.Rotate, Formats.Video, T("catalog.video.rotate.suffix"),
            (item, ctx) => H264(ctx.Settings.VideoCrf, "medium", "transpose=1", "160k"), outputFormat: _ => "mp4", subtitle: T("catalog.video.rotate.subtitle"));
        ops.Add(new PerFileOperation("video.speed2", WheelKind.Tools, T("catalog.video.speed2.label"), Glyphs.Play, Formats.Video,
            async (item, ctx, p) =>
            {
                var factor = SpeedFactor(ctx.Param("speed", "2"));
                var setpts = (1 / factor).ToString("0.####", CultureInfo.InvariantCulture) + "*PTS";
                return [await FfmpegBackend.TransformAsync(item, $"{factor.ToString("0.##", CultureInfo.InvariantCulture)}×", ctx, p,
                    [.. H264(ctx.Settings.VideoCrf, "medium", "setpts=" + setpts, "160k"), "-af", string.Join(",", Atempo(factor))],
                    outputFormat: "mp4")];
            }, subtitle: "2×")
        {
            Requires = [ExternalTool.Ffmpeg],
            Preview = PreviewKind.Video,
            Parameters =
            [
                new ParameterSpec("speed", T("param.speed.label"), "2", T("param.speed.hint")),
            ],
        });
        Ffmpeg(ops, "video.thumbnail", T("catalog.video.thumbnail.label"), Glyphs.Camera, Formats.Video, T("catalog.video.thumbnail.suffix"),
            (item, ctx) => ["-vf", "thumbnail=150", "-frames:v", "1", "-update", "1"], outputFormat: _ => "png", subtitle: T("catalog.video.thumbnail.subtitle"));
    }

    private static string[] H264(int crf, string preset, string? filter, string? audioBitrate)
    {
        var vf = (filter is null ? "" : filter + ",") + "scale=trunc(iw/2)*2:trunc(ih/2)*2";
        var args = new List<string> { "-c:v", "libx264", "-preset", preset, "-crf", crf.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-vf", vf, "-movflags", "+faststart" };
        if (audioBitrate is not null)
            args.AddRange(["-c:a", "aac", "-b:a", audioBitrate]);
        return [.. args];
    }

    /// <summary>Reads the speed factor from the dialog ("2" = twice as fast).</summary>
    private static double SpeedFactor(string text)
    {
        var raw = text.Trim().Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor) || factor is < 0.25 or > 8)
            throw new ConversionException(T("error.speed.range"));
        return factor;
    }

    /// <summary>One atempo filter handles 0.5×–2×, so bigger changes are chained.</summary>
    private static IEnumerable<string> Atempo(double factor)
    {
        while (factor > 2)
        {
            yield return "atempo=2";
            factor /= 2;
        }
        while (factor < 0.5)
        {
            yield return "atempo=0.5";
            factor *= 2;
        }
        yield return "atempo=" + factor.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void Ffmpeg(List<Operation> ops, string id, string label, string glyph, ISet<string> inputs, string suffix,
        Func<FileItem, OperationContext, IEnumerable<string>> args, Func<FileItem, string>? outputFormat = null, string? subtitle = null) =>
        ops.Add(new PerFileOperation(id, WheelKind.Tools, label, glyph, inputs,
            async (item, ctx, p) => [await FfmpegBackend.TransformAsync(item, suffix, ctx, p, args(item, ctx), outputFormat?.Invoke(item))],
            subtitle: subtitle)
        { Requires = [ExternalTool.Ffmpeg] });

    private static Operation Trim(string id, ISet<string> inputs) =>
        new PerFileOperation(id, WheelKind.Tools, T("catalog.trim.label"), Glyphs.Stopwatch, inputs, async (item, ctx, p) =>
        {
            var start = FfmpegBackend.ParseTime(ctx.Param("start", "0"));
            var endText = ctx.Param("end");
            double? length = endText.Length > 0 ? FfmpegBackend.ParseTime(endText) - start : null;
            if (length is <= 0)
                throw new ConversionException(T("error.trim.endBeforeStart"));
            var format = FfmpegBackend.SafeContainer(item.Format);
            var args = new List<string>();
            if (length is not null)
                args.AddRange(["-t", length.Value.ToString("0.###", CultureInfo.InvariantCulture)]);
            args.AddRange(Formats.Video.Contains(item.Format)
                ? H264(ctx.Settings.VideoCrf, "medium", null, "160k")
                : FfmpegBackend.CodecArgs(format, ctx.Settings));
            return [await FfmpegBackend.TransformAsync(item, T("catalog.trim.suffix"), ctx, p, args,
                Formats.Video.Contains(item.Format) ? "mp4" : format,
                inputArgs: start > 0 ? ["-ss", start.ToString("0.###", CultureInfo.InvariantCulture)] : null,
                duration: length)];
        }, subtitle: T("catalog.trim.subtitle"))
        {
            Requires = [ExternalTool.Ffmpeg],
            Preview = PreviewKind.Video,
            Parameters =
            [
                new ParameterSpec("start", T("param.trim.start.label"), "0:00", T("param.trim.start.hint")),
                new ParameterSpec("end", T("param.trim.end.label"), "", T("param.trim.end.hint")),
            ],
        };

    // ---------------------------------------------------------------- PDF

    private static void AddPdf(List<Operation> ops)
    {
        ops.Add(PdfDoc("docx", T("catalog.pdf.toWord.subtitle")));
        foreach (var image in new[] { "jpg", "png" })
        {
            ops.Add(new PerFileOperation($"pdf.to.{image}", WheelKind.Convert, Formats.Display(image), Glyphs.Photo, Formats.Pdf,
                (item, ctx, p) => PdfBackend.RenderAsync(item, image, ctx, p), subtitle: T("catalog.pdf.toImages.subtitle")));
        }
        ops.Add(new PerFileOperation("pdf.to.pptx", WheelKind.Convert, "PPTX", Glyphs.Page, Formats.Pdf,
            async (item, ctx, _) => [await OfficeBackend.ConvertAsync(item, "pptx", ctx, "impress_pdf_import")], subtitle: T("catalog.pdf.toPptx.subtitle"))
        { Requires = [ExternalTool.LibreOffice] });
        ops.Add(new PerFileOperation("pdf.to.xlsx", WheelKind.Convert, "XLSX", Glyphs.Table, Formats.Pdf,
            async (item, ctx, _) => [await PdfBackend.ToXlsxAsync(item, ctx)], subtitle: T("catalog.pdf.toXlsx.subtitle"))
        { Requires = [ExternalTool.LibreOffice] });
        foreach (var text in new[] { "txt", "md" })
        {
            ops.Add(new PerFileOperation($"pdf.to.{text}", WheelKind.Convert, Formats.Display(text), Glyphs.Text, Formats.Pdf,
                async (item, ctx, _) => [await PdfBackend.ToTextAsync(item, text, ctx)], subtitle: text == "md" ? T("catalog.pdf.toMd.subtitle") : T("catalog.pdf.toTxt.subtitle")));
        }
        ops.Add(PdfDoc("html", T("catalog.pdf.toHtml.subtitle")));
        ops.Add(PdfDoc("epub", T("catalog.pdf.toEpub.subtitle")));

        ops.Add(new BatchOperation("pdf.merge", WheelKind.Tools, T("catalog.pdf.merge.label"), Glyphs.Merge, BatchOperation.AllOf(Formats.Pdf, min: 2),
            async (items, ctx) =>
            {
                var sorted = items.Select(i => i.Path).OrderBy(p => p, NaturalComparer.Instance).ToList();
                return [await OutputPaths.ProduceAsync(ctx.OutputDirFor(sorted[0]), $"{items[0].BaseName} ({T("catalog.pdf.merge.suffix")})", "pdf",
                    tmp => Task.Run(() => PdfBackend.Merge(sorted, tmp), ctx.CancellationToken))];
            }));
        ops.Add(new PerFileOperation("pdf.split", WheelKind.Tools, T("catalog.pdf.split.label"), Glyphs.Split, Formats.Pdf,
            (item, ctx, p) => PdfBackend.SplitAsync(item, ctx, p), subtitle: T("catalog.pdf.split.subtitle")));
        ops.Add(new PerFileOperation("pdf.rotate", WheelKind.Tools, T("catalog.pdf.rotate.label"), Glyphs.Rotate, Formats.Pdf,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("catalog.pdf.rotate.suffix")})", "pdf",
                tmp => Task.Run(() => PdfBackend.Rotate(item.Path, tmp, 90), ctx.CancellationToken))], subtitle: T("catalog.pdf.rotate.subtitle")));
        ops.Add(new PerFileOperation("pdf.optimize", WheelKind.Tools, T("catalog.pdf.optimize.label"), Glyphs.Shrink, Formats.Pdf,
            async (item, ctx, _) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), $"{item.BaseName} ({T("catalog.pdf.optimize.suffix")})", "pdf",
                tmp => Task.Run(() => PdfBackend.Optimize(item.Path, tmp), ctx.CancellationToken))]));
    }

    private static Operation PdfDoc(string target, string subtitle) =>
        new PerFileOperation($"pdf.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Document, Formats.Pdf,
            async (item, ctx, _) => [await PdfBackend.ToDocumentAsync(item, target, ctx)], subtitle: subtitle)
        { Requires = [ExternalTool.Pandoc] };

    // ---------------------------------------------------------------- documents

    private static void AddDocuments(List<Operation> ops)
    {
        foreach (var target in Formats.DocumentOutputs)
        {
            ops.Add(new PerFileOperation($"doc.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Document, Formats.Documents,
                async (item, ctx, _) => [await DocumentAsync(item, target, ctx)], targetFormat: target,
                subtitle: target switch { "docx" => T("catalog.doc.toWord.subtitle"), "md" => T("catalog.doc.toMd.subtitle"), "epub" => T("catalog.doc.toEpub.subtitle"), "txt" => T("catalog.doc.toTxt.subtitle"), "html" => T("catalog.doc.toHtml.subtitle"), _ => null })
            {
                AvailableFor = (tools, item) => DocumentRoute(item.Format, target, tools) is not null,
            });
        }
    }

    private enum DocRoute { Pandoc, Office, PandocThenOffice, OfficeThenPandoc }

    private static DocRoute? DocumentRoute(string input, string target, ToolLocator tools)
    {
        var pandoc = tools.Has(ExternalTool.Pandoc);
        var office = tools.Has(ExternalTool.LibreOffice);
        bool pandocReads = Formats.PandocInputs.Contains(input), officeReads = input is "doc" or "docx" or "odt" or "rtf" or "txt" or "html";
        bool pandocWrites = target != "pdf", officeWrites = target is "pdf" or "docx" or "odt" or "txt" or "html" || (target == "rtf" && input != "html");

        // LibreOffice keeps layout better between word-processor formats; pandoc is better at everything else.
        if (office && officeReads && officeWrites && (target is "pdf" or "docx" or "odt" or "rtf" || !pandoc || !pandocReads))
            return DocRoute.Office;
        if (pandoc && pandocReads && pandocWrites)
            return DocRoute.Pandoc;
        if (pandoc && office && pandocReads && target == "pdf")
            return DocRoute.PandocThenOffice;
        if (pandoc && office && officeReads && pandocWrites)
            return DocRoute.OfficeThenPandoc;
        return null;
    }

    private static async Task<string> DocumentAsync(FileItem item, string target, OperationContext ctx)
    {
        var route = DocumentRoute(item.Format, target, ctx.Tools) ?? throw new ConversionException(
            T("error.document.needsPandocOrOffice", Formats.Display(item.Format), Formats.Display(target)));
        var dir = ctx.OutputDirFor(item.Path);
        switch (route)
        {
            case DocRoute.Office:
                return await OfficeBackend.ConvertAsync(item, target, ctx);
            case DocRoute.Pandoc:
                return await OutputPaths.ProduceAsync(dir, item.BaseName, target, tmp => PandocBackend.ConvertAsync(ctx, item.Path, item.Format, tmp, target));
            case DocRoute.PandocThenOffice:
            {
                var temp = OutputPaths.TempDirectory();
                try
                {
                    var docx = Path.Combine(temp, item.BaseName + ".docx");
                    await PandocBackend.ConvertAsync(ctx, item.Path, item.Format, docx, "docx");
                    return await OfficeBackend.ConvertAsync(new FileItem(docx), target, new OperationContextOverride(ctx, dir).Context);
                }
                finally
                {
                    OutputPaths.TryDelete(temp);
                }
            }
            default:
            {
                var docx = await OfficeBackend.ConvertToTempAsync(ctx, item.Path, "docx");
                try
                {
                    return await OutputPaths.ProduceAsync(dir, item.BaseName, target, tmp => PandocBackend.ConvertAsync(ctx, docx, "docx", tmp, target));
                }
                finally
                {
                    OutputPaths.TryDelete(Path.GetDirectoryName(docx)!);
                }
            }
        }
    }

    // ---------------------------------------------------------------- EPUB

    /// <summary>
    /// EPUB is its own group: pandoc reads EPUB and writes DOCX/TXT/MD/HTML; PDF goes through the same
    /// Pandoc→LibreOffice route as the documents group. No conversion code is duplicated here.
    /// </summary>
    private static void AddEpub(List<Operation> ops)
    {
        foreach (var target in Formats.EpubOutputs)
        {
            ops.Add(new PerFileOperation($"epub.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Document, Formats.Epub,
                async (item, ctx, _) => [await DocumentAsync(item, target, ctx)], targetFormat: target,
                subtitle: target switch { "docx" => T("catalog.doc.toWord.subtitle"), "md" => T("catalog.doc.toMd.subtitle"), "txt" => T("catalog.doc.toTxt.subtitle"), "html" => T("catalog.doc.toHtml.subtitle"), _ => null })
            {
                AvailableFor = (tools, item) => DocumentRoute(item.Format, target, tools) is not null,
            });
        }
    }

    // ---------------------------------------------------------------- slides

    private static void AddSlides(List<Operation> ops)
    {
        foreach (var target in Formats.SlideOutputs)
        {
            ops.Add(new PerFileOperation($"slides.to.{target}", WheelKind.Convert, Formats.Display(target), target is "png" or "jpg" ? Glyphs.Photo : Glyphs.Page,
                Formats.Slides,
                async (item, ctx, p) =>
                {
                    if (target is "png" or "jpg")
                    {
                        var pdf = await OfficeBackend.ConvertToTempAsync(ctx, item.Path, "pdf");
                        try
                        {
                            // Render the temporary PDF but name the output after the presentation.
                            var renamed = Path.Combine(Path.GetDirectoryName(pdf)!, item.BaseName + ".pdf");
                            if (!string.Equals(renamed, pdf, StringComparison.Ordinal))
                                File.Move(pdf, renamed);
                            return await PdfBackend.RenderAsync(new FileItem(renamed), target, new OperationContextOverride(ctx, ctx.OutputDirFor(item.Path)).Context, p);
                        }
                        finally
                        {
                            OutputPaths.TryDelete(Path.GetDirectoryName(pdf)!);
                        }
                    }
                    return [await OfficeBackend.ConvertAsync(item, target, ctx)];
                }, targetFormat: target, subtitle: target is "png" or "jpg" ? T("catalog.slides.toImages.subtitle") : null)
            { Requires = [ExternalTool.LibreOffice] });
        }
    }

    // ---------------------------------------------------------------- spreadsheets

    private static void AddSpreadsheets(List<Operation> ops)
    {
        var native = new HashSet<string>(["xlsx", "xlsm", "xls", "csv", "tsv"], StringComparer.OrdinalIgnoreCase);
        foreach (var target in Formats.SpreadsheetOutputs)
        {
            var nativeTarget = target is "xlsx" or "csv" or "tsv";
            ops.Add(new PerFileOperation($"sheet.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Table, Formats.Spreadsheets,
                async (item, ctx, _) => nativeTarget && native.Contains(item.Format)
                    ? await SpreadsheetBackend.ConvertAsync(item, target, ctx)
                    : [await OfficeBackend.ConvertAsync(item, target, ctx)],
                targetFormat: target, subtitle: target == "xlsx" ? T("catalog.sheet.toXlsx.subtitle") : null)
            {
                AvailableFor = (tools, item) => (nativeTarget && native.Contains(item.Format)) || tools.Has(ExternalTool.LibreOffice),
            });
        }
    }

    // ---------------------------------------------------------------- subtitles

    private static void AddSubtitles(List<Operation> ops)
    {
        foreach (var target in Formats.SubtitleOutputs)
        {
            ops.Add(new PerFileOperation($"subs.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Subtitles, Formats.Subtitles,
                async (item, ctx, _) => [await SubtitleBackend.ConvertAsync(item, target, ctx)], targetFormat: target,
                subtitle: target == "txt" ? T("catalog.subs.toTxt.subtitle") : null));
        }
    }

    // ---------------------------------------------------------------- archives

    private static void AddArchives(List<Operation> ops)
    {
        ops.Add(new PerFileOperation("archive.extract", WheelKind.Convert, T("catalog.archive.extract.label"), Glyphs.Extract, Formats.Archives,
            async (item, ctx, _) => [await ArchiveBackend.ExtractAsync(item.Path, ctx.OutputDirFor(item.Path), ctx.CancellationToken)]));
        foreach (var target in Formats.ArchiveOutputs)
        {
            ops.Add(new PerFileOperation($"archive.to.{target}", WheelKind.Convert, Formats.Display(target), Glyphs.Pack, Formats.Archives,
                async (item, ctx, _) => [await ArchiveBackend.RepackAsync(item, target, ctx)], targetFormat: target));
        }

        foreach (var (target, label) in new[] { ("zip", "ZIP"), ("tar.gz", "TAR.GZ") })
        {
            ops.Add(new BatchOperation($"pack.{target}", WheelKind.Tools, T("catalog.pack.label"), Glyphs.Pack, _ => true,
                async (items, ctx) => [await OutputPaths.ProduceAsync(ctx.OutputDirFor(items[0].Path), ArchiveBackend.ArchiveNameFor(items), target,
                    tmp => ArchiveBackend.PackAsync(items.Select(i => i.Path).ToList(), tmp, target, ctx.CancellationToken))],
                subtitle: label));
        }
    }
}

internal static class Helpers
{
    public static Task<string> Produce(FileItem item, OperationContext ctx, string format, Func<string, Task> produce) =>
        OutputPaths.ProduceAsync(ctx.OutputDirFor(item.Path), item.BaseName, format, produce);
}

/// <summary>Copy of a context that writes into a fixed folder (for multi-step conversions via temp files).</summary>
internal sealed class OperationContextOverride(OperationContext source, string outputDirectory)
{
    public OperationContext Context { get; } = new()
    {
        Tools = source.Tools,
        Progress = source.Progress,
        CancellationToken = source.CancellationToken,
        OutputDirectory = outputDirectory,
        Parameters = source.Parameters,
        Settings = source.Settings,
    };
}

/// <summary>Sorts "page 2" before "page 10".</summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var si = i;
                var sj = j;
                while (i < x.Length && char.IsDigit(x[i])) i++;
                while (j < y.Length && char.IsDigit(y[j])) j++;
                var a = x[si..i].TrimStart('0');
                var b = y[sj..j].TrimStart('0');
                var c = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (c != 0)
                    return c;
            }
            else
            {
                var c = char.ToLowerInvariant(x[i]).CompareTo(char.ToLowerInvariant(y[j]));
                if (c != 0)
                    return c;
                i++;
                j++;
            }
        }
        return (x.Length - i).CompareTo(y.Length - j);
    }
}
