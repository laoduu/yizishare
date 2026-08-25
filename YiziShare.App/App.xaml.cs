using System.Windows;

namespace YiziShare.App;

public partial class App : WpfApplication
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
