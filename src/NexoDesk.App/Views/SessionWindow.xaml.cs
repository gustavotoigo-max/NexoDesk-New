using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Runtime.InteropServices;
using NexoDesk.App.ViewModels;
using NexoDesk.Shared.Messages;
using NexoDesk.Ui.Controls;

namespace NexoDesk.App.Views;

public partial class SessionWindow : NxWindow
{
    private readonly SessionViewModel _vm;
    private readonly HashSet<Key> _keysDown = [];
    private readonly HashSet<PointerButton> _buttonsDown = [];
    private double _lastX, _lastY;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResizeMode;
    private Rect _previousBounds;
    private WindowChrome? _previousChrome;
    private bool _fullScreenShortcutDown;
    private bool _positioningFullScreen;
    private WindowPlacement _previousPlacement;
    private bool _hasPreviousPlacement;

    public SessionWindow(SessionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.CloseRequested += Close;

        // Controle: captura mouse e teclado sobre a imagem e envia ao PC REMOTO.
        RemoteImage.MouseMove += OnMouseMove;
        RemoteImage.MouseDown += OnMouseDown;
        RemoteImage.MouseUp += OnMouseUp;
        RemoteImage.MouseWheel += OnMouseWheel;
        RemoteImage.MouseEnter += (_, _) => { if (_vm.CanControl) RemoteImage.Focus(); };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        Deactivated += (_, _) => ReleaseHeldInputs();
        RemoteImage.LostKeyboardFocus += (_, _) => ReleaseHeldInputs();

        // Ponteiro do PC REMOTO: reposiciona quando ele se move ou a área da imagem muda.
        _vm.CursorChanged += () => { ApplyScale(); UpdateCursorOverlay(); };
        RemoteImage.SizeChanged += (_, _) => UpdateCursorOverlay();
        SizeChanged += (_, _) => { ApplyScale(); UpdateCursorOverlay(); };
    }

    /// <summary>Coloca o ponteiro remoto sobre a imagem usando a mesma conta de letterbox do controle.</summary>
    private void UpdateCursorOverlay()
    {
        if (!_vm.ShowCursorOverlay)
        {
            CursorArrow.Visibility = Visibility.Collapsed;
            return;
        }

        double containerW = RemoteImage.ActualWidth;
        double containerH = RemoteImage.ActualHeight;
        if (containerW <= 0 || containerH <= 0)
        {
            CursorArrow.Visibility = Visibility.Collapsed;
            return;
        }

        double srcW = _vm.RemoteWidth;
        double srcH = _vm.RemoteHeight;
        double scale = Math.Min(containerW / srcW, containerH / srcH);
        double contentW = srcW * scale;
        double contentH = srcH * scale;
        double offsetX = (containerW - contentW) / 2;
        double offsetY = (containerH - contentH) / 2;

        Point point = RemoteImage.TranslatePoint(new Point(offsetX + _vm.CursorX * contentW, offsetY + _vm.CursorY * contentH), CursorLayer);
        Canvas.SetLeft(CursorArrow, point.X);
        Canvas.SetTop(CursorArrow, point.Y);
        CursorArrow.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Converte a posição do mouse sobre a imagem (ajustada com letterbox pelo Stretch=Uniform)
    /// em coordenadas normalizadas 0..1 da tela remota. Retorna false se estiver fora da imagem.
    /// </summary>
    private bool TryNormalize(MouseEventArgs e, out double nx, out double ny)
    {
        nx = ny = 0;
        double containerW = RemoteImage.ActualWidth;
        double containerH = RemoteImage.ActualHeight;
        if (containerW <= 0 || containerH <= 0)
            return false;

        double srcW = _vm.RemoteWidth;
        double srcH = _vm.RemoteHeight;
        double scale = Math.Min(containerW / srcW, containerH / srcH);
        double contentW = srcW * scale;
        double contentH = srcH * scale;
        double offsetX = (containerW - contentW) / 2;
        double offsetY = (containerH - contentH) / 2;

        Point p = e.GetPosition(RemoteImage);
        double x = (p.X - offsetX) / contentW;
        double y = (p.Y - offsetY) / contentH;
        if (x < 0 || x > 1 || y < 0 || y > 1)
            return false;
        nx = x;
        ny = y;
        return true;
    }

    private void OnToggleFullScreen(object sender, RoutedEventArgs e) => ToggleFullScreen();

    /// <summary>Tela cheia local; não altera resolução, escala nem sessão do PC REMOTO.</summary>
    private void ToggleFullScreen()
    {
        ReleaseHeldInputs();
        if (IsFullScreen)
        {
            RestoreWindowMode();
        }
        else
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            IntPtr targetMonitor = MonitorFromWindow(handle, 2);
            _previousPlacement = new WindowPlacement { Size = Marshal.SizeOf<WindowPlacement>() };
            _hasPreviousPlacement = GetWindowPlacement(handle, ref _previousPlacement);
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _previousChrome = WindowChrome.GetWindowChrome(this);
            WindowState = WindowState.Normal;
            IsFullScreen = true;
            WindowChrome.SetWindowChrome(this, null);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            if (!PositionFullScreen(targetMonitor))
            {
                RestoreWindowMode();
                System.Windows.MessageBox.Show(this, "Não foi possível abrir tela cheia neste monitor.", "NexoDesk New",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        ApplyScale();
        UpdateCursorOverlay();
        RemoteImage.Focus();
    }

    private void RestoreWindowMode()
    {
        IsFullScreen = false;
        WindowStyle = _previousStyle;
        ResizeMode = _previousResizeMode;
        WindowChrome.SetWindowChrome(this, _previousChrome);
        // A posição nativa também preserva o monitor de uma janela originalmente maximizada.
        if (_hasPreviousPlacement && SetWindowPlacement(new WindowInteropHelper(this).Handle, ref _previousPlacement))
        {
            WindowState = _previousState;
            return;
        }
        if (!_previousBounds.IsEmpty)
        {
            Left = _previousBounds.Left;
            Top = _previousBounds.Top;
            Width = _previousBounds.Width;
            Height = _previousBounds.Height;
        }
        WindowState = _previousState;
    }

    private bool PositionFullScreen(IntPtr targetMonitor = default)
    {
        if (_positioningFullScreen) return true;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(targetMonitor != IntPtr.Zero ? targetMonitor : MonitorFromWindow(handle, 2), ref info)) return false;
        _positioningFullScreen = true;
        try
        {
            // Coordenadas físicas do monitor: evita converter incorretamente DPI misto/monitores à esquerda.
            // Não torna a janela Topmost: Alt+Tab continua permitindo usar as ferramentas locais.
            return SetWindowPos(handle, IntPtr.Zero, info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top, 0x0004 | 0x0010 | 0x0020);
        }
        finally { _positioningFullScreen = false; }
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (_vm is null) return;
        if (IsFullScreen) PositionFullScreen();
        ApplyScale();
        UpdateCursorOverlay();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Size;
        public uint Flags, ShowCommand;
        public NativePoint MinPosition, MaxPosition;
        public NativeRect NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    private void OnToggleScale(object sender, RoutedEventArgs e) { ApplyScale(); UpdateCursorOverlay(); }
    private void OnVideoScroll(object sender, ScrollChangedEventArgs e) { if (_vm is not null) UpdateCursorOverlay(); }
    private void ApplyScale()
    {
        bool actual = ActualScale.IsChecked == true;
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this);
        RemoteImage.Width = actual ? _vm.RemoteWidth / dpi.DpiScaleX : double.NaN;
        RemoteImage.Height = actual ? _vm.RemoteHeight / dpi.DpiScaleY : double.NaN;
        VideoScroll.HorizontalScrollBarVisibility = actual ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        VideoScroll.VerticalScrollBarVisibility = actual ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
    }

    private static PointerButton Button(MouseButton b) => b switch
    {
        MouseButton.Left => PointerButton.Left,
        MouseButton.Right => PointerButton.Right,
        MouseButton.Middle => PointerButton.Middle,
        _ => PointerButton.None,
    };

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm.CanControl && TryNormalize(e, out double nx, out double ny))
            _vm.SendPointer(_lastX = nx, _lastY = ny, PointerButton.None, PointerAction.Move);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.CanControl)
            return;
        PointerButton button = Button(e.ChangedButton);
        if (button != PointerButton.None && TryNormalize(e, out double nx, out double ny))
        {
            RemoteImage.Focus();
            RemoteImage.CaptureMouse();
            _buttonsDown.Add(button);
            _vm.SendPointer(_lastX = nx, _lastY = ny, button, PointerAction.Down);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_vm.CanControl)
            return;
        PointerButton button = Button(e.ChangedButton);
        if (button != PointerButton.None && _buttonsDown.Remove(button))
        {
            if (TryNormalize(e, out double nx, out double ny)) { _lastX = nx; _lastY = ny; }
            _vm.SendPointer(_lastX, _lastY, button, PointerAction.Up);
        }
        RemoteImage.ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_vm.CanControl && TryNormalize(e, out double nx, out double ny))
            _vm.SendPointer(nx, ny, PointerButton.None, PointerAction.Wheel, e.Delta);
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F11 && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            if (!e.IsRepeat) ToggleFullScreen();
            _fullScreenShortcutDown = true;
            e.Handled = true;
            return;
        }
        if (SendKey(e, KeyAction.Down))
            e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.F11 && _fullScreenShortcutDown)
        {
            _fullScreenShortcutDown = false;
            e.Handled = true;
            return;
        }
        if (SendKey(e, KeyAction.Up))
            e.Handled = true;
    }

    private bool SendKey(KeyEventArgs e, KeyAction action)
    {
        if (!_vm.CanControl || !RemoteImage.IsKeyboardFocused)
            return false;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (action == KeyAction.Down) _keysDown.Add(key); else _keysDown.Remove(key);
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk <= 0)
            return false;
        uint scan = MapVirtualKey((uint)vk, 0);
        bool extended = IsExtended(key);
        _vm.SendKey((ushort)scan, (ushort)vk, extended, action);
        return true;
    }

    private void ReleaseHeldInputs()
    {
        foreach (Key key in _keysDown)
        {
            int vk = KeyInterop.VirtualKeyFromKey(key);
            uint scan = MapVirtualKey((uint)vk, 0);
            _vm.SendKey((ushort)scan, (ushort)vk, IsExtended(key), KeyAction.Up);
        }
        _keysDown.Clear();
        foreach (PointerButton button in _buttonsDown)
            _vm.SendPointer(_lastX, _lastY, button, PointerAction.Up);
        _buttonsDown.Clear();
        RemoteImage.ReleaseMouseCapture();
    }

    private static bool IsExtended(Key key) => key is
        Key.Up or Key.Down or Key.Left or Key.Right or
        Key.Home or Key.End or Key.PageUp or Key.PageDown or Key.Insert or Key.Delete or
        Key.RightCtrl or Key.RightAlt or Key.Apps or Key.LWin or Key.RWin or Key.PrintScreen;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    /// <summary>Fechar a janela encerra a sessão de forma ordenada (avisa o PC REMOTO).</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (!e.Cancel && !_vm.IsEnded)
            _ = _vm.DisconnectAsync();
    }
}
