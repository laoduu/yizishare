using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly NetworkScanner _scanner = new();
    private readonly ShareFinder _shareFinder = new();
    private DeviceViewModel? _selectedDevice;
    private bool _isScanning;
    private string _searchText = "";
    private string _statusMessage = "就绪";
    private bool _isPanelExpanded = false;
    private bool _isDarkTheme;
    private bool _autoRefreshEnabled;
    private int _autoRefreshIntervalMinutes = 5;

    private readonly Dictionary<string, Device> _deviceCache = new();
    private DispatcherTimer? _autoRefreshTimer;
    private DateTimeOffset _lastNetworkChangeTime;
    private bool _rescanScheduled;

    public MainViewModel()
    {
        _scanner.LogMessage += (_, msg) => StatusMessage = msg;

        // 聊天发现的陌生对端补进主列表，保证用户能在设备列表里看到并发起会话
        Chat.PeerDiscovered += (_, info) => EnsureChatPeer(info.Ip, info.Name);

        // 对端自报昵称（心跳/消息携带）同步到设备卡：显示优先级 备注 > 对方昵称 > 主机名
        Chat.PeerPresenceChanged += (_, peer) =>
            WpfApplication.Current?.Dispatcher.BeginInvoke(() => SyncDevicePeerName(peer.PeerIp, peer.PeerName));

        // 对端在线期间改名（跃迁事件不覆盖改名场景）
        Chat.PeerProfileUpdated += (_, peer) =>
            WpfApplication.Current?.Dispatcher.BeginInvoke(() => SyncDevicePeerName(peer.PeerIp, peer.PeerName));

        // 会话未读数 → 设备卡片角标：IP 是会话与设备卡的公共键
        WireUpConversationUnread();

        ScanCommand = new RelayCommand(_ => _ = ScanNetworkAsync(), _ => !IsScanning);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        TogglePanelCommand = new RelayCommand(_ => IsPanelExpanded = !IsPanelExpanded);
        OpenAllCommand = new RelayCommand(_ => OpenAllShares(), _ => SelectedDevice?.HasShares == true);
        EditNicknameCommand = new RelayCommand(_ => EditNickname());
        OpenShareCommand = new RelayCommand(param => OpenShare(param as SharedFolder));
        RecheckCommand = new RelayCommand(param => _ = RecheckDeviceAsync(param as DeviceViewModel));
        FixCommand = new RelayCommand(param => _ = ExecuteFixAsync(param as DeviceViewModel));
        CredentialsCommand = new RelayCommand(param => _ = RequestCredentialsAsync(param as DeviceViewModel));

        // 网络变化（切 Wi-Fi / 插拔网线 / DHCP 续租）→ 防抖后自动重扫
        System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        _lastNetworkChangeTime = DateTimeOffset.UtcNow;
        if (_rescanScheduled) return;
        _rescanScheduled = true;
        _ = RescanAfterNetworkChangeAsync();
    }

    private async Task RescanAfterNetworkChangeAsync()
    {
        try
        {
            // 拖尾去抖：等最后一次变化后满 3 秒再扫
            while (true)
            {
                var sinceLast = DateTimeOffset.UtcNow - _lastNetworkChangeTime;
                if (sinceLast.TotalMilliseconds >= 3000) break;
                await Task.Delay(Math.Max(100, 3000 - (int)sinceLast.TotalMilliseconds));
            }

            if (!IsScanning)
            {
                Logger.Info("网络地址变化，自动重扫");
                StatusMessage = "🌐 网络已变化，自动重新扫描...";

                // 先重绑聊天（换网卡后广播地址变了），避免在线对端被误判为离线
                await Chat.RebindAsync();
                await ScanNetworkAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("网络变化重扫失败", ex);
        }
        finally
        {
            _rescanScheduled = false;
        }
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

    /// <summary>局域网聊天（服务包装与未读管理）</summary>
    public ChatViewModel Chat { get; } = new();

    /// <summary>启动聊天服务</summary>
    public async Task StartChatAsync()
    {
        // 我的昵称优先；未设置时对端显示本机机器名
        var myNick = SettingsStore.GetMyNickname();
        Chat.LocalName = string.IsNullOrWhiteSpace(myNick) ? Environment.MachineName : myNick;
        await Chat.StartAsync();
    }

    /// <summary>停止聊天服务并落盘历史</summary>
    public async Task StopChatAsync()
    {
        await Chat.StopAsync();
    }

    /// <summary>
    /// 把每个会话的未读数同步到对应设备卡（按 IP 匹配）：
    /// 会话集合动态增删，因此监听 CollectionChanged + 单个会话的 PropertyChanged。
    /// </summary>
    private void WireUpConversationUnread()
    {
        Chat.Conversations.CollectionChanged += (_, _) =>
        {
            foreach (var conversation in Chat.Conversations)
            {
                conversation.PropertyChanged -= OnConversationUnreadChanged;
                conversation.PropertyChanged += OnConversationUnreadChanged;
            }
            SyncAllDeviceUnread();
        };

        foreach (var conversation in Chat.Conversations)
            conversation.PropertyChanged += OnConversationUnreadChanged;
    }

    private void OnConversationUnreadChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not ConversationViewModel conversation) return;

        if (e.PropertyName == nameof(ConversationViewModel.UnreadCount))
            SyncDeviceUnread(conversation.PeerIp, conversation.UnreadCount);
        else if (e.PropertyName == nameof(ConversationViewModel.IsOnline))
            SyncDeviceChatOnline(conversation.PeerIp, conversation.IsOnline);
    }

    /// <summary>会话集合重建后（LoadHistory / 排序移动）全量对齐一次角标与聊天在线态</summary>
    private void SyncAllDeviceUnread()
    {
        foreach (var device in _masterDevices)
        {
            var conversation = FindConversationByIp(device.IpAddress);
            device.UnreadCount = conversation?.UnreadCount ?? 0;
            device.IsChatOnline = conversation?.IsOnline ?? false;
        }
    }

    private void SyncDeviceUnread(string peerIp, int unreadCount)
    {
        var device = _masterDevices.FirstOrDefault(d =>
            string.Equals(d.IpAddress, peerIp, StringComparison.OrdinalIgnoreCase));
        if (device != null)
            device.UnreadCount = unreadCount;
    }

    private void SyncDeviceChatOnline(string peerIp, bool online)
    {
        var device = _masterDevices.FirstOrDefault(d =>
            string.Equals(d.IpAddress, peerIp, StringComparison.OrdinalIgnoreCase));
        if (device != null)
            device.IsChatOnline = online;
    }

    /// <summary>把对端自报昵称写入设备卡（显示优先级：备注 > 对方昵称 > 主机名）</summary>
    private void SyncDevicePeerName(string peerIp, string peerName)
    {
        if (string.IsNullOrWhiteSpace(peerName)) return;

        var device = _masterDevices.FirstOrDefault(d =>
            string.Equals(d.IpAddress, peerIp, StringComparison.OrdinalIgnoreCase));
        if (device == null) return;

        if (!string.Equals(device.Model.PeerName, peerName, StringComparison.Ordinal))
        {
            device.Model.PeerName = peerName;
            device.RefreshDeviceInfo();
        }
    }

    private ConversationViewModel? FindConversationByIp(string ip)
    {
        return Chat.Conversations.FirstOrDefault(c =>
            string.Equals(c.PeerIp, ip, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 把"聊天认识但扫描没发现"的设备补进主列表，让用户在设备列表里也能看到并发起会话。
    /// </summary>
    public void EnsureChatPeer(string ipAddress, string peerName)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return;
        if (_masterDevices.Any(d => d.IpAddress == ipAddress)) return;

        var conversation = FindConversationByIp(ipAddress);
        var card = new DeviceViewModel(new Device
        {
            IpAddress = ipAddress,
            // 聊天发现的是对端自报昵称，存 PeerName（备注层 NickName 留给用户设置）
            PeerName = peerName,
            IsReachable = true,
            LastSeen = DateTime.Now
        });

        // 历史会话里已积压的未读数与聊天在线态立刻反映到新卡片上
        card.UnreadCount = conversation?.UnreadCount ?? 0;
        card.IsChatOnline = conversation?.IsOnline ?? false;
        if (conversation != null && !string.IsNullOrWhiteSpace(conversation.PeerName))
            card.Model.PeerName = conversation.PeerName;
        _masterDevices.Add(card);

        ApplyView();
    }

    /// <summary>让聊天模块拿到本机对外展示名（昵称优先）</summary>
    public void SetLocalChatName(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            Chat.LocalName = name;
    }

    /// <summary>全量设备（扫描合并的唯一真实来源，Devices 为筛选+排序后的展示视图）</summary>
    private readonly List<DeviceViewModel> _masterDevices = [];

    /// <summary>仅显示收藏设备（由界面筛选下拉控制）</summary>
    public bool FilterFavorites { get; set; }

    /// <summary>排序键：0=IP，1=名称（由界面排序下拉控制）</summary>
    public int SortMode { get; set; }

    public IReadOnlyList<DeviceViewModel> AllDevices => _masterDevices;

    /// <summary>按当前筛选与排序重建展示集合（收藏始终置顶）</summary>
    public void ApplyView()
    {
        IEnumerable<DeviceViewModel> query = _masterDevices;
        if (FilterFavorites)
            query = query.Where(d => d.IsFavorite);

        query = SortMode == 1
            ? query.OrderByDescending(d => d.IsFavorite).ThenBy(d => d.DisplayName)
            : query.OrderByDescending(d => d.IsFavorite).ThenBy(d => d.IpAddress);

        Devices.Clear();
        foreach (var device in query)
            Devices.Add(device);
    }

    public DeviceViewModel? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice != null)
                _selectedDevice.IsSelected = false;
            SetProperty(ref _selectedDevice, value);
            if (_selectedDevice != null)
                _selectedDevice.IsSelected = true;
            OnPropertyChanged(nameof(HasSelectedDevice));
        }
    }

    public bool HasSelectedDevice => _selectedDevice != null;

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        set
        {
            SetProperty(ref _isScanning, value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsPanelExpanded
    {
        get => _isPanelExpanded;
        set => SetProperty(ref _isPanelExpanded, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => SetProperty(ref _isDarkTheme, value);
    }

    public bool AutoRefreshEnabled
    {
        get => _autoRefreshEnabled;
        set
        {
            if (SetProperty(ref _autoRefreshEnabled, value))
            {
                UpdateAutoRefreshTimer();
            }
        }
    }

    public int AutoRefreshIntervalMinutes
    {
        get => _autoRefreshIntervalMinutes;
        set
        {
            if (SetProperty(ref _autoRefreshIntervalMinutes, value))
            {
                UpdateAutoRefreshTimer();
            }
        }
    }

    public ICommand ScanCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand TogglePanelCommand { get; }
    public ICommand OpenAllCommand { get; }
    public ICommand EditNicknameCommand { get; }
    public ICommand OpenShareCommand { get; }
    public ICommand RecheckCommand { get; }
    public ICommand FixCommand { get; }
    public ICommand CredentialsCommand { get; }

    public async Task ScanNetworkAsync(string? subnet = null)
    {
        IsScanning = true;
        StatusMessage = $"🔍 正在扫描 {subnet ?? _scanner.GetLocalSubnet()}...";

        try
        {
            var devices = await _scanner.ScanAsync(subnet ?? _scanner.GetLocalSubnet(), timeoutMs: 4000);

            foreach (var device in devices)
            {
                var saved = NicknameStore.GetNickname(device.IpAddress);
                if (!string.IsNullOrEmpty(saved))
                    device.NickName = saved;
            }

            UpdateDeviceCache(devices);

            var newDevices = devices.OrderBy(d => d.IpAddress).ToList();
            MergeDevices(newDevices);

            StatusMessage = $"✅ 发现 {_masterDevices.Count} 台设备 | {DateTime.Now:HH:mm:ss}";

            foreach (var device in _masterDevices)
                _ = LoadSharesAsync(device);
        }
        catch (Exception ex)
        {
            StatusMessage = $"❌ 扫描失败: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void UpdateDeviceCache(List<Device> devices)
    {
        _deviceCache.Clear();
        foreach (var device in devices)
            _deviceCache[device.IpAddress] = device;
    }

    private void MergeDevices(List<Device> newDevices)
    {
        var newIpSet = new HashSet<string>(newDevices.Select(d => d.IpAddress));

        for (int i = _masterDevices.Count - 1; i >= 0; i--)
        {
            if (!newIpSet.Contains(_masterDevices[i].IpAddress))
                _masterDevices.RemoveAt(i);
        }

        foreach (var device in newDevices)
        {
            var existing = _masterDevices.FirstOrDefault(d => d.IpAddress == device.IpAddress);
            if (existing == null)
            {
                var card = new DeviceViewModel(device);
                // 扫描新建的卡片要带出会话已知的未读数与聊天在线态
                var conversation = FindConversationByIp(device.IpAddress);
                card.UnreadCount = conversation?.UnreadCount ?? 0;
                card.IsChatOnline = conversation?.IsOnline ?? false;
                _masterDevices.Add(card);
            }
            else
            {
                existing.Model.HostName = device.HostName;
                existing.Model.MacAddress = device.MacAddress;
                existing.Model.Manufacturer = device.Manufacturer;
                existing.Model.IsReachable = device.IsReachable;
                existing.Model.LastSeen = device.LastSeen;
                existing.RefreshDeviceInfo();
            }
        }

        // 合并后按当前筛选与排序重建展示集合
        ApplyView();
    }

    private async Task LoadSharesAsync(DeviceViewModel device)
    {
        try
        {
            var diagnosis = await SmbDiagnostics.DiagnoseAsync(device.IpAddress, timeoutMs: 3000);
            device.LastDiagnosis = diagnosis;

            if (diagnosis.IsAccessible)
            {
                var shares = await _shareFinder.GetSharesFromIpAsync(device.IpAddress);
                device.UpdateShares(shares);
                device.Model.HasShares = shares.Count > 0;
            }
            else
            {
                device.UpdateShares([]);
                device.Model.HasShares = false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"加载共享失败 {device.IpAddress}", ex);
        }
    }

    private async Task RecheckDeviceAsync(DeviceViewModel? device)
    {
        if (device == null) return;
        StatusMessage = $"🔄 正在重新检测 {device.IpAddress} ...";
        await LoadSharesAsync(device);
    }

    private async Task ExecuteFixAsync(DeviceViewModel? device)
    {
        if (device?.LastDiagnosis == null) return;

        var actions = device.LastDiagnosis.Actions
            .Where(a => a.Kind is FixActionKind.Cmd or FixActionKind.PowerShell or FixActionKind.DeleteNetSession)
            .ToList();
        if (actions.Count == 0) return;

        StatusMessage = $"🛠 正在对 {device.IpAddress} 执行一键修复...";

        var service = new RepairService();
        service.LogMessage += (_, line) => StatusMessage = line;
        var result = await service.ExecuteAllAsync(actions);

        StatusMessage = result.Success ? "✅ 修复完成" : "❌ 修复未全部完成";
        await RecheckDeviceAsync(device);
    }

    private async Task RequestCredentialsAsync(DeviceViewModel? device)
    {
        if (device == null) return;

        string lastUser = "";
        string? lastError = null;

        // 已保存过凭据 → 先静默尝试自动重连
        var saved = WindowsCredentialManager.GetSavedCredential(device.IpAddress);
        if (saved != null && !string.IsNullOrEmpty(saved.Value.Password))
        {
            StatusMessage = $"🔑 正在使用保存的凭据连接 {device.IpAddress} ...";
            var savedCode = await SmbProbe.ProbeIpcAsync(device.IpAddress, saved.Value.UserName, saved.Value.Password);
            if (savedCode == 0)
            {
                StatusMessage = $"✅ 已用保存的凭据自动连接 {device.IpAddress}";
                await RecheckDeviceAsync(device);
                return;
            }

            lastUser = saved.Value.UserName;
            lastError = "保存的凭据已失效，请重新输入";
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var dlg = new CredentialDialog(device.IpAddress, device.DisplayName, lastUser, lastError) { Owner = App.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            lastUser = dlg.UserName;
            StatusMessage = $"🔑 正在连接 {device.IpAddress} ...";

            var code = await SmbProbe.ProbeIpcAsync(device.IpAddress, dlg.UserName, dlg.Password);
            if (code == 0)
            {
                if (dlg.RememberCredentials)
                {
                    if (WindowsCredentialManager.Save(device.IpAddress, dlg.UserName, dlg.Password))
                        StatusMessage = $"✅ 已连接，凭据已保存，下次自动连接 {device.IpAddress}";
                    else
                        StatusMessage = $"✅ 已连接 {device.IpAddress}（凭据保存失败）";
                }
                else
                {
                    // 明确不记住 → 清掉可能存在的旧凭据
                    WindowsCredentialManager.Delete(device.IpAddress);
                    StatusMessage = $"✅ 已连接 {device.IpAddress}";
                }

                await RecheckDeviceAsync(device);
                return;
            }

            lastError = code is 1326 or 86
                ? $"用户名或密码错误，请重试（账号格式通常为 {device.IpAddress}\\用户名）"
                : $"连接失败（Win32 错误码 {code}）";
        }
        StatusMessage = $"❌ {device.IpAddress} 连接未成功，可稍后重试";
    }

    public void ToggleTheme()
    {
        ThemeManager.Toggle();
        IsDarkTheme = ThemeManager.IsDark;
    }

    private void OpenAllShares()
    {
        if (SelectedDevice == null) return;
        foreach (var share in SelectedDevice.SharedFolders)
            OpenShare(share);
    }

    /// <summary>打开共享文件夹的请求（窗口订阅后切换到面板内置浏览视图）</summary>
    public event EventHandler<string>? ShareOpenRequested;

    private void OpenShare(SharedFolder? share)
    {
        if (share == null) return;
        try
        {
            ShareOpenRequested?.Invoke(this, share.Path);
            Logger.Info($"打开共享(内置浏览): {share.Path}");
            _ = HistoryStore.RecordConnectionAsync(share.DeviceIp, share.DeviceName);
        }
        catch (Exception ex)
        {
            Logger.Error($"打开共享失败: {share.Path}", ex);
            System.Windows.MessageBox.Show($"无法打开: {share.Path}\n{ex.Message}", "错误",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    private void EditNickname()
    {
        // 由 View 层处理弹窗
    }

    public void ApplyNickname(DeviceViewModel device, string newName)
    {
        device.Model.NickName = newName;
        device.DisplayName = newName;
        _ = NicknameStore.SetNickname(device.IpAddress, newName);
    }

    private void UpdateAutoRefreshTimer()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = null;

        if (AutoRefreshEnabled && AutoRefreshIntervalMinutes > 0)
        {
            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(AutoRefreshIntervalMinutes)
            };
            _autoRefreshTimer.Tick += async (_, _) => await ScanNetworkAsync();
            _autoRefreshTimer.Start();
        }
    }

    public void StartAutoRefresh()
    {
        UpdateAutoRefreshTimer();
    }

    public void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
        _autoRefreshTimer = null;
    }
}
