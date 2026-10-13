using System.Net;
using NexoDesk.Shared.Audit;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Session;

namespace NexoDesk.IntegrationTests;

public class PasswordSessionTests
{
    [Theory]
    [InlineData("Senha-real-2026!", true)]
    [InlineData("Senha-errada-2026!", false)]
    [InlineData(null, false)]
    public async Task Password_IsRequiredOverRealMutualTls(string? password, bool allowed)
    {
        using var server = TestCertificates.Server();
        using var client = TestCertificates.Client();
        var verifier = AccessPassword.Create("Senha-real-2026!");
        var audit = new MemoryAuditLog();
        var input = new RecordingInput();
        await using var host = new AgentHost(new()
        {
            Endpoint = new(IPAddress.Loopback, 0), ServerCertificate = server,
            Clients = new InMemoryClientStore([new("Admin", CertificateFingerprint.Sha256(client))]),
            Session = new()
            {
                ServerId = "pc-test", DisplayName = "Teste", AppVersion = "test", Features = [Features.View, Features.Control],
                PasswordProvider = () => verifier,
            },
        }, ScriptedPolicy.AllowAll, audit, input: input);
        host.Start();
        var options = new AdminConnectOptions
        {
            Host = "127.0.0.1", Port = host.LocalEndpoint!.Port, ClientCertificate = client,
            ServerFingerprints = [CertificateFingerprint.Sha256(server)], ClientId = "admin-test",
            AppVersion = "test", Mode = SessionMode.Control, AccessPassword = password,
        };
        if (allowed)
        {
            await using var session = await AdminSession.ConnectAsync(options, CancellationToken.None);
            await session.DisconnectAsync();
            Assert.Contains(audit.Events, e => e.Event == AuditEventType.SessionStarted);
        }
        else
        {
            await Assert.ThrowsAsync<SessionDeniedException>(() => AdminSession.ConnectAsync(options, CancellationToken.None));
            Assert.DoesNotContain(audit.Events, e => e.Event == AuditEventType.SessionStarted);
            Assert.Equal(0, input.ReleaseCount);
        }
        Assert.All(audit.Events, e => Assert.DoesNotContain("Senha-real", e.ToString()));
    }

    [Fact]
    public async Task MissingRemotePassword_FailsClosed()
    {
        using var server = TestCertificates.Server();
        using var client = TestCertificates.Client();
        await using var host = new AgentHost(new()
        {
            Endpoint = new(IPAddress.Loopback, 0), ServerCertificate = server,
            Clients = new InMemoryClientStore([new("Admin", CertificateFingerprint.Sha256(client))]),
            Session = new() { ServerId = "pc-test", DisplayName = "Teste", AppVersion = "test", PasswordProvider = () => null },
        }, ScriptedPolicy.AllowAll, new MemoryAuditLog());
        host.Start();
        await Assert.ThrowsAsync<SessionDeniedException>(() => AdminSession.ConnectAsync(new()
        {
            Host = "127.0.0.1", Port = host.LocalEndpoint!.Port, ClientCertificate = client,
            ServerFingerprints = [CertificateFingerprint.Sha256(server)], ClientId = "admin-test",
            AppVersion = "test",
        }, CancellationToken.None));
    }

    [Fact]
    public async Task InputFailure_DoesNotKeepSessionSlotReserved()
    {
        using var server = TestCertificates.Server();
        using var client = TestCertificates.Client();
        await using var host = new AgentHost(new()
        {
            Endpoint = new(IPAddress.Loopback, 0), ServerCertificate = server,
            Clients = new InMemoryClientStore([new("Admin", CertificateFingerprint.Sha256(client))]),
            Session = new() { ServerId = "pc-test", DisplayName = "Teste", AppVersion = "test", Features = [Features.View, Features.Control] },
        }, ScriptedPolicy.AllowAll, new MemoryAuditLog(), input: new FailingInput());
        host.Start();
        var options = new AdminConnectOptions
        {
            Host = "127.0.0.1", Port = host.LocalEndpoint!.Port, ClientCertificate = client,
            ServerFingerprints = [CertificateFingerprint.Sha256(server)], ClientId = "admin-test", AppVersion = "test", Mode = SessionMode.Control,
        };
        await using (var first = await AdminSession.ConnectAsync(options, CancellationToken.None))
        {
            await first.SendKeyAsync(new(30, 65, false, KeyAction.Down), CancellationToken.None);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (host.CurrentSession is not null) await Task.Delay(20, timeout.Token);
        }
        await using var second = await AdminSession.ConnectAsync(options, CancellationToken.None);
        Assert.True(second.CanControl);
    }

    private sealed class FailingInput : IInputSink
    {
        public void Apply(PointerEventMessage pointer) => throw new InvalidOperationException("Falha simulada.");
        public void Apply(KeyEventMessage key) => throw new InvalidOperationException("Falha simulada.");
        public void ReleaseAll() { }
    }
}