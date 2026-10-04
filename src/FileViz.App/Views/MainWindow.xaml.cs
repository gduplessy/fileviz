using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileViz.App.ViewModels;
using FileViz.Core;
using FileViz.Windows;
namespace FileViz.App.Views;

public partial class MainWindow : Window
{
    public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public ShellViewModel Model
    {
        get;
    }
    public MainWindow(string? database = null)
    {
        InitializeComponent();
        Model = new(database);
        DataContext = Model;
        Loaded += async (_, _) => { try { await Model.InitializeAsync(); Ready.TrySetResult(); } catch (Exception e) { Ready.TrySetException(e); Model.Status = e.Message; } };
        Closed += (_, _) => Model.Dispose();
    }
    private void Theme_Click(object sender, RoutedEventArgs e) => App.ToggleTheme();
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
            Model.Status = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()).Message;
    }
    private void CleanupFiles_Click(object sender, RoutedEventArgs e)
    {
        var selections = FilesGrid.SelectedItems.Cast<FileEntry>().Where(x => !x.IsDirectory).Select(x => new CleanupSelection(x)).ToArray();
        Review(selections);
    }
    private void CleanupDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var rows = DuplicatesGrid.SelectedItems.Cast<DuplicateRow>().ToArray();
        var removals = rows.Select(x => x.Entry.Path).ToArray();
        var selections = new List<CleanupSelection>();
        foreach (var row in rows)
        {
            if (row.Evidence == "Name candidate")
            {
                Model.Status = "Name matches cannot authorize duplicate cleanup. Run content analysis first.";
                return;
            }
            var keeper = Model.KeeperFor(row, removals);
            if (keeper == null)
            {
                Model.Status = "Keep at least one unselected copy in every duplicate group.";
                return;
            }
            selections.Add(new(row.Entry, keeper));
        }
        Review(selections.ToArray());
    }
    private void Review(CleanupSelection[] selections)
    {
        if (Model.Busy || selections.Length == 0)
            return;
        var preview = new Window { Title = "Review quarantine", Owner = this, Width = 850, Height = 550, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)Application.Current.Resources["Surface"], Foreground = (System.Windows.Media.Brush)Application.Current.Resources["Ink"] };
        var panel = new DockPanel { Margin = new Thickness(20) };
        var header = new TextBlock { Text = $"Move {selections.Length} explicitly selected files to same-volume quarantine?\nFiles remain recoverable. This does not free disk space. Duplicate keepers are byte-compared, including alternate streams.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 10, 0), IsCancel = true };
        cancel.Click += (_, _) => preview.DialogResult = false;
        var confirm = new Button { Content = "Quarantine these files", Padding = new Thickness(16, 8, 16, 8) };
        confirm.Click += (_, _) => preview.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        var details = string.Join("\n\n", selections.Select(x => "REMOVE: " + x.Target.Path + "\nKEEP: " + (x.Keeper?.Path ?? "Manual file selection; no duplicate keeper") + "\nSIZE: " + Format.Bytes(x.Target.Length)));
        panel.Children.Add(new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        preview.Content = panel;
        if (preview.ShowDialog() == true)
            Model.Run(() => Model.CleanupAsync(selections));
    }
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (CleanupGrid.SelectedItem is CleanupRecord record)
            Model.Restore(record);
    }
    private void Recycle_Click(object sender, RoutedEventArgs e)
    {
        if (CleanupGrid.SelectedItem is CleanupRecord record && MessageBox.Show(this, "Ask Windows to send this quarantined file to its Recycle Bin? Windows will display any additional confirmation or unavailable-recycle warning.", "Review Recycle Bin request", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            Model.Recycle(record);
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size; public uint Mask; public IntPtr Window; public string? Verb, File, Parameters, Directory; public int Show; public IntPtr Instance, IdList; public string? Class; public IntPtr ClassKey; public uint HotKey; public IntPtr Icon, Process;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
}
