using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileViz.App.ViewModels;
using FileViz.Core;
using FileViz.Windows;
namespace FileViz.App.Views.Sections;

public partial class ExplorerView : UserControl
{
    public ExplorerView() => InitializeComponent();
    private ExplorerViewModel Model => (ExplorerViewModel)DataContext;
    private void Files_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is FileEntry entry && entry.IsDirectory)
            Model.Navigate(entry.Path);
    }
    private void Folders_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((DataGrid)sender).SelectedItem is Breakdown folder)
            Model.Navigate(folder.Name);
    }
    private void Treemap_FolderChosen(object? sender, string path) => Model.Navigate(path);
    private void Explorer_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not FileEntry file)
            return;
        var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        info.ArgumentList.Add("/select,");
        info.ArgumentList.Add(file.Path);
        Process.Start(info);
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is FileEntry file)
            Clipboard.SetText(file.Path);
    }
    private void Properties_Click(object sender, RoutedEventArgs e)
    {
        if (FilesGrid.SelectedItem is not FileEntry file)
            return;
        var info = new ShellExecuteInfo { Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 12, Verb = "properties", File = file.Path, Show = 1 };
        if (!ShellExecuteExW(ref info))
            Model.Session.Status = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
    }
    private void CleanupFiles_Click(object sender, RoutedEventArgs e)
    {
        var selections = FilesGrid.SelectedItems.Cast<FileEntry>().Where(x => !x.IsDirectory).Select(x => new CleanupSelection(x)).ToArray();
        ReviewDialog.Review(Window.GetWindow(this), Model.Session, Model.Cleanup, selections);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size; public uint Mask; public IntPtr Window; public string? Verb, File, Parameters, Directory; public int Show; public IntPtr Instance, IdList; public string? Class; public IntPtr ClassKey; public uint HotKey; public IntPtr Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
}
