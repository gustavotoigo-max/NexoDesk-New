using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexoDesk.App.Services;
using NexoDesk.Shared.Messages;

namespace NexoDesk.App.Controls;

/// <summary>
/// Imagem original em 100% e no ajuste anterior; redução/nitidez na GPU somente quando necessário.
/// A apresentação é coalescida, mas todos os blocos são aplicados em ordem: nenhuma alteração é perdida.
/// </summary>
public sealed class AdaptiveDesktopImage : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(BitmapSource),
        typeof(AdaptiveDesktopImage), new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnSourceChanged));
    public static readonly DependencyProperty EnhancedFitProperty = DependencyProperty.Register(nameof(EnhancedFit), typeof(bool),
        typeof(AdaptiveDesktopImage), new FrameworkPropertyMetadata(true, OnModeChanged));
    public static readonly DependencyProperty ActualSizeProperty = DependencyProperty.Register(nameof(ActualSize), typeof(bool),
        typeof(AdaptiveDesktopImage), new FrameworkPropertyMetadata(false, OnModeChanged));
    private static readonly DependencyPropertyKey FitStatusPropertyKey = DependencyProperty.RegisterReadOnly(nameof(FitStatus), typeof(string),
        typeof(AdaptiveDesktopImage), new PropertyMetadata("Aguardando imagem"));
    public static readonly DependencyProperty FitStatusProperty = FitStatusPropertyKey.DependencyProperty;
    private readonly Func<GpuFitSurface> _createSurface;
    private GpuFitSurface? _gpu;
    private bool _needsFullUpload = true;
    private bool _ready, _failed, _scheduled;
    private DesktopImageLayout _renderedLayout;

    public AdaptiveDesktopImage() : this(() => new GpuFitSurface()) { }

    internal AdaptiveDesktopImage(Func<GpuFitSurface> createSurface)
    {
        _createSurface = createSurface;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => RequestRender();
        Unloaded += (_, _) => { CancelRender(); ReleaseGpu(); };
        SizeChanged += (_, _) => Refresh();
    }

    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public bool EnhancedFit { get => (bool)GetValue(EnhancedFitProperty); set => SetValue(EnhancedFitProperty, value); }
    public bool ActualSize { get => (bool)GetValue(ActualSizeProperty); set => SetValue(ActualSizeProperty, value); }
    public string FitStatus => (string)GetValue(FitStatusProperty);
    public DesktopImageLayout Layout => Source is { } source ?
        DesktopImageLayout.Fit(source.PixelWidth, source.PixelHeight, RenderSize,
            VisualTreeHelper.GetDpi(this).DpiScaleX, VisualTreeHelper.GetDpi(this).DpiScaleY) : default;
    public event Action<string>? EnhancementFailed;

    private static void OnSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var image = (AdaptiveDesktopImage)owner;
        // WriteableBitmap também pode notificar uma subpropriedade. Só uma nova base pede carga completa.
        if (!ReferenceEquals(args.OldValue, args.NewValue))
        {
            image._needsFullUpload = true;
            image._ready = false;
        }
        image.RequestRender();
    }

    private static void OnModeChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var image = (AdaptiveDesktopImage)owner;
        image._failed = false;
        image.Refresh();
    }

    /// <summary>Mantém o foco de teclado na imagem; um clique remoto não deve focar o ScrollViewer.</summary>
    internal void FocusForRemoteInput(System.Windows.Input.MouseButtonEventArgs? click = null)
    {
        Focus();
        if (click is not null) click.Handled = true;
    }

    public void Refresh()
    {
        _ready = false;
        InvalidateVisual();
        RequestRender();
    }

    /// <summary>Chamado após o presenter estável, na thread UI, antes da confirmação do lote.</summary>
    public void ApplyBlocks(DesktopUpdate update, IReadOnlyList<byte[]> pixels)
    {
        if (_gpu is not null && !_needsFullUpload && !_failed && EnhancedFit && !ActualSize)
        {
            try
            {
                for (int i = 0; i < update.Blocks.Count; i++)
                {
                    var b = update.Blocks[i];
                    _gpu.Renderer.UploadBlock(b.X, b.Y, b.Width, b.Height, pixels[i]);
                }
            }
            catch (Exception ex) when (IsGraphicsFailure(ex)) { UseOriginal(ex); }
        }
        else _needsFullUpload = true;
        RequestRender();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Ocupa a área útil; as barras só entram quando a janela define Width/Height no modo 100%.
        return new Size(double.IsFinite(availableSize.Width) ? availableSize.Width : Source?.Width ?? 0,
            double.IsFinite(availableSize.Height) ? availableSize.Height : Source?.Height ?? 0);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Mantém hit testing sobre o controle; o mapeamento recusa cliques nas faixas sem imagem.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        DesktopImageLayout layout = Layout;
        if (Source is null || layout.Bounds.Width <= 0 || layout.Bounds.Height <= 0) return;
        bool enhanced = _ready && _gpu is not null && _gpu.Image.IsFrontBufferAvailable && _renderedLayout == layout &&
            EnhancedFit && !ActualSize && layout.IsDownscale && !_failed;
        drawingContext.DrawImage(enhanced ? _gpu!.Image : Source, layout.Bounds);
    }

    private void RequestRender()
    {
        if (_scheduled || !IsLoaded) return;
        _scheduled = true;
        CompositionTarget.Rendering += OnCompositionRendering;
    }

    private void CancelRender()
    {
        if (!_scheduled) return;
        _scheduled = false;
        CompositionTarget.Rendering -= OnCompositionRendering;
    }

    private void OnCompositionRendering(object? sender, EventArgs args) => PresentPendingFrame();

    internal void PresentPendingFrame()
    {
        CancelRender();
        DesktopImageLayout layout = Layout;
        if (Source is null || layout.PixelWidth == 0) return;
        if (ActualSize || !EnhancedFit || !layout.IsDownscale || _failed)
        {
            _ready = false;
            SetValue(FitStatusPropertyKey, ActualSize ? "100% — pixels originais" :
                _failed ? "Ajuste original — nitidez indisponível" : !layout.IsDownscale ? "Ajuste sem redução" : "Ajuste original");
            ReleaseGpu();
            InvalidateVisual();
            return;
        }
        try
        {
            if (_gpu is null)
            {
                _gpu = _createSurface();
                _gpu.Image.IsFrontBufferAvailableChanged += OnFrontBufferChanged;
                _needsFullUpload = true;
            }
            if (_needsFullUpload)
            {
                _gpu.Renderer.UploadFrame(Source);
                _needsFullUpload = false;
            }
            _ready = _gpu.Present(layout.PixelWidth, layout.PixelHeight);
            if (_ready)
            {
                _renderedLayout = layout;
                SetValue(FitStatusPropertyKey, "Ajuste nítido");
            }
            else
            {
                SetValue(FitStatusPropertyKey, "Ajuste original — aguardando imagem");
                // TryLock não bloqueia o controle. Sem front buffer, o evento abaixo retoma a renderização.
                if (_gpu.Image.IsFrontBufferAvailable) RequestRender();
            }
            InvalidateVisual();
        }
        catch (Exception ex) when (IsGraphicsFailure(ex)) { UseOriginal(ex); }
    }

    private void OnFrontBufferChanged(object sender, DependencyPropertyChangedEventArgs args) => Refresh();

    private static bool IsGraphicsFailure(Exception ex) => ex is SharpGen.Runtime.SharpGenException or
        System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or NotSupportedException;

    private void UseOriginal(Exception error)
    {
        _failed = true;
        _ready = false;
        CancelRender();
        ReleaseGpu();
        SetValue(FitStatusPropertyKey, "Ajuste original — nitidez indisponível");
        InvalidateVisual();
        EnhancementFailed?.Invoke($"Ajuste de nitidez indisponível ({error.GetType().Name}, 0x{error.HResult:X8}); visualização original mantida.");
    }

    private void ReleaseGpu()
    {
        if (_gpu is null) return;
        _gpu.Image.IsFrontBufferAvailableChanged -= OnFrontBufferChanged;
        _gpu.Dispose();
        _gpu = null;
        _ready = false;
        _needsFullUpload = true;
    }
}
