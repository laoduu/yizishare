using System.IO;
using System.Text;

namespace YiziShare.Core;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class Logger
{
    private static readonly string LogDirectory;
    private static readonly string LogFilePath;
    private static readonly object LockObj = new();
    private const long MaxLogSize = 5 * 1024 * 1024;

    static Logger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        LogDirectory = Path.Combine(appData, "YiziShare");
        Directory.CreateDirectory(LogDirectory);
        LogFilePath = Path.Combine(LogDirectory, "app.log");
    }

    public static void Debug(string message) => WriteLog(LogLevel.Debug, message);
    public static void Info(string message) => WriteLog(LogLevel.Info, message);
    public static void Warning(string message) => WriteLog(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) => WriteLog(LogLevel.Error, message, ex);

    private static void WriteLog(LogLevel level, string message, Exception? ex = null)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}");
            if (ex != null)
            {
                sb.AppendLine();
                sb.Append($"  Exception: {ex.GetType().Name}: {ex.Message}");
                if (ex.StackTrace != null)
                {
                    sb.AppendLine();
                    sb.Append($"  StackTrace: {ex.StackTrace}");
                }
            }

            lock (LockObj)
            {
                RotateLogIfNeeded();
                File.AppendAllText(LogFilePath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 日志写入失败时静默，避免递归
        }
    }

    private static void RotateLogIfNeeded()
    {
        try
        {
            if (File.Exists(LogFilePath))
            {
                var info = new FileInfo(LogFilePath);
                if (info.Length > MaxLogSize)
                {
                    var backupPath = LogFilePath + ".old";
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(LogFilePath, backupPath);
                }
            }
        }
        catch { }
    }

    public static string GetLogFilePath() => LogFilePath;
}
