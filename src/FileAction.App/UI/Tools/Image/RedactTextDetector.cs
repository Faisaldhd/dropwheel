using System.IO;
using System.Runtime.InteropServices;
using FileAction.App.Native;
using FileAction.Core;

namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// "Detect text": asks Windows OCR (the same engine the "extract text" operation uses) for the words it
/// sees in the image and returns one box per text line. Runs fully offline through the raw WinRT ABI the
/// app already uses for OCR — no SDK projection, no extra package and nothing leaves the PC.
/// </summary>
/// <remarks>
/// The decoding and recognition steps mirror <c>Native/WinRtOcr.cs</c> (the proven OCR path); this file adds
/// the two calls that flow needs to expose rectangles: <c>IOcrLine.Words</c> (slot 6) and
/// <c>IOcrWord.BoundingRect</c> (slot 6, returned through an out pointer because WinRT returns structs that
/// way). Both objects are addressed through their default interface, which is how
/// <c>IVectorView&lt;OcrLine&gt;</c> and <c>IVectorView&lt;OcrWord&gt;</c> hand their elements out; the
/// runtime class name is still checked before the rectangle call so an unexpected pointer can never be
/// read as a rectangle.
/// </remarks>
internal static class RedactTextDetector
{
    private const string OcrEngineClass = "Windows.Media.Ocr.OcrEngine";
    private const string BitmapDecoderClass = "Windows.Graphics.Imaging.BitmapDecoder";
    private const string BitmapTransformClass = "Windows.Graphics.Imaging.BitmapTransform";
    private const string StorageFileClass = "Windows.Storage.StorageFile";
    private const string OcrWordRuntimeClass = "Windows.Media.Ocr.OcrWord";

    // Same IIDs/slots as the OCR path in Native\WinRtOcr.cs (Windows SDK 10.0.19041 metadata).
    private static readonly Guid IID_IOcrEngineStatics = new("5bffa85a-3384-3540-9940-699120d428a8");
    private static readonly Guid IID_IOcrResult = new("9bd235b2-175b-3d6a-92e2-388c206e2f63");
    private static readonly Guid IID_IOcrLine = new("0043a16f-e31f-3a24-899c-d444bd088124");
    private static readonly Guid IID_IBitmapDecoderStatics = new("438ccb26-bcef-4e95-bad6-23a822e58d01");
    private static readonly Guid IID_IBitmapFrame = new("72a49a1c-8081-438d-91bc-94ecfc8185c6");
    private static readonly Guid IID_IBitmapFrameWithSoftwareBitmap = new("fe287c9a-420c-4963-87ad-691436e08383");
    private static readonly Guid IID_IBitmapTransform = new("ae755344-e268-4d35-adcf-e995d31a8d34");
    private static readonly Guid IID_ISoftwareBitmap = new("689e0708-7eef-483f-963f-da938818e073");

    // FileAccessMode.Read
    private const int FileAccessModeRead = 0;
    // BitmapPixelFormat.Bgra8
    private const int BitmapPixelFormatBgra8 = 87;
    // BitmapAlphaMode.Premultiplied
    private const int BitmapAlphaModePremultiplied = 0;
    // BitmapInterpolationMode.Fant
    private const int BitmapInterpolationModeFant = 3;
    // ExifOrientationMode.RespectExifOrientation
    private const int ExifOrientationModeRespect = 1;
    // ColorManagementMode.DoNotColorManage
    private const int ColorManagementModeDoNotColorManage = 0;

    /// <summary>Windows.Foundation.Rect: four floats, returned by value through an out pointer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetRectDelegate(nint instance, out NativeRect value);

    /// <summary>
    /// Words found in <paramref name="png"/>, grouped into one box per OCR line, in the PNG's own pixels.
    /// Returns null when Windows OCR is not available on this PC; an empty list when it found no text.
    /// </summary>
    public static IReadOnlyList<RedactionBox>? Detect(byte[] png, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(png);

        var directory = OutputPaths.TempDirectory();
        try
        {
            var path = Path.Combine(directory, "redact-ocr.png");
            File.WriteAllBytes(path, png);

            var engine = CreateEngine();
            if (engine == 0)
                return null;
            try
            {
                return ReadWords(engine, path, cancellationToken);
            }
            finally
            {
                Marshal.Release(engine);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Missing OCR language packs, a COM failure or an unreadable image all mean "no detection".
            return null;
        }
        finally
        {
            OutputPaths.TryDelete(directory);
        }
    }

    /// <summary>The user's OCR language, or the first installed one; 0 when OCR cannot run here.</summary>
    private static nint CreateEngine()
    {
        WinRt.EnsureCom();
        var engine = WinRtOcr.TryCreateFromUserProfileLanguages();
        if (engine != 0)
            return engine;

        foreach (var language in WinRtOcr.LanguagePointers())
        {
            engine = WinRtOcr.TryCreateFromLanguage(language);
            if (engine != 0)
                return engine;
        }
        return 0;
    }

    /// <summary>One box per OCR line, in PNG pixels (the bitmap is scaled down for the engine, then back).</summary>
    private static List<RedactionBox> ReadWords(nint engine, string pngPath, CancellationToken cancellationToken)
    {
        WinRt.EnsureCom();
        var boxes = new List<RedactionBox>();
        var maxDimension = MaxImageDimension();

        var fileFactory = WinRt.ActivationFactory(StorageFileClass, WinRt.IID_IStorageFileStatics);
        nint storageFile = 0;
        try
        {
            nint operation = 0;
            var path = WinRt.CreateString(Path.GetFullPath(pngPath));
            try
            {
                var getFile = WinRt.Method<WinRt.AsyncWithStringDelegate>(fileFactory, 6);
                WinRt.Check(getFile(fileFactory, path, out operation));
            }
            finally
            {
                WinRt.DeleteString(path);
            }

            var fileObject = WinRt.CallAndWait(operation, cancellationToken);
            try
            {
                storageFile = WinRt.QueryInterface(fileObject, WinRt.IID_IStorageFile);
            }
            finally
            {
                Marshal.Release(fileObject);
            }
        }
        finally
        {
            Marshal.Release(fileFactory);
        }

        try
        {
            nint operation = 0;
            var open = WinRt.Method<WinRt.OpenAsyncDelegate>(storageFile, 8);
            WinRt.Check(open(storageFile, FileAccessModeRead, out operation));
            var stream = WinRt.CallAndWait(operation, cancellationToken);
            try
            {
                var decoderFactory = WinRt.ActivationFactory(BitmapDecoderClass, IID_IBitmapDecoderStatics);
                nint decoder = 0;
                try
                {
                    var create = WinRt.Method<WinRt.AsyncActionDelegate>(decoderFactory, 14);
                    WinRt.Check(create(decoderFactory, stream, out operation));
                    decoder = WinRt.CallAndWait(operation, cancellationToken);
                }
                finally
                {
                    Marshal.Release(decoderFactory);
                }

                try
                {
                    ReadLines(engine, decoder, maxDimension, cancellationToken, boxes);
                }
                finally
                {
                    Marshal.Release(decoder);
                }
            }
            finally
            {
                Marshal.Release(stream);
            }
        }
        finally
        {
            Marshal.Release(storageFile);
        }

        return boxes;
    }

    private static void ReadLines(
        nint engine,
        nint decoder,
        uint maxDimension,
        CancellationToken cancellationToken,
        List<RedactionBox> boxes)
    {
        uint width, height;
        var frame = WinRt.QueryInterface(decoder, IID_IBitmapFrame);
        try
        {
            var getWidth = WinRt.Method<WinRt.GetUInt32Delegate>(frame, 12);
            var getHeight = WinRt.Method<WinRt.GetUInt32Delegate>(frame, 13);
            WinRt.Check(getWidth(frame, out width));
            WinRt.Check(getHeight(frame, out height));
        }
        finally
        {
            Marshal.Release(frame);
        }

        // The engine rejects images above MaxImageDimension; find the scale so boxes can be mapped back.
        var scale = Math.Min(1.0, (double)maxDimension / Math.Max(1u, Math.Max(width, height)));

        var transformObject = WinRt.Activate(BitmapTransformClass);
        nint transform = 0;
        try
        {
            transform = WinRt.QueryInterface(transformObject, IID_IBitmapTransform);
        }
        finally
        {
            Marshal.Release(transformObject);
        }

        nint operation = 0;
        nint softwareBitmap = 0;
        try
        {
            var putWidth = WinRt.Method<WinRt.SetUInt32Delegate>(transform, 7);
            var putHeight = WinRt.Method<WinRt.SetUInt32Delegate>(transform, 9);
            var putInterpolation = WinRt.Method<WinRt.SetInt32Delegate>(transform, 11);
            WinRt.Check(putWidth(transform, (uint)Math.Max(1, width * scale)));
            WinRt.Check(putHeight(transform, (uint)Math.Max(1, height * scale)));
            WinRt.Check(putInterpolation(transform, BitmapInterpolationModeFant));

            var frameWithBitmap = WinRt.QueryInterface(decoder, IID_IBitmapFrameWithSoftwareBitmap);
            try
            {
                var getBitmap = WinRt.Method<WinRt.GetSoftwareBitmapDelegate>(frameWithBitmap, 8);
                WinRt.Check(getBitmap(frameWithBitmap, BitmapPixelFormatBgra8, BitmapAlphaModePremultiplied, transform,
                    ExifOrientationModeRespect, ColorManagementModeDoNotColorManage, out operation));
            }
            finally
            {
                Marshal.Release(frameWithBitmap);
            }
        }
        finally
        {
            Marshal.Release(transform);
        }

        var bitmapObject = WinRt.CallAndWait(operation, cancellationToken);
        try
        {
            softwareBitmap = WinRt.QueryInterface(bitmapObject, IID_ISoftwareBitmap);
        }
        finally
        {
            Marshal.Release(bitmapObject);
        }

        try
        {
            var recognize = WinRt.Method<WinRt.AsyncActionDelegate>(engine, 6);
            WinRt.Check(recognize(engine, softwareBitmap, out operation));
        }
        finally
        {
            Marshal.Release(softwareBitmap);
        }

        var resultObject = WinRt.CallAndWait(operation, cancellationToken);
        try
        {
            ReadLinesFromResult(resultObject, 1 / scale, cancellationToken, boxes);
        }
        finally
        {
            Marshal.Release(resultObject);
        }
    }

    private static void ReadLinesFromResult(
        nint resultObject,
        double toPngPixels,
        CancellationToken cancellationToken,
        List<RedactionBox> boxes)
    {
        var result = WinRt.QueryInterface(resultObject, IID_IOcrResult);
        try
        {
            var getLines = WinRt.Method<WinRt.GetObjectDelegate>(result, 6);
            WinRt.Check(getLines(result, out var lines));
            if (lines == 0)
                return;

            try
            {
                var count = Count(lines);
                var getAt = WinRt.Method<WinRt.GetAtDelegate>(lines, 6);
                for (uint i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    nint lineObject;
                    WinRt.Check(getAt(lines, i, out lineObject));
                    try
                    {
                        if (TryReadLine(lineObject, toPngPixels, cancellationToken, out var line))
                            boxes.Add(line);
                    }
                    finally
                    {
                        Marshal.Release(lineObject);
                    }
                }
            }
            finally
            {
                Marshal.Release(lines);
            }
        }
        finally
        {
            Marshal.Release(result);
        }
    }

    private static bool TryReadLine(
        nint lineObject,
        double toPngPixels,
        CancellationToken cancellationToken,
        out RedactionBox line)
    {
        line = default;

        var ocrLine = WinRt.QueryInterface(lineObject, IID_IOcrLine);
        try
        {
            var getWords = WinRt.Method<WinRt.GetObjectDelegate>(ocrLine, 6);
            WinRt.Check(getWords(ocrLine, out var words));
            if (words == 0)
                return false;

            try
            {
                var count = Count(words);
                var getAt = WinRt.Method<WinRt.GetAtDelegate>(words, 6);
                var wordBoxes = new List<RedactionBox>((int)Math.Min(count, 64u));
                for (uint i = 0; i < count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    nint word;
                    WinRt.Check(getAt(words, i, out word));
                    try
                    {
                        if (TryReadWord(word, toPngPixels, out var box))
                            wordBoxes.Add(box);
                    }
                    finally
                    {
                        Marshal.Release(word);
                    }
                }

                line = RedactionText.Union(wordBoxes);
                return !line.IsEmpty;
            }
            finally
            {
                Marshal.Release(words);
            }
        }
        finally
        {
            Marshal.Release(ocrLine);
        }
    }

    private static bool TryReadWord(nint word, double toPngPixels, out RedactionBox box)
    {
        box = default;
        if (!IsOcrWord(word))
            return false;

        var getBounds = WinRt.Method<GetRectDelegate>(word, 6);
        if (getBounds(word, out var rect) < 0)
            return false;

        // Guard the ABI reading: a rectangle the engine could not have produced is dropped, so a mapping
        // problem can never turn into a redacted area over the wrong pixels.
        if (!float.IsFinite(rect.X) || !float.IsFinite(rect.Y) ||
            !float.IsFinite(rect.Width) || !float.IsFinite(rect.Height) ||
            rect.Width <= 0 || rect.Height <= 0)
            return false;

        box = new RedactionBox(
            rect.X * toPngPixels,
            rect.Y * toPngPixels,
            rect.Width * toPngPixels,
            rect.Height * toPngPixels);
        return !box.IsEmpty;
    }

    /// <summary>
    /// Confirms the object really is an OCR word before slot 6 is read as its rectangle: the vector hands
    /// elements out through their default interface, but a wrong class would make that call meaningless.
    /// </summary>
    private static bool IsOcrWord(nint instance)
    {
        try
        {
            var getName = WinRt.Method<WinRt.GetObjectDelegate>(instance, 4);
            if (getName(instance, out var name) < 0 || name == 0)
                return false;
            try
            {
                return string.Equals(WinRt.ReadString(name), OcrWordRuntimeClass, StringComparison.Ordinal);
            }
            finally
            {
                WinRt.DeleteString(name);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static uint Count(nint vector)
    {
        var getSize = WinRt.Method<WinRt.GetUInt32Delegate>(vector, 7);
        WinRt.Check(getSize(vector, out var count));
        return count;
    }

    private static uint MaxImageDimension()
    {
        var factory = WinRt.ActivationFactory(OcrEngineClass, IID_IOcrEngineStatics);
        try
        {
            var getMax = WinRt.Method<WinRt.GetUInt32Delegate>(factory, 6);
            WinRt.Check(getMax(factory, out var value));
            return value;
        }
        finally
        {
            Marshal.Release(factory);
        }
    }
}
