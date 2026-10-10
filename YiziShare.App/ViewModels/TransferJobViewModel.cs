using System.IO;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

public enum TransferState
{
    Pending,
    Running,
    Completed,
    Cancelled,
    Failed
}

/// <summary>传输窗口中的单个任务行；进度回调由 UI 线程调用</summary>
public class TransferJobViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _cts = new();
    private long _bytesCopied;
    private long _totalBytes = -1;
    private TransferState _state = TransferState.Pending;
    private string _statusText = "等待中";

    private DateTimeOffset _lastSampleTime;
    private long _lastSampleBytes;
    private double _speedBytesPerSec;

    public TransferJobViewModel(string sourcePath, string destDir)
    {
        SourcePath = sourcePath;
        DestDir = destDir;
    }

    public CancellationToken Token => _cts.Token;

    public string SourcePath { get; }
    public string DestDir { get; }
    public string FileName => Path.GetFileName(SourcePath);

    public TransferState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(CanCancel));
            StatusText = value switch
            {
                TransferState.Pending => "等待中",
                TransferState.Running => FormatSpeed(_speedBytesPerSec),
                TransferState.Completed => "已完成",
                TransferState.Cancelled => "已取消",
                TransferState.Failed => $"失败: {ErrorMessage}",
                _ => ""
            };
        }
    }

    public string ErrorMessage { get; private set; } = "";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool CanCancel => State is TransferState.Pending or TransferState.Running;

    public long TotalBytes
    {
        get => _totalBytes;
        private set
        {
            _totalBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public string SizeText => TotalBytes < 0 ? "—" : FormatSize(TotalBytes);

    public long BytesCopied
    {
        get => _bytesCopied;
        private set
        {
            _bytesCopied = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Percent));
        }
    }

    public int Percent => TotalBytes <= 0
        ? (State == TransferState.Completed ? 100 : 0)
        : (int)Math.Clamp(BytesCopied * 100 / TotalBytes, 0, 100);

    /// <summary>由 TransferWindow 在 UI 线程调用</summary>
    public void ReportProgress(TransferProgress p)
    {
        if (TotalBytes < p.TotalBytes) TotalBytes = p.TotalBytes;
        BytesCopied = p.BytesCopied;

        var now = DateTimeOffset.UtcNow;
        if (_lastSampleTime == default)
        {
            _lastSampleTime = now;
            _lastSampleBytes = p.BytesCopied;
            return;
        }

        var elapsed = now - _lastSampleTime;
        if (elapsed.TotalMilliseconds < 400) return;

        var instant = (p.BytesCopied - _lastSampleBytes) / elapsed.TotalSeconds;
        _speedBytesPerSec = _speedBytesPerSec == 0 ? instant : _speedBytesPerSec * 0.6 + instant * 0.4;
        _lastSampleTime = now;
        _lastSampleBytes = p.BytesCopied;
        StatusText = FormatSpeed(_speedBytesPerSec);
    }

    public void Cancel()
    {
        if (CanCancel) _cts.Cancel();
    }

    public void MarkFailed(string message)
    {
        ErrorMessage = message;
        State = TransferState.Failed;
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return "0 KB/s";
        string[] sizes = { "B/s", "KB/s", "MB/s", "GB/s" };
        int order = 0;
        double v = bytesPerSec;
        while (v >= 1024 && order < sizes.Length - 1) { order++; v /= 1024; }
        return $"{v:0.#} {sizes[order]}";
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
        return $"{size:0.##} {sizes[order]}";
    }
}
