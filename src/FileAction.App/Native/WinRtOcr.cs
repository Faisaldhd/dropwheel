using System.Runtime.InteropServices;
using System.Text;

namespace FileAction.App.Native;

/// <summary>
/// Windows OCR ("extract text") through the raw WinRT ABI: <c>Windows.Media.Ocr</c>,
/// <c>Windows.Graphics.Imaging</c> and <c>Windows.Storage</c> without the CsWinRT projection.
/// </summary>
/// <remarks>
/// Interface IIDs and vtable slots come from the Windows SDK metadata (10.0.19041). WinRT interfaces
/// start their own members at vtable slot 6 (IUnknown 0-2, IInspectable 3-5). Every COM pointer is
/// released in a <c>finally</c>; the only things kept for the process lifetime are the
/// <c>IVectorView&lt;Language&gt;</c> returned by <c>AvailableRecognizerLanguages</c> and the language
/// pointers it owns.
/// </remarks>
internal static class WinRtOcr
{
    private const string OcrEngineClass = "Windows.Media.Ocr.OcrEngine";
    private const string BitmapDecoderClass = "Windows.Graphics.Imaging.BitmapDecoder";
    private const string BitmapTransformClass = "Windows.Graphics.Imaging.BitmapTransform";
    private const string StorageFileClass = "Windows.Storage.StorageFile";

    private static readonly Guid IID_IOcrEngineStatics = new("5bffa85a-3384-3540-9940-699120d428a8");
    private static readonly Guid IID_IOcrEngine = new("5a14bc41-5b76-3140-b680-8825562683ac");
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

    private static readonly object Gate = new();

    /// <summary>COM pointers kept for the process lifetime (their children stay valid because of them).</summary>
    private static readonly List<nint> LifetimeHandles = [];

    // Per thread: a Language object created in one apartment must not be handed to another one.
    [ThreadStatic]
    private static nint[]? _languagePointers;

    /// <summary>
    /// The <c>Language</c> pointers offered by <c>OcrEngine.AvailableRecognizerLanguages</c>, created for
    /// the calling thread. They stay valid because the vector that owns them is kept in
    /// <see cref="LifetimeHandles"/>.
    /// </summary>
    internal static nint[] LanguagePointers() => _languagePointers ??= LoadLanguagePointers();

    private static nint[] LoadLanguagePointers()
    {
        WinRt.EnsureCom();
        var factory = WinRt.ActivationFactory(OcrEngineClass, IID_IOcrEngineStatics);
        try
        {
            var getLanguages = WinRt.Method<WinRt.GetObjectDelegate>(factory, 7);
            WinRt.Check(getLanguages(factory, out var vector));
            lock (Gate)
                LifetimeHandles.Add(vector);

            var getSize = WinRt.Method<WinRt.GetUInt32Delegate>(vector, 7);
            WinRt.Check(getSize(vector, out var count));

            var getAt = WinRt.Method<WinRt.GetAtDelegate>(vector, 6);
            var pointers = new nint[count];
            for (uint i = 0; i < count; i++)
                WinRt.Check(getAt(vector, i, out pointers[i]));
            return pointers;
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    /// <summary>Returns an <c>IOcrEngine</c> pointer, or zero when Windows cannot create one.</summary>
    internal static nint TryCreateFromUserProfileLanguages()
    {
        WinRt.EnsureCom();
        var factory = WinRt.ActivationFactory(OcrEngineClass, IID_IOcrEngineStatics);
        try
        {
            var create = WinRt.Method<WinRt.GetObjectDelegate>(factory, 10);
            WinRt.Check(create(factory, out var engine));
            return ToEngine(engine);
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    /// <summary>Returns an <c>IOcrEngine</c> pointer for one language, or zero when it is unsupported.</summary>
    internal static nint TryCreateFromLanguage(nint language)
    {
        WinRt.EnsureCom();
        var factory = WinRt.ActivationFactory(OcrEngineClass, IID_IOcrEngineStatics);
        try
        {
            var create = WinRt.Method<WinRt.GetObjectWithObjectDelegate>(factory, 9);
            WinRt.Check(create(factory, language, out var engine));
            return ToEngine(engine);
        }
        finally
        {
            Marshal.Release(factory);
        }
    }

    private static nint ToEngine(nint engine)
    {
        if (engine == 0)
            return 0;
        try
        {
            return WinRt.QueryInterface(engine, IID_IOcrEngine);
        }
        finally
        {
            Marshal.Release(engine);
        }
    }

    /// <summary>Recognizes one PNG file and returns the text lines joined with newlines.</summary>
    internal static string Recognize(nint ocrEngine, string pngPath)
    {
        WinRt.EnsureCom();

        var max = GetMaxImageDimension();

        var fileFactory = WinRt.ActivationFactory(StorageFileClass, WinRt.IID_IStorageFileStatics);
        nint storageFile = 0;
        try
        {
            nint operation = 0;
            var hpath = WinRt.CreateString(Path.GetFullPath(pngPath));
            try
            {
                var getFile = WinRt.Method<WinRt.AsyncWithStringDelegate>(fileFactory, 6);
                WinRt.Check(getFile(fileFactory, hpath, out operation));
            }
            finally
            {
                WinRt.DeleteString(hpath);
            }

            var fileObject = WinRt.CallAndWait(operation);
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
            var stream = WinRt.CallAndWait(operation);
            try
            {
                var decoderFactory = WinRt.ActivationFactory(BitmapDecoderClass, IID_IBitmapDecoderStatics);
                nint decoder = 0;
                try
                {
                    var create = WinRt.Method<WinRt.AsyncActionDelegate>(decoderFactory, 14);
                    WinRt.Check(create(decoderFactory, stream, out operation));
                    decoder = WinRt.CallAndWait(operation);
                }
                finally
                {
                    Marshal.Release(decoderFactory);
                }

                try
                {
                    return RecognizeBitmap(ocrEngine, decoder, max);
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
    }

    private static string RecognizeBitmap(nint ocrEngine, nint decoder, uint max)
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

        // The engine rejects images above MaxImageDimension; scale those down exactly like the old code.
        var scale = Math.Min(1.0, (double)max / Math.Max(width, height));

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

        var bitmapObject = WinRt.CallAndWait(operation);
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
            var recognize = WinRt.Method<WinRt.AsyncActionDelegate>(ocrEngine, 6);
            WinRt.Check(recognize(ocrEngine, softwareBitmap, out operation));
        }
        finally
        {
            Marshal.Release(softwareBitmap);
        }

        var resultObject = WinRt.CallAndWait(operation);
        try
        {
            return ReadLines(resultObject);
        }
        finally
        {
            Marshal.Release(resultObject);
        }
    }

    private static string ReadLines(nint resultObject)
    {
        var result = WinRt.QueryInterface(resultObject, IID_IOcrResult);
        try
        {
            var getLines = WinRt.Method<WinRt.GetObjectDelegate>(result, 6);
            WinRt.Check(getLines(result, out var lines));
            try
            {
                var getSize = WinRt.Method<WinRt.GetUInt32Delegate>(lines, 7);
                WinRt.Check(getSize(lines, out var count));
                var getAt = WinRt.Method<WinRt.GetAtDelegate>(lines, 6);

                var text = new StringBuilder();
                for (uint i = 0; i < count; i++)
                {
                    nint lineObject;
                    WinRt.Check(getAt(lines, i, out lineObject));
                    try
                    {
                        var line = WinRt.QueryInterface(lineObject, IID_IOcrLine);
                        try
                        {
                            var getText = WinRt.Method<WinRt.GetObjectDelegate>(line, 7);
                            WinRt.Check(getText(line, out var hstring));
                            var lineText = WinRt.ReadString(hstring);
                            WinRt.DeleteString(hstring);
                            if (text.Length > 0)
                                text.Append('\n');
                            text.Append(lineText);
                        }
                        finally
                        {
                            Marshal.Release(line);
                        }
                    }
                    finally
                    {
                        Marshal.Release(lineObject);
                    }
                }

                return text.ToString().Trim();
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

    private static uint GetMaxImageDimension()
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



