using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using FileViz.Core;
namespace FileViz.Windows;

public static class Native
{
    public const uint BackupSemantics = 0x02000000, OpenReparsePoint = 0x00200000;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, IntPtr data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo data);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeviceIoControl(SafeFileHandle handle, uint control, byte[]? input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumePathNameW(string path, StringBuilder root, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetVolumeNameForVolumeMountPointW(string path, StringBuilder name, int size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint pid);
    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetGetConnectionW(string local, StringBuilder remote, ref int size);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint desired, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(SafeAccessTokenHandle token, bool disable, ref TokenPrivileges privileges, int size, IntPtr previous, IntPtr returned);
    [StructLayout(LayoutKind.Sequential)] private struct TokenPrivileges { public uint Count; public long Luid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] public struct HandleInfo
    {
        public uint Attributes; public uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    public static bool IsElevated => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    public static void EnableBackupPrivilege()
    {
        if (!IsElevated) return;
        if (!OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, 0x28, out var token)) return;
        using (token)
        {
            if (!LookupPrivilegeValueW(null, "SeBackupPrivilege", out var luid)) return;
            var value = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            AdjustTokenPrivileges(token, false, ref value, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }
    public static string LongPath(string path) => path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    public static string ResolveNetwork(string path)
    {
        path = Paths.Normalize(path);
        if (path.Length < 2 || path[1] != ':') return path;
        var remote = new StringBuilder(32768); var length = remote.Capacity;
        return WNetGetConnectionW(path[..2], remote, ref length) == 0 ? Paths.Normalize(remote + path[2..]) : path;
    }
    public static string VolumeRoot(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return System.IO.Path.GetPathRoot(path)!;
        var root = new StringBuilder(32768);
        return GetVolumePathNameW(LongPath(path), root, root.Capacity) ? root.ToString().Replace(@"\\?\", "") : System.IO.Path.GetPathRoot(path)!;
    }
    public static string VolumeName(string path)
    {
        var root = VolumeRoot(path); var name = new StringBuilder(128);
        return GetVolumeNameForVolumeMountPointW(root, name, name.Capacity) ? name.ToString() : root;
    }
    public static FileEntry ReadEntry(string path, uint access = 0, uint share = 7)
    {
        using var handle = CreateFileW(LongPath(path), access, share, IntPtr.Zero, 3, BackupSemantics | OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), path);
        return ReadEntry(handle, path);
    }
    public static FileEntry ReadEntry(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var buffer = Marshal.AllocHGlobal(40);
        try
        {
            string? identity = null; long? allocated = null; long changed = 0;
            if (GetFileInformationByHandleEx(handle, 18, buffer, 24))
                identity = Marshal.ReadInt64(buffer).ToString("x16") + ":" + Convert.ToHexString(ReadBuffer(buffer + 8, 16));
            else if (info.IndexHigh != 0 || info.IndexLow != 0)
                identity = info.VolumeSerial.ToString("x8") + ":" + (((ulong)info.IndexHigh << 32) | info.IndexLow).ToString("x16");
            if (GetFileInformationByHandleEx(handle, 1, buffer, 24)) allocated = Marshal.ReadInt64(buffer);
            if (GetFileInformationByHandleEx(handle, 0, buffer, 40)) changed = FileTime(Marshal.ReadInt64(buffer, 24));
            return new FileEntry(path, System.IO.Path.GetDirectoryName(path) ?? path, System.IO.Path.GetFileName(path), identity,
                (info.Attributes & 16) != 0, checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow)), allocated,
                FileTime(((long)info.WriteHigh << 32) | info.WriteLow), changed, info.Attributes);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    public static byte[] ReadBuffer(IntPtr ptr, int size) { var value = new byte[size]; Marshal.Copy(ptr, value, 0, size); return value; }
    public static long FileTime(long value) { try { return DateTime.FromFileTimeUtc(value).Ticks; } catch (ArgumentOutOfRangeException) { return 0; } }
}