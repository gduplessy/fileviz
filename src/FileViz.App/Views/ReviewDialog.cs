using System.Windows;
using FileViz.App.ViewModels;
using FileViz.Windows;
namespace FileViz.App.Views;

/// <summary>Opens the cleanup review and quarantines the files whose precheck passed.</summary>
public static class ReviewDialog
{
    /// <summary>Returns true when the user confirmed and the quarantine started.</summary>
    public static bool Review(Window? owner, SessionViewModel session, CleanupViewModel cleanup, CleanupSelection[] selections)
    {
        if (session.Busy || selections.Length == 0)
            return false;
        var window = new ReviewWindow(new(selections)) { Owner = owner };
        if (window.ShowDialog() != true)
            return false;
        var ready = window.Model.ReadySelections;
        if (ready.Length == 0)
            return false;
        session.Run(() => cleanup.CleanupAsync(ready));
        return true;
    }
}
