using System.Windows.Controls;
using System.Windows.Input;
using FileViz.App.ViewModels;
namespace FileViz.App.Views.Sections;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();
    private void Open()
    {
        if (DataContext is HomeViewModel model && SnapshotsGrid.SelectedItem is SnapshotRow row && model.Session.CanInteract)
            model.Session.OpenSnapshot(row.Value.Id);
    }
    private void ExtraRoots_LostFocus(object sender, System.Windows.RoutedEventArgs e) => ((HomeViewModel)DataContext).ChangedRoots();
    private void Snapshots_DoubleClick(object sender, MouseButtonEventArgs e) => Open();
    private void Snapshots_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        Open();
        e.Handled = true;
    }
}
