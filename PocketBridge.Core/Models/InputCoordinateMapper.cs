namespace PocketBridge.Core.Models;

public sealed record VisibleVideoRect(double Left, double Top, double Width, double Height)
{
    public NormalizedPoint? ToNormalized(double x, double y)
    {
        if (Width <= 0 || Height <= 0 || x < Left || y < Top || x > Left + Width || y > Top + Height) return null;
        return new NormalizedPoint((x - Left) / Width, (y - Top) / Height).Clamp();
    }
    public (double X, double Y) ToControl(NormalizedPoint point) { var value = point.Clamp(); return (Left + value.X * Width, Top + value.Y * Height); }
}

public static class InputCoordinateMapper
{
    public static VisibleVideoRect Fit(double hostWidth, double hostHeight, double videoWidth, double videoHeight, double originX = 0, double originY = 0)
    {
        if (hostWidth <= 0 || hostHeight <= 0 || videoWidth <= 0 || videoHeight <= 0) return new(originX, originY, 0, 0);
        var scale = Math.Min(hostWidth / videoWidth, hostHeight / videoHeight); var width = videoWidth * scale; var height = videoHeight * scale;
        return new(originX + (hostWidth - width) / 2, originY + (hostHeight - height) / 2, width, height);
    }
}
