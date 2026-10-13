using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Protocol;

namespace NexoDesk.Shared.Streaming;

/// <summary>Compara todos os pixels; transmite só blocos alterados e mantém uma captura pendente por vez.</summary>
public sealed class DesktopTileEncoder
{
    private byte[]? _previous;
    private int _width, _height;
    private readonly Queue<DesktopUpdate> _pending = new();
    public bool HasPending => _pending.Count != 0;

    public void Reset() { _previous = null; _pending.Clear(); _width = _height = 0; }
    public DesktopUpdate Take(uint index) => _pending.Dequeue() with { FrameIndex = index };

    public void Capture(ReadOnlySpan<byte> pixels, int width, int height, long timestamp)
    {
        if (HasPending) throw new InvalidOperationException("A captura anterior ainda não foi entregue.");
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > DesktopUpdate.MaxScreenPixels ||
            pixels.Length != checked(width * height * 4))
            throw new ProtocolException(ErrorCode.MalformedPayload, "dimensões de captura inválidas");
        bool reset = _previous is null || width != _width || height != _height;
        var blocks = new List<DesktopBlock>();
        int encoded = DesktopUpdate.HeaderSize;
        bool first = true;
        for (int y = 0; y < height; y += DesktopBlock.TileSize)
        for (int x = 0; x < width; x += DesktopBlock.TileSize)
        {
            int w = Math.Min(DesktopBlock.TileSize, width - x);
            int h = Math.Min(DesktopBlock.TileSize, height - y);
            bool changed = reset;
            for (int row = 0; !changed && row < h; row++)
            {
                int offset = ((y + row) * width + x) * 4;
                changed = !pixels.Slice(offset, w * 4).SequenceEqual(_previous!.AsSpan(offset, w * 4));
            }
            if (!changed) continue;
            byte[] raw = new byte[w * h * 4];
            for (int row = 0; row < h; row++)
                pixels.Slice(((y + row) * width + x) * 4, w * 4).CopyTo(raw.AsSpan(row * w * 4));
            var block = new DesktopBlock(x, y, w, h, DesktopBlock.Compress(raw));
            int length = DesktopUpdate.BlockHeaderSize + block.CompressedPixels.Length;
            if (blocks.Count != 0 && (encoded + length > DesktopUpdate.MaxPayload || blocks.Count == DesktopUpdate.MaxBlocks))
            {
                _pending.Enqueue(new(0, timestamp, width, height, reset && first, false, blocks));
                first = false;
                blocks = [];
                encoded = DesktopUpdate.HeaderSize;
            }
            blocks.Add(block);
            encoded += length;
        }
        if (blocks.Count != 0)
            _pending.Enqueue(new(0, timestamp, width, height, reset && first, true, blocks));
        if (reset) _previous = new byte[pixels.Length];
        pixels.CopyTo(_previous);
        _width = width; _height = height;
    }
}