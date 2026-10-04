using System.Windows;
namespace FileViz.App;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e) { base.OnStartup(e); new Window { Title = "FileViz", Width = 1200, Height = 800, Content = "FileViz" }.Show(); }
}