using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Session;

namespace NexoDesk.IntegrationTests;

public class DesktopStreamTests
{
    private sealed class Source : IFrameSource, IDesktopUpdateSource
    {
        public int Captures;
        public Task<VideoFrame?> NextFrameAsync(uint index,CancellationToken ct) => throw new InvalidOperationException("Não negociou tiles.");
        public async Task<DesktopUpdate?> NextUpdateAsync(uint index,CancellationToken ct)
        {
            await Task.Delay(5,ct);
            Interlocked.Increment(ref Captures);
            return new(index,0,1,1,index==0,true,[new(0,0,1,1,DesktopBlock.Compress(new byte[]{1,2,3,0}))]);
        }
        public void Reset() { }
    }

    [Fact]
    public async Task SlowPresentation_DoesNotQueueImagesOrBlockInput()
    {
        using var server=TestCertificates.Server();
        using var client=TestCertificates.Client();
        var source=new Source();
        var input=new RecordingInput();
        await using var agent=new LoopbackAgent(server,[new("Admin",CertificateFingerprint.Sha256(client))],
            features:[Features.View,Features.Control,Features.DesktopTiles],input:input,frames:source);
        await using var session=await AdminSession.ConnectAsync(new()
        {
            Host="127.0.0.1",Port=agent.Port,ClientCertificate=client,ServerFingerprints=[agent.ServerFingerprint],
            ClientId="admin",AppVersion="1.1.0",Mode=SessionMode.Control,PingInterval=TimeSpan.FromMilliseconds(100)
        },CancellationToken.None);
        var arrived=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var present=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count=0;
        session.AttachDesktopConsumer(async (update,ct) =>
        {
            Assert.Equal(new byte[]{1,2,3,0},update.Blocks[0].Decompress());
            if (Interlocked.Increment(ref count)==1) { arrived.TrySetResult(); await present.Task.WaitAsync(ct); }
            else second.TrySetResult();
        });
        await arrived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.SendPointerAsync(new(.2,.3,PointerButton.Left,PointerAction.Down,0),CancellationToken.None);
        DateTime deadline=DateTime.UtcNow.AddSeconds(3);
        while (true)
        {
            lock (input.Events) if (input.Events.Count != 0) break;
            if (DateTime.UtcNow>deadline) throw new TimeoutException("Entrada bloqueada por imagem.");
            await Task.Delay(10);
        }
        Assert.Equal(1,Volatile.Read(ref source.Captures));
        present.TrySetResult();
        await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Volatile.Read(ref source.Captures)>=2);
        await session.DisconnectAsync();
    }
}