using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Protocol;

namespace NexoDesk.App.Services;

/// <summary>Aplica alterações sem reenquadrar ou redecodificar a tela inteira. Uso na thread da interface.</summary>
public sealed class DesktopFramePresenter
{
    public WriteableBitmap? Bitmap { get; private set; }
    public WriteableBitmap Apply(DesktopUpdate update, IReadOnlyList<byte[]> pixels)
    {
        update.Validate();
        if (pixels.Count != update.Blocks.Count)
            throw new ProtocolException(ErrorCode.MalformedPayload);
        if (update.Reset)
            Bitmap = new WriteableBitmap(update.Width, update.Height, 96, 96, PixelFormats.Bgr32, null);
        if (Bitmap is null || Bitmap.PixelWidth != update.Width || Bitmap.PixelHeight != update.Height)
            throw new ProtocolException(ErrorCode.MalformedPayload, "atualização sem imagem base");
        for (int i = 0; i < pixels.Count; i++)
        {
            DesktopBlock b = update.Blocks[i];
            if (pixels[i].Length != b.Width * b.Height * 4)
                throw new ProtocolException(ErrorCode.MalformedPayload);
            Bitmap.WritePixels(new Int32Rect(b.X, b.Y, b.Width, b.Height), pixels[i], b.Width * 4, 0);
        }
        return Bitmap;
    }
}