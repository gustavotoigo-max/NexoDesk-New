using System.Net;
using System.Security.Cryptography.X509Certificates;
using NexoDesk.Shared.Audit;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Session;
using NexoDesk.Shared.Streaming;

namespace NexoDesk.IntegrationTests;

public class SimultaneousSessionTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private sealed class Sink : IInputSink
    {
        private readonly object _gate = new();
        private readonly HashSet<ushort> _keys = [];
        public int Releases;
        public bool Down(ushort scan) { lock (_gate) return _keys.Contains(scan); }
        public void Apply(KeyEventMessage key) { lock (_gate) { if (key.Action == KeyAction.Down) _keys.Add(key.ScanCode); else _keys.Remove(key.ScanCode); } }
        public void Apply(PointerEventMessage pointer) { }
        public void ReleaseAll() { lock (_gate) { _keys.Clear(); Interlocked.Increment(ref Releases); } }
    }
    private sealed class Screen { public int Pixel = 1; }
    private sealed class Frames(Screen screen) : IFrameSource, IDesktopUpdateSource, IDisposable
    {
        private readonly DesktopTileEncoder _encoder = new();
        public int Captures, Resets;
        public bool Disposed;
        public Task<VideoFrame?> NextFrameAsync(uint index, CancellationToken ct) => throw new InvalidOperationException("Esperava blocos.");
        public async Task<DesktopUpdate?> NextUpdateAsync(uint index, CancellationToken ct)
        {
            if (_encoder.HasPending) return _encoder.Take(index);
            await Task.Delay(10, ct);
            Interlocked.Increment(ref Captures);
            _encoder.Capture(new byte[] { (byte)Volatile.Read(ref screen.Pixel), 2, 3, 0 }, 1, 1, 0);
            return _encoder.HasPending ? _encoder.Take(index) : null;
        }
        public void Reset() { Interlocked.Increment(ref Resets); _encoder.Reset(); }
        public void Dispose() => Disposed = true;
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public readonly X509Certificate2 Server = TestCertificates.Server();
        public readonly X509Certificate2[] Clients = [TestCertificates.Client("A"), TestCertificates.Client("B"), TestCertificates.Client("C")];
        public readonly Sink Input = new();
        public readonly Screen Screen = new();
        public readonly List<Frames> Sources = [];
        public readonly InputCoordinator Coordinator;
        public readonly AgentHost Host;
        public Fixture(string? password = null)
        {
            Coordinator = new InputCoordinator(Input);
            PasswordVerifier? verifier = password is null ? null : AccessPassword.Create(password);
            Host = new AgentHost(new()
            {
                Endpoint = new IPEndPoint(IPAddress.Loopback, 0), ServerCertificate = Server,
                Clients = new InMemoryClientStore(Clients.Select((cert, i) => new AuthorizedClient($"Admin {i+1}", CertificateFingerprint.Sha256(cert)))),
                MaxSessions = 2, MaxConnectionAttemptsPerMinute = 20,
                Session = new()
                {
                    ServerId = "remote", DisplayName = "Remote", AppVersion = "test",
                    Features = [Features.View, Features.Control, Features.DesktopTiles, Features.ActivityControl],
                    PasswordProvider = verifier is null ? null : () => verifier,
                },
            }, ScriptedPolicy.AllowAll, new MemoryAuditLog(), new FixedDisplay(), Input,
                frameFactory: () => { var source = new Frames(Screen); lock (Sources) Sources.Add(source); return source; }, coordinator: Coordinator);
            Host.Start();
        }
        public AdminConnectOptions Options(int index, SessionMode mode = SessionMode.Control, bool modern = true, string? password = null) => new()
        {
            Host = "127.0.0.1", Port = Host.LocalEndpoint!.Port, ClientCertificate = Clients[index],
            ServerFingerprints = [CertificateFingerprint.Sha256(Server)], ClientId = $"admin-{index}", AppVersion = "test",
            Mode = mode, AccessPassword = password, PingInterval = TimeSpan.FromMilliseconds(100),
            Capabilities = modern ? [Features.View, Features.Control, Features.DesktopTiles, Features.ActivityControl] : [Features.View, Features.Control, Features.DesktopTiles],
        };
        public Task<AdminSession> Connect(int index, SessionMode mode = SessionMode.Control, bool modern = true, string? password = null) =>
            AdminSession.ConnectAsync(Options(index, mode, modern, password), default);
        public async ValueTask DisposeAsync()
        { await Host.DisposeAsync(); foreach (var cert in Clients) cert.Dispose(); Server.Dispose(); }
    }
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(Wait);
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private static Task Key(AdminSession session, ushort scan, KeyAction action = KeyAction.Down) =>
        session.SendKeyAsync(new(scan, 65, false, action), default);
    private static Task Move(AdminSession session) => session.SendPointerAsync(new(.2,.3,PointerButton.None,PointerAction.Move,0), default);
    private static void Consume(AdminSession session) => session.AttachDesktopConsumer((_, _) => Task.CompletedTask);

    [Fact]
    public async Task BothConnectAndActivityTransfersControlWithoutDisconnecting()
    {
        await using var fixture = new Fixture();
        await using var a = await fixture.Connect(0); Consume(a);
        await using var b = await fixture.Connect(1); Consume(b);
        await Until(() => fixture.Host.Sessions.Length == 2);
        await Key(a, 30); await Until(() => fixture.Input.Down(30));
        await Move(b); await Until(() => fixture.Coordinator.State.OwnerId == b.Accepted.SessionId);
        Assert.False(fixture.Input.Down(30));
        await Until(() => a.ControlState?.OwnerId == b.Accepted.SessionId && b.ControlState?.OwnerId == b.Accepted.SessionId);
        await Key(b, 48); await Until(() => fixture.Input.Down(48));
        await Key(a, 30, KeyAction.Up);
        await a.DisconnectAsync(); await Until(() => fixture.Host.Sessions.Length == 1);
        Assert.True(fixture.Input.Down(48));
        Assert.Equal(SessionState.Streaming, b.StateMachine.State);
        fixture.Coordinator.NotifyLocalActivity(); await Until(() => fixture.Coordinator.State.OwnerId == "local");
        Assert.False(fixture.Input.Down(48));
        await Move(b); await Until(() => fixture.Coordinator.State.OwnerId == b.Accepted.SessionId);
        Assert.Equal(SessionState.Streaming, b.StateMachine.State);
    }

    [Fact]
    public async Task SlowViewerDoesNotBlockOtherViewerAndEachGetsCompleteBaselineAndPixelChange()
    {
        await using var fixture = new Fixture();
        await using var a = await fixture.Connect(0);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int pixelA = 0, pixelB = 0, resetsA = 0, resetsB = 0;
        a.AttachDesktopConsumer(async (update, ct) =>
        {
            if (update.Reset) Interlocked.Increment(ref resetsA);
            Volatile.Write(ref pixelA, update.Blocks[0].Decompress()[0]);
            first.TrySetResult(); await release.Task.WaitAsync(ct);
        });
        await first.Task.WaitAsync(Wait);
        await using var b = await fixture.Connect(1);
        b.AttachDesktopConsumer((update, _) =>
        {
            if (update.Reset) Interlocked.Increment(ref resetsB);
            Volatile.Write(ref pixelB, update.Blocks[0].Decompress()[0]);
            return Task.CompletedTask;
        });
        await Until(() => Volatile.Read(ref pixelB) == 1);
        Volatile.Write(ref fixture.Screen.Pixel, 9);
        await Until(() => Volatile.Read(ref pixelB) == 9);
        Assert.Equal(1, Volatile.Read(ref pixelA));
        Assert.Equal(1, Volatile.Read(ref fixture.Sources[0].Captures));
        release.TrySetResult(); await Until(() => Volatile.Read(ref pixelA) == 9);
        Assert.Equal(1, resetsA); Assert.Equal(1, resetsB);
        await a.DisconnectAsync(); await Until(() => fixture.Host.Sessions.Length == 1);
        Assert.Equal(0, fixture.Sources[1].Resets);
        Volatile.Write(ref fixture.Screen.Pixel, 10); await Until(() => Volatile.Read(ref pixelB) == 10);
    }

    [Fact]
    public async Task ThirdAdminIsDeniedAndLeavingOneFreesOnlyItsSlot()
    {
        await using var fixture = new Fixture();
        await using var a = await fixture.Connect(0); Consume(a);
        await using var b = await fixture.Connect(1); Consume(b);
        var denied = await Assert.ThrowsAsync<SessionDeniedException>(() => fixture.Connect(2));
        Assert.Equal(DenyReason.Busy, denied.Reason);
        Assert.Equal(SessionState.Streaming, a.StateMachine.State);
        Assert.Equal(SessionState.Streaming, b.StateMachine.State);
        await a.DisconnectAsync(); await Until(() => fixture.Host.Sessions.Length == 1);
        await using var c = await fixture.Connect(2); Consume(c);
        await Until(() => fixture.Host.Sessions.Length == 2);
    }

    [Fact]
    public async Task WrongPasswordDoesNotReleaseFirstAdminsInput()
    {
        await using var fixture = new Fixture("Senha-compartilhada-2026!");
        await using var a = await fixture.Connect(0, password: "Senha-compartilhada-2026!"); Consume(a);
        await Key(a, 30); await Until(() => fixture.Input.Down(30));
        var denied = await Assert.ThrowsAsync<SessionDeniedException>(() => fixture.Connect(1, password: "Senha-incorreta-2026!"));
        Assert.Equal(DenyReason.NotAuthorized, denied.Reason);
        Assert.True(fixture.Input.Down(30)); Assert.Single(fixture.Host.Sessions);
        Assert.Equal(0, fixture.Input.Releases);
    }

    [Fact]
    public async Task LegacyClientDoesNotReceiveNewMessageAndCanShareTheScreen()
    {
        await using var fixture = new Fixture();
        await using var a = await fixture.Connect(0); Consume(a);
        await using var legacy = await fixture.Connect(1, modern: false); Consume(legacy);
        await Key(legacy, 30); await Until(() => fixture.Input.Down(30));
        await Until(() => a.ControlState?.OwnerId == legacy.Accepted.SessionId);
        Assert.Null(legacy.ControlState); Assert.Equal(SessionState.Streaming, legacy.StateMachine.State);
    }

    [Fact]
    public async Task LocalEndEndsEveryConnectedAdmin()
    {
        await using var fixture = new Fixture();
        await using var a = await fixture.Connect(0); Consume(a);
        await using var b = await fixture.Connect(1); Consume(b);
        await Until(() => fixture.Host.Sessions.Length == 2);
        fixture.Host.EndCurrentSession(EndReason.LocalUserEnded);
        await Until(() => a.StateMachine.State == SessionState.Disconnected && b.StateMachine.State == SessionState.Disconnected);
        await Until(() => fixture.Host.Sessions.Length == 0);
    }

    [Fact]
    public async Task ViewOnlyParticipantCannotTakeInputOrReleaseControllersKeys()
    {
        await using var fixture = new Fixture();
        await using var a = await fixture.Connect(0); Consume(a);
        await using var view = await fixture.Connect(1, SessionMode.View); Consume(view);
        await Key(a, 30); await Until(() => fixture.Input.Down(30));
        await Move(view); Assert.False(view.CanControl);
        await view.DisconnectAsync(); await Until(() => fixture.Host.Sessions.Length == 1);
        Assert.True(fixture.Input.Down(30)); Assert.Equal(0, fixture.Input.Releases);
    }
}
