using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NexoDesk.App.ViewModels;
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
                var settings = new SettingsWindow(null!) { DataContext = new PreviewSettings(), Height = 1000, MaxHeight = 1000 };
                var settingsContent = (FrameworkElement)settings.Content;
                settings.Content = null;
                settingsContent.DataContext = new PreviewSettings();
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

    public sealed class PreviewSettings
    {
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
        public string Version => "1.2.0";
        public PreviewUpdate Update { get; } = new();
    }

    public sealed class PreviewUpdate
    {
        public string Status => "Versão 1.2.0 atualizada.";
        public bool Available => false;
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