using System.Windows;
using System.Windows.Media;
using FileViz.App.Views;
namespace FileViz.App;
public partial class App : Application
{
    private static bool dark;
    public static void ToggleTheme(){dark=!dark;ApplyTheme();}
    public static void ApplyTheme()
    {
        Current.Resources["Surface"]=new SolidColorBrush(dark?Color.FromRgb(22,28,38):Color.FromRgb(244,246,250));
        Current.Resources["Card"]=new SolidColorBrush(dark?Color.FromRgb(31,39,52):Colors.White);
        Current.Resources["Ink"]=new SolidColorBrush(dark?Color.FromRgb(230,235,244):Color.FromRgb(30,40,56));
        Current.Resources["Line"]=new SolidColorBrush(dark?Color.FromRgb(56,69,85):Color.FromRgb(217,223,231));
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);ApplyTheme();DispatcherUnhandledException+=(_,args)=>{MessageBox.Show(args.Exception.Message,"FileViz",MessageBoxButton.OK,MessageBoxImage.Error);args.Handled=true;};
        var smoke=e.Args.Length==3&&e.Args[0]=="--smoke";
        var window=new MainWindow(smoke?Path.Combine(Path.GetFullPath(e.Args[2]),"smoke.db"):null);window.Show();
        if(smoke)window.ContentRendered+=async (_,_)=>
        {
            try
            {
                await window.Ready.Task;window.Model.PreferMft=false;await window.Model.ScanAsync([Path.GetFullPath(e.Args[1])]);
                await Task.Delay(500);window.UpdateLayout();
                var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);
                var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using(var output=File.Create(Path.Combine(Path.GetFullPath(e.Args[2]),"desktop.png")))encoder.Save(output);
                File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[2]),"smoke.json"),System.Text.Json.JsonSerializer.Serialize(new { window.Model.Status,window.Model.SummaryText,Rows=window.Model.Files.Count,Snapshots=window.Model.History.Count,Errors=window.Model.Errors.Count,RenderedVisibleWindow=true }));
                Shutdown(window.Model.Files.Count>0&&window.Model.Errors.Count==0?0:1);
            }
            catch(Exception error){File.WriteAllText(Path.Combine(Path.GetFullPath(e.Args[2]),"smoke-error.txt"),error.ToString());Shutdown(1);}
        };
    }
}