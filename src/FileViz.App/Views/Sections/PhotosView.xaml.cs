using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
using FileViz.Windows;

namespace FileViz.App.Views.Sections;

public partial class PhotosView : UserControl
{
    public PhotosView() => InitializeComponent();
    private PhotosViewModel Model => (PhotosViewModel)DataContext;
    private void Show_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Selected is { } row) Shell.ShowInExplorer(row.Path);
    }
    private void Review_Click(object sender, RoutedEventArgs e)
    {
        var selections = Model.BuildSelections();
        if (selections.Length > 0 && ReviewDialog.Review(Window.GetWindow(this), Model.Session, Model.Cleanup, selections)) Model.ClearSelection();
    }
}
