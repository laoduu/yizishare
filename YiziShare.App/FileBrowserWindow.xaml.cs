using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YiziShare.App;

public partial class FileBrowserWindow : Window
{
    private string _currentPath = "";
    private readonly ObservableCollection<FileItem> _files = new();

    public FileBrowserWindow(string initialPath)
    {
        InitializeComponent();
        _currentPath = initialPath;
        PathText.Text = _currentPath;
        FileList.ItemsSource = _files;
        LoadDirectory(_currentPath);
    }

    private void LoadDirectory(string path)
    {
        _files.Clear();
        StatusText.Text = "正在加载...";

        try
        {
            if (!Directory.Exists(path))
            {
                StatusText.Text = "目录不存在";
                return;
            }

            foreach (var dir in Directory.GetDirectories(path))
            {
                var info = new DirectoryInfo(dir);
                _files.Add(new FileItem
                {
                    Name = info.Name,
                    FullName = info.FullName,
                    IsDirectory = true,
                    SizeText = "<目录>",
                    ModifiedTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }

            foreach (var file in Directory.GetFiles(path))
            {
                var info = new FileInfo(file);
                _files.Add(new FileItem
                {
                    Name = info.Name,
                    FullName = info.FullName,
                    IsDirectory = false,
                    SizeText = FormatSize(info.Length),
                    ModifiedTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
                });
            }

            StatusText.Text = $"共 {_files.Count} 项";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"加载失败: {ex.Message}";
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        LoadDirectory(_currentPath);
    }

    private void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;

        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            try
            {
                var destPath = Path.Combine(dialog.SelectedPath, item.Name);
                if (item.IsDirectory)
                {
                    CopyDirectory(item.FullName, destPath);
                }
                else
                {
                    File.Copy(item.FullName, destPath, true);
                }
                StatusText.Text = $"已下载到: {destPath}";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"下载失败: {ex.Message}";
            }
        }
    }

    private void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            foreach (var fileName in dialog.FileNames)
            {
                try
                {
                    var destPath = Path.Combine(_currentPath, Path.GetFileName(fileName));
                    File.Copy(fileName, destPath, true);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"上传失败: {ex.Message}";
                    return;
                }
            }
            LoadDirectory(_currentPath);
            StatusText.Text = "上传完成";
        }
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;

        if (item.IsDirectory)
        {
            _currentPath = item.FullName;
            PathText.Text = _currentPath;
            LoadDirectory(_currentPath);
        }
        else
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.FullName,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"打开失败: {ex.Message}";
            }
        }
    }

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }
}

public class FileItem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string SizeText { get; set; } = "";
    public string ModifiedTime { get; set; } = "";
}
