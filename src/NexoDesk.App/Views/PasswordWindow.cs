using System.Windows;
using System.Windows.Controls;
using NexoDesk.Ui.Controls;

namespace NexoDesk.App.Views;

/// <summary>Entrada de senha mascarada; não participa de bindings, logs ou configurações em texto.</summary>
public sealed class PasswordWindow : NxWindow
{
    private readonly PasswordBox _password = new() { MinWidth = 300, Margin = new Thickness(0, 12, 0, 8) };
    private readonly PasswordBox? _confirm;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };
    public string? Password { get; private set; }

    private PasswordWindow(string title, bool creating)
    {
        Title = title; Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = creating ? "Use de 12 a 128 caracteres. Esta senha autoriza os ADMINs pareados, inclusive com a tela bloqueada."
            : "Informe a senha configurada no PC REMOTO. Ela será salva com a proteção do Windows nesta conta.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
        _password.SetResourceReference(Control.ForegroundProperty, "Nx.Text");
        _password.SetResourceReference(Control.BackgroundProperty, "Nx.SurfaceAlt");
        _password.SetResourceReference(Control.BorderBrushProperty, "Nx.Border");
        panel.Children.Add(_password);
        if (creating)
        {
            panel.Children.Add(new TextBlock { Text = "Confirme a senha:" });
            _confirm = new PasswordBox { Margin = new Thickness(0, 8, 0, 8) };
            _confirm.SetResourceReference(Control.ForegroundProperty, "Nx.Text");
            _confirm.SetResourceReference(Control.BackgroundProperty, "Nx.SurfaceAlt");
            _confirm.SetResourceReference(Control.BorderBrushProperty, "Nx.Border");
            panel.Children.Add(_confirm);
        }
        panel.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = creating ? "Definir senha" : "Conectar", IsDefault = true, Padding = new Thickness(14, 7, 14, 7) };
        save.Click += (_, _) =>
        {
            string value = _password.Password;
            if (value.Length is < 12 or > 128 || value.All(char.IsWhiteSpace)) { _error.Text = "Use de 12 a 128 caracteres."; return; }
            if (_confirm is not null && value != _confirm.Password) { _error.Text = "As senhas não coincidem."; return; }
            Password = value;
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); panel.Children.Add(buttons); Content = panel;
        Loaded += (_, _) => _password.Focus();
    }

    public static string? Ask(string title, bool creating = false)
    {
        var window = new PasswordWindow(title, creating) { Owner = Application.Current.MainWindow };
        return window.ShowDialog() == true ? window.Password : null;
    }
}