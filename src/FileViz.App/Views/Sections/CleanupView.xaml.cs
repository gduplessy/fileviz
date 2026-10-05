using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
namespace FileViz.App.Views.Sections;

public partial class CleanupView : UserControl
{
    public CleanupView() => InitializeComponent();
    private CleanupViewModel Model => (CleanupViewModel)DataContext;
    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Selected is { } record)
            Model.Restore(record);
    }
    private void Show_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Selected is { } record)
            Shell.ShowInExplorer(record.State == "Quarantined" ? record.Destination : record.Original);
    }
    private void Recycle_Click(object sender, RoutedEventArgs e)
    {
        if (Model.Selected is { } record && MessageBox.Show(Window.GetWindow(this), "Ask Windows to send this quarantined file to its Recycle Bin? Windows will display any additional confirmation or unavailable-recycle warning.", "Send to Recycle Bin", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
            Model.Recycle(record);
    }
}
