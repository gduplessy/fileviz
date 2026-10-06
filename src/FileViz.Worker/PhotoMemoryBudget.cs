using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileViz.Worker;

/// <summary>Windows enforces the codec process's committed-memory budget, including native allocations.</summary>
internal static class PhotoMemoryBudget
{
    private static readonly Lazy<SafeFileHandle> Job = new(Create);
    public static void Ensure() => _ = Job.Value;
    private static SafeFileHandle Create()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var limits = new ExtendedLimits { Basic = new() { Flags = 0x100 }, ProcessMemory = (UIntPtr)(384UL * 1024 * 1024) };
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<ExtendedLimits>());
        try
        {
            Marshal.StructureToPtr(limits, buffer, false);
            if (!SetInformationJobObject(job, 9, buffer, (uint)Marshal.SizeOf<ExtendedLimits>()) || !AssignProcessToJobObject(job, GetCurrentProcess()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot enforce the image worker memory budget.");
            return job;
        }
        catch { job.Dispose(); throw; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
    {
        public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
        public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters
    {
        public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits
    {
        public BasicLimits Basic; public IoCounters Io;
        public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetInformationJobObject(SafeFileHandle job, int information, IntPtr buffer, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
}
