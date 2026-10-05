using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
namespace FileViz.App.Views.Sections;

public partial class DiagnosticsView : UserControl
{
    public DiagnosticsView() => InitializeComponent();
    private DiagnosticsViewModel Model => (DiagnosticsViewModel)DataContext;
    private static DiagnosticRow? Row(object sender) => (sender as FrameworkElement)?.DataContext as DiagnosticRow;
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row)
            Shell.ShowInExplorer(row.Path);
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (Row(sender) is { } row)
            Clipboard.SetText(row.Path);
    }
    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        var lines = Model.Rows.Select(x => $"{x.Label}\t{x.Path}\t{x.Message}");
        Clipboard.SetText(Model.Headline + Environment.NewLine + string.Join(Environment.NewLine, lines));
        Model.Session.Status = "Diagnostics copied to the clipboard.";
    }
}
