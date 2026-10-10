using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YiziShare.Core;

public class Device : INotifyPropertyChanged
{
    private string _ipAddress = "";
    private string _macAddress = "";
    private string _hostName = "";
    private string _nickName = "";
    private string _manufacturer = "";
    private bool _isReachable;
    private bool _hasShares;
    private DateTime _lastSeen = DateTime.Now;
    private List<SharedFolder> _sharedFolders = [];
    private SmbDiagnostics.DiagnosisResult? _lastDiagnosis;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string IpAddress
    {
        get => _ipAddress;
        set { _ipAddress = value; OnPropertyChanged(); }
    }

    public string MacAddress
    {
        get => _macAddress;
        set { _macAddress = value; OnPropertyChanged(); }
    }

    public string HostName
    {
        get => _hostName;
        set { _hostName = value; OnPropertyChanged(); }
    }

    public string NickName
    {
        get => _nickName;
        set { _nickName = value; OnPropertyChanged(); }
    }

    private string _peerName = "";

    /// <summary>对方自己设置的昵称（聊天广播携带）；显示优先级低于本机备注 NickName、高于 HostName</summary>
    public string PeerName
    {
        get => _peerName;
        set { _peerName = value; OnPropertyChanged(); }
    }

    public string Manufacturer
    {
        get => _manufacturer;
        set { _manufacturer = value; OnPropertyChanged(); }
    }

    public bool IsReachable
    {
        get => _isReachable;
        set { _isReachable = value; OnPropertyChanged(); }
    }

    public bool HasShares
    {
        get => _hasShares;
        set { _hasShares = value; OnPropertyChanged(); }
    }

    public DateTime LastSeen
    {
        get => _lastSeen;
        set { _lastSeen = value; OnPropertyChanged(); }
    }

    public List<SharedFolder> SharedFolders
    {
        get => _sharedFolders;
        set { _sharedFolders = value; OnPropertyChanged(); }
    }

    public SmbDiagnostics.DiagnosisResult? LastDiagnosis
    {
        get => _lastDiagnosis;
        set { _lastDiagnosis = value; OnPropertyChanged(); }
    }

    /// <summary>显示名：本机备注 > 对方自报昵称 > 主机名</summary>
    public string DisplayName =>
        !string.IsNullOrWhiteSpace(NickName) ? NickName :
        !string.IsNullOrWhiteSpace(PeerName) ? PeerName :
        HostName;

    public string StatusText => LastDiagnosis?.IsAccessible == true
        ? $"📁 {SharedFolders.Count} 个共享"
        : (LastDiagnosis == null ? "⏳ 扫描中..." : "⚠️ 无法访问");

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public class SharedFolder
{
    public string ShareName { get; set; } = "";
    public string Path { get; set; } = "";
    public string DeviceIp { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAccessible { get; set; }
}
