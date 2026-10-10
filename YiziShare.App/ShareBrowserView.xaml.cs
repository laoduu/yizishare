using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YiziShare.App;

/// <summary>
/// 共享文件夹内置浏览视图：嵌在详情面板内（不弹窗），提供目录导航、上传下载、
/// 右键菜单与快捷键（Ctrl+C/X/V、Delete、F2）的完整文件操作，语义对齐资源管理器。
/// 路径均为对方的 UNC 共享，凭据已由 SMB 诊断阶段建立。
/// </summary>
public partial class ShareBrowserView : System.Windows.Controls.UserControl
{
    /// <summary>浏览根（共享文件夹本身），"上级"到此为止</summary>
    private string _rootPath = "";
    private string _currentPath = "";
    private readonly ObservableCollection<FileItem> _files = new();
    private int _loadSeq;

    /// <summary>剪切标记：粘贴时移动而非复制</summary>
    private readonly List<string> _cutPaths = new();

    public ShareBrowserView()
    {
        InitializeComponent();
        FileList.ItemsSource = _files;

        // 列头点击排序（列表模式；图标模式共享同一集合视图，同样生效）
        FileList.AddHandler(GridViewColumnHeader.ClickEvent, new RoutedEventHandler(ColumnHeader_Click));

        // 恢复上次的视图模式偏好
        ApplyViewMode(YiziShare.Core.SettingsStore.GetFileBrowseIconMode());
    }

    #region 列头排序

    private enum SortField { None, Name, Size, Modified }

    private SortField _sortField = SortField.None;
    private bool _sortDescending;

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader header) return;

        var field = header.Content switch
        {
            string s when s.StartsWith("名称") => SortField.Name,
            string s when s.StartsWith("大小") => SortField.Size,
            string s when s.StartsWith("修改时间") => SortField.Modified,
            _ => SortField.None
        };
        if (field == SortField.None) return;

        // 同列再次点击切换升降序，新列默认升序
        if (_sortField == field) _sortDescending = !_sortDescending;
        else { _sortField = field; _sortDescending = false; }

        ApplySortIndicators();
        ApplySort();
    }

    private void ApplySortIndicators()
    {
        if (FileList.View is not GridView grid || grid.Columns.Count < 3) return;

        grid.Columns[0].Header = HeaderText("名称", SortField.Name);
        grid.Columns[1].Header = HeaderText("大小", SortField.Size);
        grid.Columns[2].Header = HeaderText("修改时间", SortField.Modified);
    }

    private string HeaderText(string baseName, SortField field)
    {
        if (_sortField != field) return baseName;
        return _sortDescending ? baseName + " ▼" : baseName + " ▲";
    }

    private void ApplySort()
    {
        if (_sortField == SortField.None) return;

        var field = _sortField;
        bool desc = _sortDescending;

        if (System.Windows.Data.CollectionViewSource.GetDefaultView(_files)
            is System.Windows.Data.ListCollectionView view)
        {
            view.CustomSort = new FileItemComparer(field, desc);
        }
    }

    private sealed class FileItemComparer(SortField field, bool descending) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (x is not FileItem a) return y is FileItem ? -1 : 0;
            if (y is not FileItem b) return 1;

            int result = field switch
            {
                SortField.Name => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                SortField.Size => a.SizeBytes.CompareTo(b.SizeBytes),
                SortField.Modified => a.Modified.CompareTo(b.Modified),
                _ => 0
            };

            return descending ? -result : result;
        }
    }

    #endregion

    /// <summary>打开一个共享根目录</summary>
    public void Open(string path)
    {
        _rootPath = path;
        _currentPath = path;
        _cutPaths.Clear();
        PathText.Text = path;
        _ = LoadDirectoryAsync(path);
    }

    #region 视图模式（列表 / 图标）

    private bool _iconMode;

    /// <summary>切换 列表/图标 视图并记忆偏好</summary>
    private void ApplyViewMode(bool iconMode)
    {
        _iconMode = iconMode;
        YiziShare.Core.SettingsStore.SetFileBrowseIconMode(iconMode);

        if (iconMode)
        {
            FileList.View = null;
            FileList.ItemTemplate = (DataTemplate)Resources["IconItemTemplate"];
            FileList.ItemsPanel = (ItemsPanelTemplate)Resources["IconItemsPanel"];
            FileList.ItemContainerStyle = (Style)Resources["IconItemContainerStyle"];
            SetGlyph(BtnViewMode, "\uE8FD"); // 列表字形（当前处于图标模式，按钮切到列表）
            BtnViewMode.ToolTip = "切换为列表视图";

            // 关键：禁用横向滚动（附加属性会传入内部 ScrollViewer），
            // WrapPanel 拿到有限宽度才会换行；从视觉树里捞 ScrollViewer 的方式受模板应用时机影响，不可靠
            System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Disabled);
        }
        else
        {
            FileList.View = (ViewBase)Resources["DetailsView"];
            FileList.ItemTemplate = null;
            FileList.ItemsPanel = (ItemsPanelTemplate)Resources["DetailsItemsPanel"];
            FileList.ItemContainerStyle = (Style)Resources["DetailsItemContainerStyle"];
            SetGlyph(BtnViewMode, "\uE71D"); // 网格字形（当前处于列表模式，按钮切到图标）
            BtnViewMode.ToolTip = "切换为图标视图";

            System.Windows.Controls.ScrollViewer.SetHorizontalScrollBarVisibility(FileList, ScrollBarVisibility.Auto);
        }
    }

    /// <summary>把按钮内容设为 MDL2 字形（重建 TextBlock，避免与样式冲突）</summary>
    private static void SetGlyph(System.Windows.Controls.Button button, string glyph)
    {
        button.Content = new TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Text = glyph
        };
    }

    private void BtnViewMode_Click(object sender, RoutedEventArgs e) => ApplyViewMode(!_iconMode);

    #endregion

    #region 拖放传输

    /// <summary>从本机/资源管理器拖入文件 → 上传到当前目录（复用传输队列）</summary>
    private void FileList_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            FileList.Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["CardHoverBrush"];
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
    }

    private void FileList_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        FileList.Background = System.Windows.Media.Brushes.Transparent;
    }

    private void FileList_Drop(object sender, System.Windows.DragEventArgs e)
    {
        FileList.Background = System.Windows.Media.Brushes.Transparent;

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || files.Length == 0) return;

        // 同粘贴语义：重名询问（覆盖/跳过/取消），不静默覆盖
        var conflicts = files.Where(f =>
            File.Exists(System.IO.Path.Combine(_currentPath, System.IO.Path.GetFileName(f))) ||
            Directory.Exists(System.IO.Path.Combine(_currentPath, System.IO.Path.GetFileName(f)))).ToList();

        if (conflicts.Count > 0)
        {
            var preview = string.Join("\n", conflicts.Take(5).Select(f => "· " + System.IO.Path.GetFileName(f)));
            if (conflicts.Count > 5) preview += $"\n· ...等共 {conflicts.Count} 项";

            var choice = System.Windows.MessageBox.Show(
                $"目标文件夹中已有 {conflicts.Count} 个同名项：\n{preview}\n\n" +
                "「是」全部覆盖　「否」跳过重名项　「取消」中止上传",
                "上传冲突", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (choice == MessageBoxResult.Cancel)
            {
                StatusText.Text = "已取消上传";
                return;
            }

            if (choice == MessageBoxResult.No)
            {
                // 跳过重名：只上传不冲突的
                var conflictSet = new HashSet<string>(conflicts, StringComparer.OrdinalIgnoreCase);
                files = files.Where(f => !conflictSet.Contains(f)).ToArray();
                if (files.Length == 0)
                {
                    StatusText.Text = "全部为重名项，已跳过";
                    return;
                }
            }
        }

        TransferWindow.Enqueue(files, _currentPath,
            onQueueDrained: () => _ = LoadDirectoryAsync(_currentPath));
        StatusText.Text = $"已加入传输队列 {files.Length} 项";
    }

    /// <summary>从列表内拖出 → 以 FileDropList 发起拖拽，资源管理器/桌面接住即为下载（复制语义）</summary>
    private void FileList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (Selected is not FileItem item) return;
        if (e.OriginalSource is DependencyObject source && IsWithinSelectableItem(source) == false) return;

        // 位移超过阈值才启动拖拽，避免误触
        if (Math.Abs(_dragStart.X - e.GetPosition(FileList).X) < 6 &&
            Math.Abs(_dragStart.Y - e.GetPosition(FileList).Y) < 6) return;

        var list = new System.Collections.Specialized.StringCollection { item.FullName };
        var data = new System.Windows.DataObject();
        data.SetFileDropList(list);
        // 非目录与目录都可拖出；资源管理器对 UNC 路径执行复制语义
        System.Windows.DragDrop.DoDragDrop(FileList, data, System.Windows.DragDropEffects.Copy);
    }

    private System.Windows.Point _dragStart;

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(FileList);
    }

    private bool IsWithinSelectableItem(DependencyObject source)
    {
        while (source != null && source != FileList)
        {
            if (source is System.Windows.Controls.ListViewItem) return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void BtnOpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = _currentPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"打开资源管理器失败: {ex.Message}";
        }
    }

    #endregion

    /// <summary>用户点击"← 返回"：通知宿主回到共享列表</summary>
    public event EventHandler? BackRequested;

    /// <summary>异步枚举目录：UNC 不可达时同步枚举会冻结 UI 线程</summary>
    private async Task LoadDirectoryAsync(string path)
    {
        int seq = ++_loadSeq;
        _files.Clear();
        StatusText.Text = "正在加载...";
        BtnRefresh.IsEnabled = false;
        // 根目录没有"上级"可言：藏掉而不是禁用，避免出现一个看似坏掉的按钮
        BtnUp.Visibility = path != _rootPath ? Visibility.Visible : Visibility.Collapsed;

        try
        {
            var items = await Task.Run(() => EnumerateDirectory(path));

            if (seq != _loadSeq) return;

            foreach (var item in items)
                _files.Add(item);

            StatusText.Text = $"共 {_files.Count} 项";
        }
        catch (Exception ex)
        {
            if (seq == _loadSeq)
                StatusText.Text = $"加载失败: {ex.Message}";
        }
        finally
        {
            if (seq == _loadSeq)
                BtnRefresh.IsEnabled = true;
        }
    }

    private static List<FileItem> EnumerateDirectory(string path)
    {
        var result = new List<FileItem>();

        if (!Directory.Exists(path))
            return result;

        foreach (var dir in Directory.GetDirectories(path))
        {
            var info = new DirectoryInfo(dir);
            result.Add(new FileItem
            {
                Name = info.Name,
                FullName = info.FullName,
                IsDirectory = true,
                SizeText = "<目录>",
                SizeBytes = -1, // 目录排在文件前
                ModifiedTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                Modified = info.LastWriteTime
            });
        }

        foreach (var file in Directory.GetFiles(path))
        {
            var info = new FileInfo(file);
            result.Add(new FileItem
            {
                Name = info.Name,
                FullName = info.FullName,
                IsDirectory = false,
                SizeText = FormatSize(info.Length),
                SizeBytes = info.Length,
                ModifiedTime = info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                Modified = info.LastWriteTime,
                ShellIcon = Services.FileTypeIconCache.GetIconImage(info.Name)
            });
        }

        return result;
    }

    private void Navigate(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _currentPath = path;
        PathText.Text = path;
        _ = LoadDirectoryAsync(path);
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void BtnUp_Click(object sender, RoutedEventArgs e)
    {
        var parent = Path.GetDirectoryName(_currentPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(parent) || parent.Length < _rootPath.Length) return;
        Navigate(parent);
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => _ = LoadDirectoryAsync(_currentPath);

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;

        if (item.IsDirectory)
        {
            Navigate(item.FullName);
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

    private void BtnUpload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        if (dialog.ShowDialog() == true)
        {
            TransferWindow.Enqueue(dialog.FileNames, _currentPath,
                onQueueDrained: () => _ = LoadDirectoryAsync(_currentPath));
            StatusText.Text = $"已加入传输队列 {dialog.FileNames.Length} 项";
        }
    }

    private async void MenuDownload_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;

        var dialog = new System.Windows.Forms.FolderBrowserDialog();
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        try
        {
            var target = dialog.SelectedPath;
            StatusText.Text = $"正在下载 {item.Name} ...";
            await Task.Run(() => CopyPath(item.FullName, Path.Combine(target, item.Name)));
            StatusText.Text = $"已下载 {item.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"下载失败: {ex.Message}";
        }
    }

    #region 右键菜单

    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasSelection = FileList.SelectedItem is FileItem;
        var canPaste = System.Windows.Clipboard.ContainsFileDropList();

        MenuOpen.IsEnabled = hasSelection;
        MenuDownload.IsEnabled = hasSelection;
        MenuCopy.IsEnabled = hasSelection;
        MenuCut.IsEnabled = hasSelection;
        MenuPaste.IsEnabled = canPaste;
        MenuRename.IsEnabled = hasSelection;
        MenuDelete.IsEnabled = hasSelection;
    }

    private void MenuOpen_Click(object sender, RoutedEventArgs e) => FileList_MouseDoubleClick(sender, null!);

    private void MenuCopy_Click(object sender, RoutedEventArgs e) => CopySelection();

    private void MenuCut_Click(object sender, RoutedEventArgs e) => CutSelection();

    private void MenuPaste_Click(object sender, RoutedEventArgs e) => _ = PasteAsync();

    private void MenuRename_Click(object sender, RoutedEventArgs e) => _ = RenameAsync();

    private void MenuDelete_Click(object sender, RoutedEventArgs e) => _ = DeleteAsync();

    private FileItem? Selected => FileList.SelectedItem as FileItem;

    #endregion

    #region 剪贴板与文件操作

    private void CopySelection()
    {
        if (Selected is not FileItem item) return;
        _cutPaths.Clear();
        SetClipboardPaths([item.FullName]);
        StatusText.Text = $"已复制 {item.Name}";
    }

    private void CutSelection()
    {
        if (Selected is not FileItem item) return;
        _cutPaths.Clear();
        _cutPaths.Add(item.FullName);
        SetClipboardPaths([item.FullName]);
        StatusText.Text = $"已剪切 {item.Name}（粘贴后移动）";
    }

    private static void SetClipboardPaths(string[] paths)
    {
        var list = new System.Collections.Specialized.StringCollection();
        list.AddRange(paths);
        System.Windows.Clipboard.SetFileDropList(list);
    }

    /// <summary>粘贴：来源可以是本应用复制/剪切的 UNC 路径，也可以是资源管理器里的任意文件。<br/>
    /// 同名冲突不会静默覆盖，而是询问：覆盖 / 跳过重名 / 取消。</summary>
    private async Task PasteAsync()
    {
        if (!System.Windows.Clipboard.ContainsFileDropList())
        {
            StatusText.Text = "剪贴板中没有文件";
            return;
        }

        var sources = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToList();
        var isCut = sources.All(p => _cutPaths.Contains(p, StringComparer.OrdinalIgnoreCase)) && _cutPaths.Count > 0;

        // 冲突检测：目标目录里已有同名项
        var plans = sources
            .Select(s => (Source: s, Dest: Path.Combine(_currentPath, Path.GetFileName(s.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))))
            .ToList();
        var conflicts = plans.Where(p => Directory.Exists(p.Dest) || File.Exists(p.Dest)).ToList();

        bool overwriteAll = false;
        if (conflicts.Count > 0)
        {
            var preview = string.Join("\n", conflicts.Take(5).Select(c => "· " + Path.GetFileName(c.Dest)));
            if (conflicts.Count > 5) preview += $"\n· ...等共 {conflicts.Count} 项";

            var choice = System.Windows.MessageBox.Show(
                $"目标文件夹中已有 {conflicts.Count} 个同名项：\n{preview}\n\n" +
                "「是」全部覆盖　「否」跳过重名项　「取消」中止粘贴",
                "粘贴冲突", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (choice == MessageBoxResult.Cancel)
            {
                StatusText.Text = "已取消粘贴";
                return;
            }
            overwriteAll = choice == MessageBoxResult.Yes;
        }

        StatusText.Text = $"正在粘贴 {plans.Count} 项...";
        try
        {
            int copied = 0, skipped = 0;
            await Task.Run(() =>
            {
                foreach (var (source, dest) in plans)
                {
                    bool exists = Directory.Exists(dest) || File.Exists(dest);
                    if (exists && !overwriteAll)
                    {
                        skipped++;
                        continue;
                    }

                    if (isCut)
                        MovePath(source, dest);
                    else
                        CopyPath(source, dest);
                    copied++;
                }
            });

            StatusText.Text = (skipped > 0)
                ? $"已粘贴 {copied} 项，跳过 {skipped} 个重名项"
                : $"已粘贴 {copied} 项";
            _cutPaths.Clear();
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"粘贴失败: {ex.Message}";
        }
    }

    private async Task RenameAsync()
    {
        if (Selected is not FileItem item) return;

        var dlg = new InputDialog("重命名", $"将「{item.Name}」重命名为", item.Name) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var dest = Path.Combine(_currentPath, dlg.Value);
            await Task.Run(() => MovePath(item.FullName, dest));
            StatusText.Text = $"已重命名为 {dlg.Value}";
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"重命名失败: {ex.Message}";
        }
    }

    private async Task DeleteAsync()
    {
        if (Selected is not FileItem item) return;

        var confirm = System.Windows.MessageBox.Show(
            $"确定删除「{item.Name}」吗？此操作不可撤销。", "删除确认",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            await Task.Run(() =>
            {
                if (item.IsDirectory)
                    Directory.Delete(item.FullName, recursive: true);
                else
                    File.Delete(item.FullName);
            });
            StatusText.Text = $"已删除 {item.Name}";
            await LoadDirectoryAsync(_currentPath);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"删除失败: {ex.Message}";
        }
    }

    private static void CopyPath(string source, string dest)
    {
        if (Directory.Exists(source))
            CopyDirectory(new DirectoryInfo(source), dest);
        else
            File.Copy(source, dest, overwrite: true);
    }

    private static void CopyDirectory(DirectoryInfo source, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in source.GetFiles())
            file.CopyTo(Path.Combine(destDir, file.Name), overwrite: true);
        foreach (var dir in source.GetDirectories())
            CopyDirectory(dir, Path.Combine(destDir, dir.Name));
    }

    private static void MovePath(string source, string dest)
    {
        if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase)) return;
        if (Directory.Exists(source))
            Directory.Move(source, dest);
        else
            File.Move(source, dest, overwrite: true);
    }

    #endregion

    #region 快捷键

    private void Browser_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;

        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

        if (ctrl && e.Key == Key.C) { CopySelection(); e.Handled = true; }
        else if (ctrl && e.Key == Key.X) { CutSelection(); e.Handled = true; }
        else if (ctrl && e.Key == Key.V) { _ = PasteAsync(); e.Handled = true; }
        else if (e.Key == Key.Delete) { _ = DeleteAsync(); e.Handled = true; }
        else if (e.Key == Key.F2) { _ = RenameAsync(); e.Handled = true; }
    }

    #endregion

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
}

public class FileItem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public string SizeText { get; set; } = "";
    /// <summary>排序用真实字节数；目录为 -1（恒排在文件之前）</summary>
    public long SizeBytes { get; set; }
    public string ModifiedTime { get; set; } = "";
    public DateTime Modified { get; set; }

    /// <summary>按扩展名提取的 Shell 类型图标（资源管理器同源）；目录为 null（保持金色文件夹矢量）</summary>
    public System.Windows.Media.ImageSource? ShellIcon { get; set; }
    public bool HasShellIcon => ShellIcon != null;
}
