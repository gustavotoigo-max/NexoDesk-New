using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Security;
using NexoDesk.Shared.Session;
using NexoDesk.Ui;
using NexoDesk.Ui.Controls;

namespace NexoDesk.App.ViewModels;

/// <summary>
/// Janela de sessão: mostra o vídeo remoto (a partir do M3), latência, FPS medido e estado.
/// Frames atrasados são descartados: só o mais recente é decodificado.
/// </summary>
public sealed class SessionViewModel : ObservableObject
{
    private readonly AdminSession _session;
    private readonly AdminViewModel _main;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _fpsTimer;

    // Bomba de entrada: movimentos do mouse chegam muito rápido (a cada poucos ms). Enviar
    // um por um entope o canal e o cursor remoto fica "arrastando" posições velhas. Aqui os
    // movimentos são coalescidos (só o mais recente fica na fila) e há no máximo um envio em
    // andamento; cliques, roda e teclas nunca são descartados e mantêm a ordem — cada clique
    // já carrega a própria coordenada, então largar movimentos intermediários não erra o alvo.
    private readonly object _inputLock = new();
    private readonly Queue<Func<CancellationToken, Task>> _inputEvents = new();
    private readonly SemaphoreSlim _inputSignal = new(0);
    private readonly CancellationTokenSource _inputCts = new();
    private PointerEventMessage? _pendingMove;
    private bool _pumpIdle = true;

    private VideoFrame? _pendingFrame;
    private int _decoding;
    private int _framesThisSecond;
    private long _bytesThisSecond;
    private string _dataRateText = "0 Mbit/s";
    private readonly Services.DesktopFramePresenter _presenter = new();
    private ImageSource? _frame;
    private string _latencyText = "—";
    private string _fpsText = "0";
    private bool _ended;
    private string? _lastControlOwner;
    private bool _presentationDetached;
    private string _endReason = "";
    private string _displayText = "aguardando";
    private double _cursorX;
    private double _cursorY;
    private bool _cursorVisible;
    private CursorUpdateMessage? _pendingCursor;
    private int _cursorScheduled;

    public SessionViewModel(AdminSession session, MachineViewModel machine, AdminViewModel main)
    {
        _session = session;
        _main = main;
        Machine = machine;
        _dispatcher = Application.Current.Dispatcher;

        _session.LatencyMeasured += l => _dispatcher.InvokeAsync(() => LatencyText = $"{l.TotalMilliseconds:0} ms");
        _session.DisplayChanged += d => _dispatcher.InvokeAsync(() => DisplayText = $"{d.Width} × {d.Height} · escala {d.DpiScale:0.##}");
        _session.FrameReceived += OnFrame;
        _session.AttachDesktopConsumer(OnDesktopUpdateAsync);
        _session.CursorMoved += OnCursor;
        _lastControlOwner = _session.ControlState?.OwnerId;
        _session.ControlChanged += OnControlChanged;
        _session.Ended += reason => _dispatcher.InvokeAsync(() => OnEnded(reason));
        _main.ShowRemotePointerChanged += OnPointerPreferenceChanged;

        if (_session.Display is { } display)
            _displayText = $"{display.Width} × {display.Height} · escala {display.DpiScale:0.##}";

        _fpsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _fpsTimer.Tick += (_, _) =>
        {
            FpsText = Interlocked.Exchange(ref _framesThisSecond, 0).ToString();
            DataRateText = $"{Interlocked.Exchange(ref _bytesThisSecond, 0) * 8 / 1_000_000.0:0.##} Mbit/s";
        };
        _fpsTimer.Start();

        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => !IsEnded);
        ReconnectCommand = new AsyncRelayCommand(ReconnectAsync, () => IsEnded);
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke());

        _ = Task.Run(() => InputPumpAsync(_inputCts.Token));
    }

    private void OnControlChanged(ControlStateMessage state) => _dispatcher.InvokeAsync(() =>
    {
        if (_presentationDetached || _session.ControlState?.Epoch != state.Epoch) return;
        bool wasOwner = _lastControlOwner == SessionId;
        _lastControlOwner = state.OwnerId;
        OnPropertyChanged(nameof(ControlStatus));
        if (wasOwner && state.OwnerId != SessionId)
        {
            lock (_inputLock) { _pendingMove = null; _inputEvents.Clear(); }
            ControlLost?.Invoke();
        }
    });

    private void OnPointerPreferenceChanged() => _dispatcher.InvokeAsync(() =>
    {
        if (_presentationDetached) return;
        OnPropertyChanged(nameof(ShowRemotePointer)); OnPropertyChanged(nameof(ShowCursorOverlay)); CursorChanged?.Invoke();
    });

    internal void DetachPresentation()
    {
        _presentationDetached = true;
        _session.ControlChanged -= OnControlChanged;
        _main.ShowRemotePointerChanged -= OnPointerPreferenceChanged;
    }

    public MachineViewModel Machine { get; }
    public string Title => Machine.DisplayName;
    public string Tagline => Machine.Address;
    public string ServerId => _session.Server.ServerId;
    public string ServerName => _session.Server.DisplayName;
    public string SessionId => _session.Accepted.SessionId;
    public string Protocol => _session.NegotiatedProtocol.Replace("Tls", "TLS ").Replace("TLS 13", "TLS 1.3").Replace("TLS 12", "TLS 1.2");
    public string Fingerprint => CertificateFingerprint.Short(_session.ServerFingerprint);
    public string ModeText => _session.Accepted.Mode == SessionMode.Control ? "Controlar" : "Visualizar";
    public string AgentVersion => _session.Server.AppVersion;

    public ICommand DisconnectCommand { get; }
    public ICommand ReconnectCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>Conexão em streaming, inclusive no modo Ver a tela.</summary>
    public event Action? ControlLost;
    public string ControlStatus => IsEnded ? "Conexão encerrada" : _session.ControlState is not { } owner ? ModeText
        : owner.OwnerId == SessionId ? "Você controla — outra atividade pode assumir"
        : owner.OwnerId == "none" ? (_session.Accepted.Mode == SessionMode.Control ? "Mova o mouse ou digite para assumir" : "Somente visualização")
        : $"Controle: {owner.OwnerName}" + (_session.Accepted.Mode == SessionMode.Control ? " — mova ou digite para assumir" : "");
    public bool IsActive => !IsEnded && _session.StateMachine.State == SessionState.Streaming;
    /// <summary>Verdadeiro quando a sessão é de controle e está ativa (envia mouse/teclado).</summary>
    public bool CanControl => _session.CanControl && !IsEnded;

    public int RemoteWidth => (_frame as BitmapSource)?.PixelWidth ?? _session.Display?.Width ?? 1920;
    public int RemoteHeight => (_frame as BitmapSource)?.PixelHeight ?? _session.Display?.Height ?? 1080;

    /// <summary>Envia um evento de ponteiro (coordenadas já normalizadas 0..1). Não bloqueia a interface.</summary>
    public void SendPointer(double nx, double ny, PointerButton button, PointerAction action, int wheel = 0)
    {
        if (!CanControl)
            return;
        double x = Math.Clamp(nx, 0, 1);
        double y = Math.Clamp(ny, 0, 1);
        var msg = new PointerEventMessage(x, y, button, action, wheel);

        if (action == PointerAction.Move)
            EnqueueMove(msg);
        else
            Enqueue(ct => _session.SendPointerAsync(msg, ct));
    }

    public void SendKey(ushort scanCode, ushort virtualKey, bool isExtended, KeyAction action)
    {
        if (!CanControl)
            return;
        var msg = new KeyEventMessage(scanCode, virtualKey, isExtended, action);
        Enqueue(ct => _session.SendKeyAsync(msg, ct));
    }

    /// <summary>Guarda só o movimento mais recente; os anteriores ainda não enviados são descartados.</summary>
    private void EnqueueMove(PointerEventMessage move)
    {
        bool wake;
        lock (_inputLock)
        {
            _pendingMove = move;
            wake = _pumpIdle;
            if (wake)
                _pumpIdle = false;
        }
        if (wake)
            _inputSignal.Release();
    }

    /// <summary>Eventos discretos (clique, roda, tecla) entram em ordem e nunca são descartados.</summary>
    private void Enqueue(Func<CancellationToken, Task> job)
    {
        bool wake;
        lock (_inputLock)
        {
            _inputEvents.Enqueue(job);
            wake = _pumpIdle;
            if (wake)
                _pumpIdle = false;
        }
        if (wake)
            _inputSignal.Release();
    }

    private async Task InputPumpAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _inputSignal.WaitAsync(ct).ConfigureAwait(false);
                while (true)
                {
                    Func<CancellationToken, Task>? job;
                    lock (_inputLock)
                    {
                        if (_inputEvents.Count > 0)
                        {
                            job = _inputEvents.Dequeue();
                        }
                        else if (_pendingMove is { } move)
                        {
                            _pendingMove = null;
                            job = c => _session.SendPointerAsync(move, c);
                        }
                        else
                        {
                            _pumpIdle = true; // nada pendente: dorme até o próximo sinal.
                            break;
                        }
                    }
                    try { await job(ct).ConfigureAwait(false); }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException or NexoDesk.Shared.Protocol.ProtocolException or InvalidOperationException or OperationCanceledException) { }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public event Action? CloseRequested;
    public event Action<DesktopUpdate, IReadOnlyList<byte[]>>? DesktopUpdated;
    public bool EnhancedWindowFit
    {
        get => _main.EnhancedWindowFit;
        set { _main.EnhancedWindowFit = value; OnPropertyChanged(); }
    }
    public void LogImageFallback(string message) => _main.AddLog(LogLevel.Warn, message);

    public ImageSource? Frame
    {
        get => _frame;
        private set
        {
            if (Set(ref _frame, value))
            {
                OnPropertyChanged(nameof(RemoteWidth));
                OnPropertyChanged(nameof(RemoteHeight));
                CursorChanged?.Invoke();
                OnPropertyChanged(nameof(HasFrame));
                OnPropertyChanged(nameof(ShowWaiting));
                OnPropertyChanged(nameof(ShowCursorOverlay));
            }
        }
    }

    public bool HasFrame => Frame is not null;

    /// <summary>Conectado e ainda sem imagem.</summary>
    public bool ShowWaiting => !HasFrame && !IsEnded;

    // --- Ponteiro do PC REMOTO (desenhado por cima da imagem; vem à parte dos frames) ---

    /// <summary>Posição X do cursor remoto, normalizada 0..1.</summary>
    public double CursorX => _cursorX;
    /// <summary>Posição Y do cursor remoto, normalizada 0..1.</summary>
    public double CursorY => _cursorY;

    /// <summary>Preferência do ADMIN de mostrar o ponteiro remoto.</summary>
    public bool ShowRemotePointer => _main.ShowRemotePointer;

    /// <summary>Desenhar o ponteiro agora: ligado na preferência, visível na tela remota e com imagem.</summary>
    public bool ShowCursorOverlay => ShowRemotePointer && _cursorVisible && HasFrame && !IsEnded;

    /// <summary>Disparado quando a posição/visibilidade do ponteiro muda (a janela reposiciona o overlay).</summary>
    public event Action? CursorChanged;

    private void OnCursor(CursorUpdateMessage c)
    {
        Interlocked.Exchange(ref _pendingCursor, c);
        ScheduleCursor();
    }

    private void ScheduleCursor()
    {
        if (Interlocked.CompareExchange(ref _cursorScheduled, 1, 0) != 0) return;
        _dispatcher.InvokeAsync(() =>
        {
            try
            {
                var c = Interlocked.Exchange(ref _pendingCursor, null);
                if (c is null || IsEnded) return;
                _cursorX = c.X;
                _cursorY = c.Y;
                if (_cursorVisible != c.Visible)
                {
                    _cursorVisible = c.Visible;
                    OnPropertyChanged(nameof(ShowCursorOverlay));
                }
                CursorChanged?.Invoke();
            }
            finally
            {
                Volatile.Write(ref _cursorScheduled, 0);
                if (!IsEnded && Volatile.Read(ref _pendingCursor) is not null) ScheduleCursor();
            }
        }, DispatcherPriority.Render);
    }
    /// <summary>Conectado em "Só ver": mostra um aviso de que o mouse/teclado não comandam.</summary>
    public bool ViewOnly => !IsEnded && !_session.CanControl;

    public string LatencyText
    {
        get => _latencyText;
        private set => Set(ref _latencyText, value);
    }

    public string DataRateText { get => _dataRateText; private set => Set(ref _dataRateText, value); }

    public string FpsText
    {
        get => _fpsText;
        private set => Set(ref _fpsText, value);
    }

    public string DisplayText
    {
        get => _displayText;
        private set => Set(ref _displayText, value);
    }

    public bool IsEnded
    {
        get => _ended;
        private set
        {
            if (Set(ref _ended, value))
            {
                OnPropertyChanged(nameof(StateText));
                OnPropertyChanged(nameof(StateKind));
                OnPropertyChanged(nameof(ShowWaiting));
                OnPropertyChanged(nameof(ViewOnly));
                OnPropertyChanged(nameof(CanControl));
                OnPropertyChanged(nameof(ShowCursorOverlay));
                CursorChanged?.Invoke();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string EndReason
    {
        get => _endReason;
        private set => Set(ref _endReason, value);
    }

    public string StateText => IsEnded ? "Encerrada" : (_session.Accepted.Mode == SessionMode.Control ? "Controlando" : "Visualizando");
    public BadgeKind StateKind => IsEnded ? BadgeKind.Neutral : BadgeKind.Success;

    public Task DisconnectAsync() => _session.DisconnectAsync();

    private async Task ReconnectAsync()
    {
        CloseRequested?.Invoke();
        await _main.ReconnectAsync(Machine);
    }

    private async Task OnDesktopUpdateAsync(DesktopUpdate update, CancellationToken ct)
    {
        // Confirma somente após aplicar os pixels: há no máximo um lote em trânsito, sem fila atrasada na UI.
        byte[][] decoded = await Task.Run(() => update.Blocks.Select(b => b.Decompress()).ToArray(), ct).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() =>
        {
            if (IsEnded) return;
            Frame = _presenter.Apply(update, decoded);
            DesktopUpdated?.Invoke(update, decoded);
            Interlocked.Add(ref _bytesThisSecond, update.EncodedLength);
            if (update.Final) Interlocked.Increment(ref _framesThisSecond);
        }, DispatcherPriority.Render, ct).Task.ConfigureAwait(false);
    }
    private void OnFrame(VideoFrame frame)
    {
        Interlocked.Add(ref _bytesThisSecond, frame.Jpeg.Length);
        Interlocked.Exchange(ref _pendingFrame, frame);
        StartLegacyDecoder();
    }

    private void StartLegacyDecoder()
    {
        if (Interlocked.CompareExchange(ref _decoding, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                while (Interlocked.Exchange(ref _pendingFrame, null) is { } next)
                {
                    BitmapImage image = Decode(next);
                    await _dispatcher.InvokeAsync(() =>
                    {
                        if (IsEnded) return;
                        Frame = image;
                        Interlocked.Increment(ref _framesThisSecond);
                    }, DispatcherPriority.Render).Task.ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or OperationCanceledException)
            { _main.AddLog(LogLevel.Warn, $"{Machine.DisplayName}: frame inválido descartado."); }
            finally
            {
                Volatile.Write(ref _decoding, 0);
                // Fecha a corrida entre a última leitura da fila e a liberação do decodificador.
                if (!IsEnded && Volatile.Read(ref _pendingFrame) is not null) StartLegacyDecoder();
            }
        });
    }
    private static BitmapImage Decode(VideoFrame frame)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.StreamSource = new MemoryStream(frame.Jpeg.ToArray(), writable: false);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void OnEnded(string reason)
    {
        _fpsTimer.Stop();
        FpsText = "0";
        EndReason = reason;
        IsEnded = true;
        OnPropertyChanged(nameof(ControlStatus));
        try { _inputCts.Cancel(); } catch (ObjectDisposedException) { }
        _main.OnSessionEnded(Machine, SessionId, reason);
    }
}
