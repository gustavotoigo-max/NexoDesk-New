using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace NexoDesk.App.Services;

/// <summary>Somente executáveis conhecidos no diretório desta instalação podem ser encerrados.</summary>
internal static class InstallationProcesses
{
    internal static bool Owns(string root, string executable, bool includeInterfaces)
    {
        string path = Path.GetFullPath(executable);
        return string.Equals(path, Path.Combine(Path.GetFullPath(root), "service", "NexoDesk.Service.exe"), StringComparison.OrdinalIgnoreCase) ||
            includeInterfaces && string.Equals(path, Path.Combine(Path.GetFullPath(root), "NexoDesk New.exe"), StringComparison.OrdinalIgnoreCase);
    }

    private static List<Process> Find(string root, bool includeInterfaces, Func<string, Process[]>? enumerate = null)
    {
        var found = new List<Process>();
        foreach (string name in includeInterfaces ? new[] { "NexoDesk.Service", "NexoDesk New" } : new[] { "NexoDesk.Service" })
        {
            foreach (Process process in (enumerate ?? Process.GetProcessesByName)(name))
            {
                try
                {
                    if (process.Id != Environment.ProcessId && ExecutablePath(process.Id) is { } path && Owns(root, path, includeInterfaces))
                    { found.Add(process); continue; }
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception ex)
                {
                    process.Dispose();
                    foreach (var item in found) item.Dispose();
                    throw new IOException("Não foi possível conferir um processo NexoDesk. Tente sair novamente como administrador.", ex);
                }
                process.Dispose();
            }
        }
        return found;
    }

    private static string? ExecutablePath(int pid)
    {
        // PROCESS_QUERY_LIMITED_INFORMATION evita pedir leitura da memória de agentes SYSTEM.
        using SafeProcessHandle handle = OpenProcess(0x1000, false, pid);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 87) return null; // O processo terminou entre enumeração e consulta.
            throw new Win32Exception(error);
        }
        return ExecutablePath(handle);
    }

    private static string ExecutablePath(SafeProcessHandle handle)
    {
        var path = new StringBuilder(32768);
        int length = path.Capacity;
        if (!QueryFullProcessImageName(handle, 0, path, ref length)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return path.ToString();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle handle, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint timeout);

    internal static async Task<bool> WaitForServicesAsync(string root, TimeSpan timeout, Func<string, Process[]>? enumerate = null)
    {
        long start = Stopwatch.GetTimestamp();
        do
        {
            var processes = Find(root, false, enumerate);
            bool gone = processes.Count == 0;
            foreach (var process in processes) process.Dispose();
            if (gone) return true;
            await Task.Delay(200).ConfigureAwait(false);
        } while (Stopwatch.GetElapsedTime(start) < timeout);
        return false;
    }

    internal static async Task TerminateAsync(string root, bool includeInterfaces, Func<string, Process[]>? enumerate = null)
    {
        // Não usa /IM nem Kill(entireProcessTree): um processo de outro produto, inclusive Tailscale,
        // nunca entra na lista, mesmo que seja descendente da interface.
        var processes = Find(root, includeInterfaces, enumerate);
        var handles = new List<SafeProcessHandle>();
        try
        {
            foreach (Process process in processes)
            {
                // Consulta e encerramento usam o MESMO handle: um PID reutilizado não pode
                // fazer a limpeza atingir outro executável entre a conferência e a parada.
                SafeProcessHandle handle = OpenProcess(0x00101001, false, process.Id);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error(); handle.Dispose();
                    if (error == 87) continue;
                    throw new IOException("Um processo antigo do NexoDesk não pôde ser encerrado.", new Win32Exception(error));
                }
                handles.Add(handle);
                if (!Owns(root, ExecutablePath(handle), includeInterfaces)) { handles.Remove(handle); handle.Dispose(); continue; }
                if (!TerminateProcess(handle, 0))
                {
                    int error = Marshal.GetLastWin32Error();
                    // Matar o supervisor pode já estar encerrando este agente pelo Job Object.
                    if (await Task.Run(() => WaitForSingleObject(handle, 5000)).ConfigureAwait(false) != 0)
                        throw new IOException("Um processo antigo do NexoDesk não pôde ser encerrado.", new Win32Exception(error));
                }
            }
            // Todas as esperas são limitadas e fora da thread da interface.
            uint[] results = await Task.WhenAll(handles.Select(handle => Task.Run(() => WaitForSingleObject(handle, 5000)))).ConfigureAwait(false);
            if (results.Any(result => result != 0)) throw new IOException("Um processo antigo do NexoDesk ainda está em execução.");
        }
        finally
        {
            foreach (var handle in handles) handle.Dispose();
            foreach (Process process in processes) process.Dispose();
        }
        var remaining = Find(root, includeInterfaces, enumerate);
        try { if (remaining.Count > 0) throw new IOException("Ainda existem processos desta instalação em execução."); }
        finally { foreach (var process in remaining) process.Dispose(); }
    }
}
