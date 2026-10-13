using System.Buffers.Binary;
using System.IO.Pipes;
using System.Text.Json;
using NexoDesk.Shared.Messages;

namespace NexoDesk.Desktop;

public sealed record ServiceRequest(string Command);
public sealed record ServiceStatus(bool Ready, string? Problem, string Address, string? SessionId, string? ClientName,
    SessionMode? Mode, string? AccessCode, DateTimeOffset PairingExpiresAt, string[] Events);

/// <summary>IPC local limitado; o servidor concede acesso apenas a SYSTEM e administradores elevados.</summary>
public static class ServiceProtocol
{
    public const string PipeName = "NexoDesk-New.Control.v1";
    private const int Limit = 64 * 1024;

    public static async Task<ServiceStatus> RequestAsync(string command, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        ValidateServer(pipe);
        await WriteAsync(pipe, new ServiceRequest(command), timeout.Token).ConfigureAwait(false);
        return await ReadAsync<ServiceStatus>(pipe, timeout.Token).ConfigureAwait(false);
    }

    private static void ValidateServer(NamedPipeClientStream pipe)
    {
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out uint pid))
            throw new UnauthorizedAccessException("Servidor IPC não autenticado.");
        IntPtr process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) throw new UnauthorizedAccessException("Servidor IPC inacessível.");
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out token)) throw new UnauthorizedAccessException("Identidade IPC inacessível.");
            using var identity = new System.Security.Principal.WindowsIdentity(token);
            if (!identity.IsSystem) throw new UnauthorizedAccessException("O servidor IPC não é o agente SYSTEM.");
        }
        finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(process); }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint processId);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > Limit) throw new IOException("Resposta IPC excede o limite.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is < 1 or > Limit) throw new IOException("Comprimento IPC inválido.");
        byte[] bytes = new byte[length];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new IOException("Mensagem IPC vazia.");
    }
}