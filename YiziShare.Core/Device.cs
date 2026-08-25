namespace YiziShare.Core;

/// <summary>
/// 局域网设备数据模型
/// </summary>
public class Device
{
    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string HostName { get; set; } = "";
    public string NickName { get; set; } = ""; // 用户自定义昵称
    public string Manufacturer { get; set; } = "";
    public bool IsReachable { get; set; }
    public bool HasShares { get; set; } // 是否有共享文件夹
    public DateTime LastSeen { get; set; } = DateTime.Now;
    public List<SharedFolder> SharedFolders { get; set; } = [];
    
    // 诊断结果
    public SmbDiagnostics.DiagnosisResult? LastDiagnosis { get; set; }
    
    // 显示名称：优先用昵称，其次用主机名
    public string DisplayName => string.IsNullOrEmpty(NickName) ? HostName : NickName;
    
    // 状态描述
    public string StatusText => LastDiagnosis?.IsAccessible == true 
        ? $"📁 {SharedFolders.Count} 个共享" 
        : (LastDiagnosis == null ? "⏳ 扫描中..." : "⚠️ 无法访问");
}

/// <summary>
/// 共享文件夹数据模型
/// </summary>
public class SharedFolder
{
    public string ShareName { get; set; } = "";
    public string Path { get; set; } = "";
    public string DeviceIp { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsAccessible { get; set; }
}
