using System.Windows;
using System.IO;

namespace taskmanager;

public partial class App : Application
{
    //protected override void OnStartup(StartupEventArgs e)
    //{
    //    base.OnStartup(e);

    //    // Check if app lock is enabled
    //    string lockFilePath = Path.Combine("Settings", "applock.config");
    //    if (File.Exists(lockFilePath))
    //    {
    //        var appLockDialog = new AppLockDialog();
    //        appLockDialog.ShowDialog();

    //        if (!appLockDialog.IsAuthenticated)
    //        {
    //            // If not authenticated, exit the application
    //            Shutdown();
    //            return;
    //        }
    //    }

    //    // Continue with normal startup
    //    MainWindow mainWindow = new MainWindow();
    //    mainWindow.Show();
    //}

}

