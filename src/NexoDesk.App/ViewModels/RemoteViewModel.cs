using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NexoDesk.App.Services;
using NexoDesk.App.Views;
using NexoDesk.Desktop;
using NexoDesk.Shared.Configuration;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Security;
using NexoDesk.Ui;
using NexoDesk.Ui.Controls;

namespace NexoDesk.App.ViewModels;

public sealed record NetworkChoice(NetworkMode Mode, string Label);

public enum RemoteStatus { Starting, Listening, InSession, Error }
public sealed record AdminRow(string Name, string Fingerprint, string ShortFingerprint, string PairedAtText);

/// <summary>A interface configura e acompanha o serviço; o acesso continua antes do login.</summary>
public sealed class RemoteViewModel : ObservableObject, IAsyncDisposable
{
    private readonly AppState _state;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ServiceStatus? _snapshot;
    private bool _polling, _disposed;
    private string? _localProblem;
    private string[] _lastEvents = [];

    public RemoteViewModel(AppState state, Dispatcher dispatcher)
    {
        _state = state;
        _timer.Tick += async (_, _) => await PollAsync();
        OpenPairingCommand = new AsyncRelayCommand(() => CommandAsync("pair"), () => IsReady);
        CancelPairingCommand = new AsyncRelayCommand(() => CommandAsync("cancelPair"), () => PairingActive);
        EndSessionCommand = new AsyncRelayCommand(() => CommandAsync("end"), () => HasSession);
        RestartCommand = new AsyncRelayCommand(StartAsync);
        CopyAddressCommand = new RelayCommand(() => Copy(Address));
        CopyCodeCommand = new RelayCommand(() => Copy(AccessCode ?? ""), () => PairingActive);
        RemoveAdminCommand = new AsyncRelayCommand(async p => { if (p is AdminRow row) await RemoveAdminAsync(row); });
        ClearLogCommand = new RelayCommand(() => Log.Clear());
        SetPasswordCommand = new AsyncRelayCommand(SetPasswordAsync);
        ApplyNetworkCommand = new AsyncRelayCommand(ApplyNetworkAsync);
        SelectedNetworkMode = _state.Config.Remote.NetworkMode;
        RefreshAddresses();
        SelectedLocalAddress = _state.Config.Remote.ListenAddress == "auto" ? (LocalAddresses.Count == 1 ? LocalAddresses[0] : null) : _state.Config.Remote.ListenAddress;
        LoadAdmins();
    }

    public ObservableCollection<AdminRow> Admins { get; } = [];
    public ObservableCollection<LogLine> Log { get; } = [];
    public ObservableCollection<string> LocalAddresses { get; } = [];
    public NetworkChoice[] NetworkModes { get; } = [new(NetworkMode.Tailscale, "Tailscale (internet)"), new(NetworkMode.LocalNetwork, "Rede local")];
    public NetworkMode SelectedNetworkMode { get; set; }
    public string? SelectedLocalAddress { get; set; }
    public ICommand OpenPairingCommand { get; }
    public ICommand CancelPairingCommand { get; }
    public ICommand CopyAddressCommand { get; }
    public ICommand CopyCodeCommand { get; }
    public ICommand RemoveAdminCommand { get; }
    public ICommand EndSessionCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SetPasswordCommand { get; }
    public ICommand ApplyNetworkCommand { get; }
    public event Action<ServiceStatus>? SessionActivated;
    public event Action? SessionFinished;
    public event Action? StatusChanged;
    public string DeviceName => _state.Config.DeviceName;
    public RemoteStatus Status => _localProblem is not null || _snapshot is { Ready: false } ? RemoteStatus.Error
        : HasSession ? RemoteStatus.InSession : IsReady ? RemoteStatus.Listening : RemoteStatus.Starting;
    public string StatusText => Status switch { RemoteStatus.Listening => "Serviço disponível", RemoteStatus.InSession => "Acesso remoto ativo", RemoteStatus.Error => "Agente indisponível", _ => "Iniciando…" };
    public BadgeKind StatusKind => Status switch { RemoteStatus.Listening => BadgeKind.Success, RemoteStatus.InSession => BadgeKind.Warning, RemoteStatus.Error => BadgeKind.Danger, _ => BadgeKind.Neutral };
    public bool IsReady => _localProblem is null && _snapshot?.Ready == true;
    public string? Problem => _localProblem ?? _snapshot?.Problem;
    public bool HasProblem => Problem is not null;
    public string Address => _snapshot?.Address is { Length: > 0 } address ? address : "aguardando serviço";
    public string IpText => Address;
    public string TailscaleText => _state.Config.Remote.NetworkMode == NetworkMode.LocalNetwork ? "rede local · sem Tailscale" : "Tailscale";
    public string? AccessCode => _snapshot?.AccessCode;
    public string? PairingCode => AccessCode;
    public bool PairingActive => AccessCode is not null && _snapshot!.PairingExpiresAt > DateTimeOffset.UtcNow;
    public string PairingRemaining => PairingActive ? $"Expira em {Math.Max(0, (int)(_snapshot!.PairingExpiresAt - DateTimeOffset.UtcNow).TotalSeconds)} s" : "";
    public string? SessionAdmin => _snapshot?.ClientName;
    public string SessionHeadline => (_snapshot?.Accesses?.Length ?? 1) > 1
        ? $"ADMINs {SessionAdmin} estão acessando este computador" : $"ADMIN {SessionAdmin} está acessando este computador";
    public string? SessionDetail => HasSession
        ? $"{_snapshot?.Accesses?.Length ?? 1} conexão(ões). Controle: {_snapshot?.ControlOwner ?? "por atividade"}." : null;
    public bool HasSession => _snapshot?.SessionId is not null;
    public bool HasAdmins => Admins.Count > 0;
    public string PasswordStatus => _state.Config.Remote.AccessPassword is null ? "Senha ainda não definida." : "Senha definida. Exigida em cada conexão.";

    public bool UnattendedAccess
    {
        get => _state.Config.Remote.UnattendedAccessEnabled;
        set
        {
            if (value == UnattendedAccess) return;
            if (value && _state.Config.Remote.AccessPassword is null) { AddLog(LogLevel.Warn, "Defina uma senha antes de habilitar o acesso."); OnPropertyChanged(); return; }
            _state.Update(c => c.Remote.UnattendedAccessEnabled = value);
            OnPropertyChanged();
            _ = CommandAsync("reload");
        }
    }

    public bool StartWithWindows
    {
        get => _state.Config.Remote.StartWithWindows;
        set
        {
            _ = SetStartupAsync(value);
        }
    }

    private async Task SetStartupAsync(bool enabled)
    {
        try
        {
            await ServiceControl.ConfigureStartupAsync(enabled);
            _state.Update(c => c.Remote.StartWithWindows = enabled);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        { AddLog(LogLevel.Error, ex.Message); }
        finally { OnPropertyChanged(nameof(StartWithWindows)); }
    }

    public async Task StartAsync()
    {
        _localProblem = null;
        try
        {
            await ServiceControl.EnsureStartedAsync();
            await ServiceControl.ConfigureStartupAsync(StartWithWindows);
            _timer.Start();
            await PollAsync();
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { _localProblem = ex.Message; Notify(); }
    }

    public void EndSession() => _ = CommandAsync("end");
    public Task StopAsync() { _timer.Stop(); return Task.CompletedTask; }
    public async ValueTask DisposeAsync() { _disposed = true; await StopAsync(); }

    private async Task SetPasswordAsync()
    {
        string? password = PasswordWindow.Ask("Definir senha de acesso", creating: true);
        if (password is null) return;
        var verifier = await Task.Run(() => AccessPassword.Create(password));
        _state.Update(c => c.Remote.AccessPassword = verifier);
        OnPropertyChanged(nameof(PasswordStatus));
        AddLog(LogLevel.Info, "Senha de acesso definida. O ADMIN deverá informá-la na próxima conexão.");
        await CommandAsync("reload");
    }

    private async Task ApplyNetworkAsync()
    {
        RefreshAddresses();
        if (SelectedNetworkMode == NetworkMode.LocalNetwork &&
            (SelectedLocalAddress is null || !LocalAddresses.Contains(SelectedLocalAddress)))
        { AddLog(LogLevel.Error, "Selecione um endereço de rede local ativo."); return; }
        _state.Update(c => { c.Remote.NetworkMode = SelectedNetworkMode; c.Remote.ListenAddress = SelectedNetworkMode == NetworkMode.Tailscale ? "auto" : SelectedLocalAddress!; });
        await CommandAsync("reload");
        OnPropertyChanged(nameof(TailscaleText));
    }

    private void RefreshAddresses()
    {
        LocalAddresses.Clear();
        foreach (IPAddress address in PrivateNetwork.LocalAddresses()) LocalAddresses.Add(address.ToString());
    }

    private async Task PollAsync()
    {
        if (_polling || _disposed) return;
        _polling = true;
        try { Apply(await ServiceProtocol.RequestAsync("status")); _localProblem = null; Notify(); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
        { _localProblem = "O serviço não respondeu. Instale o NexoDesk New ou clique em Tentar de novo."; Notify(); }
        finally { _polling = false; }
    }

    private async Task CommandAsync(string command)
    {
        try { Apply(await ServiceProtocol.RequestAsync(command)); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { AddLog(LogLevel.Error, "O serviço não respondeu ao comando."); }
    }

    private void Apply(ServiceStatus status)
    {
        string? before = _snapshot?.SessionId;
        _snapshot = status;
        _state.Reload();
        LoadAdmins();
        if (before != status.SessionId)
        {
            if (before is not null) SessionFinished?.Invoke();
            if (status.SessionId is not null) SessionActivated?.Invoke(status);
        }
        if (!status.Events.SequenceEqual(_lastEvents))
        {
            foreach (string line in status.Events.Except(_lastEvents)) AddLog(LogLevel.Info, line);
            _lastEvents = status.Events;
        }
        Notify();
    }

    private void Notify()
    {
        foreach (string property in new[] { nameof(Status), nameof(StatusText), nameof(StatusKind), nameof(IsReady), nameof(Problem),
            nameof(HasProblem), nameof(Address), nameof(IpText), nameof(TailscaleText), nameof(AccessCode), nameof(PairingCode),
            nameof(PairingActive), nameof(PairingRemaining), nameof(SessionAdmin), nameof(SessionHeadline), nameof(SessionDetail), nameof(HasSession), nameof(PasswordStatus), nameof(UnattendedAccess) })
            OnPropertyChanged(property);
        CommandManager.InvalidateRequerySuggested();
        StatusChanged?.Invoke();
    }

    private void LoadAdmins()
    {
        Admins.Clear();
        foreach (PairedAdmin a in _state.Config.Remote.Admins)
            Admins.Add(new(a.Name, a.Sha256, CertificateFingerprint.Short(a.Sha256) + "…", a.PairedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm")));
        OnPropertyChanged(nameof(HasAdmins));
    }

    private async Task RemoveAdminAsync(AdminRow row)
    {
        if (!NxDialogWindow.Ask(Application.Current.MainWindow, new DialogOptions { Kind = DialogKind.Danger,
            Title = $"Remover o ADMIN {row.Name}?", Text = "O acesso será revogado, inclusive se houver uma sessão aberta.",
            PrimaryText = "Remover", SecondaryText = "Cancelar", PrimaryIsDanger = true })) return;
        _state.Update(c => c.Remote.Admins.RemoveAll(a => a.Sha256 == row.Fingerprint));
        LoadAdmins();
        await CommandAsync("reload");
    }

    private void Copy(string text)
    {
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.COMException) { AddLog(LogLevel.Warn, "Área de transferência ocupada."); }
    }
    internal void AddLog(LogLevel level, string text)
    {
        Log.Add(new(DateTime.Now, level, text));
        while (Log.Count > 300) Log.RemoveAt(0);
    }
}