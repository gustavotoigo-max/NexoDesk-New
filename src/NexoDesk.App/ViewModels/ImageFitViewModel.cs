using System.Windows.Input;
using System.Windows.Threading;
using NexoDesk.Shared.Configuration;
using NexoDesk.Ui;

namespace NexoDesk.App.ViewModels;

/// <summary>Preferência compartilhada por todas as sessões e configurações do ADMIN.
/// A prévia é imediata; grava somente após uma pausa no ajuste ou ao fechar/sair.</summary>
public sealed class ImageFitViewModel : ObservableObject
{
    private readonly Action<bool, int> _save;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool _enabled, _dirty;
    private int _sharpnessPercent;

    public ImageFitViewModel(AdminSettings initial, Action<bool, int> save)
    {
        _enabled = initial.EnhancedWindowFit;
        _sharpnessPercent = initial.ImageSharpnessPercent;
        _save = save;
        _saveTimer.Tick += (_, _) => Flush();
        ResetCommand = new RelayCommand(() =>
        {
            EnhancedWindowFit = true;
            SharpnessPercent = AdminSettings.DefaultImageSharpnessPercent;
        });
    }
    public ICommand ResetCommand { get; }
    public bool EnhancedWindowFit
    {
        get => _enabled;
        set { if (Set(ref _enabled, value)) ScheduleSave(); }
    }
    public int SharpnessPercent
    {
        get => _sharpnessPercent;
        set { if (Set(ref _sharpnessPercent, Math.Clamp(value, 0, 100))) ScheduleSave(); }
    }
    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer.Stop(); _saveTimer.Start();
    }
    public void Flush()
    {
        _saveTimer.Stop();
        if (!_dirty) return;
        _save(_enabled, _sharpnessPercent);
        _dirty = false;
    }
}
