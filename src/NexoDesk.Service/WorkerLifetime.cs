using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NexoDesk.Service;

/// <summary>O Windows encerra o agente se o supervisor morrer inesperadamente.</summary>
internal sealed class WorkerLifetime : IDisposable
{
    public Process Process { get; }
    private readonly SafeFileHandle _job;

    public WorkerLifetime(Process process, SafeFileHandle job) { Process = process; _job = job; }
    public void Dispose() { _job.Dispose(); Process.Dispose(); }

    public static SafeFileHandle Attach(IntPtr process)
    {
        SafeFileHandle job = CreateJobObject(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var info = new EXTENDED_LIMIT_INFORMATION { Basic = new BASIC_LIMIT_INFORMATION { LimitFlags = 0x2000 } };
        if (!SetInformationJobObject(job, 9, ref info, Marshal.SizeOf<EXTENDED_LIMIT_INFORMATION>()) ||
            !AssignProcessToJobObject(job, process))
        {
            int error = Marshal.GetLastWin32Error(); job.Dispose(); throw new Win32Exception(error);
        }
        return job;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BASIC_LIMIT_INFORMATION
    {
        public long PerProcessTime, PerJobTime;
        public uint LimitFlags;
        public UIntPtr MinWorkingSet, MaxWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct EXTENDED_LIMIT_INFORMATION
    {
        public BASIC_LIMIT_INFORMATION Basic;
        public IO_COUNTERS Io;
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref EXTENDED_LIMIT_INFORMATION info, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
}