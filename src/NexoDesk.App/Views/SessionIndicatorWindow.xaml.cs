using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NexoDesk.Shared.Messages;

namespace NexoDesk.App.Views;

/// <summary>Aviso inicial de dez segundos; fechá-lo não encerra o acesso.</summary>
public partial class SessionIndicatorWindow : Window
{
    private readonly Action _endSession;
    private readonly DispatcherTimer _dismissTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public SessionIndicatorWindow(Action endSession, NexoDesk.Desktop.ServiceStatus session)
    {
        InitializeComponent();
        _endSession = endSession;
        AdminRun.Text = "ADMIN " + session.ClientName;
        ModeRun.Text = session.Mode == SessionMode.Control ? "controlando" : "visualizando";

        _dismissTimer.Tick += (_, _) => Close();
        Loaded += (_, _) => { PlaceAtTopCenter(); _dismissTimer.Start(); };
        Closed += (_, _) => _dismissTimer.Stop();
        SizeChanged += (_, _) => PlaceAtTopCenter();
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && e.Source is not System.Windows.Controls.Button)
                DragMove();
        };
    }

    public void CloseForSessionEnd() => Close();
    private void OnHide(object sender, RoutedEventArgs e) => Close();

    private void OnEnd(object sender, RoutedEventArgs e)
    {
        EndButton.IsEnabled = false;
        EndButton.Content = "Encerrando…";
        _endSession();
    }

    private void PlaceAtTopCenter()
    {
        Rect area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top;
    }
}
