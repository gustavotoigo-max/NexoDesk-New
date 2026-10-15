using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoDesk.App.Services;
using NexoDesk.Shared.Imaging;
using NexoDesk.Shared.Streaming;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/scale-comparison");
        Directory.CreateDirectory(output);
        var source = args.Length > 1 ? LoadSource(args[1]) : CreateFixture();
        int sourceWidth = source.PixelWidth, sourceHeight = source.PixelHeight;
        int[] widths = new[] { 0.5, 2.0 / 3, 0.75, 0.9 }.Select(r => Math.Max(1, (int)(sourceWidth * r))).Distinct().ToArray();
        int[] levels = [0, 20, 35, 50, 65, 80, 100];
        // Antes dos filtros, verifica reconstrução do transporte sem perda no mesmo quadro.
        byte[] original = ToPixels(source);
        var tiles = new DesktopTileEncoder(); var presenter = new DesktopFramePresenter();
        tiles.Capture(original, sourceWidth, sourceHeight, 0);
        while (tiles.HasPending)
        {
            var update = tiles.Take(0);
            presenter.Apply(update, update.Blocks.Select(b => b.Decompress()).ToArray());
        }
        byte[] decoded = ToPixels(presenter.Bitmap!);
        var roundtrip = PixelQualityMetrics.Compare(original, decoded, sourceWidth, sourceHeight);
        Save(source, Path.Combine(output, "original.png"));
        var results = new List<object>();
        foreach (int width in widths)
        {
            int height = Math.Max(1, width * sourceHeight / sourceWidth);
            Save(RenderFant(source, width, height), Path.Combine(output, $"{width}-fant.png"));
            using var renderer = new Direct2DScaleRenderer(software: true);
            renderer.UploadFrame(source); renderer.SetOutput(width, height);
            renderer.Render(0);
            byte[] cubic = renderer.ReadPixels();
            SavePixels(cubic, width, height, Path.Combine(output, $"{width}-cubic.png"));
            var samples = new List<object>();
            foreach (int level in levels)
            {
                renderer.Render(level / 100f); byte[] pixels = renderer.ReadPixels();
                SavePixels(pixels, width, height, Path.Combine(output, $"{width}-sharp-{level}.png"));
                if (level == 35) SavePixels(pixels, width, height, Path.Combine(output, $"{width}-sharp.png"));
                samples.Add(new { sharpnessPercent = level, versusCubic = PixelQualityMetrics.Compare(cubic, pixels, width, height) });
            }
            // Este tempo inclui conclusão GPU e leitura de diagnóstico; não é latência de rede ou FPS da sessão.
            var times = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew(); renderer.Render(); _ = renderer.ReadPixels();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            results.Add(new { width, height, software = true, samples, completedRenderAndReadbackMs = times.Average() });
        }
        object hardware;
        try
        {
            using var surface = new GpuFitSurface();
            surface.Renderer.UploadFrame(source);
            bool presented = surface.Present(1280, 720);
            byte[] pixels = surface.Renderer.ReadPixels();
            surface.Present(1280, 720, 0); byte[] noSharpen = surface.Renderer.ReadPixels();
            surface.Present(1280, 720, 1); byte[] maxSharpen = surface.Renderer.ReadPixels();
            bool manualSharpnessChangesPixels = !noSharpen.SequenceEqual(maxSharpen);
            if (!pixels.Where((_, i) => i % 4 != 3).Any(p => p > 0)) throw new InvalidOperationException("Saída compartilhada vazia.");
            var times = new List<double>();
            int tileWidth = Math.Min(128, sourceWidth), tileHeight = Math.Min(128, sourceHeight);
            byte[] tile = new byte[tileWidth * tileHeight * 4];
            for (int i = 0; i < 40; i++)
            {
                tile[0] = (byte)i;
                var watch = Stopwatch.StartNew();
                surface.Renderer.UploadBlock(0, 0, tileWidth, tileHeight, tile);
                surface.Present(1280, 720);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            hardware = new { available = true, presented, manualSharpnessChangesPixels, changedBlockAndSubmissionMs = times.Average() };
            SavePixels(pixels, 1280, 720, Path.Combine(output, "1280-hardware-shared.png"));
        }
        catch (Exception ex)
        {
            hardware = new { available = false, error = ex.GetType().Name, hresult = $"0x{ex.HResult:X8}" };
        }
        var report = new { source = args.Length > 1 ? "Imagem fornecida localmente" : "Texto/planilha/linhas sintéticos", sourceWidth, sourceHeight,
            losslessRoundtrip = roundtrip, results, hardware,
            reference = "Comparações reduzidas usam cúbico sem reforço no mesmo tamanho; SSIM de luminância por blocos 8x8. Maior similaridade ao cúbico não significa melhor legibilidade.",
            note = "Contraste maior pode vir de contornos artificiais. Overshoot conta pixels além do intervalo de luminância 3x3 da referência com tolerância de 2/255; não é detector definitivo de halos. WARP é CPU; submission não mede conclusão GPU nem latência entre PCs." };
        File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "index.html"), """
            <!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>Comparação de nitidez</title>
            <style>body{font:16px system-ui;background:#20242b;color:#eee;padding:24px}button,select{padding:8px;margin:4px}img{display:block;max-width:none;margin-top:16px}p{max-width:900px}a{color:#9bf}</style>
            <h1>Nitidez da imagem em janela</h1><p>Compare com zoom do navegador em 100%. Mesma imagem e mesmo tamanho de saída. O reforço aumenta contraste; excesso pode criar contornos. Nenhum nível é universalmente melhor.</p>
            <label>Largura <select id="size"></select></label>
            <label>Reforço <select id="level"><option>0</option><option>20</option><option selected>35</option><option>50</option><option>65</option><option>80</option><option>100</option></select>%</label>
            <button onclick="mode='fant';show()">Anterior (Fant)</button><button onclick="mode='sharp';show()">GPU / nitidez</button>
            <p><a href="comparison.json">Métricas e limites da comparação</a> · SSIM relativo ao cúbico sem reforço não é uma nota de conforto visual.</p>
            <p id="label"></p><img id="image" alt="Imagem local para comparar texto, planilha e linhas">
            <script>const widths=__WIDTHS__;let mode='sharp';const size=document.getElementById('size'),level=document.getElementById('level'),image=document.getElementById('image'),label=document.getElementById('label');
            widths.forEach(w=>size.add(new Option(w,w)));size.value=widths[Math.min(1,widths.length-1)];
            function show(){image.src=size.value+'-'+(mode==='fant'?'fant':'sharp-'+level.value)+'.png';label.textContent=size.value+' pixels — '+(mode==='fant'?'Anterior (Fant)':'Nitidez '+level.value+'%')}size.onchange=level.onchange=show;show()</script></html>
            """.Replace("__WIDTHS__", JsonSerializer.Serialize(widths)));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static BitmapSource LoadSource(string path)
    {
        using var file = File.OpenRead(Path.GetFullPath(path));
        var image = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        image.Freeze(); return image;
    }
    private static byte[] ToPixels(BitmapSource source)
    {
        BitmapSource bgr = source.Format == PixelFormats.Bgr32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        byte[] pixels = new byte[checked(bgr.PixelWidth * bgr.PixelHeight * 4)]; bgr.CopyPixels(pixels, bgr.PixelWidth * 4, 0); return pixels;
    }

    private static BitmapSource CreateFixture()
    {
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1920, 1080));
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 43, 65)), null, new Rect(0, 0, 1920, 88));
            Text(drawing, "NexoDesk — comparação de texto e detalhes", 32, Brushes.White, 32, 20);
            int top = 120;
            foreach (double size in new[] { 12d, 14d, 16d, 18d, 22d, 28d })
            {
                Text(drawing, $"{size}px: Pedido 1039 · R$ 1.234,56 · Recepção · 0 O 1 l I · 8 B · . , : ;", size, Brushes.Black, 30, top);
                Text(drawing, "Texto colorido: ATIVO / ERRO / https://exemplo.local", size, Brushes.DarkBlue, 30, top + 40);
                top += 95;
            }
            for (int row = 0; row < 9; row++)
            {
                int y = 720 + row * 34;
                drawing.DrawLine(new Pen(Brushes.Gray, 1), new Point(30, y), new Point(1320, y));
                Text(drawing, $"Produto {row + 1:00}       Quantidade: 12       Valor: R$ 19,90       Total: R$ 238,80", 16, Brushes.Black, 42, y + 8);
            }
            foreach (int x in new[] { 30, 250, 500, 750, 1000, 1320 })
                drawing.DrawLine(new Pen(Brushes.Gray, 1), new Point(x, 720), new Point(x, 1026));
            Text(drawing, "Linhas de 1 pixel", 20, Brushes.Black, 1410, 140);
            for (int i = 0; i < 45; i++)
                drawing.DrawLine(new Pen(i % 3 == 0 ? Brushes.DarkRed : Brushes.Black, 1), new Point(1410 + i * 5, 190), new Point(1410 + i * 5, 540));
            Text(drawing, "Fundo escuro: 1.234,56", 18, Brushes.White, 1430, 730);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 29, 34)), null, new Rect(1390, 650, 450, 300));
            Text(drawing, "Fundo escuro: 1.234,56", 18, Brushes.White, 1410, 680);
            Text(drawing, "Pequenas alterações: . : ;", 14, Brushes.LightGray, 1410, 730);
        }
        var bitmap = new RenderTargetBitmap(1920, 1080, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    private static void Text(DrawingContext drawing, string text, double size, Brush color, double x, double y)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, color, 1);
        drawing.DrawText(formatted, new Point(x, y));
    }

    private static BitmapSource RenderFant(BitmapSource source, int width, int height)
    {
        var visual = new DrawingVisual(); RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(source, new Rect(0, 0, width, height));
        var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual); result.Freeze(); return result;
    }

    private static void SavePixels(byte[] pixels, int width, int height, string path) =>
        Save(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4), path);

    private static void Save(BitmapSource source, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using var file = File.Create(path); encoder.Save(file);
    }
}
