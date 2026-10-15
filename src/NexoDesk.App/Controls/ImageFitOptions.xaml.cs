using System.Windows.Controls;

namespace NexoDesk.App.Controls;

public partial class ImageFitOptions : UserControl
{
    public ImageFitOptions() => InitializeComponent();
    public void FocusSlider() => SharpnessSlider.Focus();
}
