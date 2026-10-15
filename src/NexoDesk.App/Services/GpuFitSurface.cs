using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vortice.Direct3D9;

namespace NexoDesk.App.Services;

/// <summary>Uma única superfície D3D9Ex compartilhada entre Direct2D/D3D11 e o compositor WPF.</summary>
public sealed class GpuFitSurface : IDisposable
{
    private IDirect3D9Ex? _d3d;
    private IDirect3DDevice9Ex? _device;
    private IDirect3DTexture9? _texture;
    private IDirect3DSurface9? _surface;
    public D3DImage Image { get; } = new();
    public Direct2DScaleRenderer Renderer { get; private set; } = null!;
    private int _width, _height;

    public GpuFitSurface()
    {
        try
        {
            _d3d = D3D9.Direct3DCreate9Ex();
            // Não cria uma janela nem muda o monitor. FPU preserve e multithread são exigidos pelo interop WPF.
            IntPtr desktop = GetDesktopWindow();
            _device = _d3d.CreateDeviceEx(0, DeviceType.Hardware, desktop,
                CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded | CreateFlags.FpuPreserve,
                new PresentParameters
                {
                    Windowed = true, SwapEffect = SwapEffect.Discard, DeviceWindowHandle = desktop,
                    BackBufferWidth = 1, BackBufferHeight = 1, BackBufferFormat = Format.Unknown
                });
            Renderer = new Direct2DScaleRenderer(adapterLuid: _d3d.GetAdapterLuid(0));
        }
        catch { Dispose(); throw; }
    }

    public bool Present(int width, int height, float sharpness = 0.35f)
    {
        ObjectDisposedException.ThrowIf(_device is null, this);
        if (!Image.IsFrontBufferAvailable || !Image.TryLock(new Duration(TimeSpan.Zero))) return false;
        try
        {
            if (_width != width || _height != height)
            {
                Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                _surface?.Dispose(); _surface = null;
                _texture?.Dispose(); _texture = null;
                IntPtr sharedHandle = IntPtr.Zero;
                _texture = _device.CreateTexture((uint)width, (uint)height, 1, Usage.RenderTarget,
                    Format.A8R8G8B8, Pool.Default, ref sharedHandle);
                _surface = _texture.GetSurfaceLevel(0);
                Renderer.SetOutput(width, height, sharedHandle);
                _width = width; _height = height;
            }
            Renderer.Render(sharpness);
            // Reassociar também recupera a referência liberada pelo WPF quando o front buffer some.
            Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface!.NativePointer);
            Image.AddDirtyRect(new Int32Rect(0, 0, width, height));
            return true;
        }
        finally { Image.Unlock(); }
    }

    public void Dispose()
    {
        // Solta a referência do compositor antes dos objetos nativos.
        Image.Lock();
        try { Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero); }
        finally { Image.Unlock(); }
        Renderer?.Dispose();
        _surface?.Dispose(); _surface = null;
        _texture?.Dispose(); _texture = null;
        _device?.Dispose(); _device = null;
        _d3d?.Dispose(); _d3d = null;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();
}
