using System.Windows;
using System.Windows.Threading;
using YiziShare.Core;

namespace YiziShare.App;

public partial class App : WpfApplication
{
    /// <summary>应用版本号（发版时与 VERSION_LOG 同步更新）</summary>
    public const string Version = "0.9.36";

    protected override void OnStartup(StartupEventArgs e)
    {
        // 最后一道防线：未处理的 UI 异常记入日志而不是无声杀进程。
        // v0.9.3 的 TargetParameterCountException 事故表明——静默死亡会把问题
        // 掩盖成"应用莫名其妙挂了"，排查只能靠事件查看器。
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Error("未处理的 UI 异常（已拦截，应用继续运行）", args.Exception);
            args.Handled = true;
        };

        try
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"启动失败: {ex.Message}\n\n堆栈跟踪:\n{ex.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
