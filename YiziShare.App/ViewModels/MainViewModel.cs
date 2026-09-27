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

    public MainViewModel()
    {
        _scanner.LogMessage += (_, msg) => StatusMessage = msg;

        ScanCommand = new RelayCommand(_ => _ = ScanNetworkAsync(), _ => !IsScanning);
        ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());
        TogglePanelCommand = new RelayCommand(_ => IsPanelExpanded = !IsPanelExpanded);
        OpenAllCommand = new RelayCommand(_ => OpenAllShares(), _ => SelectedDevice?.HasShares == true);
        EditNicknameCommand = new RelayCommand(_ => EditNickname());
        OpenShareCommand = new RelayCommand(param => OpenShare(param as SharedFolder));
        RecheckCommand = new RelayCommand(param => _ = RecheckDeviceAsync(param as DeviceViewModel));
        FixCommand = new RelayCommand(param => _ = ExecuteFixAsync(param as DeviceViewModel));
        CredentialsCommand = new RelayCommand(param => _ = RequestCredentialsAsync(param as DeviceViewModel));
    }

    public ObservableCollection<DeviceViewModel> Devices { get; } = [];

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

            StatusMessage = $"✅ 发现 {Devices.Count} 台设备 | {DateTime.Now:HH:mm:ss}";

            foreach (var device in Devices)
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

        for (int i = Devices.Count - 1; i >= 0; i--)
        {
            if (!newIpSet.Contains(Devices[i].IpAddress))
                Devices.RemoveAt(i);
        }

        foreach (var device in newDevices)
        {
            var existing = Devices.FirstOrDefault(d => d.IpAddress == device.IpAddress);
            if (existing == null)
            {
                Devices.Add(new DeviceViewModel(device));
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

        var sorted = Devices.OrderBy(d => d.IpAddress).ToList();
        Devices.Clear();
        foreach (var device in sorted)
            Devices.Add(device);
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

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var dlg = new CredentialDialog(device.IpAddress, device.DisplayName, lastUser, lastError) { Owner = App.Current.MainWindow };
            if (dlg.ShowDialog() != true) return;

            lastUser = dlg.UserName;
            StatusMessage = $"🔑 正在连接 {device.IpAddress} ...";

            var code = await SmbProbe.ProbeIpcAsync(device.IpAddress, dlg.UserName, dlg.Password);
            if (code == 0)
            {
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

    private void OpenShare(SharedFolder? share)
    {
        if (share == null) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = share.Path,
                UseShellExecute = true
            });
            Logger.Info($"打开共享: {share.Path}");
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
