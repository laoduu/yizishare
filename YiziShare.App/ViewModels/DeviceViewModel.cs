using System.Collections.ObjectModel;
using System.Windows.Media;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

public class DeviceViewModel : ViewModelBase
{
    private readonly Device _device;
    private bool _isSelected;

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
        set => SetProperty(ref _isSelected, value);
    }

    public WpfBrush BackgroundBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldSoftColor"])
        : new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["ListBgColor"]);

    public WpfBrush BorderBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldColor"])
        : new SolidColorBrush(Colors.Transparent);

    public WpfBrush AvatarBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["NavyPrimaryColor"])
        : new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["NavyHoverColor"]);

    public WpfBrush TextBrush => IsSelected
        ? new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["AccentGoldColor"])
        : new SolidColorBrush((WpfColor)WpfApplication.Current.Resources["TextPrimaryColor"]);

    public bool HasShares => _device.HasShares;
    public bool IsReachable => _device.IsReachable;
    public DateTime LastSeen => _device.LastSeen;

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
    }
}
