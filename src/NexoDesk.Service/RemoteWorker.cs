using System.IO.Pipes;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using NexoDesk.Desktop;
using NexoDesk.Shared.Audit;
using NexoDesk.Shared.Configuration;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Pairing;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Session;

namespace NexoDesk.Service;

internal sealed class RemoteWorker
{
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly PairingWindow _pairing = new();
    private readonly Queue<string> _events = new();
    private MachineState? _state;
    private DesktopThread? _desktop;
    private readonly PhysicalInputState _physicalState = new();
    private InputCoordinator? _inputCoordinator;
    private LocalInputMonitor? _localInput;
    private AgentHost? _host;
    private string _signature = "";
    private string _address = "";
    private string? _problem = "Escolha o papel deste computador na interface.";
    private string? _accessCode;

    public async Task RunAsync()
    {
        _pairing.Changed += () => { if (!_pairing.IsOpen) _accessCode = null; };
        Task monitor = MonitorAsync();
        while (!_stop.IsCancellationRequested)
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            foreach (WellKnownSidType sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
                security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(sid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            await using var pipe = NamedPipeServerStreamAcl.Create(ServiceProtocol.PipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
            await pipe.WaitForConnectionAsync().ConfigureAwait(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                // Clientes de rede não possuem PID local: rejeição antes de ler comandos.
                if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint _)) continue;
                ServiceRequest request = await ServiceProtocol.ReadAsync<ServiceRequest>(pipe, timeout.Token).ConfigureAwait(false);
                await _operations.WaitAsync(timeout.Token).ConfigureAwait(false);
                try
                {
                    switch (request.Command)
                    {
                        case "status": break;
                        case "shutdown": _stop.Cancel(); break;
                        case "pair":
                            if (_host is null || _state is null) throw new IOException("O agente não está disponível.");
                            string code = _pairing.Open(TimeSpan.FromMinutes(10));
                            _accessCode = AccessCode.Create(_address, _state.Config.Remote.Port, PairingCode.Normalize(code)!);
                            break;
                        case "cancelPair": _pairing.Close(); break;
                        case "end": _host?.EndCurrentSession(EndReason.LocalUserEnded); break;
                        case "reload":
                            _signature = "";
                            try { await RefreshAsync().ConfigureAwait(false); }
                            catch (Exception ex) { _problem = ex.Message; await StopHostAsync().ConfigureAwait(false); }
                            break;
                        default: throw new IOException("Comando IPC desconhecido.");
                    }
                    await ServiceProtocol.WriteAsync(pipe, Snapshot(), timeout.Token).ConfigureAwait(false);
                }
                finally { _operations.Release(); }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or System.Text.Json.JsonException) { }
        }
        await monitor.ConfigureAwait(false);
        await StopHostAsync().ConfigureAwait(false);
        _localInput?.Dispose();
        _desktop?.Dispose();
        _state?.Dispose();
    }

    private async Task MonitorAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await _operations.WaitAsync().ConfigureAwait(false);
            try { await RefreshAsync().ConfigureAwait(false); }
            catch (Exception ex)
            {
                _problem = $"Agente indisponível: {ex.Message}";
                await StopHostAsync().ConfigureAwait(false);
                _signature = "";
            }
            finally { _operations.Release(); }
            await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
    }

    private async Task RefreshAsync()
    {
        if (!File.Exists(MachineState.ConfigFile)) return;
        _state ??= new MachineState();
        _state.Reload();
        RemoteSettings settings = _state.Config.Remote;
        if (TransmissionModes.Effective(settings.TransmissionMode) != TransmissionMode.Stable)
            throw new InvalidOperationException("Motor de transmissão indisponível.");
        // Revogação e desligamento da política cortam a sessão já aberta.
        foreach (AgentSession session in _host?.Sessions ?? [])
            if (session.Client is { } client && (_state.Find(client.Fingerprint) is null || !settings.UnattendedAccessEnabled))
                session.End(EndReason.PermissionRevoked);
        if (_state.Config.Role != DeviceRole.Remote)
        {
            await StopHostAsync().ConfigureAwait(false);
            _problem = "Este computador não está configurado como PC REMOTO.";
            _signature = "";
            return;
        }
        IPAddress address = PrivateNetwork.Resolve(settings);
        string signature = $"{address}|{settings.Port}|{_state.Config.DeviceName}|{settings.AccessPassword?.Hash}|{settings.UnattendedAccessEnabled}|{settings.SimultaneousAccessEnabled}";
        if (_host is not null && signature == _signature) return;
        await StopHostAsync().ConfigureAwait(false);
        _address = address.ToString();
        _desktop ??= new DesktopThread(() => _state.Config.Remote.MaxFps, () => _state.Config.Remote.JpegQuality, _physicalState);
        if (settings.SimultaneousAccessEnabled)
        {
            _inputCoordinator ??= new InputCoordinator(_desktop);
            _localInput ??= new LocalInputMonitor(_physicalState, _inputCoordinator.NotifyLocalActivity, _inputCoordinator.SetLocalMonitoringAvailable);
        }
        else
        {
            _localInput?.Dispose(); _localInput = null;
            _inputCoordinator = null; _physicalState.Clear();
        }
        _host = new AgentHost(new AgentHostOptions
        {
            Endpoint = new IPEndPoint(address, settings.Port),
            ServerCertificate = _state.Certificate,
            Clients = _state, Pairing = _pairing,
            MaxSessions = settings.SimultaneousAccessEnabled ? 2 : 1,
            Session = new AgentSessionOptions
            {
                ServerId = _state.Config.DeviceId, DisplayName = _state.Config.DeviceName,
                AppVersion = typeof(RemoteWorker).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                Features = settings.SimultaneousAccessEnabled
                    ? [Features.View, Features.Control, Features.DesktopTiles, Features.ActivityControl]
                    : [Features.View, Features.Control, Features.DesktopTiles],
                PasswordProvider = () => { _state.Reload(); return _state.Config.Remote.AccessPassword; },
            },
        }, new WorkerPolicy(_state), new WorkerAudit(this), _desktop, _desktop, _desktop, _desktop,
            frameFactory: settings.SimultaneousAccessEnabled ? _desktop.CreateFrameSource : null, coordinator: _inputCoordinator);
        _host.SessionActivated += session => { if (!settings.SimultaneousAccessEnabled) _desktop.Reset(); _ = SessionNotice.WatchAsync(session); };
        _host.Start();
        _signature = signature;
        _problem = settings.AccessPassword is null ? "Defina uma senha de acesso para habilitar conexões."
            : !settings.UnattendedAccessEnabled ? "Habilite o acesso na interface para aceitar conexões." : null;
    }

    private async Task StopHostAsync()
    {
        _pairing.Close();
        if (_host is not null) { await _host.DisposeAsync().ConfigureAwait(false); _host = null; }
    }

    private ServiceStatus Snapshot()
    {
        AgentSession[] sessions = _host?.Sessions ?? [];
        string[] events;
        lock (_events) events = _events.ToArray();
        string? key = sessions.Length == 0 ? null : string.Join("|", sessions.Select(s => s.SessionId).Order());
        string? names = sessions.Length == 0 ? null : string.Join(" e ", sessions.Select(s => s.ClientName));
        string? problem = _problem ?? _localInput?.Problem;
        return new(_host is { IsRunning: true }, problem, _address, key, names,
            sessions.FirstOrDefault()?.Mode, _pairing.IsOpen ? _accessCode : null, _pairing.ExpiresAt, events,
            sessions.Select(s => new ActiveAccess(s.SessionId, s.ClientName, s.Mode ?? SessionMode.View)).ToArray(),
            _inputCoordinator?.State.OwnerName);
    }

    private sealed class WorkerPolicy(MachineState state) : IAgentPolicy
    {
        public Task<PolicyDecision> RequestSessionAsync(SessionRequest request, CancellationToken ct)
        {
            state.Reload();
            return Task.FromResult(state.Config.Role == DeviceRole.Remote && state.Config.Remote.UnattendedAccessEnabled
                && state.Config.Remote.AccessPassword is not null && state.Find(request.Client.Fingerprint) is not null
                ? PolicyDecision.Allow()
                : PolicyDecision.Deny(DenyReason.NotAuthorized, "O acesso remoto está desabilitado neste computador."));
        }
    }

    private sealed class WorkerAudit(RemoteWorker owner) : IAuditLog
    {
        private readonly JsonLinesAuditLog _disk = new(MachineState.Logs);
        public void Write(AuditEvent e)
        {
            _disk.Write(e);
            lock (owner._events)
            {
                owner._events.Enqueue($"{e.TimestampUtc:HH:mm:ss} {e.Event} {e.ClientName} {e.Reason}");
                while (owner._events.Count > 30) owner._events.Dequeue();
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint processId);
}