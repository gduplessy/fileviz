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
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.F6)
                return;
            CycleFocus(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            e.Handled = true;
        };
    }
    /// <summary>F6 and Shift+F6 move between the navigation pane, the section content, and the title bar search.</summary>
    private void CycleFocus(bool backwards)
    {
        var current = NavPane.IsKeyboardFocusWithin ? 0 : ContentHost.IsKeyboardFocusWithin ? 1 : SearchBox.IsKeyboardFocusWithin ? 2 : -1;
        for (var step = 1; step <= 3; step++)
        {
            var next = ((current < 0 ? (backwards ? 1 : -1) : current) + (backwards ? -step : step) + 3) % 3;
            if (next switch { 0 => FocusNavigation(), 1 => ContentHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), _ => SearchBox.IsEnabled && SearchBox.Focus() })
                return;
        }
    }
    private bool FocusNavigation()
    {
        var items = new List<System.Windows.Controls.RadioButton>();
        void Collect(DependencyObject node)
        {
            for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); index++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, index);
                if (child is System.Windows.Controls.RadioButton radio)
                    items.Add(radio);
                Collect(child);
            }
        }
        Collect(NavPane);
        return (items.FirstOrDefault(x => x.IsChecked == true) ?? items.FirstOrDefault())?.Focus() == true;
    }
    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }
}
