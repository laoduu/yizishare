using System.Net.Sockets;

namespace YiziShare.Core;

/// <summary>修复作用域：决定 UI 渲染哪种操作闭环</summary>
public enum FixScope
{
    None,
    LocalElevated,   // 本机可一键修复（UAC 提权执行）
    Credentials,     // 需要用户输入凭据建立连接
    GuidedRemote     // 只能引导目标设备侧操作
}

public enum FixActionKind
{
    Cmd,                  // cmd.exe /c 执行（需提权时 Verb=runas）
    PowerShell,           // powershell -Command 执行（需提权时 Verb=runas）
    DeleteNetSession,     // 删除到目标的旧连接（免提权）
    OpenCredentialDialog, // 由界面层处理：弹出凭据对话框
    OpenExplorer          // 由界面层处理：资源管理器打开 \\ip
}

/// <summary>
/// 单个原子修复动作。一条动作只包含一条命令，禁止 && 跨宿主拼接（cmd 与 PowerShell 语法不兼容）。
/// </summary>
public sealed class FixAction
{
    /// <summary>按钮/步骤标题</summary>
    public string Title { get; init; } = "";
    /// <summary>执行前风险确认说明；null 表示无需确认</summary>
    public string? RiskNote { get; init; }
    public string Command { get; init; } = "";
    public FixActionKind Kind { get; init; }
}

/// <summary>
/// SMB 连接问题诊断引擎：端口探测 + IPC$ 真实 Win32 错误码 → 结构化诊断与修复方案
/// </summary>
public static class SmbDiagnostics
{
    public enum SmbIssueType
    {
        None,
        PortNotOpen,            // SMB端口不通
        LegacyNetBiosOnly,      // 仅NetBIOS(139)开放，445未开放
        FirewallBlocking,       // 防火墙阻止/网络路径不通(53)
        NetworkProfilePublic,   // 目标侧共享安全模型问题(1396)
        NetworkDiscoveryOff,    // 网络发现相关(1231)
        CredentialsConflict,    // 凭据冲突(1219)
        AccessDenied,           // 拒绝访问(5)
        LogonFailure,           // 登录失败(1326/86)
        GuestAuthBlocked,       // 来宾访问被策略阻止(1244)
        Smb1OnlyServer,         // 对端仅支持SMB1(58/64)
        Unknown                 // 未知错误
    }

    public class DiagnosisResult
    {
        public bool IsAccessible { get; set; }
        public SmbIssueType IssueType { get; set; }
        /// <summary>原始 Win32 错误码；-1 表示端口层失败未发起连接，0 表示成功</summary>
        public int Win32Error { get; set; } = -1;
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        /// <summary>纯文本总述（卡片顶部展示）</summary>
        public string Solution { get; set; } = "";
        public FixScope Scope { get; set; }
        /// <summary>本机可执行的动作序列（按序执行）</summary>
        public List<FixAction> Actions { get; } = [];
        /// <summary>目标设备侧的分步指引（GuidedRemote 渲染 + 复制）</summary>
        public List<string> ManualSteps { get; } = [];
    }

    /// <summary>
    /// 全面诊断指定IP的SMB连接问题。签名保持稳定，调用方无需感知内部实现变化。
    /// </summary>
    public static async Task<DiagnosisResult> DiagnoseAsync(string ip, int timeoutMs = 3000)
    {
        // 第1步：检测SMB直连端口(445)
        if (!await IsPortOpenAsync(ip, 445, timeoutMs))
        {
            // 检测NetBIOS端口(139)：判断是"完全不通"还是"仅旧协议"
            var port139Open = await IsPortOpenAsync(ip, 139, timeoutMs);
            var result = BuildPortResult(port139Open, ip);
            Logger.Info($"诊断 {ip}: {result.Title}");
            return result;
        }

        // 第2步：IPC$ 连接探测，拿真实 Win32 错误码（不再解析本地化文本输出）
        var code = await SmbProbe.ProbeIpcAsync(ip);

        // 凭据冲突：先自动清一次旧会话再重探，多数情况可直接自愈
        if (code == 1219)
        {
            await SmbProbe.DeleteSessionAsync(ip);
            code = await SmbProbe.ProbeIpcAsync(ip);
            if (code == 0)
            {
                Logger.Info($"诊断 {ip}: 凭据冲突已自动修复");
                return BuildAccessible();
            }
        }

        var diagnosis = code == 0 ? BuildAccessible() : BuildFromErrorCode(code, ip);
        Logger.Info($"诊断 {ip}: {diagnosis.Title} (Win32: {code})");
        return diagnosis;
    }

    private static DiagnosisResult BuildAccessible() => new()
    {
        IsAccessible = true,
        IssueType = SmbIssueType.None,
        Win32Error = 0,
        Scope = FixScope.None
    };

    /// <summary>端口层诊断结果构造（纯函数，单测覆盖）</summary>
    public static DiagnosisResult BuildPortResult(bool netbiosOnly, string ip)
    {
        if (netbiosOnly)
        {
            var result = new DiagnosisResult
            {
                IssueType = SmbIssueType.LegacyNetBiosOnly,
                Win32Error = -1,
                Scope = FixScope.GuidedRemote,
                Title = "目标仅开放旧版 NetBIOS 共享(SMB1)",
                Description = $"目标 {ip} 仅开放 NetBIOS 端口(139)，现代 SMB 直连端口(445)未开放。常见于老旧 NAS、路由器 USB 共享，或未安装「Microsoft 网络的文件和打印机共享」的设备。",
            };
            result.Solution = "优先在目标设备上启用 SMB2/SMB3；仅当目标确实无法升级时才考虑在本机临时启用 SMB1 客户端（有安全风险）。";
            result.ManualSteps.AddRange(
            [
                "【目标设备】NAS：进入管理后台升级固件，并在服务设置中启用 SMB2/SMB3",
                "【目标设备】Windows：网络适配器属性中确认已勾选「Microsoft 网络的文件和打印机共享」，并启动 Server 服务",
                "【不推荐·最后手段】本机临时启用 SMB1 客户端（管理员 PowerShell，需重启）：",
                "    Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client"
            ]);
            return result;
        }

        var closed = new DiagnosisResult
        {
            IssueType = SmbIssueType.PortNotOpen,
            Win32Error = -1,
            Scope = FixScope.GuidedRemote,
            Title = "SMB 服务未启动或端口被屏蔽",
            Description = $"无法连接到 {ip} 的 445 端口，目标设备的 SMB 服务可能未启动、防火墙拦截，或设备已关机。",
        };
        closed.Solution = "需要目标设备的操作，本程序无法远程修复。请按以下步骤在目标设备上检查：";
        closed.ManualSteps.AddRange(
        [
            "1. 确认目标设备已开机且与本机在同一局域网（IP 前三段相同）",
            "2. 控制面板 → 网络和共享中心 → 更改高级共享设置：开启「文件和打印机共享」",
            "3. 将目标网络位置设为「专用网络」而非「公用网络」",
            "4. services.msc 确认 Server 服务正在运行（管理员命令：net start LanmanServer）",
            $"5. 目标防火墙放行 SMB 入站（管理员命令）：netsh advfirewall firewall set rule group=\"@firewallapi.dll,-28502\" new enable=Yes"
        ]);
        return closed;
    }

    /// <summary>
    /// Win32 错误码 → 诊断结果映射（纯函数，单测核心）。
    /// </summary>
    public static DiagnosisResult BuildFromErrorCode(int win32Error, string ip)
    {
        return win32Error switch
        {
            1326 or 86 => MapLogonFailure(win32Error, ip),
            5 => MapAccessDenied(ip),
            1219 => MapCredentialsConflict(ip),
            1244 => MapGuestAuthBlocked(ip),
            53 => MapFirewallBlocking(ip),
            1231 or 1222 => MapNetworkDiscoveryOff(ip),
            1396 => MapTargetAccountName(ip),
            58 or 64 => MapSmb1OnlyServer(win32Error, ip),
            _ => MapUnknown(win32Error, ip),
        };
    }

    // 1326 登录失败 / 86 密码错误 —— 最常见的"需要登录"场景
    private static DiagnosisResult MapLogonFailure(int code, string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.LogonFailure,
            Win32Error = code,
            Scope = FixScope.Credentials,
            Title = code == 86 ? "密码错误（错误86）" : "需要登录（用户名或密码验证失败）",
            Description = $"目标 {ip} 启用了密码保护共享，当前凭据无法通过验证。",
        };
        result.Solution = $"在下方输入目标设备上有权限的账号即可连接。\n账号格式通常为「{ip}\\用户名」或「主机名\\用户名」；若目标是 NAS，请使用其管理界面创建的用户。";
        result.Actions.Add(new FixAction
        {
            Title = "🔑 输入用户名密码连接",
            Kind = FixActionKind.OpenCredentialDialog,
            Command = ""
        });
        return result;
    }

    // 5 访问被拒绝 —— 可能缺凭据，也可能目标侧权限配置问题
    private static DiagnosisResult MapAccessDenied(string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.AccessDenied,
            Win32Error = 5,
            Scope = FixScope.Credentials,
            Title = "错误5：访问被拒绝",
            Description = $"连接被拒绝，目标 {ip} 启用了密码保护共享或当前账号权限不足。",
        };
        result.Solution = "先尝试输入目标设备上的有效账号；若仍被拒绝，需要在目标设备上调整共享权限。";
        result.Actions.Add(new FixAction
        {
            Title = "🔑 输入用户名密码连接",
            Kind = FixActionKind.OpenCredentialDialog,
            Command = ""
        });
        result.ManualSteps.AddRange(
        [
            "【目标设备·方案A】允许无需密码的共享访问：",
            "    控制面板 → 网络和共享中心 → 更改高级共享设置 → 所有网络 → 选择「无密码保护的共享」→ 保存",
            "【目标设备·方案B】为本机创建专用账号，并用该账号在下方连接"
        ]);
        return result;
    }

    // 1219 同一目标不允许两套凭据并存
    private static DiagnosisResult MapCredentialsConflict(string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.CredentialsConflict,
            Win32Error = 1219,
            Scope = FixScope.LocalElevated,
            Title = "错误1219：已有其他凭据的连接",
            Description = $"本机已使用其他账号连接过 {ip}，Windows 不允许同一目标同时使用两套凭据。删除旧连接后即可重连。",
        };
        result.Solution = "点击下方按钮删除旧连接（无需管理员权限），随后会自动重新检测。";
        result.Actions.Add(new FixAction
        {
            Title = "🧹 断开旧连接并重试",
            Kind = FixActionKind.DeleteNetSession,
            Command = $"net use \\\\{ip} /delete /y"
        });
        return result;
    }

    // 1244 来宾(guest)访问被本机策略阻止 —— NAS/路由器 USB 共享最高频场景
    private static DiagnosisResult MapGuestAuthBlocked(string ip)
    {
        const string riskNote =
            "开启后，此类共享将以匿名方式明文访问，流量可能被窃听或篡改。" +
            "请仅在家庭等可信网络中开启；更安全的做法是在 NAS 上创建真实账号。";
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.GuestAuthBlocked,
            Win32Error = 1244,
            Scope = FixScope.LocalElevated,
            Title = "来宾访问被 Windows 安全策略阻止",
            Description = $"目标 {ip} 提供的是无需账号密码的「来宾(guest)」共享，但 Windows 10/11 默认禁止不安全的来宾访问。家用 NAS、路由器 USB 存储常属此类。",
        };
        result.Solution = riskNote + "\n如需恢复默认，以管理员运行：reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters\" /v AllowInsecureGuestAuth /t REG_DWORD /d 0 /f";
        result.Actions.Add(new FixAction
        {
            Title = "⚡ 允许来宾访问此设备（需管理员授权）",
            Kind = FixActionKind.Cmd,
            RiskNote = riskNote,
            Command = "reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\LanmanWorkstation\\Parameters\" /v AllowInsecureGuestAuth /t REG_DWORD /d 1 /f"
        });
        result.ManualSteps.AddRange(
        [
            "【更安全的替代方案】在 NAS/路由器管理界面创建真实账号、关闭 guest 访问，",
            "或升级固件启用 SMB2 + SMB 加密后重新扫描"
        ]);
        return result;
    }

    // 53 找不到网络路径 —— 本机侧多为公用网络配置/防火墙
    private static DiagnosisResult MapFirewallBlocking(string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.FirewallBlocking,
            Win32Error = 53,
            Scope = FixScope.LocalElevated,
            Title = "错误53：找不到网络路径",
            Description = $"无法建立到 {ip} 的网络路径。通常是本机处于「公用网络」配置或防火墙规则不全所致，也可能是目标未开启共享。",
        };
        result.Solution = "先尝试本机一键修复（将网络设为专用 + 放行文件和打印机共享）；无效则按右侧指引检查目标设备。";
        result.Actions.Add(new FixAction
        {
            Title = "⚡ 将本机网络设为专用网络（需管理员授权）",
            Kind = FixActionKind.PowerShell,
            RiskNote = "会将本机的网络连接全部设为「专用」，专用网络下邻居发现等功能开启。请仅在家庭/办公可信网络中执行。",
            Command = "Set-NetConnectionProfile -NetworkCategory Private"
        });
        result.Actions.Add(new FixAction
        {
            Title = "⚡ 放行文件和打印机共享防火墙规则（需管理员授权）",
            Kind = FixActionKind.Cmd,
            Command = "netsh advfirewall firewall set rule group=\"@firewallapi.dll,-28502\" new enable=Yes"
        });
        result.ManualSteps.AddRange(
        [
            "若上述修复后仍失败，请在目标设备上检查：",
            "1. 已开启「网络发现」和「文件和打印机共享」（高级共享设置）",
            "2. 网络位置为「专用网络」",
            $"3. 可直接在资源管理器地址栏输入 \\\\{ip} 测试"
        ]);
        return result;
    }

    // 1231/1222 网络位置不可达 —— 本机网络发现相关规则被禁用
    private static DiagnosisResult MapNetworkDiscoveryOff(string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.NetworkDiscoveryOff,
            Win32Error = 1231,
            Scope = FixScope.LocalElevated,
            Title = "错误1231：网络位置无法访问",
            Description = $"本机网络发现相关的防火墙规则可能被禁用，导致无法到达 {ip}。",
        };
        result.Solution = "先尝试本机一键修复（启用网络发现与文件打印机共享规则）；无效则需在目标设备上排查。";
        result.Actions.Add(new FixAction
        {
            Title = "⚡ 启用网络发现防火墙规则（需管理员授权）",
            Kind = FixActionKind.Cmd,
            Command = "netsh advfirewall firewall set rule group=\"@firewallapi.dll,-28503\" new enable=Yes"
        });
        result.Actions.Add(new FixAction
        {
            Title = "⚡ 启用文件和打印机共享防火墙规则（需管理员授权）",
            Kind = FixActionKind.Cmd,
            Command = "netsh advfirewall firewall set rule group=\"@firewallapi.dll,-28502\" new enable=Yes"
        });
        result.ManualSteps.AddRange(
        [
            "若修复后仍失败，请在目标设备上开启：",
            "设置 → 网络和 Internet → 高级网络设置 → 高级共享设置 → 打开「网络发现」和「文件和打印机共享」"
        ]);
        return result;
    }

    // 1396 目标账户名不正确 —— 工作组环境本地账户模型问题（目标侧）
    private static DiagnosisResult MapTargetAccountName(string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.NetworkProfilePublic,
            Win32Error = 1396,
            Scope = FixScope.GuidedRemote,
            Title = "错误1396：目标账户名不正确",
            Description = "通常出现在工作组环境中，目标的本地账户与网络登录方式冲突。需要目标设备操作。",
        };
        result.Solution = "请在目标设备上修改本地安全策略：";
        result.ManualSteps.AddRange(
        [
            "【专业版】Win+R 输入 secpol.msc → 本地策略 → 安全选项 →",
            "    「网络访问: 本地帐户的共享和安全模型」改为「经典 - 对本地用户进行身份验证」→ 重启",
            "【Home 版无 secpol】以管理员运行（等效注册表，需重启）：",
            "    reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\Lsa\" /v forceguest /t REG_DWORD /d 0 /f"
        ]);
        return result;
    }

    // 58/64 目标仅支持 SMB1 —— 老旧 NAS/嵌入式设备
    private static DiagnosisResult MapSmb1OnlyServer(int code, string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.Smb1OnlyServer,
            Win32Error = code,
            Scope = FixScope.GuidedRemote,
            Title = code == 64 ? "目标服务器不支持 SMB2/SMB3（错误64）" : "目标服务器不支持 SMB2/SMB3（错误58）",
            Description = $"指定的服务器无法执行请求的操作——目标 {ip} 很可能仅支持已被淘汰的 SMB1 协议（老旧 NAS、路由器 USB 共享常见）。",
        };
        result.Solution = "推荐在目标设备上启用 SMB2/SMB3；本机启用 SMB1 是最后的临时手段（有已知安全漏洞）。";
        result.ManualSteps.AddRange(
        [
            "【推荐】目标 NAS 升级固件，在服务/SMB 设置中启用 SMB2 或 SMB3 协议",
            "【不推荐·临时手段】本机启用 SMB1 客户端（管理员 PowerShell，需重启）：",
            "    Enable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol-Client"
        ]);
        return result;
    }

    private static DiagnosisResult MapUnknown(int code, string ip)
    {
        var result = new DiagnosisResult
        {
            IssueType = SmbIssueType.Unknown,
            Win32Error = code,
            Scope = FixScope.GuidedRemote,
            Title = $"连接失败（Win32 错误码：{code}）",
            Description = $"无法获取 {ip} 的共享信息。可先用资源管理器直接打开目标查看系统给出的具体提示。",
        };
        result.Solution = "通用排查步骤：";
        result.Actions.Add(new FixAction
        {
            Title = $"📂 在资源管理器中打开 \\\\{ip}",
            Kind = FixActionKind.OpenExplorer,
            Command = ""
        });
        result.ManualSteps.AddRange(
        [
            "1. 确认目标设备已开机且网络正常",
            "2. 检查是否在同一子网（IP 前三段相同）",
            "3. 尝试以管理员身份运行本程序后重新扫描"
        ]);
        return result;
    }

    private static async Task<bool> IsPortOpenAsync(string ip, int port, int timeoutMs)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port);
            var result = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            return result == connectTask && client.Connected;
        }
        catch { return false; }
    }
}
