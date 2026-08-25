using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using YiziShare.Core;

namespace YiziShare.App;

public partial class MainWindow : Window
{
    private readonly NetworkScanner _scanner = new();
    private readonly ShareFinder _shareFinder = new();
    private Device? _selectedDevice;
    private bool _isScanning;
    private List<Device> _devices = [];
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    public MainWindow()
    {
        ThemeManager.Load();   // 在渲染前恢复主题偏好
        InitializeComponent();
        _scanner.LogMessage += OnLog;
    }

    #region 系统托盘
    private void InitNotifyIcon()
    {
        try
        {
            var iconPath = System.IO.Path.Combine(
                AppContext.BaseDirectory, "Assets", "app.ico");

            if (!System.IO.File.Exists(iconPath)) return;

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Icon = new System.Drawing.Icon(iconPath),
                Visible = true,
                Text = "YiziShare - 局域网共享"
            };

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem("📂 打开YiziShare");
            openItem.Click += (s, e) => ShowFromTray();
            contextMenu.Items.Add(openItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var scanItem = new System.Windows.Forms.ToolStripMenuItem("🔄 快速扫描");
            scanItem.Click += async (s, e) => await QuickScanAsync();
            contextMenu.Items.Add(scanItem);

            contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("❌ 退出");
            exitItem.Click += (s, e) =>
            {
                _notifyIcon?.Dispose();
                _notifyIcon = null;
                WpfApplication.Current.Shutdown();
            };
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.MouseDoubleClick += (s, e) => ShowFromTray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"托盘初始化失败: {ex.Message}");
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
        await ScanNetwork(_scanner.GetLocalSubnet());
        _notifyIcon?.ShowBalloonTip(2000, "YiziShare", $"扫描完成，发现 {_devices.Count} 台设备", System.Windows.Forms.ToolTipIcon.Info);
    }
    #endregion

    #region 窗口事件
    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitNotifyIcon();
        BtnThemeToggle.Content = ThemeManager.IsDark ? "☀️" : "🌙";
        await NicknameStore.LoadAsync();

        // 加载并应用已保存的昵称
        foreach (var device in _devices)
        {
            var saved = NicknameStore.GetNickname(device.IpAddress);
            if (!string.IsNullOrEmpty(saved))
                device.NickName = saved;
        }

        await ScanNetwork(_scanner.GetLocalSubnet());
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
            // 最小化到托盘
            Hide();
            _notifyIcon?.ShowBalloonTip(1000, "YiziShare", "已最小化到系统托盘，双击图标恢复窗口", System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e)
    {
        if (!_isScanning)
            _ = ScanNetwork(_scanner.GetLocalSubnet());
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
    {
        // 最小化到托盘
        Hide();
        _notifyIcon?.ShowBalloonTip(1000, "YiziShare", "已最小化到系统托盘，双击图标恢复窗口", System.Windows.Forms.ToolTipIcon.Info);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        // 关闭也最小化到托盘
        Hide();
    }

    private void HeaderBg_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            // 双击不做最大化（我们没有标题栏了）
            return;
        }
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        WpfMessageBox.Show("设置功能开发中...", "YiziShare", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    #endregion

    #region QQ风格折叠面板
    /// <summary>
    /// 展开右侧面板（同时加宽窗口，并显示标题栏收起按钮）
    /// </summary>
    private void ExpandRightPanel()
    {
        Width = 900;
        var parentGrid = (Grid)RightPanel.Parent;
        parentGrid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        RightPanel.Visibility = Visibility.Visible;
        BtnTogglePanel.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 收起右侧面板（同时缩窄窗口，隐藏标题栏收起按钮）
    /// </summary>
    private void CollapseRightPanel()
    {
        var parentGrid = (Grid)RightPanel.Parent;
        parentGrid.ColumnDefinitions[1].Width = new GridLength(0);
        RightPanel.Visibility = Visibility.Collapsed;
        BtnTogglePanel.Visibility = Visibility.Collapsed;
        _selectedDevice = null;
        DeviceInfoPanel.Visibility = Visibility.Collapsed;
        Width = 320;
    }

    /// <summary>标题栏 ◀ 按钮：收起右侧面板（原「✕ 收起」已迁移至此）</summary>
    private void BtnTogglePanel_Click(object sender, RoutedEventArgs e)
    {
        CollapseRightPanel();
    }

    /// <summary>明暗主题切换：换 Surface 字典后重绘代码拼装的界面</summary>
    private void BtnThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        BtnThemeToggle.Content = ThemeManager.IsDark ? "☀️" : "🌙";
        RenderDeviceList();
        if (_selectedDevice != null)
            SelectDevice(_selectedDevice);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DeviceListPanel == null) return;
        foreach (var child in DeviceListPanel.Children.OfType<Border>())
        {
            if (child.Tag is Device device)
            {
                var search = SearchBox.Text.ToLower().Trim();
                var match = string.IsNullOrEmpty(search) ||
                            device.DisplayName.ToLower().Contains(search) ||
                            device.IpAddress.ToLower().Contains(search) ||
                            device.HostName.ToLower().Contains(search) ||
                            device.NickName.ToLower().Contains(search);
                child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }
    #endregion

    #region 网络扫描
    private async Task ScanNetwork(string subnet)
    {
        _isScanning = true;
        BtnScan.IsEnabled = false;
        SetStatus($"🔍 正在扫描 {subnet}...");

        try
        {
            _devices = await _scanner.ScanAsync(subnet, timeoutMs: 4000);

            // 应用保存的昵称
            foreach (var device in _devices)
            {
                var saved = NicknameStore.GetNickname(device.IpAddress);
                if (!string.IsNullOrEmpty(saved))
                    device.NickName = saved;
            }

            Dispatcher.Invoke(() => RenderDeviceList());

            foreach (var device in _devices)
                _ = LoadSharesAsync(device);

            SetStatus($"✅ 发现 {_devices.Count} 台设备 | {DateTime.Now:HH:mm:ss}");
        }
        catch (Exception ex)
        {
            SetStatus($"❌ 扫描失败: {ex.Message}");
        }
        finally
        {
            _isScanning = false;
            BtnScan.IsEnabled = true;
        }
    }

    private async Task LoadSharesAsync(Device device)
    {
        try
        {
            var diagnosis = await SmbDiagnostics.DiagnoseAsync(device.IpAddress, timeoutMs: 3000);
            device.LastDiagnosis = diagnosis;

            if (diagnosis.IsAccessible)
            {
                var shares = await _shareFinder.GetSharesFromIpAsync(device.IpAddress);
                device.SharedFolders = shares;
                device.HasShares = shares.Count > 0;
            }
            else
            {
                device.HasShares = false;
                device.SharedFolders = [];
            }

            Dispatcher.Invoke(() =>
            {
                RefreshDeviceListItem(device);
                if (_selectedDevice?.IpAddress == device.IpAddress)
                    LoadSharesToPanel(device);
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载共享失败 {device.IpAddress}: {ex.Message}");
        }
    }

    private void RenderDeviceList()
    {
        DeviceListPanel.Children.Clear();
        if (_devices.Count == 0)
        {
            AddEmptyState();
            return;
        }
        foreach (var device in _devices.OrderBy(d => d.IpAddress))
            AddDeviceItem(device);
    }

    /// <summary>空态页：logo 水印 + 引导文案（QQ 空会话风格）</summary>
    private void AddEmptyState()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 70, 0, 0)
        };

        var logo = new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/logo.png")),
            Width = 64, Height = 64, Opacity = 0.35
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        logo.Clip = new EllipseGeometry(new System.Windows.Point(32, 32), 32, 32);
        panel.Children.Add(logo);

        panel.Children.Add(new TextBlock
        {
            Text = "未发现共享设备",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeColors.TextSecondaryBrush,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 14, 0, 0)
        });
        panel.Children.Add(new TextBlock
        {
            Text = "点击右上角 🔄 重新扫描\n或确认目标设备已开启共享",
            FontSize = 11,
            Foreground = ThemeColors.TextTertiaryBrush,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0)
        });

        DeviceListPanel.Children.Add(panel);
    }
    #endregion

    #region 界面渲染 - 设备项
    private void AddDeviceItem(Device device)
    {
        var isSelected = _selectedDevice == device;
        var border = new Border
        {
            Height = 72,
            Background = new SolidColorBrush(isSelected ? ThemeColors.AccentGoldSoft : ThemeColors.ListBg),
            BorderBrush = new SolidColorBrush(isSelected ? ThemeColors.AccentGold : System.Windows.Media.Color.FromArgb(0, 0, 0, 0)),
            BorderThickness = new Thickness(isSelected ? 3 : 0, 0, 0, 0),
            CornerRadius = new CornerRadius(12),
            Cursor = WpfCursors.Hand,
            Tag = device,
            Margin = new Thickness(12, 4, 12, 4)
        };

        var grid = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 头像
        var avatarPanel = new Grid { Width = 48, Height = 48 };
        var avatarBg = new Border
        {
            Width = 48, Height = 48,
            CornerRadius = new CornerRadius(24),
            Background = new SolidColorBrush(isSelected ? ThemeColors.NavyPrimary : ThemeColors.NavyHover)
        };
        var avatarText = new TextBlock
        {
            Text = GetDeviceIcon(device),
            FontSize = 20,
            Foreground = WpfBrushes.White,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        avatarPanel.Children.Add(avatarBg);
        avatarPanel.Children.Add(avatarText);

        // 在线状态点
        var onlineDot = new Ellipse
        {
            Width = 14, Height = 14,
            Fill = new SolidColorBrush(WpfColor.FromRgb(76, 175, 80)),
            Stroke = WpfBrushes.White,
            StrokeThickness = 2,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 2, 2)
        };
        avatarPanel.Children.Add(onlineDot);
        grid.Children.Add(avatarPanel);

        // 中间信息
        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        var nameRow = new StackPanel { Orientation = WpfOrientation.Horizontal };
        var nameText = new TextBlock
        {
            Text = device.DisplayName,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(isSelected ? ThemeColors.AccentGold : ThemeColors.TextPrimary),
            VerticalAlignment = VerticalAlignment.Center
        };
        var editBtn = new WpfButton
        {
            Content = "✏️",
            FontSize = 12,
            Style = (Style)FindResource("EditIconButton"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "修改昵称"
        };
        editBtn.Click += (s, e) => { e.Handled = true; ShowNicknameDialog(device); };

        nameRow.Children.Add(nameText);
        nameRow.Children.Add(editBtn);
        infoPanel.Children.Add(nameRow);

        infoPanel.Children.Add(new TextBlock
        {
            Text = $"{device.IpAddress} • {device.Manufacturer}",
            FontSize = 11,
            Foreground = ThemeColors.TextTertiaryBrush,
            Margin = new Thickness(0, 3, 0, 0)
        });

        var statusTb = new TextBlock
        {
            Text = device.StatusText,
            FontSize = 10,
            Foreground = ThemeColors.TextFaintBrush,
            Margin = new Thickness(0, 2, 0, 0)
        };
        infoPanel.Children.Add(statusTb);

        Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(infoPanel);

        // 右侧警告或箭头
        if (device.LastDiagnosis != null && !device.LastDiagnosis.IsAccessible)
        {
            var warnBtn = new WpfButton
            {
                Content = "⚠️",
                FontSize = 16,
                Background = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = WpfCursors.Hand,
                Margin = new Thickness(0, 0, 4, 0),
                ToolTip = device.LastDiagnosis.Title
            };
            // 点击警告=选中设备，进入详情面板查看统一修复卡
            warnBtn.Click += (s, e) => { e.Handled = true; SelectDevice(device); };
            Grid.SetColumn(warnBtn, 2);
            grid.Children.Add(warnBtn);
        }
        else if (device.HasShares)
        {
            var arrow = new TextBlock
            {
                Text = "▶",
                FontSize = 10,
                Foreground = ThemeColors.TextFaintBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(arrow, 2);
            grid.Children.Add(arrow);
        }

        border.Child = grid;

        // 右键菜单（QQ 会话右键风格）
        var menu = new ContextMenu();
        var openAllItem = new MenuItem
        {
            Header = "📂 打开全部共享",
            IsEnabled = device.SharedFolders.Count > 0
        };
        openAllItem.Click += (s, e) => { foreach (var share in device.SharedFolders) OpenShare(share); };
        var recheckItem = new MenuItem { Header = "🔄 重新检测" };
        recheckItem.Click += (s, e) => OnDiagnosisRecheck(device);
        var nickItem = new MenuItem { Header = "✏️ 修改昵称" };
        nickItem.Click += (s, e) => ShowNicknameDialog(device);
        menu.Items.Add(openAllItem);
        menu.Items.Add(recheckItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(nickItem);
        border.ContextMenu = menu;

        // 悬停
        border.MouseEnter += (s, e) =>
        {
            editBtn.Opacity = 1;
            if (!isSelected)
                border.Background = ThemeColors.AccentGoldSoftBrush;
        };
        border.MouseLeave += (s, e) =>
        {
            editBtn.Opacity = 0;
            if (!isSelected)
                border.Background = ThemeColors.ListBgBrush;
        };

        border.MouseLeftButtonDown += (s, e) => SelectDevice(device);
        DeviceListPanel.Children.Add(border);
    }

    private void RefreshDeviceListItem(Device device)
    {
        for (int i = 0; i < DeviceListPanel.Children.Count; i++)
        {
            if (DeviceListPanel.Children[i] is Border border && border.Tag is Device d && d.IpAddress == device.IpAddress)
            {
                DeviceListPanel.Children.RemoveAt(i);
                AddDeviceItem(device);
                var newItem = DeviceListPanel.Children[DeviceListPanel.Children.Count - 1];
                DeviceListPanel.Children.Remove(newItem);
                DeviceListPanel.Children.Insert(i, newItem);
                break;
            }
        }
    }
    #endregion

    #region 界面渲染 - 详情面板
    private void SelectDevice(Device device)
    {
        // 展开右侧面板（QQ风格：点击设备后展开对话面板）
        ExpandRightPanel();

        _selectedDevice = device;

        // 更新选中状态
        foreach (var child in DeviceListPanel.Children.OfType<Border>())
        {
            if (child.Tag is Device d)
            {
                var selected = d.IpAddress == device.IpAddress;
                child.Background = new SolidColorBrush(selected ? ThemeColors.AccentGoldSofter : ThemeColors.ListBg);
                child.BorderBrush = new SolidColorBrush(selected ? ThemeColors.AccentGold : System.Windows.Media.Color.FromArgb(0, 0, 0, 0));
                child.BorderThickness = new Thickness(selected ? 3 : 0, 0, 0, 1);
            }
        }

        DeviceInfoPanel.Visibility = Visibility.Visible;
        DeviceTitle.Text = device.DisplayName;
        DeviceIp.Text = $"📌 {device.IpAddress}";
        DeviceMac.Text = $"🔗 {device.MacAddress}";
        DeviceAvatarIcon.Text = GetDeviceIcon(device);

        if (device.LastDiagnosis?.IsAccessible == true)
        {
            StatusBadge.Background = ThemeColors.SemanticGreenBgBrush;
            StatusText.Foreground = ThemeColors.SemanticGreenBrush;
            StatusText.Text = "🟢 已连接";
            BtnOpenAll.Visibility = device.SharedFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        else if (device.LastDiagnosis != null)
        {
            StatusBadge.Background = ThemeColors.SemanticOrangeBgBrush;
            StatusText.Foreground = ThemeColors.SemanticOrangeBrush;
            StatusText.Text = "🟠 " + device.LastDiagnosis.Title;
            BtnOpenAll.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusBadge.Background = ThemeColors.ListBgBrush;
            StatusText.Foreground = ThemeColors.TextTertiaryBrush;
            StatusText.Text = "⏳ 扫描中...";
            BtnOpenAll.Visibility = Visibility.Collapsed;
        }

        LoadSharesToPanel(device);
    }

    private void LoadSharesToPanel(Device device)
    {
        ShareContentPanel.Children.Clear();

        if (device.LastDiagnosis != null && !device.LastDiagnosis.IsAccessible)
        {
            var card = new DiagnosisCard(device.LastDiagnosis, device.IpAddress);
            card.FixRequested += async (_, _) => await OnFixRequestedAsync(device, card);
            card.CredentialsRequested += async (_, _) => await OnCredentialsRequestAsync(device);
            card.RecheckRequested += (_, _) => OnDiagnosisRecheck(device);
            ShareContentPanel.Children.Add(card);
            return;
        }

        if (device.SharedFolders.Count == 0)
        {
            var emptyPanel = new StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 30, 0, 0)
            };
            emptyPanel.Children.Add(new TextBlock
            {
                FontFamily = new WpfFontFamily("Segoe MDL2 Assets"),
                Text = "\uE8B7",
                FontSize = 26,
                Foreground = ThemeColors.TextFaintBrush,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            });
            emptyPanel.Children.Add(new TextBlock
            {
                Text = "该设备暂无共享文件夹",
                Foreground = ThemeColors.TextTertiaryBrush,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0)
            });
            ShareContentPanel.Children.Add(emptyPanel);
            return;
        }

        foreach (var share in device.SharedFolders)
            ShareContentPanel.Children.Add(CreateShareCard(share));
    }

    private UIElement CreateShareCard(SharedFolder share)
    {
        var border = new Border
        {
            Background = ThemeColors.CardBgBrush,
            BorderBrush = ThemeColors.CardBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 8),
            Cursor = WpfCursors.Hand
        };
        border.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 8,
            ShadowDepth = 1,
            Direction = 270,
            Color = System.Windows.Media.Color.FromArgb(15, 0, 0, 0),
            Opacity = 0.3
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new TextBlock
        {
            FontFamily = new WpfFontFamily("Segoe MDL2 Assets"),
            Text = "\uE8B7",   // Folder 单色字形，随主题着色（emoji 为固定彩色，深色下不可见）
            FontSize = 20,
            Foreground = ThemeColors.AccentGoldBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        });

        var infoPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(new TextBlock { Text = share.ShareName, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = ThemeColors.TextPrimaryBrush });
        infoPanel.Children.Add(new TextBlock { Text = share.Path, FontSize = 10, Foreground = ThemeColors.TextFaintBrush, Margin = new Thickness(0, 2, 0, 0) });
        Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(infoPanel);

        var openBtn = new WpfButton
        {
            Content = "打开",
            Style = (Style)FindResource("GlassButton"),
            Background = ThemeColors.AccentGoldBrush,
            Padding = new Thickness(12, 5, 12, 5),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        };
        openBtn.Click += (s, e) => OpenShare(share);
        Grid.SetColumn(openBtn, 2);
        grid.Children.Add(openBtn);

        border.Child = grid;
        border.MouseEnter += (s, e) => border.Background = ThemeColors.CardHoverBrush;
        border.MouseLeave += (s, e) => border.Background = ThemeColors.CardBgBrush;
        border.MouseLeftButtonDown += (s, e) => { if (e.ClickCount >= 2) OpenShare(share); };

        return border;
    }
    #endregion

    #region 诊断修复
    /// <summary>「我修好了，重新检测」/ 修复后自动复检</summary>
    private void OnDiagnosisRecheck(Device device)
    {
        SetStatus($"🔄 正在重新检测 {device.IpAddress} ...");
        _ = LoadSharesAsync(device);
    }

    /// <summary>需要登录：应用内凭据对话框 → 建立连接 → 复检。密码仅在内存中传递。</summary>
    private async Task OnCredentialsRequestAsync(Device device)
    {
        string lastUser = "";
        string? lastError = null;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var dlg = new CredentialDialog(device.IpAddress, device.DisplayName, lastUser, lastError) { Owner = this };
            if (dlg.ShowDialog() != true) return;   // 用户取消

            lastUser = dlg.UserName;
            SetStatus($"🔑 正在连接 {device.IpAddress} ...");

            var code = await SmbProbe.ProbeIpcAsync(device.IpAddress, dlg.UserName, dlg.Password);
            if (code == 0)
            {
                // 会话已建立，复检会直接通过并枚举出共享
                OnDiagnosisRecheck(device);
                return;
            }

            lastError = code is 1326 or 86
                ? "用户名或密码错误，请重试（账号格式通常为 " + device.IpAddress + @"\用户名）"
                : $"连接失败（Win32 错误码 {code}）";
        }
        SetStatus($"❌ {device.IpAddress} 连接未成功，可稍后重试");
    }

    /// <summary>一键修复：风险确认 → UAC 提权逐条执行 → 无论成败都复检刷新界面</summary>
    private async Task OnFixRequestedAsync(Device device, DiagnosisCard card)
    {
        var diagnosis = device.LastDiagnosis;
        if (diagnosis == null) return;

        var risky = diagnosis.Actions.FirstOrDefault(a => !string.IsNullOrEmpty(a.RiskNote));
        if (risky?.RiskNote != null)
        {
            var confirm = WpfMessageBox.Show(
                risky.RiskNote + "\n\n确定要继续吗？",
                "执行前请确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
        }

        var actions = diagnosis.Actions
            .Where(a => a.Kind is FixActionKind.Cmd or FixActionKind.PowerShell or FixActionKind.DeleteNetSession)
            .ToList();
        if (actions.Count == 0) return;

        card.SetRunning(actions.Count);
        SetStatus($"🛠 正在对 {device.IpAddress} 执行一键修复...");

        var service = new RepairService();
        service.LogMessage += (_, line) => Dispatcher.Invoke(() => card.AppendLog(line));
        var result = await service.ExecuteAllAsync(actions);

        card.SetFinished(result.Success);
        OnDiagnosisRecheck(device);
    }
    #endregion

    #region 对话框
    private void ShowNicknameDialog(Device device)
    {
        var dlg = new Window
        {
            Title = "修改昵称",
            Width = 360, Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = ThemeColors.CardBgBrush,
            ResizeMode = ResizeMode.NoResize
        };

        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = $"为 {device.IpAddress} 设置昵称：", Margin = new Thickness(0, 0, 0, 8), Foreground = ThemeColors.TextSecondaryBrush });

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

        // 按钮区域
        var cancelBtn = new WpfButton
        {
            Content = "取消",
            Style = (Style)this.FindResource("SecondaryGlassButton"),
            Padding = new Thickness(20, 8, 20, 8)
        };
        cancelBtn.Click += (s, e) => dlg.Close();

        var okBtn = new WpfButton
        {
            Content = "确定",
            Style = (Style)this.FindResource("GlassButton"),
            Padding = new Thickness(20, 8, 20, 8),
            Margin = new Thickness(12, 0, 0, 0)
        };
        okBtn.Click += async (s, e) =>
        {
            var newName = textBox.Text.Trim();
            device.NickName = newName;
            await NicknameStore.SetNickname(device.IpAddress, newName);
            dlg.Close();
            RenderDeviceList();
            if (_selectedDevice?.IpAddress == device.IpAddress)
                SelectDevice(device);
        };

        var btnStack = new StackPanel { Orientation = WpfOrientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        btnStack.Children.Add(okBtn);
        btnStack.Children.Add(cancelBtn);

        stack.Children.Add(new TextBlock { Margin = new Thickness(0, 16, 0, 0) }); // spacer
        stack.Children.Add(btnStack);

        dlg.Content = stack;
        dlg.ShowDialog();
    }
    #endregion

    #region 操作
    private void BtnOpenAll_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice == null) return;
        foreach (var share in _selectedDevice.SharedFolders)
            OpenShare(share);
    }

    private void BtnEditNickname_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice != null) ShowNicknameDialog(_selectedDevice);
    }

    private void OpenShare(SharedFolder share)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = share.Path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"无法打开: {share.Path}\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

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

    private void SetStatus(string message) => BottomStatus.Text = message;

    private void OnLog(object? sender, string message) => Dispatcher.Invoke(() => BottomStatus.Text = message);
    #endregion
}
