using System.Runtime.InteropServices;
using NexoDesk.Shared.Messages;

namespace NexoDesk.Desktop;

/// <summary>Observa atividade física; nunca bloqueia o teclado/mouse nem registra caracteres.</summary>
public sealed class LocalInputMonitor : IDisposable
{
    private readonly PhysicalInputState _state;
    private readonly Action _activity;
    private readonly Action<bool> _availability;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;
    private readonly HookProc _keyboard, _mouse;
    private IntPtr _keyboardHook, _mouseHook;
    private volatile bool _healthy;
    public bool Healthy => _healthy;
    public string? Problem { get; private set; }

    public LocalInputMonitor(PhysicalInputState state, Action activity, Action<bool> availability)
    {
        _state = state; _activity = activity; _availability = availability;
        _keyboard = Keyboard; _mouse = Mouse;
        _availability(false);
        _thread = new Thread(Run) { IsBackground = true, Name = "NexoDesk.PhysicalInput" };
        _thread.Start();
    }

    private void Run()
    {
        IntPtr original = GetThreadDesktop(GetCurrentThreadId()), attached = IntPtr.Zero;
        string? current = null;
        long renewed = 0;
        IntPtr[] handles = [_stop.SafeWaitHandle.DangerousGetHandle()];
        try
        {
            // Cria apenas a fila de mensagens; não cria janela que impediria SetThreadDesktop.
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            while (!_stop.WaitOne(0))
            {
                IntPtr desktop = OpenInputDesktop(0, false, 0x10000000);
                if (desktop != IntPtr.Zero)
                {
                    var name = new System.Text.StringBuilder(256);
                    GetUserObjectInformation(desktop, 2, name, 512, out _);
                    string next = name.ToString();
                    bool changed = next != current;
                    bool refresh = System.Diagnostics.Stopwatch.GetElapsedTime(renewed).TotalSeconds >= 5;
                    if (changed || !_healthy || refresh)
                    {
                        Unhook();
                        if (SetThreadDesktop(desktop))
                        {
                            if (attached != IntPtr.Zero) CloseDesktop(attached);
                            attached = desktop; desktop = IntPtr.Zero;
                            if (changed) _state.Clear();
                            current = next;
                            IntPtr module = GetModuleHandle(null);
                            _keyboardHook = SetWindowsHookEx(13, _keyboard, module, 0);
                            _mouseHook = SetWindowsHookEx(14, _mouse, module, 0);
                            bool healthy = _keyboardHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
                            Problem = healthy ? null : "O monitor de atividade local não pôde ser preparado.";
                            _healthy = healthy; _availability(healthy);
                            if (!healthy) Unhook();
                            renewed = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                        else { _healthy = false; _availability(false); Problem = "O desktop de entrada não está disponível."; }
                    }
                    if (desktop != IntPtr.Zero) CloseDesktop(desktop);
                }
                else { _healthy = false; _availability(false); Problem = "O desktop de entrada não está disponível."; }
                if (MsgWaitForMultipleObjectsEx(1, handles, 250, 0x04FF, 4) == 0) break;
                while (PeekMessage(out MSG message, IntPtr.Zero, 0, 0, 1))
                { TranslateMessage(ref message); DispatchMessage(ref message); }
            }
        }
        finally
        {
            Unhook(); _healthy = false; _availability(false);
            SetThreadDesktop(original);
            if (attached != IntPtr.Zero) CloseDesktop(attached);
        }
    }

    private IntPtr Keyboard(int code, UIntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<KEYBOARD>(data);
            if (_state.ObserveKeyboard((uint)message, (ushort)value.Scan, value.Flags)) _activity();
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr Mouse(int code, UIntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var value = Marshal.PtrToStructure<MOUSE>(data);
            if (_state.ObserveMouse((uint)message, value.Point.X, value.Point.Y, value.Flags)) _activity();
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private void Unhook()
    {
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }
    public void Dispose() { _stop.Set(); if (_thread.Join(TimeSpan.FromSeconds(3))) _stop.Dispose(); }
    private delegate IntPtr HookProc(int code, UIntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBOARD { public uint Vk, Scan, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSE { public POINT Point; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr Window; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public POINT Point; public uint Private; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, UIntPtr message, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PeekMessage(out MSG message, IntPtr window, uint first, uint last, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref MSG message);
    [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint timeout, uint mask, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint thread);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder name, uint length, out uint needed);
}
