using System.Reflection;
using System.Windows.Input;
using NexoDesk.App.Services;
using NexoDesk.Shared.Configuration;
using NexoDesk.Shared.Security;
using NexoDesk.Ui;

namespace NexoDesk.App.ViewModels;

public static class AppInfo
{
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}

/// <summary>Primeira abertura: escolher se este computador é ADMIN ou PC REMOTO.</summary>
public sealed class SetupViewModel : ObservableObject
{
    private readonly AppState _state;
    private DeviceRole? _role;
    private string _deviceName;
    private string _tailscaleText = "Verificando o Tailscale…";
    private bool _tailscaleOk;

    public SetupViewModel(AppState state)
    {
        _state = state;
        _deviceName = state.Config.DeviceName;
        ContinueCommand = new RelayCommand(Continue, () => Role is not null && !string.IsNullOrWhiteSpace(DeviceName));
        _ = CheckTailscaleAsync();
    }

    public ICommand ContinueCommand { get; }
    public event Action? Completed;

    public DeviceRole? Role
    {
        get => _role;
        set
        {
            if (Set(ref _role, value))
            {
                OnPropertyChanged(nameof(IsAdmin));
                OnPropertyChanged(nameof(IsRemote));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsAdmin
    {
        get => Role == DeviceRole.Admin;
        set { if (value) Role = DeviceRole.Admin; }
    }

    public bool IsRemote
    {
        get => Role == DeviceRole.Remote;
        set { if (value) Role = DeviceRole.Remote; }
    }

    public string DeviceName
    {
        get => _deviceName;
        set
        {
            if (Set(ref _deviceName, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string TailscaleText { get => _tailscaleText; private set => Set(ref _tailscaleText, value); }
    public bool TailscaleOk { get => _tailscaleOk; private set => Set(ref _tailscaleOk, value); }

    private async Task CheckTailscaleAsync()
    {
        TailscaleStatus ts = await TailscaleInfo.QueryAsync();
        TailscaleOk = ts.Installed && ts.Connected;
        TailscaleText = !ts.Installed
            ? "Tailscale não encontrado. Para acesso pela internet, instale em tailscale.com/download. Na rede local, ele é dispensável."
            : !ts.Connected
                ? "Tailscale instalado, mas desconectado. Abra o Tailscale e entre na rede da empresa."
                : $"Tailscale conectado{(ts.BestAddress is { } a ? $" · este computador é {a}" : "")}.";
    }

    private void Continue()
    {
        string name = DeviceName.Trim();
        _state.Update(c =>
        {
            c.Role = Role;
            c.DeviceName = name.Length > 64 ? name[..64] : name;
            c.DeviceId = AppConfig.MakeId(name);
        });
        Completed?.Invoke();
    }
}

/// <summary>Moldura comum aos dois modos: cabeçalho, rodapé e configurações.</summary>
public sealed class ShellViewModel : ObservableObject
{
    private readonly AppState _state;

    public ShellViewModel(AppState state, object content, Action switchRole, Func<Task> requestExit)
    {
        _state = state;
        Content = content;
        OpenLogsCommand = new RelayCommand(() =>
        {
            System.IO.Directory.CreateDirectory(AppPaths.LogDirectory);
            AppPaths.Open(AppPaths.LogDirectory);
        });
        Update = new UpdateViewModel(requestExit);
        Settings = new SettingsViewModel(state, switchRole, Update);
        _state.Changed += () => OnPropertyChanged(nameof(DeviceName));
        Update.StartPeriodicChecks();
    }

    public object Content { get; }
    public SettingsViewModel Settings { get; }
    public UpdateViewModel Update { get; }
    public ICommand OpenLogsCommand { get; }

    public bool IsAdmin => _state.Config.Role == DeviceRole.Admin;
    public string ModeText => IsAdmin ? "Modo ADMIN" : "Modo PC REMOTO";
    public string Tagline => IsAdmin ? "Acessar os PCs REMOTOS" : "Este computador pode ser acessado pelo ADMIN";
    public string DeviceName => _state.Config.DeviceName;
    public string Version => AppInfo.Version;
}

/// <summary>Atualização pela interface: consulta o GitHub e, se houver versão nova, baixa e instala.</summary>
public sealed class UpdateViewModel : ObservableObject, IDisposable
{
    private readonly Func<Task> _requestExit;
    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _checkForUpdates;
    private readonly Func<UpdateInfo, IProgress<double>?, CancellationToken, Task<bool>> _downloadAndRun;
    private readonly CancellationTokenSource _stopChecks = new();
    private UpdateInfo? _info;
    private string _status = "";
    private bool _busy;
    private bool _checking;

    public UpdateViewModel(Func<Task> requestExit,
        Func<CancellationToken, Task<UpdateCheckResult>>? checkForUpdates = null,
        Func<UpdateInfo, IProgress<double>?, CancellationToken, Task<bool>>? downloadAndRun = null)
    {
        _requestExit = requestExit;
        _checkForUpdates = checkForUpdates ?? (ct => UpdateService.PollAsync(ct));
        _downloadAndRun = downloadAndRun ?? UpdateService.DownloadAndRunAsync;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !_busy && !_checking && !_stopChecks.IsCancellationRequested);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => Available && !_busy && !_checking && !_stopChecks.IsCancellationRequested);
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => !_busy && !_checking && !_stopChecks.IsCancellationRequested);
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand UpdateCommand { get; }
    public string Heading => Available ? AvailableText : $"Versão instalada: {AppInfo.Version}";
    public string ActionText => Busy ? "Atualizando…" : _checking ? "Verificando…" : "Atualizar";

    public bool Available => _info is not null;
    public string AvailableText => _info is null ? "" : $"Atualização disponível: versão {_info.Version}";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                OnPropertyChanged(nameof(ActionText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public async Task CheckAsync()
    {
        if (_checking || _busy || _stopChecks.IsCancellationRequested)
            return;
        _checking = true;
        OnPropertyChanged(nameof(ActionText));
        CommandManager.InvalidateRequerySuggested();
        Status = "Procurando atualizações…";
        try
        {
            UpdateCheckResult result = await _checkForUpdates(_stopChecks.Token);
            _info = result.Info;
            OnPropertyChanged(nameof(Available));
            OnPropertyChanged(nameof(AvailableText));
            OnPropertyChanged(nameof(Heading));
            Status = result.Message;
        }
        catch (OperationCanceledException) when (_stopChecks.IsCancellationRequested) { }
        finally
        {
            _checking = false;
            OnPropertyChanged(nameof(ActionText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>O botão principal consulta o release novamente antes de baixar.</summary>
    public async Task UpdateAsync()
    {
        if (_checking || _busy || _stopChecks.IsCancellationRequested) return;
        await CheckAsync();
        if (Available && !_stopChecks.IsCancellationRequested) await InstallAsync();
    }

    private bool _monitorStarted;
    public static TimeSpan CheckInterval => TimeSpan.FromMinutes(1);
    public void StartPeriodicChecks()
    {
        if (_monitorStarted || _stopChecks.IsCancellationRequested) return;
        _monitorStarted = true;
        _ = MonitorAsync();
    }
    private async Task MonitorAsync()
    {
        try
        {
            await CheckAsync();
            using var timer = new PeriodicTimer(CheckInterval);
            while (await timer.WaitForNextTickAsync(_stopChecks.Token)) await CheckAsync();
        }
        catch (OperationCanceledException) when (_stopChecks.IsCancellationRequested) { }
    }
    public void Dispose() => _stopChecks.Cancel();

    private async Task InstallAsync()
    {
        if (_info is null || _busy || _checking || _stopChecks.IsCancellationRequested)
            return;
        Busy = true;
        Status = "Baixando a atualização…";
        try
        {
            var progress = new Progress<double>(p => Status = $"Baixando a atualização… {p:P0}");
            bool ok = await _downloadAndRun(_info, progress, _stopChecks.Token);
            if (ok)
            {
                // Fecha o NexoDesk New para o instalador conseguir substituir os arquivos.
                // Sem isso, o instalador tenta fechar sozinho, mas no PC REMOTO o X só esconde
                // para a bandeja — então o arquivo fica em uso e o processo fica preso.
                Status = "Instalador aberto. Fechando o NexoDesk New para concluir a atualização…";
                await _requestExit();
            }
            else
            {
                Status = "Não foi possível baixar a atualização. Tente mais tarde.";
            }
        }
        catch (OperationCanceledException) when (_stopChecks.IsCancellationRequested) { }
        finally
        {
            Busy = false;
        }
    }
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly Action _switchRole;
    private string _deviceName;

    public SettingsViewModel(AppState state, Action switchRole, UpdateViewModel update)
    {
        _state = state;
        _switchRole = switchRole;
        Update = update;
        _deviceName = state.Config.DeviceName;
        SaveNameCommand = new RelayCommand(SaveName, () => !string.IsNullOrWhiteSpace(DeviceName) && DeviceName.Trim() != _state.Config.DeviceName);
        SwitchRoleCommand = new RelayCommand(() => _switchRole());
        SetThemeCommand = new RelayCommand(p =>
        {
            if (p is string s && Enum.TryParse(s, out NxThemeMode mode))
            {
                ThemeManager.Apply(mode);
                OnPropertyChanged(nameof(ThemeAuto));
                OnPropertyChanged(nameof(ThemeLight));
                OnPropertyChanged(nameof(ThemeDark));
            }
        });
        OpenDataFolderCommand = new RelayCommand(() => AppPaths.Open(AppPaths.DataDirectory));
    }

    public UpdateViewModel Update { get; }

    public ICommand SaveNameCommand { get; }
    public ICommand SwitchRoleCommand { get; }
    public ICommand SetThemeCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public string DeviceName
    {
        get => _deviceName;
        set
        {
            if (Set(ref _deviceName, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsAdmin => _state.Config.Role == DeviceRole.Admin;
    public bool SimultaneousAccessEnabled
    {
        get => _state.Config.Remote.SimultaneousAccessEnabled;
        set
        {
            if (value == SimultaneousAccessEnabled) return;
            _state.Update(c => c.Remote.SimultaneousAccessEnabled = value);
            OnPropertyChanged();
        }
    }
    public bool EnhancedWindowFit
    {
        get => _state.Config.Admin.EnhancedWindowFit;
        set
        {
            if (value == EnhancedWindowFit) return;
            _state.Update(c => c.Admin.EnhancedWindowFit = value);
            OnPropertyChanged();
        }
    }
    public bool StableTransmission
    {
        get => TransmissionModes.Effective(_state.Config.Remote.TransmissionMode) == TransmissionMode.Stable;
        set
        {
            if (!value) return;
            _state.Update(c => c.Remote.TransmissionMode = TransmissionMode.Stable);
            OnPropertyChanged();
        }
    }
    public bool ExperimentalTransmission => TransmissionModes.Effective(_state.Config.Remote.TransmissionMode) == TransmissionMode.Experimental;
    public bool ExperimentalTransmissionAvailable => TransmissionModes.IsAvailable(TransmissionMode.Experimental);
    public string TransmissionHint => IsAdmin
        ? "A transmissão é definida nas configurações do PC REMOTO. O modo estável continua sendo usado."
        : "Estável mantém a transmissão atual sem perda. O modo experimental será liberado em uma próxima versão.";
    public string RoleText => IsAdmin ? "ADMIN — acessa os PCs REMOTOS" : "PC REMOTO — pode ser acessado pelo ADMIN";
    public string SwitchRoleText => IsAdmin ? "Mudar para PC REMOTO" : "Mudar para ADMIN";

    public bool ThemeAuto => ThemeManager.Mode == NxThemeMode.Auto;
    public bool ThemeLight => ThemeManager.Mode == NxThemeMode.Light;
    public bool ThemeDark => ThemeManager.Mode == NxThemeMode.Dark;

    public string Fingerprint => CertificateFingerprint.Short(_state.Fingerprint) + "…";
    public string FullFingerprint => _state.Fingerprint;
    public string ValidityText
    {
        get
        {
            int days = DeviceIdentity.DaysUntilExpiry(_state.Certificate);
            string date = _state.Certificate.NotAfter.ToString("dd/MM/yyyy");
            return days < 30 ? $"vence em {date} ({days} dias) — será preciso parear de novo" : $"válida até {date}";
        }
    }

    public string Version => AppInfo.Version;

    private void SaveName()
    {
        string name = DeviceName.Trim();
        _state.Update(c => c.DeviceName = name.Length > 64 ? name[..64] : name);
        DeviceName = _state.Config.DeviceName;
    }
}
