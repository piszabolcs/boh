using System.Windows;
using BOH.ViewModels;
using BOH.Views;

namespace BOH;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        new MainWindow(new MainViewModel()).Show();
    }
}
