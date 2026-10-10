using System.Collections.ObjectModel;
using System.Windows.Media;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

public class DeviceViewModel : ViewModelBase
{
    private readonly Device _device;
    private bool _isSelected;
    private int _unreadCount;

    public DeviceViewModel(Device device)
    {
        _device = device;
        _device.PropertyChanged += (_, _) => OnPropertyChanged(string.Empty);
    }

    public Device Model => _device;

    public string IpAddress => _device.IpAddress;
    public string MacAddress => _device.MacAddress;
    public string HostName => _device.HostName;
    public string Manufacturer => _device.Manufacturer;
    public string NickName => _device.NickName;

    public string DisplayName
    {
        get => _device.DisplayName;
        set
        {
            _device.NickName = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            // 画刷与箭头方向均由 IsSelected 派生，需一并通知以刷新列表项外观
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
                OnPropertyChanged(nameof(BorderBrush));
                OnPropertyChanged(nameof(AvatarBrush));
                OnPropertyChanged(nameof(TextBrush));
                OnPropertyChanged(nameof(PanelArrowIcon));
                OnPropertyChanged(nameof(PanelArrowTip));
            }
        }
    }

    /// <summary>当前设备的详情面板已展开时箭头反向，提示点击可收起</summary>
    public string PanelArrowIcon => IsSelected ? "◀" : "▶";

    public string PanelArrowTip => IsSelected ? "收起详情面板" : "展开详情面板";

    public WpfBrush BackgroundBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldSoftColor"])
        : new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["ListBgColor"]);

    public WpfBrush BorderBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldColor"])
        : new SolidColorBrush(Colors.Transparent);

    public WpfBrush AvatarBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldColor"])
        : (WpfBrush)WpfApplication.Current.Resources["AccentGoldHoverBrush"];

    public WpfBrush TextBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldColor"])
        : new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["TextPrimaryColor"]);

    public bool HasShares => _device.HasShares;
    public bool IsReachable => _device.IsReachable;
    public DateTime LastSeen => _device.LastSeen;

    /// <summary>该设备的未读聊天消息数（由 MainViewModel 按 IP 从 ChatViewModel 同步）</summary>
    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            bool increased = value > _unreadCount;
            if (SetProperty(ref _unreadCount, value))
            {
                OnPropertyChanged(nameof(HasUnread));
                OnPropertyChanged(nameof(UnreadBadgeText));
                // 未读数上升沿触发头像脉冲动画（XAML 里的 DataTrigger 由该属性驱动）
                if (increased)
                {
                    UnreadPulse = true;
                    OnPropertyChanged(nameof(UnreadPulse));
                    _ = ResetPulseAfterDelayAsync();
                }
            }
        }
    }

    private bool _isChatOnline;

    /// <summary>对端 YiziShare 是否在线（UDP 心跳级，区别于"设备在网络上可达"）</summary>
    public bool IsChatOnline
    {
        get => _isChatOnline;
        set
        {
            if (SetProperty(ref _isChatOnline, value))
                OnPropertyChanged(nameof(ChatStatusTip));
        }
    }

    /// <summary>在线状态点的悬停提示</summary>
    public string ChatStatusTip => IsChatOnline
        ? "YiziShare 在线，可直接聊天"
        : "设备在线（未检测到 YiziShare），暂不可聊天";

    public bool HasUnread => UnreadCount > 0;

    public string UnreadBadgeText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    /// <summary>未读数上升沿置 true 一瞬，供头像脉冲动画的 DataTrigger 捕捉</summary>
    public bool UnreadPulse { get; private set; }

    /// <summary>动画播完自动复位，下一条消息的上升沿才能再次触发</summary>
    private async Task ResetPulseAfterDelayAsync()
    {
        await Task.Delay(450);
        UnreadPulse = false;
        OnPropertyChanged(nameof(UnreadPulse));
    }

    public bool IsFavorite => FavoritesStore.IsFavorite(_device.IpAddress);

    /// <summary>MDL2 字形：已收藏实心星(E735)，未收藏空心星(E734)</summary>
    public string FavoriteIcon => IsFavorite ? "\uE735" : "\uE734";

    public WpfBrush FavoriteBrush => IsFavorite
        ? (WpfBrush)WpfApplication.Current.Resources["AccentGoldBrightBrush"]
        : (WpfBrush)WpfApplication.Current.Resources["TextSecondaryBrush"];

    public async Task ToggleFavoriteAsync()
    {
        await FavoritesStore.ToggleAsync(_device.IpAddress);
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(FavoriteIcon));
        OnPropertyChanged(nameof(FavoriteBrush));
    }

    public ObservableCollection<SharedFolder> SharedFolders { get; } = [];

    public SmbDiagnostics.DiagnosisResult? LastDiagnosis
    {
        get => _device.LastDiagnosis;
        set
        {
            _device.LastDiagnosis = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(IsAccessible));
        }
    }

    public bool IsAccessible => LastDiagnosis?.IsAccessible == true;

    public string StatusText => LastDiagnosis?.IsAccessible == true
        ? $"📁 {SharedFolders.Count} 个共享"
        : (LastDiagnosis == null ? "⏳ 扫描中..." : "⚠️ 无法访问");

    public string StatusTitle => LastDiagnosis?.Title ?? "";

    public void UpdateShares(List<SharedFolder> shares)
    {
        SharedFolders.Clear();
        foreach (var share in shares)
            SharedFolders.Add(share);
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasShares));
    }

    public void RefreshDeviceInfo()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasShares));
        OnPropertyChanged(nameof(IsAccessible));
        OnPropertyChanged(nameof(LastDiagnosis));
    }

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(AvatarBrush));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(FavoriteBrush));
    }
}
