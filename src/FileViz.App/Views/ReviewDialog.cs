using System.Windows;
using System.Windows.Controls;
using FileViz.App.ViewModels;
using FileViz.Core;
using FileViz.Windows;
namespace FileViz.App.Views;

/// <summary>Explicit review before quarantine. Phase 5 replaces this with the per-file precheck dialog in docs/design.md.</summary>
public static class ReviewDialog
{
    public static void Review(Window? owner, SessionViewModel session, CleanupViewModel cleanup, CleanupSelection[] selections)
    {
        if (session.Busy || selections.Length == 0)
            return;
        var preview = new Window { Title = "Review quarantine", Owner = owner, Width = 850, Height = 550, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
        var panel = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new TextBlock { Text = $"Move {selections.Length} files to quarantine?", Style = (Style)Application.Current.FindResource("SubtitleText") });
        header.Children.Add(new TextBlock { Text = "Files stay on the same volume and can be restored. This does not free disk space. Duplicate keepers are byte-compared, including alternate streams.", Style = (Style)Application.Current.FindResource("BodyText"), Margin = new Thickness(0, 6, 0, 0) });
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var cancel = new Button { Content = "Cancel", MinWidth = 120, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => preview.DialogResult = false;
        var confirm = new Button { Content = $"Quarantine {selections.Length} files", MinWidth = 180, Style = (Style)Application.Current.FindResource("AccentButtonStyle") };
        confirm.Click += (_, _) => preview.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        var details = string.Join("\n\n", selections.Select(x => "REMOVE: " + x.Target.Path + "\nKEEP: " + (x.Keeper?.Path ?? "Manual file selection; no duplicate keeper") + "\nSIZE: " + Format.Bytes(x.Target.Length)));
        panel.Children.Add(new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("MonoFont") });
        preview.Content = panel;
        if (preview.ShowDialog() == true)
            session.Run(() => cleanup.CleanupAsync(selections));
    }
}
