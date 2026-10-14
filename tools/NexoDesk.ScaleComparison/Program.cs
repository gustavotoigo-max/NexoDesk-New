using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoDesk.App.Services;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/scale-comparison");
        Directory.CreateDirectory(output);
        var source = CreateFixture();
        Save(source, Path.Combine(output, "original.png"));
        var results = new List<object>();
        foreach (int width in new[] { 960, 1280, 1440 })
        {
            int height = width * 1080 / 1920;
            Save(RenderFant(source, width, height), Path.Combine(output, $"{width}-fant.png"));
            using var renderer = new Direct2DScaleRenderer(software: true);
            renderer.UploadFrame(source); renderer.SetOutput(width, height);
            renderer.Render(0);
            SavePixels(renderer.ReadPixels(), width, height, Path.Combine(output, $"{width}-cubic.png"));
            renderer.Render();
            SavePixels(renderer.ReadPixels(), width, height, Path.Combine(output, $"{width}-sharp.png"));
            // Este tempo inclui conclusão GPU e leitura de diagnóstico; não é latência de rede ou FPS da sessão.
            var times = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                var watch = Stopwatch.StartNew(); renderer.Render(); _ = renderer.ReadPixels();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            results.Add(new { width, height, software = true, completedRenderAndReadbackMs = times.Average() });
        }
        object hardware;
        try
        {
            using var surface = new GpuFitSurface();
            surface.Renderer.UploadFrame(source);
            bool presented = surface.Present(1280, 720);
            byte[] pixels = surface.Renderer.ReadPixels();
            if (!pixels.Where((_, i) => i % 4 != 3).Any(p => p > 0)) throw new InvalidOperationException("Saída compartilhada vazia.");
            var times = new List<double>();
            byte[] tile = new byte[128 * 128 * 4];
            for (int i = 0; i < 40; i++)
            {
                tile[0] = (byte)i;
                var watch = Stopwatch.StartNew();
                surface.Renderer.UploadBlock(0, 0, 128, 128, tile);
                surface.Present(1280, 720);
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            hardware = new { available = true, presented, changedBlockAndSubmissionMs = times.Average() };
            SavePixels(pixels, 1280, 720, Path.Combine(output, "1280-hardware-shared.png"));
        }
        catch (Exception ex)
        {
            hardware = new { available = false, error = ex.GetType().Name, hresult = $"0x{ex.HResult:X8}" };
        }
        var report = new { source = "1920x1080, texto/planilha/linhas sintéticos", results, hardware,
            note = "Medidas locais sintéticas. WARP é CPU; submission não mede conclusão GPU. Não demonstram ganho em todos os textos nem latência entre PCs." };
        File.WriteAllText(Path.Combine(output, "comparison.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "index.html"), """
            <!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>Comparação de ajuste da imagem</title>
            <style>body{font:16px system-ui;background:#20242b;color:#eee;padding:24px}button{padding:10px;margin:4px}img{display:block;max-width:none;margin-top:16px}select{padding:8px}p{max-width:850px}</style>
            <h1>Ajuste da imagem em janela</h1><p>Compare na escala de 100% do navegador. O bitmap original é o mesmo; resolução remota 1920×1080. Nitidez é visual e depende do texto e do tamanho de redução.</p>
            <label>Tamanho <select id="size"><option>960</option><option selected>1280</option><option>1440</option></select></label>
            <button onclick="mode='fant';show()">Anterior (Fant)</button><button onclick="mode='cubic';show()">Cúbico</button><button onclick="mode='sharp';show()">Cúbico + nitidez</button>
            <p id="label"></p><img id="image" alt="Texto, planilha e linhas para comparar">
            <script>let mode='sharp';function show(){image.src=size.value+'-'+mode+'.png';label.textContent=size.value+' pixels — '+mode}size.onchange=show;show()</script></html>
            """);
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
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
