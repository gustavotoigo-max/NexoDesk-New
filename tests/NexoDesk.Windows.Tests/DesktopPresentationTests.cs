using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using NexoDesk.App.Services;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Streaming;
using Drawing = System.Drawing;
using Imaging = System.Drawing.Imaging;

namespace NexoDesk.Windows.Tests;

public class DesktopPresentationTests
{
    [Fact]
    public void TextUpdate_IsLosslessAndSmallerThanWholeScreenJpeg()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                const int width=1920,height=1080;
                using var bitmap=new Drawing.Bitmap(width,height,Imaging.PixelFormat.Format32bppRgb);
                using (Drawing.Graphics g=Drawing.Graphics.FromImage(bitmap))
                using (var font=new Drawing.Font("Arial",14))
                {
                    g.Clear(Drawing.Color.White);
                    g.FillRectangle(Drawing.Brushes.Navy,0,0,width,80);
                    g.DrawString("NexoDesk New - Tela de teste",font,Drawing.Brushes.White,30,25);
                    for (int row=0;row<30;row++)
                        g.DrawString($"Linha {row:00}  |  Código ABC-{row:000}  |  Valor 1234,56  |  Status aguardando",
                            font,Drawing.Brushes.Black,30,100+row*29);
                }
                var encoder=new DesktopTileEncoder();
                var presenter=new DesktopFramePresenter();
                byte[] original=Pixels(bitmap);
                encoder.Capture(original,width,height,1);
                ApplyPending(encoder,presenter);
                using (Drawing.Graphics g=Drawing.Graphics.FromImage(bitmap))
                using (var font=new Drawing.Font("Arial",14))
                {
                    g.FillRectangle(Drawing.Brushes.White,24,100,850,25);
                    g.DrawString("Linha 00  |  Código ABC-000  |  Valor 1234,56  |  Status concluído",
                        font,Drawing.Brushes.Black,30,100);
                }
                byte[] changed=Pixels(bitmap);
                var clock=Stopwatch.StartNew();
                byte[] jpeg;
                using (var output=new MemoryStream())
                using (var parameters=new Imaging.EncoderParameters(1))
                {
                    parameters.Param[0]=new Imaging.EncoderParameter(Imaging.Encoder.Quality,85L);
                    bitmap.Save(output,Imaging.ImageCodecInfo.GetImageEncoders().First(c=>c.FormatID==Imaging.ImageFormat.Jpeg.Guid),parameters);
                    jpeg=output.ToArray();
                }
                double jpegMs=clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                encoder.Capture(changed,width,height,2);
                int bytes=ApplyPending(encoder,presenter);
                double deltaMs=clock.Elapsed.TotalMilliseconds;
                Assert.True(bytes<jpeg.Length, $"Delta {bytes} bytes; JPEG completo {jpeg.Length} bytes.");
                byte[] rendered=new byte[changed.Length];
                presenter.Bitmap!.CopyPixels(rendered,width*4,0);
                // BGR32 não usa o quarto byte; confira os três canais reais sem depender desse padding.
                for(int i=3;i<rendered.Length;i+=4) rendered[i]=changed[i];
                Assert.Equal(changed,rendered);
                string root=WindowsIntegrationTests.FindRoot();
                Directory.CreateDirectory(Path.Combine(root,"artifacts"));
                File.WriteAllText(Path.Combine(root,"artifacts","performance-synthetic.json"),JsonSerializer.Serialize(new
                {
                    Scenario="Alteração de uma linha de texto em tela sintética; não mede WAN nem captura real.",
                    Width=width,Height=height,WholeScreenJpegBytes=jpeg.Length,LosslessChangedBlocksBytes=bytes,
                    JpegEncodingMs=jpegMs,DeltaEncodingAndPresentationMs=deltaMs,
                    ReductionPercent=100.0*(1.0-(double)bytes/jpeg.Length)
                },new JsonSerializerOptions{WriteIndented=true}));
            }
            catch(Exception ex) { failure=ex; }
        }) { IsBackground=true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)),"Teste de apresentação não terminou.");
        if(failure is not null) throw failure;
    }

    private static int ApplyPending(DesktopTileEncoder encoder,DesktopFramePresenter presenter)
    {
        int bytes=0;
        while(encoder.HasPending)
        {
            DesktopUpdate update=encoder.Take(0);
            bytes+=update.EncodedLength;
            presenter.Apply(update,update.Blocks.Select(b=>b.Decompress()).ToArray());
        }
        return bytes;
    }

    private static byte[] Pixels(Drawing.Bitmap bitmap)
    {
        var data=bitmap.LockBits(new Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height),Imaging.ImageLockMode.ReadOnly,Imaging.PixelFormat.Format32bppRgb);
        try
        {
            byte[] pixels=new byte[bitmap.Width*bitmap.Height*4];
            for(int row=0;row<bitmap.Height;row++)
                Marshal.Copy(IntPtr.Add(data.Scan0,row*data.Stride),pixels,row*bitmap.Width*4,bitmap.Width*4);
            return pixels;
        }
        finally { bitmap.UnlockBits(data); }
    }
}