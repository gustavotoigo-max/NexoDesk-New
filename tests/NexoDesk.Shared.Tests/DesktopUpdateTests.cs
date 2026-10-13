using System.Buffers.Binary;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Protocol;
using NexoDesk.Shared.Streaming;

namespace NexoDesk.Shared.Tests;

public class DesktopUpdateTests
{
    private static Envelope EnvelopeOf(byte[] data) => new(new(ProtocolConstants.CurrentVersion, MessageType.DesktopUpdate, (uint)data.Length, 1), data);

    [Fact]
    public void NoChange_ProducesNoUpdate()
    {
        var encoder = new DesktopTileEncoder();
        byte[] pixels = new byte[256 * 256 * 4];
        encoder.Capture(pixels, 256, 256, 1);
        while (encoder.HasPending) encoder.Take(0);
        encoder.Capture(pixels, 256, 256, 2);
        Assert.False(encoder.HasPending);
    }

    [Fact]
    public void OnePixelChange_IsDetectedAndOnlyItsBlockIsSent()
    {
        var encoder = new DesktopTileEncoder();
        byte[] pixels = new byte[256 * 256 * 4];
        encoder.Capture(pixels, 256, 256, 1);
        encoder.Take(0);
        pixels[(200 * 256 + 201) * 4 + 2] = 123;
        encoder.Capture(pixels, 256, 256, 2);
        DesktopUpdate change = encoder.Take(1);
        Assert.False(change.Reset);
        Assert.True(change.Final);
        DesktopBlock block = Assert.Single(change.Blocks);
        Assert.Equal((128,128), (block.X,block.Y));
        Assert.Equal(123, block.Decompress()[((200-128)*128+201-128)*4+2]);
    }

    [Fact]
    public void LargeIncompressibleCapture_IsBoundedAndReconstructsExactly()
    {
        const int width = 512, height = 512;
        byte[] pixels = new byte[width * height * 4];
        new Random(123).NextBytes(pixels);
        var encoder = new DesktopTileEncoder();
        encoder.Capture(pixels, width, height, 77);
        byte[] reconstructed = new byte[pixels.Length];
        var updates = new List<DesktopUpdate>();
        while (encoder.HasPending)
        {
            DesktopUpdate update = encoder.Take((uint)updates.Count);
            byte[] wire = update.Encode();
            Assert.InRange(wire.Length, DesktopUpdate.HeaderSize, DesktopUpdate.MaxPayload);
            update = DesktopUpdate.Decode(EnvelopeOf(wire));
            updates.Add(update);
            foreach (DesktopBlock b in update.Blocks)
            {
                byte[] raw = b.Decompress();
                for (int row=0; row<b.Height; row++)
                    raw.AsSpan(row*b.Width*4,b.Width*4).CopyTo(reconstructed.AsSpan(((b.Y+row)*width+b.X)*4));
            }
        }
        Assert.True(updates.Count > 1);
        Assert.True(updates[0].Reset);
        Assert.All(updates.Skip(1), u => Assert.False(u.Reset));
        Assert.All(updates.SkipLast(1), u => Assert.False(u.Final));
        Assert.True(updates[^1].Final);
        Assert.Equal(pixels, reconstructed);
    }

    [Fact]
    public void ResolutionChange_StartsNewBaseImage()
    {
        var encoder = new DesktopTileEncoder();
        encoder.Capture(new byte[16*8*4], 16, 8, 1);
        encoder.Take(0);
        encoder.Capture(new byte[16*8*4], 8, 16, 2);
        Assert.True(encoder.Take(1).Reset);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    public void TruncationOrTrailingBytes_AreRejected(int difference)
    {
        var update = new DesktopUpdate(3,4,1,1,true,true,[new(0,0,1,1,DesktopBlock.Compress(new byte[4]))]);
        byte[] wire = update.Encode();
        byte[] changed = new byte[wire.Length + difference];
        wire.AsSpan(0, Math.Min(wire.Length,changed.Length)).CopyTo(changed);
        Assert.Throws<ProtocolException>(() => DesktopUpdate.Decode(EnvelopeOf(changed)));
    }

    [Fact]
    public void BlockOutsideScreen_IsRejectedBeforeRendering()
    {
        var update = new DesktopUpdate(3,4,1,1,true,true,[new(0,0,1,1,DesktopBlock.Compress(new byte[4]))]);
        byte[] wire = update.Encode();
        BinaryPrimitives.WriteUInt16LittleEndian(wire.AsSpan(DesktopUpdate.HeaderSize), 1);
        Assert.Throws<ProtocolException>(() => DesktopUpdate.Decode(EnvelopeOf(wire)));
    }

    [Fact]
    public void DecompressionCannotExpandBeyondDeclaredPixels()
    {
        var block = new DesktopBlock(0,0,1,1,DesktopBlock.Compress(new byte[128*128*4]));
        Assert.Throws<ProtocolException>(() => block.Decompress());
    }

    [Fact]
    public async Task OnlyMatchingAck_ReleasesNextImage()
    {
        var gate = new FrameDeliveryGate();
        Task first = gate.Begin(8);
        Assert.False(gate.Acknowledge(7));
        Assert.False(first.IsCompleted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.Begin(9));
        Assert.True(gate.Acknowledge(8));
        await first;
        Assert.False(gate.Acknowledge(8));
        Task second = gate.Begin(9);
        Assert.True(gate.Acknowledge(9));
        await second;
    }
}