using System.Windows;
using FileViz.App.ViewModels;
namespace FileViz.App.Views;

public partial class ReviewWindow : Window
{
    private readonly CancellationTokenSource cancellation = new();
    public ReviewViewModel Model
    {
        get;
    }
    public ReviewWindow(ReviewViewModel model)
    {
        InitializeComponent();
        Model = model;
        DataContext = model;
        Loaded += async (_, _) =>
        {
            try { await model.CheckAsync(cancellation.Token); }
            catch (OperationCanceledException) { }
        };
        Closed += (_, _) => { cancellation.Cancel(); cancellation.Dispose(); };
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
