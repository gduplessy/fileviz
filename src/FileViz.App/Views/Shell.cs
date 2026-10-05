using System.Diagnostics;
using System.Runtime.InteropServices;
namespace FileViz.App.Views;

/// <summary>Windows shell actions on a path: reveal in File Explorer and open the Properties dialog.</summary>
public static class Shell
{
    public static void ShowInExplorer(string path)
    {
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        info.ArgumentList.Add("/select,");
        info.ArgumentList.Add(path);
        Process.Start(info);
    }
    /// <summary>Opens the Windows Properties dialog. Returns an error message, or null on success.</summary>
    public static string? ShowProperties(string path)
    {
        var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 12, Verb = "properties", File = path, Show = 1 };
        return ShellExecuteExW(ref info) ? null : new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size; public uint Mask; public IntPtr Window; public string? Verb, File, Parameters, Directory; public int Show; public IntPtr Instance, IdList; public string? Class; public IntPtr ClassKey; public uint HotKey; public IntPtr Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
}
