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

    public MainWindow()
    {
        ThemeManager.Load();
        InitializeComponent();
        _viewModel = (MainViewModel)DataContext;
        _viewModel.IsDarkTheme = ThemeManager.IsDark;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusMessage))
        {
            var message = _viewModel.StatusMessage;
            if (message.StartsWith("✅ 发现") && _notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(2000, "YiziShare", message, System.Windows.Forms.ToolTipIcon.Info);
            }
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

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.MouseDoubleClick += (_, _) => ShowFromTray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"托盘初始化失败: {ex.Message}");
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _notifyIcon?.ShowBalloonTip(1000, "YiziShare", "窗口已打开", System.Windows.Forms.ToolTipIcon.Info);
    }

    private async Task QuickScanAsync()
    {
        await _viewModel.ScanNetworkAsync();
        _notifyIcon?.ShowBalloonTip(2000, "YiziShare", $"扫描完成，发现 {_viewModel.Devices.Count} 台设备", System.Windows.Forms.ToolTipIcon.Info);
    }
    #endregion

    #region 窗口事件
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitNotifyIcon();
        BtnThemeToggle.Content = ThemeManager.IsDark ? "☀️" : "🌙";
        await NicknameStore.LoadAsync();
        await _viewModel.ScanNetworkAsync();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _notifyIcon?.Dispose();
        _notifyIcon = null;
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _notifyIcon?.ShowBalloonTip(1000, "YiziShare", "已最小化到系统托盘，双击图标恢复窗口", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.IsScanning)
            _ = _viewModel.ScanNetworkAsync();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _notifyIcon?.ShowBalloonTip(1000, "YiziShare", "已最小化到系统托盘，双击图标恢复窗口", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void HeaderBg_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2) return;
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
        ApplySort();
    }

    private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplySort();
    }

    private void ApplySort()
    {
        if (_viewModel == null || CmbSort == null) return;

        var devices = _viewModel.Devices.ToList();

        var sortIndex = CmbSort.SelectedIndex;
        devices = sortIndex switch
        {
            1 => devices.OrderBy(d => d.DisplayName).ToList(),
            2 => devices.OrderBy(d => d.Manufacturer).ThenBy(d => d.IpAddress).ToList(),
            _ => devices.OrderBy(d => d.IpAddress).ToList()
        };

        _viewModel.Devices.Clear();
        foreach (var device in devices)
            _viewModel.Devices.Add(device);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_viewModel) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ToggleTheme();
        BtnThemeToggle.Content = ThemeManager.IsDark ? "☀️" : "🌙";
        foreach (var device in _viewModel.Devices)
            device.RefreshTheme();
    }

    private void BtnTogglePanel_Click(object sender, RoutedEventArgs e)
    {
        CollapseRightPanel();
    }
    #endregion

    #region QQ风格折叠面板
    private void ExpandRightPanel()
    {
        var parentGrid = (Grid)RightPanel.Parent;
        parentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        RightPanel.Visibility = Visibility.Visible;
        BtnTogglePanel.Visibility = Visibility.Visible;

        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = Width,
            To = 900,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        BeginAnimation(WidthProperty, animation);
    }

    private void CollapseRightPanel()
    {
        var parentGrid = (Grid)RightPanel.Parent;
        parentGrid.ColumnDefinitions[1].Width = new GridLength(0);
        RightPanel.Visibility = Visibility.Collapsed;
        BtnTogglePanel.Visibility = Visibility.Collapsed;
        _viewModel.SelectedDevice = null;
        DeviceInfoPanel.Visibility = Visibility.Collapsed;

        var animation = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = Width,
            To = 320,
            Duration = TimeSpan.FromMilliseconds(250),
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn }
        };
        BeginAnimation(WidthProperty, animation);
    }
    #endregion

    #region 设备选择与操作
    private void DeviceListPanel_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is WpfButton) return;
        if (e.OriginalSource is FrameworkElement { DataContext: DeviceViewModel device })
        {
            SelectDevice(device);
        }
    }

    private void SelectDevice(DeviceViewModel device)
    {
        ExpandRightPanel();
        _viewModel.SelectedDevice = device;
        DeviceInfoPanel.Visibility = Visibility.Visible;
        UpdateDeviceInfoPanel(device);
    }

    private void UpdateDeviceInfoPanel(DeviceViewModel device)
    {
        DeviceTitle.Text = device.DisplayName;
        DeviceIp.Text = $"📌 {device.IpAddress}";
        DeviceMac.Text = $"🔗 {device.MacAddress}";
        DeviceAvatarIcon.Text = GetDeviceIcon(device.Model);

        var history = HistoryStore.GetEntry(device.IpAddress);
        DeviceLastSeen.Text = history != null
            ? $"最后连接: {history.LastSeen:yyyy-MM-dd HH:mm} | 连接次数: {history.ConnectCount}"
            : "";

        if (device.LastDiagnosis?.IsAccessible == true)
        {
            StatusBadge.Background = ThemeColors.SemanticGreenBgBrush;
            StatusText.Foreground = ThemeColors.SemanticGreenBrush;
            StatusText.Text = "🟢 已连接";
            BtnOpenAll.Visibility = device.SharedFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            BtnBrowse.Visibility = device.SharedFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (device.LastDiagnosis != null)
        {
            StatusBadge.Background = ThemeColors.SemanticOrangeBgBrush;
            StatusText.Foreground = ThemeColors.SemanticOrangeBrush;
            StatusText.Text = "🟠 " + device.LastDiagnosis.Title;
            BtnOpenAll.Visibility = Visibility.Collapsed;
            BtnBrowse.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusBadge.Background = ThemeColors.ListBgBrush;
            StatusText.Foreground = ThemeColors.TextTertiaryBrush;
            StatusText.Text = "⏳ 扫描中...";
            BtnOpenAll.Visibility = Visibility.Collapsed;
            BtnBrowse.Visibility = Visibility.Collapsed;
        }
    }

    private void BtnEditNickname_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice != null)
            ShowNicknameDialog(_viewModel.SelectedDevice);
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedDevice?.SharedFolders.Count > 0)
        {
            var firstShare = _viewModel.SelectedDevice.SharedFolders[0];
            var browser = new FileBrowserWindow(firstShare.Path) { Owner = this };
            browser.ShowDialog();
        }
    }

    private void BtnOpenAll_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenAllCommand.Execute(null);
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

    private void EditNickname_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem menuItem && menuItem.DataContext is DeviceViewModel device)
            ShowNicknameDialog(device);
    }
    #endregion

    #region 对话框
    private void ShowNicknameDialog(DeviceViewModel device)
    {
        var dlg = new Window
        {
            Title = "修改昵称",
            Width = 360, Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = ThemeColors.CardBgBrush,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true
        };

        var border = new Border
        {
            Background = ThemeColors.CardBgBrush,
            CornerRadius = new CornerRadius(12),
            BorderBrush = ThemeColors.CardBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(24)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = $"为 {device.IpAddress} 设置昵称：", Margin = new Thickness(0, 0, 0, 10), Foreground = ThemeColors.TextSecondaryBrush });

        var textBox = new WpfTextBox
        {
            Text = device.NickName,
            FontSize = 14,
            Padding = new Thickness(12, 8, 12, 8),
            Background = ThemeColors.InputBgBrush,
            Foreground = ThemeColors.TextPrimaryBrush,
            BorderThickness = new Thickness(0)
        };
        stack.Children.Add(textBox);

        var cancelBtn = new WpfButton
        {
            Content = "取消",
            Style = (Style)this.FindResource("SecondaryGlassButton"),
            Padding = new Thickness(20, 8, 20, 8)
        };
        cancelBtn.Click += (_, _) => dlg.Close();

        var okBtn = new WpfButton
        {
            Content = "确定",
            Style = (Style)this.FindResource("GlassButton"),
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(12, 0, 0, 0)
        };
        okBtn.Click += async (_, _) =>
        {
            var newName = textBox.Text.Trim();
            if (newName.Length > 20)
            {
                System.Windows.MessageBox.Show("昵称不能超过 20 个字符", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            if (!System.Text.RegularExpressions.Regex.IsMatch(newName, @"^[\w\u4e00-\u9fa5\-]+$"))
            {
                System.Windows.MessageBox.Show("昵称只能包含字母、数字、中文、下划线和短横线", "提示", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }
            _viewModel.ApplyNickname(device, newName);
            dlg.Close();
            UpdateDeviceInfoPanel(device);
        };

        var btnStack = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = WpfHorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        btnStack.Children.Add(okBtn);
        btnStack.Children.Add(cancelBtn);

        stack.Children.Add(btnStack);

        border.Child = stack;
        dlg.Content = border;
        dlg.ShowDialog();
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
