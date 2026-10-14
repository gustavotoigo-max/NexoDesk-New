using System.Windows;
using System.Windows.Controls;
using NexoDesk.App.Controls;
using NexoDesk.Ui.Controls;

namespace NexoDesk.App.Views;

/// <summary>Entrada de senha local ao diálogo, sem bindings, logs ou persistência em texto.</summary>
public sealed class PasswordWindow : NxWindow
{
    internal readonly PasswordEntry Entry = new();
    internal readonly PasswordEntry? Confirmation;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    public string? Password { get; private set; }

    internal PasswordWindow(string title, bool creating)
    {
        Title = title; Width = 560; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        CanMinimize = false; CanMaximize = false; ShowFooter = false; ShowInTaskbar = false;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(28, 24, 28, 24) };
        var heading = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap };
        heading.SetResourceReference(StyleProperty, "Nx.PageTitle"); panel.Children.Add(heading);
        var description = new TextBlock { Text = creating
            ? "Use de 12 a 128 caracteres. A senha autoriza os ADMINs pareados, inclusive com a tela bloqueada."
            : "Informe a senha do PC REMOTO. Ela será salva com a proteção do Windows nesta conta.", Margin = new Thickness(0, 8, 0, 24) };
        description.SetResourceReference(StyleProperty, "Nx.Body"); panel.Children.Add(description);
        AddLabel(panel, "Senha"); panel.Children.Add(Entry);
        if (creating)
        {
            Confirmation = new PasswordEntry { Margin = new Thickness(0) };
            AddLabel(panel, "Confirmar senha", 20); panel.Children.Add(Confirmation);
        }
        _error.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Nx.Danger");
        panel.Children.Add(_error);
        var footer = new Border { Margin = new Thickness(0, 12, 0, 0) };
        footer.SetResourceReference(StyleProperty, "Nx.Section");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) };
        cancel.SetResourceReference(StyleProperty, "Nx.Button");
        var save = new Button { Content = creating ? "Definir senha" : "Conectar", IsDefault = true };
        save.SetResourceReference(StyleProperty, "Nx.Button.Primary");
        save.Click += (_, _) =>
        {
            string value = Entry.Password;
            if (value.Length is < 12 or > 128 || value.All(char.IsWhiteSpace)) { _error.Text = "Use de 12 a 128 caracteres."; return; }
            if (Confirmation is not null && value != Confirmation.Password) { _error.Text = "As senhas não coincidem."; return; }
            Password = value; DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save); footer.Child = buttons; panel.Children.Add(footer);
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Loaded += (_, _) => Entry.FocusInput();
        Closed += (_, _) => { Entry.Clear(); Confirmation?.Clear(); };
    }

    private static void AddLabel(Panel panel, string text, double top = 0)
    {
        var label = new TextBlock { Text = text, Margin = new Thickness(0, top, 0, 8) };
        label.SetResourceReference(StyleProperty, "Nx.FieldLabel"); panel.Children.Add(label);
    }

    public static string? Ask(string title, bool creating = false)
    {
        var window = new PasswordWindow(title, creating) { Owner = Application.Current.MainWindow };
        bool accepted = window.ShowDialog() == true;
        string? value = accepted ? window.Password : null;
        window.Password = null;
        return value;
    }
}
