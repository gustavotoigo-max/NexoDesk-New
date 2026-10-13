using System.Buffers.Binary;
using System.IO.Compression;
using NexoDesk.Shared.Protocol;

namespace NexoDesk.Shared.Messages;

/// <summary>Bloco BGR32 sem perdas. A descompressão tem tamanho exato e limitado.</summary>
public sealed record DesktopBlock(int X, int Y, int Width, int Height, byte[] CompressedPixels)
{
    public const int TileSize = 128;
    public byte[] Decompress()
    {
        if (Width is < 1 or > TileSize || Height is < 1 or > TileSize)
            throw new ProtocolException(ErrorCode.MalformedPayload, "bloco fora do limite");
        byte[] pixels = new byte[checked(Width * Height * 4)];
        if (!BrotliDecoder.TryDecompress(CompressedPixels, pixels, out int written) || written != pixels.Length)
            throw new ProtocolException(ErrorCode.MalformedPayload, "bloco comprimido inválido");
        return pixels;
    }

    public static byte[] Compress(ReadOnlySpan<byte> pixels)
    {
        byte[] output = new byte[BrotliEncoder.GetMaxCompressedLength(pixels.Length)];
        if (!BrotliEncoder.TryCompress(pixels, output, out int written, quality: 1, window: 20))
            throw new InvalidOperationException("Falha na compressão do bloco.");
        return output.AsSpan(0, written).ToArray();
    }
}

/// <summary>Uma atualização limitada da tela; Reset inicia uma imagem, Final conclui a captura.</summary>
public sealed record DesktopUpdate(uint FrameIndex, long TimestampUnixMs, int Width, int Height,
    bool Reset, bool Final, IReadOnlyList<DesktopBlock> Blocks)
{
    public const int HeaderSize = 20;
    public const int BlockHeaderSize = 12;
    public const int MaxPayload = 192 * 1024;
    public const int MaxBlocks = 64;
    public const int MaxScreenPixels = 32 * 1024 * 1024;
    public int EncodedLength => HeaderSize + Blocks.Sum(b => BlockHeaderSize + b.CompressedPixels.Length);

    public void Validate()
    {
        if (Width is < 1 or > 8192 || Height is < 1 or > 8192 || (long)Width * Height > MaxScreenPixels ||
            Blocks.Count is < 1 or > MaxBlocks || EncodedLength > MaxPayload)
            throw new ProtocolException(ErrorCode.MalformedPayload, "atualização fora dos limites");
        foreach (DesktopBlock b in Blocks)
            if (b.X < 0 || b.Y < 0 || b.Width is < 1 or > DesktopBlock.TileSize || b.Height is < 1 or > DesktopBlock.TileSize ||
                (long)b.X + b.Width > Width || (long)b.Y + b.Height > Height ||
                b.CompressedPixels.Length is < 1 or > (DesktopBlock.TileSize * DesktopBlock.TileSize * 4 + 1024))
                throw new ProtocolException(ErrorCode.MalformedPayload, "retângulo fora da tela");
    }

    public byte[] Encode()
    {
        Validate();
        byte[] result = new byte[EncodedLength];
        Span<byte> span = result;
        BinaryPrimitives.WriteUInt32LittleEndian(span, FrameIndex);
        BinaryPrimitives.WriteInt64LittleEndian(span[4..], TimestampUnixMs);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)Height);
        span[16] = (byte)((Reset ? 1 : 0) | (Final ? 2 : 0));
        BinaryPrimitives.WriteUInt16LittleEndian(span[18..], (ushort)Blocks.Count);
        int offset = HeaderSize;
        foreach (DesktopBlock b in Blocks)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], (ushort)b.X);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 2)..], (ushort)b.Y);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 4)..], (ushort)b.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 6)..], (ushort)b.Height);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], b.CompressedPixels.Length);
            b.CompressedPixels.CopyTo(span[(offset + BlockHeaderSize)..]);
            offset += BlockHeaderSize + b.CompressedPixels.Length;
        }
        return result;
    }

    public static DesktopUpdate Decode(Envelope envelope)
    {
        if (envelope.Type != MessageType.DesktopUpdate)
            throw new ProtocolException(ErrorCode.UnexpectedMessage);
        ReadOnlySpan<byte> data = envelope.Payload;
        if (data.Length < HeaderSize || data.Length > MaxPayload || data[16] > 3 || data[17] != 0)
            throw new ProtocolException(ErrorCode.MalformedPayload, "cabeçalho de tela inválido");
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
        if (count is < 1 or > MaxBlocks)
            throw new ProtocolException(ErrorCode.MalformedPayload, "quantidade de blocos inválida");
        var blocks = new List<DesktopBlock>(count);
        int offset = HeaderSize;
        for (int i = 0; i < count; i++)
        {
            if (data.Length - offset < BlockHeaderSize)
                throw new ProtocolException(ErrorCode.MalformedPayload, "bloco truncado");
            int length = BinaryPrimitives.ReadInt32LittleEndian(data[(offset + 8)..]);
            if (length < 1 || length > data.Length - offset - BlockHeaderSize)
                throw new ProtocolException(ErrorCode.MalformedPayload, "comprimento de bloco inválido");
            blocks.Add(new(
                BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]),
                BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]),
                BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..]),
                data.Slice(offset + BlockHeaderSize, length).ToArray()));
            offset += BlockHeaderSize + length;
        }
        if (offset != data.Length)
            throw new ProtocolException(ErrorCode.MalformedPayload, "bytes excedentes");
        var update = new DesktopUpdate(BinaryPrimitives.ReadUInt32LittleEndian(data),
            BinaryPrimitives.ReadInt64LittleEndian(data[4..]), BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[14..]), (data[16] & 1) != 0, (data[16] & 2) != 0, blocks);
        update.Validate();
        return update;
    }
}

public sealed record FrameAckMessage(uint FrameIndex) : IControlMessage
{
    public void Validate() { }
}