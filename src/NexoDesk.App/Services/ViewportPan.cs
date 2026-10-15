namespace NexoDesk.App.Services;

/// <summary>Desloca apenas a câmera do viewer; não redimensiona pixels nem envia entrada remota.</summary>
public static class ViewportPan
{
    public static double Advance(double offset, double maximum, double pointer, double viewport, double seconds)
    {
        if (!double.IsFinite(offset) || !double.IsFinite(maximum) || !double.IsFinite(pointer) ||
            !double.IsFinite(viewport) || !double.IsFinite(seconds) || maximum <= 0 || viewport <= 0 || seconds <= 0)
            return double.IsFinite(offset) ? Math.Max(0, offset) : 0;
        offset = Math.Clamp(offset, 0, maximum);
        if (pointer < 0 || pointer > viewport) return offset;
        double edge = Math.Min(28, viewport / 4);
        double direction = pointer < edge ? -(edge - pointer) / edge
            : pointer > viewport - edge ? (pointer - viewport + edge) / edge : 0;
        return Math.Clamp(offset + direction * 900 * Math.Min(seconds, 0.05), 0, maximum);
    }
}
