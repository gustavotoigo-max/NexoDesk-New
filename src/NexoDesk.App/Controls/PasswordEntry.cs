using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace NexoDesk.App.Controls;

/// <summary>Senha local ao diálogo: o texto revelado é apagado ao ocultar e ao fechar.</summary>
internal sealed class PasswordEntry : Grid
{
    internal PasswordBox Masked { get; } = new() { MaxLength = 128 };
    internal TextBox Revealed { get; } = new() { MaxLength = 128, Visibility = Visibility.Collapsed, FontSize = 16, MinHeight = 46, Padding = new Thickness(12, 10, 12, 10) };
    internal Button Toggle { get; } = new() { Width = 44, Height = 46, MinWidth = 0, Padding = new Thickness(0), Margin = new Thickness(8, 0, 0, 0) };
    public bool IsRevealed => Revealed.Visibility == Visibility.Visible;
    public string Password => IsRevealed ? Revealed.Text : Masked.Password;

    public PasswordEntry()
    {
        ColumnDefinitions.Add(new ColumnDefinition());
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Masked.SetResourceReference(StyleProperty, "Nx.Password");
        Revealed.SetResourceReference(StyleProperty, "Nx.Input");
        Toggle.SetResourceReference(StyleProperty, "Nx.Button");
        Toggle.SetResourceReference(Control.FontFamilyProperty, "Nx.Icons");
        Toggle.FontSize = 18;
        SetColumn(Toggle, 1);
        Children.Add(Masked); Children.Add(Revealed); Children.Add(Toggle);
        Toggle.Click += (_, _) => SetRevealed(!IsRevealed);
        UpdateToggle();
    }

    internal void SetRevealed(bool show)
    {
        if (show == IsRevealed) return;
        if (show)
        {
            Revealed.Text = Masked.Password;
            Masked.Clear();
            Masked.Visibility = Visibility.Collapsed;
            Revealed.Visibility = Visibility.Visible;
            Revealed.Focus(); Revealed.CaretIndex = Revealed.Text.Length;
        }
        else
        {
            Masked.Password = Revealed.Text;
            Revealed.Clear();
            Revealed.Visibility = Visibility.Collapsed;
            Masked.Visibility = Visibility.Visible;
            Masked.Focus();
        }
        UpdateToggle();
    }

    public void FocusInput() { if (IsRevealed) Revealed.Focus(); else Masked.Focus(); }
    public void Clear() { Masked.Clear(); Revealed.Clear(); SetRevealed(false); }
    private void UpdateToggle()
    {
        Toggle.Content = IsRevealed ? "\uED1A" : "\uE890";
        string label = IsRevealed ? "Ocultar senha" : "Exibir senha";
        Toggle.ToolTip = label;
        AutomationProperties.SetName(Toggle, label);
    }
}
