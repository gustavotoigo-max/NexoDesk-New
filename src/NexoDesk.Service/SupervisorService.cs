using System.Diagnostics;
using System.ServiceProcess;
using NexoDesk.Desktop;

namespace NexoDesk.Service;

/// <summary>Session 0 supervisiona um agente sem interface na sessão física ativa.</summary>
public sealed class SupervisorService : ServiceBase
{
    public const string Name = "NexoDeskRemote";
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;
    private WorkerLifetime? _worker;
    private uint _session = uint.MaxValue;

    public SupervisorService() { ServiceName = Name; CanStop = true; CanShutdown = true; AutoLog = false; }
    protected override void OnStart(string[] args) => _loop = Task.Run(SuperviseAsync);
    protected override void OnShutdown() => OnStop();
    protected override void OnStop()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(10)); } catch (AggregateException) { }
        StopWorker();
    }

    private async Task SuperviseAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                uint active = SessionLauncher.ActiveConsoleSession();
                if (active != _session || _worker is { Process.HasExited: true })
                {
                    StopWorker();
                    _session = active;
                }
                if (active != uint.MaxValue && _worker is null)
                {
                    try { _worker = SessionLauncher.StartWorker(active); }
                    catch (Exception ex) { WriteFailure(ex); }
                }
                await Task.Delay(TimeSpan.FromSeconds(3), _stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        finally { StopWorker(); }
    }

    private void StopWorker()
    {
        // A parada do serviço e o finally do supervisor podem concorrer.
        // Transfere a posse antes da espera para encerrar cada agente uma única vez.
        WorkerLifetime? worker = Interlocked.Exchange(ref _worker, null);
        if (worker is null) return;
        try
        {
            if (!worker.Process.HasExited)
            {
                try { ServiceProtocol.RequestAsync("shutdown").Wait(TimeSpan.FromSeconds(4)); }
                catch (AggregateException) { }
                if (!worker.Process.WaitForExit(5000))
                {
                    worker.Process.Kill(entireProcessTree: true);
                    worker.Process.WaitForExit(5000);
                }
            }
        }
        catch (InvalidOperationException) { }
        finally { worker.Dispose(); }
    }

    private static void WriteFailure(Exception ex)
    {
        try
        {
            MachineState.SecureDirectory();
            File.AppendAllText(Path.Combine(MachineState.DataDirectory, "service-errors.log"),
                $"{DateTimeOffset.UtcNow:O} {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
        }
        catch (IOException) { }
    }
}