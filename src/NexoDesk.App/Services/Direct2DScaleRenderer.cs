using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Vortice.Direct2D1;
using Scale = Vortice.Direct2D1.Effects.Scale;
using Sharpen = Vortice.Direct2D1.Effects.Sharpen;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using FeatureLevel = Vortice.Direct3D.FeatureLevel;
using InterpolationMode = Vortice.Direct2D1.InterpolationMode;
using AlphaMode = Vortice.DCommon.AlphaMode;
using D2DPixelFormat = Vortice.DCommon.PixelFormat;

namespace NexoDesk.App.Services;

/// <summary>
/// Redução cúbica seguida de nitidez na GPU. O bitmap original continua no presenter estável.
/// Atualizações copiam somente os blocos recebidos; a saída de produção fica compartilhada na GPU.
/// </summary>
public sealed class Direct2DScaleRenderer : IDisposable
{
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _immediate;
    private ID2D1Device? _d2dDevice;
    private ID2D1DeviceContext? _context;
    private Scale? _scale;
    private Sharpen? _sharpen;
    private ID2D1Bitmap1? _input;
    private ID2D1Bitmap1? _target;
    private ID3D11Texture2D? _output;
    public int SourceWidth { get; private set; }
    public int SourceHeight { get; private set; }
    public int OutputWidth { get; private set; }
    public int OutputHeight { get; private set; }

    public Direct2DScaleRenderer(bool software = false, long? adapterLuid = null)
    {
        try
        {
            using var adapter = adapterLuid is { } luid ? FindAdapter(luid) : null;
            D3D11.D3D11CreateDevice(adapter, adapter is not null ? DriverType.Unknown : software ? DriverType.Warp : DriverType.Hardware,
                DeviceCreationFlags.BgraSupport, [FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                out _device, out _, out _immediate).CheckError();
            using var dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            _d2dDevice = D2D1.D2D1CreateDevice(dxgiDevice, null);
            _context = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
            _context.SetDpi(96, 96);
            _scale = new Scale(_context)
            {
                InterpolationMode = ScaleInterpolationMode.HighQualityCubic,
                BorderMode = BorderMode.Hard,
                CenterPoint = Vector2.Zero,
                Sharpness = 0.5f
            };
            _sharpen = new Sharpen(_context) { Sharpness = 0.35f, Threshold = 0.15f };
            _sharpen.SetInputEffect(0, _scale);
        }
        catch { Dispose(); throw; }
    }

    private static IDXGIAdapter1 FindAdapter(long luid)
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out var adapter).Success; i++)
        {
            if (adapter.Description1.Luid == luid) return adapter;
            adapter.Dispose();
        }
        throw new NotSupportedException("GPU compartilhada do WPF não encontrada.");
    }

    /// <summary>Carga completa somente na primeira imagem, troca de resolução ou retorno ao filtro.</summary>
    public void UploadFrame(BitmapSource frame)
    {
        BitmapSource bgr = frame.Format == PixelFormats.Bgr32 ? frame :
            new FormatConvertedBitmap(frame, PixelFormats.Bgr32, null, 0);
        byte[] pixels = new byte[checked(bgr.PixelWidth * bgr.PixelHeight * 4)];
        bgr.CopyPixels(pixels, bgr.PixelWidth * 4, 0);
        UploadFrame(bgr.PixelWidth, bgr.PixelHeight, pixels);
    }

    public void UploadFrame(int width, int height, byte[] pixels)
    {
        ObjectDisposedException.ThrowIf(_context is null, this);
        if (width is < 1 or > 8192 || height is < 1 or > 8192 || (long)width * height > 32 * 1024 * 1024 ||
            pixels.Length != checked(width * height * 4)) throw new ArgumentException("Imagem BGR32 inválida.");
        if (_input is null || SourceWidth != width || SourceHeight != height)
        {
            _scale!.SetInput(0, null!, true);
            _input?.Dispose();
            _input = _context.CreateBitmap(new SizeI(width, height),
                new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96));
            SourceWidth = width;
            SourceHeight = height;
            _scale.SetInput(0, _input, true);
        }
        UploadOpaque(new System.Drawing.Rectangle(0, 0, width, height), pixels);
        _scale!.SetInput(0, _input, true);
    }

    /// <summary>Mesmo um bloco de um pixel é copiado integralmente, sem amostragem ou limiar de mudança.</summary>
    public void UploadBlock(int x, int y, int width, int height, byte[] pixels)
    {
        ObjectDisposedException.ThrowIf(_context is null, this);
        if (_input is null || x < 0 || y < 0 || width <= 0 || height <= 0 ||
            (long)x + width > SourceWidth || (long)y + height > SourceHeight ||
            pixels.Length != (long)width * height * 4) throw new ArgumentException("Bloco BGR32 inválido.");
        UploadOpaque(new System.Drawing.Rectangle(x, y, width, height), pixels);
        // Invalidação explícita: mudanças pequenas devem atravessar os efeitos mesmo com cache habilitado.
        _context!.InvalidateEffectInputRectangle(_scale!, 0,
            new Vortice.RawRectF(x, y, x + width, y + height));
    }

    private void UploadOpaque(System.Drawing.Rectangle rectangle, byte[] pixels)
    {
        // BGR32 usa o quarto byte como reservado (frequentemente zero), não como transparência.
        // Os efeitos intermediários precisam de BGRA opaco; nunca modificar os bytes do presenter estável.
        byte[] opaque = ArrayPool<byte>.Shared.Rent(pixels.Length);
        try
        {
            pixels.CopyTo(opaque, 0);
            for (int i = 3; i < pixels.Length; i += 4) opaque[i] = 255;
            _input!.CopyFromMemory(rectangle, opaque, (uint)(rectangle.Width * 4)).CheckError();
        }
        finally { ArrayPool<byte>.Shared.Return(opaque); }
    }

    /// <summary>Texture compartilhada D3D9Ex para WPF; handle zero cria saída isolada para comparação/testes.</summary>
    public void SetOutput(int width, int height, IntPtr sharedHandle = default)
    {
        ObjectDisposedException.ThrowIf(_context is null, this);
        if (width is < 1 or > 8192 || height is < 1 or > 8192) throw new ArgumentOutOfRangeException(nameof(width));
        _context.Target = null;
        _target?.Dispose(); _target = null;
        _output?.Dispose(); _output = null;
        _output = sharedHandle != IntPtr.Zero ? _device!.OpenSharedResource<ID3D11Texture2D>(sharedHandle) :
            _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource
            });
        using var surface = _output.QueryInterface<IDXGISurface>();
        _target = _context.CreateBitmapFromDxgiSurface(surface,
            new BitmapProperties1(new D2DPixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied), 96, 96,
                BitmapOptions.Target | BitmapOptions.CannotDraw));
        _context.Target = _target;
        OutputWidth = width; OutputHeight = height;
    }

    public void Render(float sharpness = 0.35f)
    {
        ObjectDisposedException.ThrowIf(_context is null, this);
        if (_input is null || _target is null) throw new InvalidOperationException("Imagem/saída não preparada.");
        if (!float.IsFinite(sharpness) || sharpness < 0 || sharpness > 1) throw new ArgumentOutOfRangeException(nameof(sharpness));
        _scale!.Value = new Vector2((float)OutputWidth / SourceWidth, (float)OutputHeight / SourceHeight);
        _sharpen!.Sharpness = sharpness;
        _sharpen.SetInputEffect(0, _scale);
        _context.BeginDraw();
        _context.Clear(new Color4(0, 0, 0, 1));
        using (var image = (sharpness > 0 ? (ID2D1Effect)_sharpen : _scale).Output)
            _context.DrawImage(image, Vector2.Zero, new Vortice.RawRectF(0, 0, OutputWidth, OutputHeight),
                InterpolationMode.NearestNeighbor, CompositeMode.SourceCopy);
        _context.EndDraw().CheckError();
        _immediate!.Flush();
    }

    /// <summary>Somente comparação/testes. A apresentação normal nunca faz GPU→CPU.</summary>
    public byte[] ReadPixels()
    {
        ObjectDisposedException.ThrowIf(_immediate is null, this);
        if (_output is null) throw new InvalidOperationException("Saída não preparada.");
        Texture2DDescription desc = _output.Description;
        desc.Usage = ResourceUsage.Staging; desc.BindFlags = BindFlags.None;
        desc.CPUAccessFlags = CpuAccessFlags.Read; desc.MiscFlags = ResourceOptionFlags.None;
        using var staging = _device!.CreateTexture2D(desc);
        _immediate.CopyResource(staging, _output);
        var mapped = _immediate.Map(staging, 0, MapMode.Read);
        try
        {
            byte[] pixels = new byte[checked(OutputWidth * OutputHeight * 4)];
            for (int row = 0; row < OutputHeight; row++)
                Marshal.Copy(mapped.DataPointer + checked(row * (int)mapped.RowPitch), pixels, row * OutputWidth * 4, OutputWidth * 4);
            return pixels;
        }
        finally { _immediate.Unmap(staging, 0); }
    }

    public void Dispose()
    {
        if (_context is not null) _context.Target = null;
        _sharpen?.Dispose(); _sharpen = null;
        _scale?.Dispose(); _scale = null;
        _target?.Dispose(); _target = null;
        _input?.Dispose(); _input = null;
        _output?.Dispose(); _output = null;
        _context?.Dispose(); _context = null;
        _d2dDevice?.Dispose(); _d2dDevice = null;
        _immediate?.Dispose(); _immediate = null;
        _device?.Dispose(); _device = null;
    }
}
