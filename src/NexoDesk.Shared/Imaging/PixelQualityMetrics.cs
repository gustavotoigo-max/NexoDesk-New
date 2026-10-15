namespace NexoDesk.Shared.Imaging;

public sealed record PixelQualityResult(double MeanAbsoluteRgbError, double BlockSsim,
    double ReferenceEdgeContrast, double EdgeContrast, double NeighborhoodOvershootPercent);

/// <summary>Diagnóstico de buffers BGR(A) alinhados e do mesmo tamanho; ignora alpha.
/// SSIM de luminância em blocos 8×8 sem sobreposição (não é MS-SSIM nem nota de legibilidade).
/// Contraste/overshoot revelam o custo do reforço; nenhum número determina o melhor filtro sozinho.</summary>
public static class PixelQualityMetrics
{
    public static PixelQualityResult Compare(ReadOnlySpan<byte> reference, ReadOnlySpan<byte> candidate, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > int.MaxValue / 4 ||
            reference.Length != (long)width * height * 4 || candidate.Length != reference.Length)
            throw new ArgumentException("Buffers BGR32 e dimensões incompatíveis.");
        double error = 0, edges = 0, referenceEdges = 0, ssim = 0;
        long pairs = 0, overshoots = 0, blocks = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                for (int channel = 0; channel < 3; channel++) error += Math.Abs(reference[i + channel] - candidate[i + channel]);
                double a = Luma(reference, i), b = Luma(candidate, i);
                if (x > 0) { referenceEdges += Math.Abs(a - Luma(reference, i - 4)); edges += Math.Abs(b - Luma(candidate, i - 4)); pairs++; }
                if (y > 0) { referenceEdges += Math.Abs(a - Luma(reference, i - width * 4)); edges += Math.Abs(b - Luma(candidate, i - width * 4)); pairs++; }
                double min = a, max = a;
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                    for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                    { double v = Luma(reference, (ny * width + nx) * 4); min = Math.Min(min, v); max = Math.Max(max, v); }
                if (b < min - 2 || b > max + 2) overshoots++;
            }
        const double c1 = 6.5025, c2 = 58.5225; // (0,01×255)² e (0,03×255)²
        for (int by = 0; by < height; by += 8)
            for (int bx = 0; bx < width; bx += 8)
            {
                double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0;
                int count = 0;
                for (int y = by; y < Math.Min(by + 8, height); y++)
                    for (int x = bx; x < Math.Min(bx + 8, width); x++)
                    {
                        int i = (y * width + x) * 4;
                        double a = Luma(reference, i), b = Luma(candidate, i);
                        sa += a; sb += b; saa += a * a; sbb += b * b; sab += a * b; count++;
                    }
                double ma = sa / count, mb = sb / count;
                double va = Math.Max(0, saa / count - ma * ma), vb = Math.Max(0, sbb / count - mb * mb);
                double covariance = sab / count - ma * mb;
                ssim += (2 * ma * mb + c1) * (2 * covariance + c2) / ((ma * ma + mb * mb + c1) * (va + vb + c2));
                blocks++;
            }
        long pixels = (long)width * height;
        return new(error / (pixels * 3), ssim / blocks, pairs == 0 ? 0 : referenceEdges / pairs,
            pairs == 0 ? 0 : edges / pairs, 100.0 * overshoots / pixels);
    }
    private static double Luma(ReadOnlySpan<byte> pixels, int i) => 0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2];
}
