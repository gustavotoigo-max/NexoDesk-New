using System.IO;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using NexoDesk.Desktop;
using NexoDesk.Shared.Messages;

namespace NexoDesk.Windows.Tests;

public class WindowsIntegrationTests
{
    [Fact]
    public void SavedPassword_IsProtectedByWindows()
    {
        const string password = "Senha-forte-2026!";
        string blob = PasswordVault.Protect(password);
        Assert.DoesNotContain(password, blob);
        Assert.Equal(password, PasswordVault.Unprotect(blob));
        Assert.Null(PasswordVault.Unprotect("danificado"));
    }

    [Fact]
    public async Task Ipc_RoundTripsBoundedStatus()
    {
        using var stream = new MemoryStream();
        var value = new ServiceStatus(true, null, "10.1.1.2", "123", "Admin", SessionMode.Control, null, DateTimeOffset.UtcNow, []);
        await ServiceProtocol.WriteAsync(stream, value, CancellationToken.None);
        stream.Position = 0;
        var restored = await ServiceProtocol.ReadAsync<ServiceStatus>(stream, CancellationToken.None);
        Assert.Equal(value.ClientName, restored.ClientName);
        Assert.Equal(value.Mode, restored.Mode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(65537)]
    [InlineData(int.MaxValue)]
    public async Task Ipc_RejectsLengthBeforeAllocating(int length)
    {
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<IOException>(() => ServiceProtocol.ReadAsync<ServiceRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Ipc_RejectsTruncatedData()
    {
        using var stream = new MemoryStream([4, 0, 0, 0, 123]);
        await Assert.ThrowsAsync<EndOfStreamException>(() => ServiceProtocol.ReadAsync<ServiceRequest>(stream, CancellationToken.None));
    }

    [Fact]
    public void Executables_RequireAdministrator()
    {
        string root = FindRoot();
        XNamespace ns = "urn:schemas-microsoft-com:asm.v3";
        foreach (string project in new[] { "NexoDesk.App", "NexoDesk.Service" })
        {
            var manifest = XDocument.Load(Path.Combine(root, "src", project, "app.manifest"));
            Assert.Equal("requireAdministrator", manifest.Descendants(ns + "requestedExecutionLevel").Single().Attribute("level")!.Value);
        }
    }

    [Fact]
    public void NativeStartupStructure_HasWindowsX64Layout()
    {
        var assembly = typeof(NexoDesk.Service.SupervisorService).Assembly;
        Type launcher = assembly.GetType("NexoDesk.Service.SessionLauncher")!;
        Type startup = launcher.GetNestedType("STARTUPINFO", System.Reflection.BindingFlags.NonPublic)!;
        Type process = launcher.GetNestedType("PROCESS_INFORMATION", System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(104, Marshal.SizeOf(startup));
        Assert.Equal(24, Marshal.SizeOf(process));
    }

    [Fact]
    public void LogonTask_UsesInteractiveElevatedTokenWithoutWindowsPassword()
    {
        Type type = typeof(NexoDesk.App.Services.ServiceControl);
        var method = type.GetMethod("CreateTaskXml", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var document = (XDocument)method.Invoke(null, null)!;
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        Assert.Equal("InteractiveToken", document.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal("HighestAvailable", document.Descendants(ns + "RunLevel").Single().Value);
        Assert.Empty(document.Descendants(ns + "Password"));
        Assert.Equal("--minimized", document.Descendants(ns + "Arguments").Single().Value);
    }

    [Fact]
    public void JobLimitStructure_HasNativeX64Layout()
    {
        Type type = typeof(NexoDesk.Service.SupervisorService).Assembly.GetType("NexoDesk.Service.WorkerLifetime")!;
        Type info = type.GetNestedType("EXTENDED_LIMIT_INFORMATION", System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(144, Marshal.SizeOf(info));
    }

    [Fact]
    public async Task FakeLocalPipeServer_IsRejected()
    {
        string name = "NexoDesk-Test-" + Guid.NewGuid().ToString("N");
        await using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        await using var client = new System.IO.Pipes.NamedPipeClientStream(".", name, System.IO.Pipes.PipeDirection.InOut,
            System.IO.Pipes.PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
        var validate = typeof(ServiceProtocol).GetMethod("ValidateServer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(() => validate.Invoke(null, [client]));
        Assert.IsType<UnauthorizedAccessException>(exception.InnerException);
    }
    internal static string FindRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "NexoDesk.sln"))) return directory.FullName;
        throw new DirectoryNotFoundException("Solução não encontrada.");
    }
}