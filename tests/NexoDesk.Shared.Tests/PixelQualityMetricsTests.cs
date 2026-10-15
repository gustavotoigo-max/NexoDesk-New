using NexoDesk.Shared.Imaging;

namespace NexoDesk.Shared.Tests;

public class PixelQualityMetricsTests
{
    [Fact]
    public void ExactPixelsIncludingOddEdgeBlocksScoreOneAndAlphaIsIgnored()
    {
        var random = new Random(12); byte[] reference = new byte[13 * 9 * 4]; random.NextBytes(reference);
        byte[] candidate = (byte[])reference.Clone();
        for (int i = 3; i < candidate.Length; i += 4) candidate[i] ^= 255;
        var result = PixelQualityMetrics.Compare(reference, candidate, 13, 9);
        Assert.Equal(0, result.MeanAbsoluteRgbError); Assert.Equal(1, result.BlockSsim, 10);
        Assert.Equal(result.ReferenceEdgeContrast, result.EdgeContrast); Assert.Equal(0, result.NeighborhoodOvershootPercent);
    }
    [Fact]
    public void BlurLosesEdgeContrastAndExtraContrastCanOvershootWithoutImprovingFidelity()
    {
        byte[] reference = new byte[8 * 8 * 4], blurred = new byte[reference.Length], sharpened = new byte[reference.Length];
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) for (int c = 0; c < 3; c++)
        { int i = (y * 8 + x) * 4 + c; reference[i] = x < 4 ? (byte)80 : (byte)180; blurred[i] = x < 4 ? (byte)110 : (byte)150; sharpened[i] = x < 4 ? (byte)20 : (byte)240; }
        var blur = PixelQualityMetrics.Compare(reference, blurred, 8, 8);
        var sharp = PixelQualityMetrics.Compare(reference, sharpened, 8, 8);
        Assert.True(blur.EdgeContrast < blur.ReferenceEdgeContrast);
        Assert.True(sharp.EdgeContrast > sharp.ReferenceEdgeContrast);
        Assert.InRange(blur.BlockSsim, 0, 0.999); Assert.InRange(sharp.BlockSsim, 0, 0.999);
        Assert.True(sharp.NeighborhoodOvershootPercent > 0);
        Assert.Throws<ArgumentException>(() => PixelQualityMetrics.Compare(reference, blurred, 9, 8));
    }
}
