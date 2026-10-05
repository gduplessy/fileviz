using System.Windows;
using System.Windows.Input;
using FileViz.App.ViewModels;
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
        Loaded += async (_, _) => { try { await Model.InitializeAsync(); Ready.TrySetResult(); } catch (Exception e) { Ready.TrySetException(e); Model.Session.Status = e.Message; } };
        Closed += (_, _) => Model.Dispose();
    }
    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
