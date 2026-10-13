using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using NexoDesk.Shared.Messages;
using NexoDesk.Shared.Session;

namespace NexoDesk.Desktop;

/// <summary>GDI e SendInput ficam numa thread sem janelas, que acompanha Default/Winlogon.</summary>
public sealed class DesktopThread : IDisplaySource, IInputSink, IFrameSource, ICursorSource, IDesktopUpdateSource, IDisposable
{
    private readonly BlockingCollection<Action<Exception?>> _queue = new(64);
    private readonly Thread _thread;
    private readonly GdiScreenCapture _capture;
    private readonly SendInputSink _input = new();
    private readonly Func<int> _fps;
    private bool _disposed;
    private readonly object _tileGate = new();
    private readonly NexoDesk.Shared.Streaming.DesktopTileEncoder _tiles = new();
    private int _desktopGeneration, _encodedGeneration;
    private long _nextUpdateTicks;

    public DesktopThread(Func<int> fps, Func<int> quality)
    {
        _fps = fps;
        _capture = new GdiScreenCapture(fps, quality);
        _thread = new Thread(Run) { IsBackground = true, Name = "NexoDesk.InputDesktop" };
        _thread.Start();
    }

    private void Run()
    {
        IntPtr original = GetThreadDesktop(GetCurrentThreadId());
        IntPtr attached = IntPtr.Zero;
        string? desktopName = null;
        try
        {
            foreach (Action<Exception?> work in _queue.GetConsumingEnumerable())
            {
                Exception? error = null;
                IntPtr desktop = OpenInputDesktop(0, false, 0x10000000);
                if (desktop != IntPtr.Zero)
                {
                    var name = new System.Text.StringBuilder(256);
                    GetUserObjectInformation(desktop, 2, name, 512, out uint _);
                    string nextName = name.ToString();
                    if (desktopName is not null && desktopName != nextName)
                    {
                        try { _input.ReleaseAll(); } catch (Win32Exception) { }
                        _capture.Reset();
                        Interlocked.Increment(ref _desktopGeneration);
                    }
                    if (SetThreadDesktop(desktop))
                    {
                        if (attached != IntPtr.Zero) CloseDesktop(attached);
                        attached = desktop;
                        desktopName = nextName;
                    }
                    else { error = new Win32Exception(Marshal.GetLastWin32Error()); CloseDesktop(desktop); }
                }
                else error = new Win32Exception(Marshal.GetLastWin32Error());
                work(error);
            }
        }
        finally
        {
            _capture.Dispose();
            SetThreadDesktop(original);
            if (attached != IntPtr.Zero) CloseDesktop(attached);
        }
    }

    private Task<T> Invoke<T>(Func<T> action)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(error =>
        {
            try { if (error is not null) throw error; result.TrySetResult(action()); }
            catch (Exception ex) { result.TrySetException(ex); }
        });
        return result.Task;
    }

    public DisplayInfoMessage GetDisplayInfo() => Invoke(_capture.GetDisplayInfo).GetAwaiter().GetResult();
    public CursorSample GetCursor()
    {
        try { return Invoke(_capture.GetCursor).GetAwaiter().GetResult(); }
        catch (Win32Exception) { return new(0, 0, false); }
    }
    public void Apply(PointerEventMessage pointer) => Invoke(() => { _input.Apply(pointer); return true; }).GetAwaiter().GetResult();
    public void Apply(KeyEventMessage key) => Invoke(() => { _input.Apply(key); return true; }).GetAwaiter().GetResult();
    public void ReleaseAll() => Invoke(() => { _input.ReleaseAll(); return true; }).GetAwaiter().GetResult();
    public void Reset() => Invoke(() =>
    {
        _capture.Reset();
        lock (_tileGate) { _tiles.Reset(); _nextUpdateTicks = 0; }
        return true;
    }).GetAwaiter().GetResult();

    public async Task<VideoFrame?> NextFrameAsync(uint frameIndex, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(1.0 / Math.Clamp(_fps(), 1, 30)), ct).ConfigureAwait(false);
        try { return await Invoke(() => _capture.Capture(frameIndex)).WaitAsync(ct).ConfigureAwait(false); }
        catch (Win32Exception) { return null; }
    }

    public async Task<DesktopUpdate?> NextUpdateAsync(uint frameIndex, CancellationToken ct)
    {
        lock (_tileGate)
        {
            if (_encodedGeneration != Volatile.Read(ref _desktopGeneration))
            {
                _tiles.Reset();
                _encodedGeneration = Volatile.Read(ref _desktopGeneration);
            }
            if (_tiles.HasPending) return _tiles.Take(frameIndex);
        }
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long interval = System.Diagnostics.Stopwatch.Frequency / Math.Clamp(_fps(), 1, 30);
        if (_nextUpdateTicks > now)
            await Task.Delay(TimeSpan.FromSeconds((double)(_nextUpdateTicks - now) / System.Diagnostics.Stopwatch.Frequency), ct).ConfigureAwait(false);
        // Tempo de captura, compressão e confirmação já integra o intervalo: não adiciona espera inteira a cada frame.
        _nextUpdateTicks = System.Diagnostics.Stopwatch.GetTimestamp() + interval;
        (ScreenSnapshot?, int) captured;
        try { captured = await Invoke(() => (_capture.CapturePixels(), Volatile.Read(ref _desktopGeneration))).ConfigureAwait(false); }
        catch (Win32Exception) { return null; }
        using ScreenSnapshot? snapshot = captured.Item1;
        if (snapshot is null) return null;
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            lock (_tileGate)
            {
                if (_encodedGeneration != captured.Item2) { _tiles.Reset(); _encodedGeneration = captured.Item2; }
                _tiles.Capture(snapshot.Pixels.AsSpan(0, snapshot.Width * snapshot.Height * 4),
                    snapshot.Width, snapshot.Height, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                return _tiles.HasPending ? _tiles.Take(frameIndex) : null;
            }
        }, ct).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if (_disposed) return;
        try { ReleaseAll(); } catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { }
        _disposed = true;
        _queue.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(5))) _queue.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetThreadDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll")] private static extern IntPtr GetThreadDesktop(uint threadId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetUserObjectInformation(IntPtr handle, int index, System.Text.StringBuilder info, uint length, out uint needed);
}