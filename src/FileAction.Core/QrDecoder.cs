using ZXing;
using ZXing.Common;

namespace FileAction.Core;

/// <summary>
/// One QR code decoder, backed by ZXing.Net (Apache-2.0). It reads a BGRA32 pixel buffer the caller has
/// already rasterised, so nothing here opens a file, a window or a socket: decoding is entirely local.
/// </summary>
public static class QrDecoder
{
    /// <summary>
    /// The QR text in <paramref name="bgraPixels"/>, or null when the image carries no readable code.
    /// A malformed buffer, an unsupported rotation or a damaged code all mean "no code", never an exception.
    /// </summary>
    public static string? Decode(byte[] bgraPixels, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bgraPixels);
        if (width <= 0 || height <= 0)
            return null;
        if (bgraPixels.LongLength < (long)width * height * 4)
            return null;

        var reader = new BarcodeReaderGeneric
        {
            // Phone screenshots arrive at any rotation; inverted codes are printed white-on-black.
            AutoRotate = true,
            Options = new DecodingOptions
            {
                PossibleFormats = [BarcodeFormat.QR_CODE],
                TryHarder = true,
                TryInverted = true,
            },
        };

        try
        {
            var result = reader.Decode(bgraPixels, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
            var text = result?.Text;
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
