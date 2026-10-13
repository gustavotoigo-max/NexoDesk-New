using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace NexoDesk.Service;

internal static class SessionLauncher
{
    public static uint ActiveConsoleSession() => WTSGetActiveConsoleSessionId();

    public static WorkerLifetime StartWorker(uint sessionId)
    {
        if (!OpenProcessToken(GetCurrentProcess(), 0x0002 | 0x0008 | 0x0001 | 0x0020, out IntPtr source))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        IntPtr token = IntPtr.Zero;
        try
        {
            EnablePrivilege(source, "SeTcbPrivilege");
            if (!DuplicateTokenEx(source, 0x02000000, IntPtr.Zero, 2, 1, out token))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            EnablePrivilege(token, "SeTcbPrivilege");
            EnablePrivilege(source, "SeAssignPrimaryTokenPrivilege");
            EnablePrivilege(source, "SeIncreaseQuotaPrivilege");
            if (!SetTokenInformation(token, 12, ref sessionId, sizeof(uint)))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            string executable = Path.Combine(AppContext.BaseDirectory, "NexoDesk.Service.exe");
            if (!CreateProcessAsUser(token, executable, new StringBuilder($"\"{executable}\" --worker"),
                IntPtr.Zero, IntPtr.Zero, false, 0x08000000 | 0x00000004, IntPtr.Zero, AppContext.BaseDirectory, ref startup, out PROCESS_INFORMATION info))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            Microsoft.Win32.SafeHandles.SafeFileHandle? job = null;
            try
            {
                job = WorkerLifetime.Attach(info.hProcess);
                if (ResumeThread(info.hThread) == uint.MaxValue) throw new Win32Exception(Marshal.GetLastWin32Error());
                return new WorkerLifetime(Process.GetProcessById((int)info.dwProcessId), job);
            }
            catch
            {
                TerminateProcess(info.hProcess, 1);
                job?.Dispose();
                throw;
            }
            finally { CloseHandle(info.hThread); CloseHandle(info.hProcess); }
        }
        finally { if (token != IntPtr.Zero) CloseHandle(token); CloseHandle(source); }
    }

    private static void EnablePrivilege(IntPtr token, string name)
    {
        if (!LookupPrivilegeValue(null, name, out LUID luid)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var privileges = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attributes = 2 };
        if (!AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero) || Marshal.GetLastWin32Error() != 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] private struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public ushort wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId; }
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WTSGetActiveConsoleSessionId();
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(IntPtr existing, uint access, IntPtr attributes, int level, int type, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool SetTokenInformation(IntPtr token, int infoClass, ref uint info, int length);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES privileges, uint length, IntPtr previous, IntPtr returned);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessAsUser(IntPtr token, string application, StringBuilder commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string directory, ref STARTUPINFO startup, out PROCESS_INFORMATION info);
}