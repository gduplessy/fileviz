using System.Diagnostics;
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
        if (FilesGrid.SelectedItem is FileRow { Entry.IsDirectory: true } row)
            Model.Navigate(row.Entry.Path);
    }
    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesGrid.SelectedItem is FileRow row)
            Model.Inspect(row.Entry);
    }
    private void Folders_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (((DataGrid)sender).SelectedItem is Breakdown folder)
            Model.Navigate(folder.Name);
    }
    private void Treemap_FolderChosen(object? sender, string path) => Model.Navigate(path);
    private void Treemap_TileSelected(object? sender, MapNode node) => Model.Inspect(node);
    private void CleanupFiles_Click(object sender, RoutedEventArgs e)
    {
        var selections = FilesGrid.SelectedItems.Cast<FileRow>().Where(x => !x.Entry.IsDirectory).Select(x => new CleanupSelection(x.Entry)).ToArray();
        ReviewDialog.Review(Window.GetWindow(this), Model.Session, Model.Cleanup, selections);
    }
    private void InspectorExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Inspection is { } item)
            Shell.ShowInExplorer(item.Path);
    }
    private void InspectorCopy_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Inspection is { } item)
            Clipboard.SetText(item.Path);
    }
    private void InspectorProperties_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Inspection is { } item && Shell.ShowProperties(item.Path) is { } error)
            Model.Session.Status = error;
    }
    private void InspectorOpen_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Inspection is { IsDirectory: true } item)
            Model.Navigate(item.Path);
    }
    private void InspectorCleanup_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Inspection is { IsFile: true } item && Model.Session.Store.Entry(Model.Session.Active, item.Path) is { } entry)
            ReviewDialog.Review(Window.GetWindow(this), Model.Session, Model.Cleanup, [new CleanupSelection(entry)]);
    }
}
