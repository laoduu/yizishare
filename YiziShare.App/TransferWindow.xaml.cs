using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using YiziShare.App.ViewModels;
using YiziShare.Core;

namespace YiziShare.App;

/// <summary>
/// 全局唯一的传输窗口：汇总所有文件的进度，顺序执行队列。
/// 关闭窗口会取消未完成的任务。
/// </summary>
public partial class TransferWindow : Window
{
    private static TransferWindow? _current;

    private bool _isProcessing;
    private System.Windows.Threading.DispatcherTimer? _autoCloseTimer;

    public ObservableCollection<TransferJobViewModel> Jobs { get; } = new();

    private TransferWindow()
    {
        InitializeComponent();
        JobsList.ItemsSource = Jobs;
        UpdateHeader();
    }

    /// <summary>
    /// 把一批文件/目录加入传输队列（可从任意窗口调用）。
    /// onQueueDrained：当所有任务执行完毕（不论成败）时在 UI 线程回调一次。
    /// </summary>
    public static void Enqueue(IEnumerable<string> sourcePaths, string destDir, Action? onQueueDrained = null)
    {
        var window = _current;
        if (window == null || !window.IsLoaded)
        {
            window = new TransferWindow
            {
                Owner = WpfApplication.Current.MainWindow
            };
            _current = window;
            window.Show();
        }
        else
        {
            window.StopAutoClose();
        }

        int added = 0;
        // 清掉上一批已结束的条目：列表只反映当前批次，旧残留会污染"全部完成"判断导致窗口永不关闭
        for (int i = window.Jobs.Count - 1; i >= 0; i--)
        {
            if (window.Jobs[i].State is not (TransferState.Pending or TransferState.Running))
                window.Jobs.RemoveAt(i);
        }

        foreach (var path in sourcePaths)
        {
            window.Jobs.Add(new TransferJobViewModel(path, destDir));
            added++;
        }

        if (added == 0)
        {
            window.UpdateHeader();
            return;
        }

        if (onQueueDrained != null)
            window._drainedCallbacks.Add(onQueueDrained);

        window.UpdateHeader();
        _ = window.ProcessQueueAsync();
    }
    private readonly List<Action> _drainedCallbacks = new();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void CancelJob_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TransferJobViewModel job)
            job.Cancel();
    }

    protected override void OnClosed(EventArgs e)
    {
        foreach (var job in Jobs)
            job.Cancel();
        _current = null;
        base.OnClosed(e);
    }

    private async System.Threading.Tasks.Task ProcessQueueAsync()
    {
        if (_isProcessing) return;
        _isProcessing = true;
        try
        {
            while (true)
            {
                var job = Jobs.FirstOrDefault(j => j.State == TransferState.Pending);
                if (job == null) break;

                job.State = TransferState.Running;
                var progress = new Progress<TransferProgress>(job.ReportProgress);
                try
                {
                    if (Directory.Exists(job.SourcePath))
                        await FileTransferService.CopyDirectoryAsync(job.SourcePath, job.DestDir, progress, job.Token).ConfigureAwait(false);
                    else
                        await FileTransferService.CopyFileAsync(job.SourcePath, job.DestDir, progress, job.Token).ConfigureAwait(false);
                    job.State = TransferState.Completed;
                }
                catch (OperationCanceledException)
                {
                    job.State = TransferState.Cancelled;
                }
                catch (Exception ex)
                {
                    Logger.Error($"传输失败: {job.SourcePath}", ex);
                    job.MarkFailed(ex.Message);
                }

                UpdateHeader();
            }
        }
        finally
        {
            _isProcessing = false;
            OnQueueDrained();
        }
    }

    private void OnQueueDrained()
    {
        // ProcessQueueAsync 的 await 带 ConfigureAwait(false)，此方法跑在线程池线程——
        // 直接建 DispatcherTimer/改 UI 会抛异常并被 fire-and-forget 吞掉，
        // 表现为"自动关闭从未生效"。必须切回 UI 线程。
        WpfApplication.Current.Dispatcher.Invoke(OnQueueDrainedCore);
    }

    private void OnQueueDrainedCore()
    {
        var callbacks = new List<Action>(_drainedCallbacks);
        _drainedCallbacks.Clear();
        foreach (var callback in callbacks)
            callback();

        bool hasActive = Jobs.Any(j => j.State is TransferState.Pending or TransferState.Running);
        bool hasFailures = Jobs.Any(j => j.State is TransferState.Failed or TransferState.Cancelled);

        FooterText.Text = hasFailures
            ? $"{Jobs.Count(j => j.State == TransferState.Completed)} 完成，" +
              $"{Jobs.Count(j => j.State == TransferState.Failed)} 失败，" +
              $"{Jobs.Count(j => j.State == TransferState.Cancelled)} 取消"
            : "全部完成";

        // 完成即自动关闭：全部成功 2.5s；有失败/取消 12s（留时间看错误，不再要求手动关）
        if (!hasActive && Jobs.Count > 0)
            StartAutoClose(hasFailures ? 12 : 2.5);
    }

    private void StartAutoClose(double seconds)
    {
        StopAutoClose();
        _autoCloseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        _autoCloseTimer.Tick += (_, _) =>
        {
            StopAutoClose();
            Close();
        };
        _autoCloseTimer.Start();
    }

    private void StopAutoClose()
    {
        _autoCloseTimer?.Stop();
        _autoCloseTimer = null;
    }

    private void UpdateHeader()
    {
        int pending = Jobs.Count(j => j.State is TransferState.Pending or TransferState.Running);
        JobsCountText.Text = pending > 0 ? $"{pending} 进行中" : $"{Jobs.Count} 项";
    }
}
