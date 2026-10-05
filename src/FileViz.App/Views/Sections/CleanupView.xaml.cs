using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
using FileViz.Core;
namespace FileViz.App.Views.Sections;

public partial class CleanupView : UserControl
{
    public CleanupView() => InitializeComponent();
    private CleanupViewModel Model => (CleanupViewModel)DataContext;
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (CleanupGrid.SelectedItem is CleanupRecord record)
            Model.Restore(record);
    }
    private void Recycle_Click(object sender, RoutedEventArgs e)
    {
        if (CleanupGrid.SelectedItem is CleanupRecord record && MessageBox.Show(Window.GetWindow(this), "Ask Windows to send this quarantined file to its Recycle Bin? Windows will display any additional confirmation or unavailable-recycle warning.", "Review Recycle Bin request", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            Model.Recycle(record);
    }
}
