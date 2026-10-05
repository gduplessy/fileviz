using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
namespace FileViz.App.Views.Sections;

public partial class DuplicatesView : UserControl
{
    public DuplicatesView() => InitializeComponent();
    private void Review_Click(object sender, RoutedEventArgs e)
    {
        var model = (DuplicatesViewModel)DataContext;
        if (model.BuildSelections() is { Length: > 0 } selections && ReviewDialog.Review(Window.GetWindow(this), model.Session, model.Cleanup, selections))
            model.Completed();
    }
}
