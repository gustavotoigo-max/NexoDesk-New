using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexoDesk.App.ViewModels;
using NexoDesk.App.Controls;
using NexoDesk.App.Views;
using NexoDesk.Shared.Configuration;
using NexoDesk.Ui;
using NexoDesk.Ui.Controls;

namespace NexoDesk.Windows.Tests;

public class UiRenderTests
{
    [Fact]
    public void RemoteScreen_RendersLightAndDarkWithoutOpeningAccess()
    {
        Exception? failure = null;
        string stage = "criação do aplicativo";
        var thread = new Thread(() =>
        {
            try
            {
                var app = new NexoDesk.App.App();
                stage = "recursos do aplicativo";
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                string output = Path.Combine(WindowsIntegrationTests.FindRoot(), "artifacts", "ui");
                Directory.CreateDirectory(output);
                foreach (NxThemeMode theme in new[] { NxThemeMode.Light, NxThemeMode.Dark })
                {
                    // Carrega os recursos reais sem assinar SystemEvents nem consultar preferências do usuário.
                    stage = "recursos de tema " + theme;
                    var resources = app.Resources.MergedDictionaries;
                    resources.Clear();
                    foreach (string fileName in new[] { "Brand.xaml", theme + ".xaml", "Controls.xaml" })
                        resources.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/NexoDesk.Ui;component/Themes/" + fileName) });
                    stage = "criação da tela " + theme;
                    var view = new RemoteView { DataContext = new PreviewRemote(), Width = 1100, Height = 1150 };
                    var border = new Border { Child = view, Background = (Brush)app.FindResource("Nx.Bg") };
                    stage = "layout " + theme;
                    border.Measure(new Size(1100, 1150));
                    border.Arrange(new Rect(0, 0, 1100, 1150));
                    border.UpdateLayout();

                    stage = "renderização " + theme;
                    var image = new RenderTargetBitmap(1100, 1150, 96, 96, PixelFormats.Pbgra32);
                    image.Render(border);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(image));
                    using var file = File.Create(Path.Combine(output, $"remote-{theme.ToString().ToLowerInvariant()}.png"));
                    encoder.Save(file);
                    Assert.True(file.Length > 10_000);
                }
                stage = "configurações";
                var settings = new SettingsWindow(null!) { DataContext = new PreviewSettings { IsAdmin = true }, Height = 1000, MaxHeight = 1000 };
                var settingsContent = (FrameworkElement)settings.Content;
                settings.Content = null;
                settingsContent.DataContext = new PreviewSettings { IsAdmin = true };
                var settingsBorder = new Border { Child = settingsContent, Background = (Brush)app.FindResource("Nx.Bg") };
                System.Windows.Documents.TextElement.SetForeground(settingsBorder, (Brush)app.FindResource("Nx.Text"));
                settingsBorder.Measure(new Size(640, 1000));
                settingsBorder.Arrange(new Rect(0, 0, 640, 1000));
                settingsBorder.UpdateLayout();
                var settingsImage = new RenderTargetBitmap(640, 1000, 96, 96, PixelFormats.Pbgra32);
                settingsImage.Render(settingsBorder);
                var settingsEncoder = new PngBitmapEncoder();
                settingsEncoder.Frames.Add(BitmapFrame.Create(settingsImage));
                using (var settingsFile = File.Create(Path.Combine(output, "settings-dark.png"))) settingsEncoder.Save(settingsFile);
                settings.Close();

                stage = "diálogos e atualização";
                FrameworkElement RenderContent(Window window, object? data, string fileName, double width, double height)
                {
                    var content = (FrameworkElement)window.Content;
                    window.Content = null;
                    content.DataContext = data;
                    var root = new Border { Child = content, Background = (Brush)app.FindResource("Nx.Bg") };
                    System.Windows.Documents.TextElement.SetForeground(root, (Brush)app.FindResource("Nx.Text"));
                    root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                    var rendered = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                    rendered.Render(root);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(rendered));
                    using var target = File.Create(Path.Combine(output, fileName)); png.Save(target);
                    return content;
                }
                foreach (NxThemeMode theme in new[] { NxThemeMode.Light, NxThemeMode.Dark })
                {
                    app.Resources.MergedDictionaries[1] = new ResourceDictionary
                    { Source = new Uri("pack://application:,,,/NexoDesk.Ui;component/Themes/" + theme + ".xaml") };
                    string suffix = theme.ToString().ToLowerInvariant();
                    var imageFit = new ImageFitViewModel(new AdminSettings(), (_, _) => { });
                    var imageOptions = new ImageFitOptions { DataContext = imageFit, Width = 314 };
                    var optionsRoot = new Border { Child = imageOptions, Padding = new Thickness(18), Width = 350, Background = (Brush)app.FindResource("Nx.Surface") };
                    optionsRoot.Measure(new Size(350, 240)); optionsRoot.Arrange(new Rect(0, 0, 350, 240)); optionsRoot.UpdateLayout();
                    var optionsImage = new RenderTargetBitmap(350, 240, 96, 96, PixelFormats.Pbgra32); optionsImage.Render(optionsRoot);
                    var optionsPng = new PngBitmapEncoder(); optionsPng.Frames.Add(BitmapFrame.Create(optionsImage));
                    using (var file = File.Create(Path.Combine(output, $"sharpness-{suffix}.png"))) optionsPng.Save(file);
                    var slider = (Slider)imageOptions.FindName("SharpnessSlider");
                    slider.Value = 80; Assert.Equal(80, imageFit.SharpnessPercent);
                    imageFit.ResetCommand.Execute(null); Assert.Equal(35, slider.Value);
                    imageFit.Flush();
                    stage = "viewer sobreposto " + suffix;
                    var viewer = new SessionWindow();
                    Assert.False(viewer.ShowTitleBar); Assert.False(viewer.ShowFooter); Assert.False(viewer.IsFullScreen);
                    Assert.Equal(WindowState.Maximized, viewer.WindowState); Assert.False(viewer.Topmost);
                    Assert.Equal(0, System.Windows.Shell.WindowChrome.GetWindowChrome(viewer).CaptionHeight);
                    var sessionPreview = new PreviewSession();
                    var image = (AdaptiveDesktopImage)viewer.FindName("RemoteImage");
                    image.ActualSize = true; image.Width = 1280; image.Height = 720;
                    Assert.True(((System.Windows.Controls.Primitives.ToggleButton)viewer.FindName("ActualScale")).IsChecked);
                    var sessionContent = (Grid)RenderContent(viewer, sessionPreview, $"session-overlay-{suffix}.png", 1280, 720);
                    Assert.Empty(sessionContent.RowDefinitions); Assert.Equal(720, sessionContent.ActualHeight);
                    Assert.Equal(1280, image.Layout.PixelWidth); Assert.Equal(720, image.Layout.PixelHeight);
                    var overlay = (Grid)viewer.FindName("ControlsOverlay");
                    Assert.InRange(overlay.ActualHeight, 25, 80);
                    viewer.HideControls(); sessionContent.UpdateLayout();
                    Assert.Equal(Visibility.Visible, ((Button)viewer.FindName("CollapsedControls")).Visibility);
                    Assert.Equal(720, image.Layout.PixelHeight); // recolher não muda escala/área útil
                    var collapsedImage = new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32); collapsedImage.Render(sessionContent);
                    var collapsedPng = new PngBitmapEncoder(); collapsedPng.Frames.Add(BitmapFrame.Create(collapsedImage));
                    using (var file = File.Create(Path.Combine(output, $"session-collapsed-{suffix}.png"))) collapsedPng.Save(file);
                    viewer.ShowControls(); sessionContent.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, ((Button)viewer.FindName("CollapsedControls")).Visibility);
                    // Mesmo monitor, mas a barra local retira 40 px: priorizar a base remota sem reduzir os pixels.
                    var viewport = (ScrollViewer)viewer.FindName("VideoScroll");
                    viewport.HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    viewport.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
                    var viewportHost = (Border)sessionContent.Parent;
                    viewportHost.Measure(new Size(1280,680)); viewportHost.Arrange(new Rect(0,0,1280,680)); viewportHost.UpdateLayout();
                    Assert.Equal(680, viewport.ViewportHeight);
                    Assert.Equal(40, viewport.ScrollableHeight);
                    viewer.PositionRemoteBottom();
                    Assert.Equal(40, viewport.VerticalOffset, 2);
                    Assert.Equal(720, image.Layout.PixelHeight);
                    Assert.Equal(1280, image.Layout.PixelWidth);
                    Assert.True(image.Layout.TryNormalize(viewport.TranslatePoint(new Point(600,679),image), out _, out double bottomY));
                    Assert.True(bottomY > 0.99); // último pixel/base acessível, incluindo a barra remota
                    var bottomImage = new RenderTargetBitmap(1280,680,96,96,PixelFormats.Pbgra32); bottomImage.Render(sessionContent);
                    var bottomPng = new PngBitmapEncoder(); bottomPng.Frames.Add(BitmapFrame.Create(bottomImage));
                    using (var file = File.Create(Path.Combine(output, $"session-taskbar-{suffix}.png"))) bottomPng.Save(file);
                    viewer.Close();
                    stage = "diálogos " + suffix;
                    var pair = new AddComputerWindow();
                    RenderContent(pair, new PreviewPairing(), $"pairing-{suffix}.png", 660, 550);
                    Assert.True(pair.AddressBox.ActualWidth >= 580);
                    Assert.True(pair.AddressBox.ActualHeight >= 92);
                    pair.Close();
                    var password = new PasswordWindow("Definir senha de acesso", true);
                    password.Entry.Masked.Password = "Senha de teste com espaços!";
                    password.Entry.SetRevealed(true);
                    Assert.Equal("Senha de teste com espaços!", password.Entry.Password);
                    Assert.Empty(password.Entry.Masked.Password);
                    password.Entry.Revealed.Text += "123";
                    password.Entry.SetRevealed(false);
                    Assert.Equal("Senha de teste com espaços!123", password.Entry.Password);
                    Assert.Empty(password.Entry.Revealed.Text);
                    RenderContent(password, null, $"password-{suffix}.png", 560, 490);
                    Assert.True(password.Entry.Masked.ActualHeight >= 46);
                    password.Entry.Clear(); Assert.Empty(password.Entry.Password);
                    password.Close();
                    var update = new PreviewUpdate();
                    var shell = new PreviewShell(update);
                    var main = new MainWindow();
                    var dock = (DockPanel)RenderContent(main, shell, $"main-current-{suffix}.png", 1080, 300);
                    var banner = (Border)dock.Children[0];
                    Assert.Equal(Visibility.Collapsed, banner.Visibility);
                    update.Available = true;
                    dock.DataContext = null; dock.DataContext = shell; dock.UpdateLayout();
                    Assert.Equal(Visibility.Visible, banner.Visibility);
                    Assert.True(banner.ActualHeight > 0);
                    main.Close();
                }

                stage = "moldura da tela cheia";
                var chromeResources = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/NexoDesk.Ui;component/Themes/Generic.xaml")
                };
                var windowStyle = (Style)chromeResources[typeof(NxWindow)];
                var template = (ControlTemplate)windowStyle.Setters.OfType<Setter>().Single(s => s.Property == Control.TemplateProperty).Value;
                foreach (bool fullScreen in new[] { false, true })
                {
                    var preview = new NxWindow
                    {
                        Title = "PC de testes",
                        IsFullScreen = fullScreen,
                        Background = (Brush)app.FindResource("Nx.Bg"),
                        Content = new Border { Background = Brushes.DarkSlateBlue }
                    };
                    var visual = (Border)template.LoadContent();
                    // TemplateBinding/RelativeSource resolvem no controle real ao aplicar Template.
                    preview.Template = template;
                    preview.ApplyTemplate();
                    var titleRow = (RowDefinition)template.FindName("CaptionRow", preview);
                    var footer = (Border)template.FindName("Footer", preview);
                    Assert.Equal(fullScreen ? 0 : 36, titleRow.Height.Value);
                    Assert.Equal(fullScreen ? Visibility.Collapsed : Visibility.Visible, footer.Visibility);
                    preview.Close();
                }

                stage = "moldura sem barras";
                var noBars = new NxWindow { ShowTitleBar = false, ShowFooter = false, Template = template };
                noBars.ApplyTemplate();
                Assert.Equal(0, ((RowDefinition)template.FindName("CaptionRow", noBars)).Height.Value);
                Assert.Equal(0, ((RowDefinition)template.FindName("AccentRow", noBars)).Height.Value);
                Assert.Equal(Visibility.Collapsed, ((Border)template.FindName("Footer", noBars)).Visibility);
                Assert.False(noBars.IsFullScreen); noBars.Close();
                stage = "encerramento";
                app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"Renderização não terminou: {stage}.");
        if (failure is not null) throw new InvalidOperationException($"Falha em {stage}.", failure);
    }

    public sealed class PreviewSession
    {
        public string Title => "PC de trabalho";
        public string ServerName => Title;
        public string ControlStatus => "Você controla";
        public string ConnectionDetail => "Controlar · TLS 1.3 · Latência 8 ms · 30 FPS · Agente 1.7.0";
        public string DisplayText => "1280 × 720";
        public bool ShowWaiting => false;
        public bool ViewOnly => false;
        public bool IsEnded => false;
        public ImageFitViewModel ImageFit { get; } = new(new AdminSettings(), (_, _) => { });
        public BitmapSource Frame { get; } = CreateFrame();
        private static BitmapSource CreateFrame()
        {
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                dc.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(0,0,1280,720));
                for (int i = 0; i < 16; i++)
                    dc.DrawText(new FormattedText($"Linha {i+1:00}   Pedido 1039   R$ 1.234,56   Texto em tamanho real",
                        System.Globalization.CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 16, Brushes.Black, 1), new Point(30,85+i*31));
                dc.DrawRectangle(Brushes.DarkSlateGray, null, new Rect(0,680,1280,40));
            }
            var frame = new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32); frame.Render(drawing); frame.Freeze(); return frame;
        }
    }

    public sealed class PreviewSettings
    {
        public bool IsAdmin { get; set; }
        public ImageFitViewModel ImageFit { get; } = new(new AdminSettings(), (_, _) => { });
        public bool SimultaneousAccessEnabled { get; set; } = true;
        public string DeviceName { get; set; } = "PC de testes";
        public string RoleText => "PC REMOTO — pode ser acessado pelo ADMIN";
        public string SwitchRoleText => "Mudar para ADMIN";
        public bool StableTransmission { get; set; } = true;
        public bool ExperimentalTransmission => false;
        public bool ExperimentalTransmissionAvailable => false;
        public string TransmissionHint => "Estável mantém a transmissão atual sem perda. O modo experimental será liberado em uma próxima versão.";
        public bool ThemeAuto => false;
        public bool ThemeLight => false;
        public bool ThemeDark => true;
        public string Fingerprint => "AAAA BBBB CCCC…";
        public string FullFingerprint => new string('A', 64);
        public string ValidityText => "válida até 14/10/2027";
        public string Version => "1.6.0";
        public PreviewUpdate Update { get; } = new();
    }

    public sealed class PreviewUpdate
    {
        public string Status => "Versão 1.4.0 atualizada.";
        public bool Available { get; set; }
        public string Heading => "Atualização disponível: versão 1.4.0";
        public string ActionText => "Atualizar";
    }

    public sealed class PreviewShell(PreviewUpdate update)
    {
        public PreviewUpdate Update { get; } = update;
        public Border Content { get; } = new() { Child = new TextBlock { Text = "PCs REMOTOS", Margin = new Thickness(28) } };
    }
    public sealed class PreviewPairing
    {
        public string AccessCode { get; set; } = "NXD1-MTAwLjEwMS4xMDIuMTAzfDQ3MTE1fEFCQ0RFRkdISUpLTA";
        public string Name { get; set; } = "Computador da recepção";
        public string CancelText => "Cancelar";
        public string StatusText => "Pronto para parear.";
        public bool IsBusy => false;
    }

    public sealed class PreviewRemote
    {
        public string StatusText => "Serviço disponível";
        public NexoDesk.Ui.Controls.BadgeKind StatusKind => NexoDesk.Ui.Controls.BadgeKind.Success;
        public bool HasProblem => false;
        public bool HasSession => false;
        public string Address => "100.101.102.103";
        public string IpText => Address;
        public string TailscaleText => "Tailscale";
        public bool PairingActive => false;
        public bool HasAdmins => true;
        public AdminRow[] Admins => [new("Notebook Gustavo", new string('A',64), "AAAA AAAA AAAA…", "13/10/2026 10:00")];
        public NetworkChoice[] NetworkModes => [new(NetworkMode.Tailscale, "Tailscale (internet)"), new(NetworkMode.LocalNetwork, "Rede local")];
        public NetworkMode SelectedNetworkMode { get; set; }
        public string[] LocalAddresses => ["192.168.1.10"];
        public string SelectedLocalAddress { get; set; } = "192.168.1.10";
        public bool StartWithWindows { get; set; } = true;
        public bool UnattendedAccess { get; set; } = true;
        public string PasswordStatus => "Senha definida. Exigida em cada conexão.";
        public LogLine[] Log => [new(DateTime.Now, LogLevel.Ok, "Serviço pronto para o ADMIN pareado.")];
    }
}