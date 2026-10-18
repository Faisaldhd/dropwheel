#if !WINDOWS10_0_19041_0_OR_GREATER
using System.Runtime.InteropServices;
using FileAction.App.Native;

namespace Windows.Media.Ocr;

/// <summary>
/// Compatibility facade for the raw-COM OCR implementation. It exists only when the app targets a
/// framework without the Windows SDK projection (<c>net10.0-windows</c>), where the real
/// <c>Windows.Media.Ocr.OcrEngine</c> type does not exist. It mirrors the small part of the WinRT
/// surface the rest of the app names, so those call sites stay unchanged.
/// </summary>
internal sealed class OcrEngine
{
    private nint _handle;

    private OcrEngine(nint handle) => _handle = handle;

    internal nint Handle => _handle;

    // The WinRT projection released its engine through the RCW finalizer; keep the same lifetime.
    ~OcrEngine()
    {
        var handle = Interlocked.Exchange(ref _handle, 0);
        if (handle != 0)
            Marshal.Release(handle);
    }

    public static IReadOnlyList<Language> AvailableRecognizerLanguages => Language.Available;

    // WinRT metadata has no nullable annotations: the WinRT methods are declared non-nullable and
    // simply return null when no engine can be created, so the shim mirrors that.
    public static OcrEngine TryCreateFromUserProfileLanguages()
    {
        var handle = WinRtOcr.TryCreateFromUserProfileLanguages();
        return handle == 0 ? null! : new OcrEngine(handle);
    }

    public static OcrEngine TryCreateFromLanguage(Language language)
    {
        var handle = WinRtOcr.TryCreateFromLanguage(language.Handle);
        return handle == 0 ? null! : new OcrEngine(handle);
    }

    internal string Recognize(string png) => WinRtOcr.Recognize(Handle, png);
}

/// <summary>
/// One recognizer language. It only carries the ABI pointer of the <c>Language</c> object owned by
/// <c>OcrEngine.AvailableRecognizerLanguages</c>; it is not a projection of that WinRT class.
/// </summary>
internal sealed class Language
{
    // Per thread: the wrapped Language objects belong to the apartment that created them.
    [ThreadStatic]
    private static IReadOnlyList<Language>? _available;

    internal Language(nint handle) => Handle = handle;

    internal nint Handle { get; }

    internal static IReadOnlyList<Language> Available =>
        _available ??= WinRtOcr.LanguagePointers().Select(static handle => new Language(handle)).ToArray();
}
#endif



