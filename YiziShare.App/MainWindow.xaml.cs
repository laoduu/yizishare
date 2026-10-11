using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using YiziShare.App.ViewModels;
using YiziShare.Core;
using WinFormsMenuItem = System.Windows.Forms.ToolStripMenuItem;
using WpfApp = System.Windows.Application;

namespace YiziShare.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _shuttingDown;

    public MainWindow()
    {
        ThemeManager.Load();
        InitializeComponent();
        _viewModel = (MainViewModel)DataContext;
        _viewModel.IsDarkTheme = ThemeManager.IsDark;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 聊天事件转发到界面：状态栏、自动滚动、后台提示音与托盘气泡
        _viewModel.Chat.StatusRequested += (_, message) => _viewModel.StatusMessage = message;
        _viewModel.Chat.MessageReceivedInBackground += OnMessageReceivedInBackground;
        _viewModel.Chat.MessageReceivedWhileViewing += OnMessageReceivedWhileViewing;
        _viewModel.Chat.AttachmentStateChanged += OnAttachmentStateChanged;

        // 打开共享 → 面板内置浏览（MainViewModel 只发请求，窗口负责 UI 切换）
        _viewModel.ShareOpenRequested += (_, path) => ShowShareBrowser(path);
        ShareBrowser.BackRequested += ShareBrowser_BackRequested;

        // 窗口显示/隐藏都要同步可见状态：隐藏到托盘时若不更新，
        // IsActive 会停留在 true，导致后台消息被当成"正在看"而静默标已读
        Activated += (_, _) => { UpdateChatVisibilityState(); MarkActiveConversationRead(); };
        Deactivated += (_, _) => UpdateChatVisibilityState();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusMessage))
        {
            var message = _viewModel.StatusMessage;
            if (message.StartsWith("✅ 发现"))
            {
                // 自绘横幅替代系统气泡（系统气泡样式不可控且自带系统提示音）
                NotificationBanner.ShowInfo("扫描完成", message.Replace("✅ ", ""));
            }
        }
        else if (e.PropertyName == nameof(MainViewModel.IsScanning) && !_viewModel.IsScanning)
        {
            // 扫描结束后按当前筛选与排序重建列表
            _viewModel.ApplyView();
        }
    }

    #region 系统托盘
    private void InitNotifyIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (!System.IO.File.Exists(iconPath)) return;

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconPath),
                Visible = true,
                Text = "YiziShare - 局域网共享"
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem("📂 打开YiziShare");
            openItem.Click += (_, _) => ShowFromTray();
            contextMenu.Items.Add(openItem);

            var quickAccessItem = new WinFormsMenuItem("⭐ 快速访问");
            contextMenu.Items.Add(quickAccessItem);
            RebuildQuickAccess(quickAccessItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var scanItem = new WinFormsMenuItem("🔄 快速扫描");
            scanItem.Click += async (_, _) => await QuickScanAsync();
            contextMenu.Items.Add(scanItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new WinFormsMenuItem("❌ 退出");
            exitItem.Click += (_, _) =>
            {
                _notifyIcon?.Dispose();
                _notifyIcon = null;
                WpfApp.Current.Shutdown();
            };
            contextMenu.Items.Add(exitItem);

            contextMenu.Opening += (_, _) => RebuildQuickAccess(quickAccessItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.MouseDoubleClick += (_, _) => ShowFromTray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"托盘初始化失败: {ex.Message}");
        }
    }

    private void ShowFromTray(bool balloon = true)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        UpdateChatVisibilityState();
        if (balloon)
            NotificationBanner.ShowInfo("YiziShare", "窗口已打开");
    }

    /// <summary>隐藏到托盘：必须同步可见状态，否则后台消息会被当成"正在查看"而静默标已读</summary>
    private void HideToTray(string tip)
    {
        Hide();
        UpdateChatVisibilityState();
        NotificationBanner.ShowInfo("已最小化到托盘", "双击托盘图标恢复窗口");
    }

    private void RebuildQuickAccess(System.Windows.Forms.ToolStripMenuItem quickAccessItem)
    {
        quickAccessItem.DropDownItems.Clear();

        // 收藏优先，其余按最近连接排序
        var favoriteIps = new HashSet<string>(FavoritesStore.GetAll(), StringComparer.OrdinalIgnoreCase);
        var entries = HistoryStore.GetRecentConnections(8)
            .OrderByDescending(e => favoriteIps.Contains(e.IpAddress))
            .ThenByDescending(e => e.LastSeen)
            .Take(6)
            .ToList();

        if (entries.Count == 0)
        {
            quickAccessItem.DropDownItems.Add(new WinFormsMenuItem("（暂无历史连接）") { Enabled = false });
            return;
        }

        foreach (var entry in entries)
        {
            bool isFavorite = favoriteIps.Contains(entry.IpAddress);
            var item = new WinFormsMenuItem($"{(isFavorite ? "⭐" : "🕒")} {entry.DeviceName} ({entry.IpAddress})");
            var ip = entry.IpAddress;
            item.Click += (_, _) => OpenDeviceFromTray(ip);
            quickAccessItem.DropDownItems.Add(item);
        }
    }

    private void OpenDeviceFromTray(string ipAddress)
    {
        ShowFromTray(balloon: false);

        // 从主列表查找（筛选为"收藏"时设备可能不在展示列表中）
        var device = _viewModel.AllDevices.FirstOrDefault(d => d.IpAddress == ipAddress);
        if (device == null)
        {
            _viewModel.StatusMessage = $"⚠️ {ipAddress} 当前不在列表中，试试重新扫描";
            return;
        }

        SelectDevice(device);

        // 快速访问的用途是打开共享：SelectDevice 默认落在聊天页，这里显式切到共享页
        SwitchTab(TabShare);

        if (device.SharedFolders.Count > 0)
        {
            _viewModel.OpenShareCommand.Execute(device.SharedFolders[0]);
        }
        else
        {
            _viewModel.RecheckCommand.Execute(device);
        }
    }

    private async Task QuickScanAsync()
    {
        await _viewModel.ScanNetworkAsync();
        NotificationBanner.ShowInfo("扫描完成", $"发现 {_viewModel.AllDevices.Count} 台设备");
    }
    #endregion

    #region 窗口事件
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitNotifyIcon();
        UpdateThemeGlyph();
        VersionText.Text = $"V{App.Version}";
        TrackImeComposition();
        await NicknameStore.LoadAsync();
        await FavoritesStore.LoadAsync();
        await _viewModel.StartChatAsync();
        await _viewModel.ScanNetworkAsync();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 同步等待会与 DispatcherSynchronizationContext 自死锁（UI 线程被占住，
        // 而异步落盘正等着回到 UI 线程继续），这里必须异步退出。
        if (!_shuttingDown)
        {
            e.Cancel = true;
            _shuttingDown = true;

            await _viewModel.StopChatAsync();
            _notifyIcon?.Dispose();
            _notifyIcon = null;

            _shuttingDown = false;
            Close();
            return;
        }

        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            HideToTray("已最小化到系统托盘，双击图标恢复窗口");

        ApplyWindowChromeState();
    }

    /// <summary>最大化时去掉圆角（圆角缺口会露出透明背景），恢复时还原；同步按钮字形与窗口宽度</summary>
    private void ApplyWindowChromeState()
    {
        bool maximized = WindowState == WindowState.Maximized;
        var radius = maximized ? new CornerRadius(0) : new CornerRadius(16);

        RootBorder.CornerRadius = radius;
        HeaderBorder.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(16, 16, 0, 0);
        HeaderHighlight.CornerRadius = HeaderBorder.CornerRadius;
        // MDL2 字形：最大化 ChromeMaximize(E922) / 还原 ChromeRestore(E923)
        BtnMaximize.Content = new System.Windows.Controls.TextBlock
        {
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 12,
            Text = maximized ? "\uE923" : "\uE922"
        };
        // 最大化状态下 hover 语义是"还原窗口大小"
        BtnMaximize.ToolTip = maximized ? "还原" : "最大化";

        if (!maximized)
        {
            // 从最大化恢复：宽度动画占位必须清掉，再按面板展开状态决定落回的宽度与上限
            BeginAnimation(WidthProperty, null);
            MaxWidth = RightPanel.Visibility == Visibility.Visible ? double.PositiveInfinity : CollapsedMaxWidth;
            Width = RightPanel.Visibility == Visibility.Visible ? ExpandedDefaultWidth : 320;
        }
        else
        {
            // 最大化铺满整个工作区，不受收起态宽度上限约束
            MaxWidth = double.PositiveInfinity;
        }
    }

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            // 展开面板时的宽度动画持有 Width 本地值，不清掉会阻止最大化铺满
            BeginAnimation(WidthProperty, null);
            WindowState = WindowState.Maximized;
        }
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsScanning)
            _ = _viewModel.ScanNetworkAsync();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        HideToTray("已最小化到系统托盘，双击图标恢复窗口");
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        UpdateChatVisibilityState();
    }

    private void HeaderBg_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 双击标题栏切换最大化/还原（QQ/微信惯例）
        if (e.ClickCount >= 2)
        {
            BtnMaximize_Click(sender, e);
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MainWindow_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F5)
        {
            if (!_viewModel.IsScanning)
                _ = _viewModel.ScanNetworkAsync();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.F && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            if (RightPanel.Visibility == Visibility.Visible)
            {
                CollapseRightPanel();
                e.Handled = true;
            }
        }
    }

    private void CmbGroup_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateViewOptions();
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateViewOptions();
    }

    private void UpdateViewOptions()
    {
        if (_viewModel == null) return;
        _viewModel.FilterFavorites = CmbGroup?.SelectedIndex == 1;
        _viewModel.SortMode = CmbSort?.SelectedIndex ?? 0;
        _viewModel.ApplyView();
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceViewModel device })
        {
            await device.ToggleFavoriteAsync();
            if (ReferenceEquals(_viewModel.SelectedDevice, device))
                UpdateDeviceInfoPanel(device);
            _viewModel.ApplyView();
        }
    }

    private async void BtnFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice is DeviceViewModel device)
        {
            await device.ToggleFavoriteAsync();
            UpdateDeviceInfoPanel(device);
            _viewModel.ApplyView();
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleTheme();
        UpdateThemeGlyph();
        foreach (var device in _viewModel.Devices)
            device.RefreshTheme();
    }

    /// <summary>主题切换按钮字形：深色主题显示太阳（点击转亮色），浅色显示月亮</summary>
    private void UpdateThemeGlyph()
    {
        ThemeGlyph.Text = ThemeManager.IsDark ? "\uE706" : "\uE708";
    }

    private void BtnTogglePanel_Click(object sender, RoutedEventArgs e)
    {
        CollapseRightPanel();
    }
    #endregion

    #region QQ风格折叠面板
    /// <summary>详情面板展开时的默认宽度</summary>
    private const double ExpandedDefaultWidth = 900;
    /// <summary>收起态（仅设备列表）的窗口最大宽度：默认 320 的 1.5 倍</summary>
    private const double CollapsedMaxWidth = 480;

    private void ExpandRightPanel()
    {
        var parentGrid = (Grid)RightPanel.Parent;
        // 列表恢复固定宽，详情面板占余下空间
        parentGrid.ColumnDefinitions[0].Width = new GridLength(300);
        parentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        RightPanel.Visibility = Visibility.Visible;
        BtnTogglePanel.Visibility = Visibility.Visible;
        // 展开态无宽度限制，也可最大化
        MaxWidth = double.PositiveInfinity;

        UpdateChatVisibilityState();
        UpdateTabIndicator();

        // 最大化状态下动画会与铺满尺寸打架，直接展开即可
        if (WindowState != WindowState.Maximized)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Width,
                To = ExpandedDefaultWidth,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            BeginAnimation(WidthProperty, animation);
        }
        else
        {
            BeginAnimation(WidthProperty, null);
        }
    }

    private void CollapseRightPanel()
    {
        var parentGrid = (Grid)RightPanel.Parent;
        // 列表铺满整窗：固定 300 列 + 空白右列会在拖宽窗口时留出大片空白
        parentGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        parentGrid.ColumnDefinitions[1].Width = new GridLength(0);
        RightPanel.Visibility = Visibility.Collapsed;
        BtnTogglePanel.Visibility = Visibility.Collapsed;
        _viewModel.SelectedDevice = null;
        DeviceInfoPanel.Visibility = Visibility.Collapsed;

        // 收起后切回聊天页，下次展开时是干净的默认状态
        // （SwitchTab 离开聊天页时会清空聊天对象）
        SwitchTab(TabChat);
        UpdateChatVisibilityState();

        // 收起态（仅设备列表）限宽 1.5 倍，最大化时解除
        if (WindowState != WindowState.Maximized)
        {
            MaxWidth = CollapsedMaxWidth;
        }

        if (WindowState != WindowState.Maximized)
        {
            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Width,
                To = 320,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
            };
            BeginAnimation(WidthProperty, animation);
        }
        else
        {
            BeginAnimation(WidthProperty, null);
        }
    }
    #endregion

    #region 设备选择与操作
    private void DeviceListPanel_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 点在按钮（含其内部文字/图标）上时不做卡片选择，避免吞掉按钮点击
        if (IsWithinButton(e.OriginalSource as DependencyObject)) return;
        if (e.OriginalSource is FrameworkElement { DataContext: DeviceViewModel device })
        {
            SelectDevice(device);
        }
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is WpfButton) return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void SelectDevice(DeviceViewModel device)
    {
        ExpandRightPanel();
        // 先定设备再切 tab：SwitchTab(聊天页) 内部会把聊天对象同步到当前选中设备
        _viewModel.SelectedDevice = device;
        DeviceInfoPanel.Visibility = Visibility.Visible;
        UpdateDeviceInfoPanel(device);

        // 点击设备默认进入聊天页（QQ 形态）：聊天为主，共享内容在右侧第二个标签
        SwitchTab(TabChat);
    }

    /// <summary>列表项右侧箭头：当前设备已展开则收起面板，否则展开该设备详情</summary>
    private void PanelArrow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DeviceViewModel device })
            return;

        if (ReferenceEquals(_viewModel.SelectedDevice, device))
            CollapseRightPanel();
        else
            SelectDevice(device);
    }

    private void UpdateDeviceInfoPanel(DeviceViewModel device)
    {
        DeviceTitle.Text = device.DisplayName;
        DeviceAvatarIcon.Text = GetDeviceIcon(device.Model);
        DeviceMeta.Text = BuildDeviceMeta(device);
        BtnFavorite.Foreground = device.FavoriteBrush;

        var history = HistoryStore.GetEntry(device.IpAddress);
        DeviceLastSeen.Text = history != null
            ? $"最后连接 {history.LastSeen:yyyy-MM-dd HH:mm} · {history.ConnectCount} 次"
            : "";
        DeviceLastSeen.Visibility = history != null ? Visibility.Visible : Visibility.Collapsed;

        // 状态徽标：绿=已连接，橙=不可访问（悬停看诊断详情），灰=扫描中
        if (device.LastDiagnosis?.IsAccessible == true)
        {
            StatusBadge.Background = ThemeColors.SemanticGreenBgBrush;
            StatusText.Foreground = ThemeColors.SemanticGreenBrush;
            StatusText.Text = "已连接";
            StatusBadge.ToolTip = null;
            BtnWake.Visibility = Visibility.Collapsed;
        }
        else if (device.LastDiagnosis != null)
        {
            StatusBadge.Background = ThemeColors.SemanticOrangeBgBrush;
            StatusText.Foreground = ThemeColors.SemanticOrangeBrush;
            StatusText.Text = "不可访问";
            StatusBadge.ToolTip = device.LastDiagnosis.Title;
            BtnWake.Visibility = string.IsNullOrWhiteSpace(device.MacAddress) ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            StatusBadge.Background = ThemeColors.ListBgBrush;
            StatusText.Foreground = ThemeColors.TextTertiaryBrush;
            StatusText.Text = "扫描中";
            StatusBadge.ToolTip = null;
            BtnWake.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>设备元信息合并为一行：📌 IP · 🔗 MAC · 📂 共享数</summary>
    private static string BuildDeviceMeta(DeviceViewModel device)
    {
        var parts = new List<string> { $"📌 {device.IpAddress}" };
        if (!string.IsNullOrWhiteSpace(device.MacAddress))
            parts.Add($"🔗 {device.MacAddress}");
        parts.Add(device.SharedFolders.Count > 0
            ? $"📂 {device.SharedFolders.Count} 个共享"
            : "📂 暂无共享");
        return string.Join("   ", parts);
    }

    private void BtnEditNickname_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice != null)
            ShowNicknameDialog(_viewModel.SelectedDevice);
    }

    private void OpenShare_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton btn && btn.DataContext is SharedFolder share)
            _viewModel.OpenShareCommand.Execute(share);
    }

    private void OpenAllShares_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenAllCommand.Execute(null);
    }

    private void Recheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is DeviceViewModel device)
            _viewModel.RecheckCommand.Execute(device);
    }

    private void WakeDevice_Click(object sender, RoutedEventArgs e)
    {
        DeviceViewModel? device = sender switch
        {
            System.Windows.Controls.MenuItem { DataContext: DeviceViewModel d } => d,
            FrameworkElement { DataContext: DeviceViewModel d } => d,
            _ => null
        };
        if (device != null)
            _ = WakeDeviceAsync(device);
    }

    private void BtnWake_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice != null)
            _ = WakeDeviceAsync(_viewModel.SelectedDevice);
    }

    private async Task WakeDeviceAsync(DeviceViewModel device)
    {
        if (string.IsNullOrWhiteSpace(device.MacAddress))
        {
            _viewModel.StatusMessage = $"⚠️ {device.DisplayName} 没有 MAC 地址，无法唤醒";
            return;
        }

        try
        {
            _viewModel.StatusMessage = $"📡 正在唤醒 {device.DisplayName} ({device.MacAddress})...";
            await WakeOnLan.WakeAsync(device.MacAddress);
            _viewModel.StatusMessage = $"📡 魔术包已发送到 {device.DisplayName}，稍后自动复查";

            // 等 5 秒让设备启动，然后自动复查该设备
            await Task.Delay(5000);
            if (device.LastDiagnosis?.IsAccessible != true)
            {
                _viewModel.StatusMessage = $"🔄 复查 {device.DisplayName} ...";
                _viewModel.RecheckCommand.Execute(device);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"唤醒失败: {device.IpAddress}", ex);
            _viewModel.StatusMessage = $"⚠️ 唤醒失败: {ex.Message}";
        }
    }

    private void EditNickname_Click(object sender, RoutedEventArgs e)
    {
        DeviceViewModel? device = sender switch
        {
            System.Windows.Controls.MenuItem { DataContext: DeviceViewModel d } => d,
            FrameworkElement { DataContext: DeviceViewModel d } => d,
            _ => null
        };
        if (device != null)
            ShowNicknameDialog(device);
    }
    #endregion

    #region 对话框
    private void ShowNicknameDialog(DeviceViewModel device)
    {
        var dlg = new NicknameDialog(device) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _viewModel.ApplyNickname(device, dlg.NicknameResult!);
            UpdateDeviceInfoPanel(device);
        }
    }
    #endregion

    #region 拖拽发送
    private void DeviceCard_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) &&
            sender is FrameworkElement { DataContext: DeviceViewModel device } &&
            device.SharedFolders.Count > 0)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            HighlightCard(sender, true);
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
    }

    private void DeviceCard_DragLeave(object sender, System.Windows.DragEventArgs e)
    {
        HighlightCard(sender, false);
    }

    private void DeviceCard_Drop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        HighlightCard(sender, false);

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        if (sender is not FrameworkElement { DataContext: DeviceViewModel device }) return;

        if (device.SharedFolders.Count == 0)
        {
            _viewModel.StatusMessage = $"⚠️ {device.DisplayName} 没有可用的共享目录";
            return;
        }

        if (device.SharedFolders.Count == 1)
        {
            StartDragSend(device, files, device.SharedFolders[0].Path);
            return;
        }

        ShowSharePicker(device, files);
    }

    private void StartDragSend(DeviceViewModel device, string[] files, string destDir)
    {
        TransferWindow.Enqueue(files, destDir);
        _viewModel.StatusMessage = $"📤 正在发送 {files.Length} 项到 {device.DisplayName}";
    }

    private void ShowSharePicker(DeviceViewModel device, string[] files)
    {
        var menu = new ContextMenu();
        foreach (var share in device.SharedFolders)
        {
            var destDir = share.Path;
            var item = new MenuItem { Header = $"{share.ShareName}  ({share.Path})" };
            item.Click += (_, _) => StartDragSend(device, files, destDir);
            menu.Items.Add(item);
        }
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static void HighlightCard(object? sender, bool on)
    {
        if (sender is not Border border) return;
        border.Effect = on
            ? new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = System.Windows.Media.Color.FromRgb(0xC8, 0x91, 0x2A),
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.8
            }
            : null;
    }
    #endregion

    #region 辅助方法
    private string GetDeviceIcon(Device device)
    {
        var mac = device.MacAddress.Replace(":", "").ToUpper();
        if (mac.StartsWith("B827EB") || mac.StartsWith("DCA632") || mac.StartsWith("E45F01")) return "🍓";
        if (mac.StartsWith("000C29") || mac.StartsWith("005056")) return "🖥️";
        if (device.Manufacturer.Contains("Apple")) return "🍎";
        if (device.Manufacturer.Contains("Dell")) return "🖥️";
        if (device.Manufacturer.Contains("HP")) return "🖨️";
        if (device.Manufacturer.Contains("TP") || device.Manufacturer.Contains("Tenda")) return "📡";
        return "💻";
    }
    #endregion
}
