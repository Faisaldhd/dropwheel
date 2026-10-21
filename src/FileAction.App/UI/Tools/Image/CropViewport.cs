namespace FileAction.App.UI.Tools.Image;

/// <summary>
/// Maps original image pixels onto the crop preview. The preview card's padding is already outside the
/// photo well, so the fit uses the well's full size: subtracting the card padding here double-counted it
/// and made the drawn rectangle drift from the saved crop (the drag delta then converted through a scale
/// smaller than the one the Image control actually used).
/// </summary>
internal readonly record struct CropViewport(double Scale, double OriginX, double OriginY)
{
    /// <summary>Fits the image into <paramref name="viewWidth"/> × <paramref name="viewHeight"/>, centred.</summary>
    public static CropViewport Fit(double viewWidth, double viewHeight, int imageWidth, int imageHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
            return new CropViewport(1, 0, 0);

        var scale = Math.Min(viewWidth / imageWidth, viewHeight / imageHeight);
        if (!(scale > 0) || double.IsInfinity(scale))
            scale = 1;

        return new CropViewport(
            scale,
            (viewWidth - imageWidth * scale) / 2,
            (viewHeight - imageHeight * scale) / 2);
    }

    /// <summary>Preview X (DIP) of an original-image X.</summary>
    public double ToViewX(double imageX) => OriginX + imageX * Scale;

    /// <summary>Preview Y (DIP) of an original-image Y.</summary>
    public double ToViewY(double imageY) => OriginY + imageY * Scale;

    /// <summary>Original-image X of a point inside the photo well.</summary>
    public double ToImageX(double viewX) => (viewX - OriginX) / Scale;

    /// <summary>Original-image Y of a point inside the photo well.</summary>
    public double ToImageY(double viewY) => (viewY - OriginY) / Scale;
}
