using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Principal;
using System.Xml.Linq;
using NexoDesk.Desktop;

namespace NexoDesk.App.Services;

public static class ServiceControl
{
    private const string ServiceName = "NexoDeskRemote";
    private const string TaskName = "NexoDesk-New.Interface";
    private static readonly ConcurrentDictionary<int, Process> Commands = new();
    private static bool _stopping;

    public static async Task EnsureStartedAsync()
    {
        var query = await RunAsync("sc.exe", ["query", ServiceName]);
        if (query.Code != 0) throw new IOException("Instale o NexoDesk New com o instalador para registrar o serviço Windows.");
        await ConfigureRecoveryAsync(true);
        var start = await RunAsync("sc.exe", ["start", ServiceName]);
        if (start.Code is not 0 and not 1056) throw new IOException("O serviço Windows não iniciou. Consulte o registro do serviço.");
    }

    public static async Task ConfigureStartupAsync(bool enabled)
    {
        var query = await RunAsync("sc.exe", ["query", ServiceName]);
        if (query.Code == 0)
        {
            var result = await RunAsync("sc.exe", ["config", ServiceName, "start=", enabled ? "auto" : "demand"]);
            if (result.Code != 0) throw new IOException("O Windows não permitiu configurar o início automático do serviço.");
        }
        if (enabled)
        {
            string path = Path.Combine(MachineState.DataDirectory, "interface-task-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                CreateTaskXml().Save(path);
                var result = await RunAsync("schtasks.exe", ["/Create", "/TN", TaskName, "/XML", path, "/F"]);
                if (result.Code != 0) throw new IOException("O Windows não permitiu registrar a interface no logon.");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        else
        {
            var task = await RunAsync("schtasks.exe", ["/Query", "/TN", TaskName]);
            if (task.Code == 0)
            {
                var result = await RunAsync("schtasks.exe", ["/Delete", "/TN", TaskName, "/F"]);
                if (result.Code != 0) throw new IOException("O Windows não permitiu remover o início automático da interface.");
            }
        }
    }

    // InteractiveToken não pede nem armazena a senha Windows; HighestAvailable usa a conta administrativa atual.
    internal static XDocument CreateTaskXml()
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        string sid = WindowsIdentity.GetCurrent().User!.Value;
        XElement E(string name, object content) => new(ns + name, content);
        return new XDocument(new XDeclaration("1.0", "utf-16", null), new XElement(ns + "Task", new XAttribute("version", "1.4"),
            E("Triggers", E("LogonTrigger", new[] { E("Enabled", "true"), E("UserId", sid) })),
            E("Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"), E("UserId", sid),
                E("LogonType", "InteractiveToken"), E("RunLevel", "HighestAvailable"))),
            E("Settings", new[] { E("MultipleInstancesPolicy", "IgnoreNew"), E("DisallowStartIfOnBatteries", "false"),
                E("StopIfGoingOnBatteries", "false"), E("StartWhenAvailable", "true"), E("Enabled", "true"), E("ExecutionTimeLimit", "PT0S") }),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"), E("Exec", new[] {
                E("Command", AppPaths.ExecutablePath), E("Arguments", "--minimized") }))));
    }

    public static async Task StopAsync(bool includeInterfaces = true)
    {
        _stopping = true;
        try
        {
            // Encerra também comandos de configuração pendentes criados por esta interface.
            foreach (Process command in Commands.Values)
                try { command.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
            string root = Path.GetDirectoryName(AppPaths.ExecutablePath)!;
            var result = await RunAsync("sc.exe", ["stop", ServiceName], duringStop: true);
            if (result.Code is not 0 and not 1062 and not 1060 and not 1061)
                throw new IOException("Não foi possível parar o serviço Windows.");
            if (!await InstallationProcesses.WaitForServicesAsync(root, TimeSpan.FromSeconds(20)))
            {
                // Impede que a recuperação do SCM recrie o supervisor durante a limpeza forçada.
                await ConfigureRecoveryAsync(false, duringStop: true);
                try { await InstallationProcesses.TerminateAsync(root, includeInterfaces); }
                finally { await ConfigureRecoveryAsync(true, duringStop: true); }
            }
            else if (includeInterfaces)
                await InstallationProcesses.TerminateAsync(root, true);
        }
        finally { _stopping = false; }
    }

    private static async Task ConfigureRecoveryAsync(bool enabled, bool duringStop = false)
    {
        var result = await RunAsync("sc.exe", ["failure", ServiceName, "reset=", "86400", "actions=",
            enabled ? "restart/5000/restart/15000/restart/60000" : ""], duringStop);
        if (result.Code is not 0 and not 1060)
            throw new IOException("Não foi possível configurar a recuperação do serviço Windows.");
    }

    private static async Task<(int Code, string Output)> RunAsync(string executable, string[] args, bool duringStop = false)
    {
        if (_stopping && !duringStop) throw new IOException("O NexoDesk está encerrando os processos.");
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, executable)) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("Não foi possível executar " + executable);
        int pid = process.Id;
        Commands[pid] = process;
        try
        {
            process.StandardInput.Close();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                throw new IOException("O Windows demorou demais para concluir o comando " + executable + ".");
            }
            return (process.ExitCode, await output + await error);
        }
        finally { Commands.TryRemove(pid, out _); }
    }
}