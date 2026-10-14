using System.Diagnostics;
using System.IO;
using NexoDesk.App.Services;

namespace NexoDesk.Windows.Tests;

public class InstallationProcessesTests
{
    [Theory]
    [InlineData("service/NexoDesk.Service.exe", false, true)]
    [InlineData("NexoDesk New.exe", false, false)]
    [InlineData("NexoDesk New.exe", true, true)]
    [InlineData("service/tailscale.exe", true, false)]
    [InlineData("other/NexoDesk.Service.exe", true, false)]
    [InlineData("service/NexoDesk.Service.exe.old", true, false)]
    public void CleanupAcceptsOnlyKnownExecutablesAtExactInstallationPaths(string relative, bool interfaces, bool expected)
    {
        string root = Path.Combine(Path.GetTempPath(), "NexoDesk-scope");
        Assert.Equal(expected, InstallationProcesses.Owns(root, Path.Combine(root, relative), interfaces));
    }

    [Fact]
    public async Task CleanupKillsAnOrphanAtOwnedPathAndPreservesSameNameFromOtherInstallation()
    {
        string root = Path.Combine(Path.GetTempPath(), "NexoDesk-lifetime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "service"));
        Directory.CreateDirectory(Path.Combine(root, "other"));
        string command = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        string ownedPath = Path.Combine(root, "service", "NexoDesk.Service.exe");
        string otherPath = Path.Combine(root, "other", "NexoDesk.Service.exe");
        File.Copy(command, ownedPath); File.Copy(command, otherPath);
        Process Start(string path) => Process.Start(new ProcessStartInfo(path)
        {
            Arguments = "/d /q /k", UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!;
        using Process owned = Start(ownedPath);
        using Process other = Start(otherPath);
        Process[] Enumerate(string name) => name == "NexoDesk.Service"
            ? new[] { owned, other }.Where(p => !p.HasExited).Select(p => Process.GetProcessById(p.Id)).ToArray()
            : [];
        try
        {
            Assert.False(owned.HasExited); Assert.False(other.HasExited);
            Assert.False(await InstallationProcesses.WaitForServicesAsync(root, TimeSpan.Zero, Enumerate));
            await InstallationProcesses.TerminateAsync(root, true, Enumerate);
            Assert.True(owned.HasExited);
            Assert.False(other.HasExited);
            Assert.True(await InstallationProcesses.WaitForServicesAsync(root, TimeSpan.FromSeconds(1), Enumerate));
        }
        finally
        {
            foreach (Process process in new[] { owned, other })
                if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            owned.Dispose(); other.Dispose();
            File.SetAttributes(ownedPath, FileAttributes.Normal);
            File.SetAttributes(otherPath, FileAttributes.Normal);
            // O Windows/antivírus pode liberar a imagem alguns instantes após sinalizar a saída.
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Delete(root, true); break; }
                catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException)
                { await Task.Delay(100); }
            }
        }
    }
}
