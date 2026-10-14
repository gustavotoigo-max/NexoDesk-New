using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoDesk.App.Controls;
using NexoDesk.App.Services;

namespace NexoDesk.Windows.Tests;

public class DesktopScalingTests
{
    [Theory]
    [InlineData(1, 1000, 700, 1000, 562)]
    [InlineData(1.25, 1000, 700, 1250, 703)]
    [InlineData(1.5, 1000, 700, 1500, 843)]
    [InlineData(2, 1000, 700, 2000, 1125)]
    [InlineData(1, 700, 1000, 700, 393)]
    public void FitUsesPhysicalPixelsAndKeepsPointerWithinImage(double dpi, double w, double h, int outputW, int outputH)
    {
        var layout = DesktopImageLayout.Fit(1920, 1080, new Size(w, h), dpi, dpi);
        Assert.Equal(outputW, layout.PixelWidth);
        Assert.Equal(outputH, layout.PixelHeight);
        Assert.Equal(outputW < 1920, layout.IsDownscale);
        var bounds = layout.Bounds;
        Assert.True(bounds.Right <= w && bounds.Bottom <= h);
        Assert.True(layout.TryNormalize(new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), out double x, out double y));
        Assert.Equal(0.5, x, 9); Assert.Equal(0.5, y, 9);
        Assert.False(layout.TryNormalize(new Point(bounds.X - 1, bounds.Y), out _, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    public void ActualSizeHasOneSourcePixelPerPhysicalPixel(double dpi)
    {
        var layout = DesktopImageLayout.Fit(1920, 1080, new Size(1920 / dpi, 1080 / dpi), dpi, dpi);
        Assert.Equal(1920, layout.PixelWidth);
        Assert.Equal(1080, layout.PixelHeight);
        Assert.False(layout.IsDownscale);
        Assert.True(layout.TryNormalize(new Point(layout.Bounds.Right, layout.Bounds.Bottom), out double x, out double y));
        Assert.Equal(1, x); Assert.Equal(1, y);
    }

    [Fact]
    public void EmptyViewportDoesNotCreateGraphicsResources()
    {
        Assert.Equal(default, DesktopImageLayout.Fit(1920, 1080, new Size(0, 0), 1, 1));
    }

    [Fact]
    public void NativeEffectsPreserveSolidColorAndOpaqueAlpha()
    {
        using var renderer = new Direct2DScaleRenderer(software: true);
        renderer.UploadFrame(128, 64, Solid(128, 64, 30, 90, 170));
        renderer.SetOutput(64, 32);
        renderer.Render(0);
        var plain = renderer.ReadPixels();
        Assert.True(plain[0] > 0, $"plain={string.Join(",", plain.Take(4))}");
        renderer.Render();
        byte[] result = renderer.ReadPixels();
        for (int i = 0; i < result.Length; i += 4)
        {
            Assert.True(result[i] is >= 29 and <= 31, $"pixel={i / 4}; color={string.Join(",", result.Skip(i).Take(4))}; center={string.Join(",", result.Skip((16 * 64 + 32) * 4).Take(4))}");
            Assert.InRange(result[i + 1], (byte)89, (byte)91);
            Assert.InRange(result[i + 2], (byte)169, (byte)171);
            Assert.Equal(255, result[i + 3]);
        }
    }

    [Fact]
    public void SinglePixelChangeInvalidatesEffectsWithoutReplacingSource()
    {
        using var renderer = new Direct2DScaleRenderer(software: true);
        renderer.UploadFrame(64, 64, Solid(64, 64, 100, 100, 100));
        renderer.SetOutput(32, 32);
        renderer.Render();
        byte[] before = renderer.ReadPixels();
        renderer.UploadBlock(32, 32, 1, 1, [255, 255, 255, 0]);
        renderer.Render();
        byte[] after = renderer.ReadPixels();
        Assert.False(before.SequenceEqual(after), $"before center={string.Join(",", before.Skip((16 * 32 + 16) * 4).Take(4))}, after center={string.Join(",", after.Skip((16 * 32 + 16) * 4).Take(4))}");
        Assert.True(before.AsSpan(0, 4 * 32).SequenceEqual(after.AsSpan(0, 4 * 32)));
    }

    [Fact]
    public void SourceAndOutputResizeDoNotRetainOldPixels()
    {
        using var renderer = new Direct2DScaleRenderer(software: true);
        renderer.UploadFrame(128, 64, Solid(128, 64, 255, 255, 255));
        renderer.SetOutput(64, 32); renderer.Render();
        renderer.UploadFrame(64, 128, Solid(64, 128, 20, 50, 80));
        renderer.SetOutput(32, 64); renderer.Render();
        byte[] result = renderer.ReadPixels();
        Assert.Equal(32 * 64 * 4, result.Length);
        Assert.InRange(result[0], (byte)19, (byte)21);
        Assert.InRange(result[^2], (byte)79, (byte)81);
        renderer.UploadFrame(64, 128, Solid(64, 128, 120, 150, 180));
        renderer.Render();
        Assert.InRange(renderer.ReadPixels()[0], (byte)119, (byte)121);
    }

    [Fact]
    public void SharpnessCanBeComparedAndMalformedBlocksAreRejected()
    {
        using var renderer = new Direct2DScaleRenderer(software: true);
        byte[] pixels = Solid(64, 64, 200, 200, 200);
        for (int y = 10; y < 54; y++)
            for (int x = 20; x < 35; x++) Array.Clear(pixels, (y * 64 + x) * 4, 3);
        renderer.UploadFrame(64, 64, pixels);
        Assert.Equal(0, pixels[3]);
        renderer.SetOutput(43, 43);
        renderer.Render(0); byte[] cubic = renderer.ReadPixels();
        renderer.Render(0.35f); byte[] sharp = renderer.ReadPixels();
        Assert.False(cubic.SequenceEqual(sharp));
        Assert.Throws<ArgumentException>(() => renderer.UploadBlock(63, 0, 2, 1, new byte[8]));
        Assert.Throws<ArgumentException>(() => renderer.UploadBlock(0, 0, 1, 1, new byte[3]));
    }

    [Fact]
    public void OriginalControlRendersLivePixelsWithoutNativeWindowOrFilters()
    {
        RunSta(() =>
        {
            var frame = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgr32, null);
            frame.WritePixels(new Int32Rect(0, 0, 16, 16), Solid(16, 16, 20, 60, 120), 16 * 4, 0);
            var view = new AdaptiveDesktopImage { Source = frame, ActualSize = true, EnhancedFit = true, Width = 16, Height = 16 };
            view.Measure(new Size(16, 16)); view.Arrange(new Rect(0, 0, 16, 16));
            var target = new RenderTargetBitmap(16, 16, 96, 96, PixelFormats.Pbgra32);
            target.Render(view);
            var before = new byte[16 * 16 * 4]; target.CopyPixels(before, 64, 0);
            Assert.Equal(120, before[2]);
            frame.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[] { 255, 255, 255, 0 }, 4, 0);
            target.Clear(); target.Render(view);
            var after = new byte[16 * 16 * 4]; target.CopyPixels(after, 64, 0);
            Assert.Equal(255, after[0]);
            Assert.True(before.AsSpan(4).SequenceEqual(after.AsSpan(4)));
        });
    }

    [Fact]
    public void GraphicsFailurePreservesOriginalAndActualSizeBypassesGraphics()
    {
        RunSta(() =>
        {
            int attempts = 0;
            var frame = BitmapSource.Create(64, 64, 96, 96, PixelFormats.Bgr32, null, Solid(64, 64, 90, 120, 180), 256);
            var view = new AdaptiveDesktopImage(() =>
            {
                attempts++;
                throw new System.Runtime.InteropServices.COMException("GPU indisponível");
            }) { Source = frame };
            view.Measure(new Size(32, 32)); view.Arrange(new Rect(0, 0, 32, 32));
            view.PresentPendingFrame();
            Assert.Same(frame, view.Source);
            Assert.Contains("original", view.FitStatus);
            Assert.Equal(1, attempts);
            view.PresentPendingFrame(); // Não repetir falha a cada quadro.
            Assert.Equal(1, attempts);
            view.ActualSize = true;
            view.PresentPendingFrame();
            Assert.Contains("100%", view.FitStatus);
            Assert.Equal(1, attempts);
            view.ActualSize = false;
            view.PresentPendingFrame(); // Uma nova escolha permite tentar a GPU de novo.
            Assert.Equal(2, attempts);
            view.EnhancedFit = false;
            view.PresentPendingFrame();
            Assert.Equal("Ajuste original", view.FitStatus);
            Assert.Equal(2, attempts);
        });
    }

    internal static byte[] Solid(int w, int h, byte b, byte g, byte r)
    {
        var pixels = new byte[w * h * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = b; pixels[i + 1] = g; pixels[i + 2] = r; }
        return pixels;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Renderização sem janela não terminou.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
