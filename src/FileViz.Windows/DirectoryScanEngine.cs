using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using FileViz.Core;
using Microsoft.Win32.SafeHandles;
namespace FileViz.Windows;

public sealed class DirectoryScanEngine : IScanEngine
{
    public async IAsyncEnumerable<ScanBatch> ScanAsync(ScanScope scope, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        var entries = new List<FileEntry>(256); var budget=0; var errors = new List<ScanError>();
        var volume = Native.VolumeName(scope.Root);
        var stack = new Stack<(string Directory, IEnumerator<FileEntry> Iterator)>();
        var root = Paths.Normalize(scope.Root);
        if (Paths.Excluded(root, scope.Exclusions)) yield break;
        try
        {
            stack.Push((root, Enumerate(root, volume).GetEnumerator()));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested(); var frame = stack.Peek(); FileEntry? entry = null; var ended = false;
                try { ended = !frame.Iterator.MoveNext(); if (!ended) entry = frame.Iterator.Current; }
                catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException) { errors.Add(new(frame.Directory,e.Message)); ended = true; }
                if (ended) { stack.Pop().Iterator.Dispose(); }
                else if (entry != null && !Paths.Excluded(entry.Path, scope.Exclusions))
                {
                    entries.Add(entry);budget+=6*(entry.Path.Length+entry.Parent.Length+entry.Name.Length)+1024;
                    if (entry.IsDirectory && !entry.IsReparse && !entry.IsPlaceholder)
                    {
                        if (stack.Count >= 512) errors.Add(new(entry.Path,"Directory depth exceeds the handle budget."));
                        else stack.Push((entry.Path,Enumerate(entry.Path,volume).GetEnumerator()));
                    }
                }
                if(entries.Count >= 256 || budget>=1024*1024 || errors.Count >= 64)
                { yield return new(scope.Root,"Directory",entries.ToArray(),errors.ToArray()); entries.Clear();budget=0; errors.Clear(); }
            }
        }
        finally { while(stack.Count > 0)stack.Pop().Iterator.Dispose(); }
        if (entries.Count > 0 || errors.Count > 0) yield return new(scope.Root,"Directory",entries.ToArray(),errors.ToArray());
    }
    public static IEnumerable<FileEntry> Enumerate(string directory, string volume)
    {
        using var handle = Native.CreateFileW(Native.LongPath(directory), 1, 7, IntPtr.Zero, 3, Native.BackupSemantics | Native.OpenReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), directory);
        var buffer = Marshal.AllocHGlobal(64 * 1024);
        try
        {
            var first = true;
            while (true)
            {
                if (!Native.GetFileInformationByHandleEx(handle, first ? 20 : 19, buffer, 64 * 1024))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 18) yield break;
                    if (first && error is 1 or 50 or 87)
                    {
                        foreach (var item in Fallback(directory)) yield return item;
                        yield break;
                    }
                    throw new Win32Exception(error, directory);
                }
                first = false; var bytes = Native.ReadBuffer(buffer, 64 * 1024); var offset = 0;
                while (true)
                {
                    var next = BitConverter.ToUInt32(bytes, offset); var nameLength = BitConverter.ToInt32(bytes, offset + 60);
                    if (nameLength < 0 || nameLength % 2 != 0 || offset + 88L + nameLength > bytes.Length) throw new InvalidDataException("Invalid directory metadata.");
                    var name = System.Text.Encoding.Unicode.GetString(bytes, offset + 88, nameLength);
                    if (name is not "." and not "..")
                    {
                        var path = System.IO.Path.Combine(directory, name); var attributes = BitConverter.ToUInt32(bytes, offset + 56);
                        var identityBytes = bytes.AsSpan(offset + 72, 16).ToArray();
                        var id = identityBytes.Any(x => x != 0) ? volume + ":" + Convert.ToHexString(identityBytes) : null;
                        yield return new(path, directory, name, id, (attributes & 16) != 0, Math.Max(0, BitConverter.ToInt64(bytes, offset + 40)),
                            Math.Max(0, BitConverter.ToInt64(bytes, offset + 48)), Native.FileTime(BitConverter.ToInt64(bytes, offset + 24)),
                            Native.FileTime(BitConverter.ToInt64(bytes, offset + 32)), attributes);
                    }
                    if (next == 0) break;
                    if (next < 88 || offset + (long)next + 88 > bytes.Length) throw new InvalidDataException("Invalid directory offset.");
                    offset += checked((int)next);
                }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh, SizeHigh, SizeLow, Reserved0, Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string Alternate;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileExW(string path, int level, out FindData data, int search, IntPtr filter, uint flags);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindNextFileW(IntPtr handle, out FindData data);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool FindClose(IntPtr handle);
    private static IEnumerable<FileEntry> Fallback(string directory)
    {
        var handle = FindFirstFileExW(Native.LongPath(System.IO.Path.Combine(directory, "*")), 1, out var data, 0, IntPtr.Zero, 2);
        if (handle == new IntPtr(-1))
        {
            var error = Marshal.GetLastWin32Error(); if (error == 2) yield break;
            throw new Win32Exception(error, directory);
        }
        try
        {
            do
            {
                if (data.Name is "." or "..") continue;
                yield return new(System.IO.Path.Combine(directory, data.Name), directory, data.Name, null, (data.Attributes & 16) != 0,
                    checked((long)(((ulong)data.SizeHigh << 32) | data.SizeLow)), null, Native.FileTime(((long)data.WriteHigh << 32) | data.WriteLow), 0, data.Attributes);
            } while (FindNextFileW(handle, out data));
            var error = Marshal.GetLastWin32Error(); if (error != 18) throw new Win32Exception(error, directory);
        }
        finally { FindClose(handle); }
    }
}