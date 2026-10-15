using System.Security.Cryptography.X509Certificates;
using NexoDesk.Desktop;
using NexoDesk.Shared.Configuration;
using NexoDesk.Shared.Security;

namespace NexoDesk.App.Services;

public sealed class AppState
{
    private readonly MachineState _machine;
    private ViewModels.ImageFitViewModel? _imageFit;
    public ViewModels.ImageFitViewModel ImageFit => _imageFit ??= new(Config.Admin, (enabled, sharpness) =>
        Update(c => { c.Admin.EnhancedWindowFit = enabled; c.Admin.ImageSharpnessPercent = sharpness; }));
    public void FlushImagePreferences() => _imageFit?.Flush();
    private AppState(MachineState machine) => _machine = machine;
    public AppConfig Config => _machine.Config;
    public X509Certificate2 Certificate => _machine.Certificate;
    public bool IdentityRecreated => false;
    public string Fingerprint => CertificateFingerprint.Sha256(Certificate);
    public bool IsFirstRun => Config.Role is null;
    public event Action? Changed;
    public static AppState Load() => new(new MachineState(provision: true));
    public void Reload() => _machine.Reload();
    public void Update(Action<AppConfig> change) { _machine.Update(change); Changed?.Invoke(); }
}