using System.Windows;

namespace NexoDesk.App.Services;

/// <summary>Área de imagem e tamanho de saída calculados em pixels físicos, incluindo DPI e letterbox.</summary>
public readonly record struct DesktopImageLayout(Rect Bounds, int PixelWidth, int PixelHeight, bool IsDownscale)
{
    public static DesktopImageLayout Fit(int sourceWidth, int sourceHeight, Size viewport, double dpiX, double dpiY)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || !double.IsFinite(viewport.Width) || !double.IsFinite(viewport.Height) ||
            viewport.Width <= 0 || viewport.Height <= 0 || !double.IsFinite(dpiX) || !double.IsFinite(dpiY) || dpiX <= 0 || dpiY <= 0)
            return default;
        double scale = Math.Min(viewport.Width * dpiX / sourceWidth, viewport.Height * dpiY / sourceHeight);
        int width = Math.Max(1, (int)Math.Floor(sourceWidth * scale));
        int height = Math.Max(1, (int)Math.Floor(sourceHeight * scale));
        double left = Math.Floor((viewport.Width * dpiX - width) / 2) / dpiX;
        double top = Math.Floor((viewport.Height * dpiY - height) / 2) / dpiY;
        return new(new Rect(left, top, width / dpiX, height / dpiY), width, height,
            width < sourceWidth && height < sourceHeight);
    }

    public bool TryNormalize(Point position, out double x, out double y)
    {
        x = y = 0;
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || !Bounds.Contains(position)) return false;
        x = Math.Clamp((position.X - Bounds.X) / Bounds.Width, 0, 1);
        y = Math.Clamp((position.Y - Bounds.Y) / Bounds.Height, 0, 1);
        return true;
    }
}
