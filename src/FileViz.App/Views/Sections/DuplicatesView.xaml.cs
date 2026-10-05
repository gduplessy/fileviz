using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
using FileViz.Core;
using FileViz.Windows;
namespace FileViz.App.Views.Sections;

public partial class DuplicatesView : UserControl
{
    public DuplicatesView() => InitializeComponent();
    private void CleanupDuplicates_Click(object sender, RoutedEventArgs e)
    {
        var model = (DuplicatesViewModel)DataContext;
        var rows = DuplicatesGrid.SelectedItems.Cast<DuplicateRow>().ToArray();
        var removals = rows.Select(x => x.Entry.Path).ToArray();
        var selections = new List<CleanupSelection>();
        foreach (var row in rows)
        {
            if (row.Evidence == "Name candidate")
            {
                model.Session.Status = "Name matches cannot authorize duplicate cleanup. Run content analysis first.";
                return;
            }
            var keeper = model.KeeperFor(row, removals);
            if (keeper == null)
            {
                model.Session.Status = "Keep at least one unselected copy in every duplicate group.";
                return;
            }
            selections.Add(new(row.Entry, keeper));
        }
        ReviewDialog.Review(Window.GetWindow(this), model.Session, model.Cleanup, selections.ToArray());
    }
}
