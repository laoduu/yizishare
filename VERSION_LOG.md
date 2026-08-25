# 版本详细记录

本文档记录了每个版本的详细技术变更。

---

## Version 0.4.0 (2024-08-24)

### 新功能

#### 1. 一键诊断修复链路
**架构文件：**
- `YiziShare.Core/SmbProbe.cs` - Win32 P/Invoke 探测层（新增）
- `YiziShare.Core/SmbDiagnostics.cs` - 诊断引擎（重写）
- `YiziShare.Core/RepairService.cs` - 本机修复执行器（新增）
- `YiziShare.App/DiagnosisCard.cs` - 统一修复卡片（新增）
- `YiziShare.App/CredentialDialog.xaml(.cs)` - 应用内凭据对话框（新增）

**诊断流程：**
```csharp
// SmbProbe.ProbeIpcAsync：探测 \\ip\IPC$，返回真实 Win32 错误码
public static Task<int> ProbeIpcAsync(string ip, string? userName = null, string? password = null);

// SmbDiagnostics.DiagnoseAsync 主流程
1. TCP 445 不通 → 查 139 → BuildPortResult（仅NetBIOS / 端口未开）
2. 445 通 → ProbeIpcAsync(ip)
   - 1219 → 先 DeleteSessionAsync 清旧会话再重探
   - 0   → IsAccessible = true
   - 其他 → BuildFromErrorCode(code, ip)   // 纯函数映射，单测核心
```

**错误码映射表（BuildFromErrorCode）：**

| Win32码 | IssueType | FixScope | 界面动作 |
|---------|-----------|----------|----------|
| 1326/86 | LogonFailure | Credentials | 🔑 输入用户名密码连接 |
| 5 | AccessDenied | Credentials | 凭据按钮 + 目标侧指引 |
| 1219 | CredentialsConflict | LocalElevated | 🧹 断开旧连接并重试（免提权） |
| 1244 | GuestAuthBlocked | LocalElevated | ⚡ 允许来宾访问（风险确认+回滚说明） |
| 53 | FirewallBlocking | LocalElevated | ⚡ 网络设专用 + 防火墙放行 |
| 1231/1222 | NetworkDiscoveryOff | LocalElevated | ⚡ 启用网络发现防火墙规则 |
| 1396 | NetworkProfilePublic | GuidedRemote | 目标侧 secpol/注册表指引 |
| 58/64 | Smb1OnlyServer | GuidedRemote | 引导升级 SMB2/3（本机启用 SMB1 不推荐） |

**关键接口（FixAction 单条原子命令）：**
```csharp
public sealed class FixAction
{
    public string Title { get; init; } = "";
    public string? RiskNote { get; init; }      // 执行前风险确认
    public string Command { get; init; } = "";
    public FixActionKind Kind { get; init; }    // Cmd / PowerShell / DeleteNetSession / OpenCredentialDialog / OpenExplorer
}
```

**凭据安全设计：** 密码仅作为 `WNetAddConnection2` 参数在内存传递，不进命令行、不写凭据管理器；认证成功的会话刻意保留，失败的立即清理。

#### 2. 共享枚举改造（NetShareEnum 主路径）
**文件：** `YiziShare.Core/ShareFinder.cs`、`YiziShare.Core/SmbProbe.cs`

```csharp
// 结构化枚举，无编码/语言/空格解析问题，过滤 $ 管理共享
var shares = await SmbProbe.EnumSharesViaNetShareEnumAsync(ip);
// 为空则回退 net view：OEM 编码解码 + 语言无关解析（分隔行 + 多空格切列）
```

**删除的死代码：** `FindSharesAsync`/`EnumSharesRecursive`（递归传 IntPtr.Zero + `i<=count` 越界）、`TrySmbEnumShares` 管理共享猜测（普通用户必失败导致"需密码设备显示暂无共享"）。

#### 3. 黑金主题 + 明暗切换
**文件：** `YiziShare.App/ThemeColors.xaml`、`Surface.Light.xaml`、`Surface.Dark.xaml`、`ThemeColors.cs`、`ThemeManager.cs`（均为新增/重写）

```csharp
// ThemeManager：运行时替换 Surface 字典，偏好持久化
public static class ThemeManager
{
    public static bool IsDark { get; private set; }
    public static void Load();      // 启动时恢复偏好（主窗口渲染前调用）
    public static void Toggle();    // 切换并持久化到 %LocalAppData%\YiziShare\theme.txt
}
```

**取色（来自 app.ico）：** 黑 `#0D0D0D` / 亮金 `#F0B429` / 操作金 `#C8912A` / 深藏蓝 `#1B2A4A`。
品牌层（渐变/金/藏蓝）双主题共用；表面层（文字/卡片/语义色）Light/Dark 两套字典热切换。

#### 4. 单元测试
**文件：** `YiziShare.Tests/`（5 个测试类，38 个用例）

| 测试类 | 覆盖 |
|--------|------|
| SmbDiagnosticsMappingTests | 错误码→Scope 映射、1244 风险/回滚、58/64 非本机修复、无 `&&` 原子性、RDMA 文案回归 |
| IpRangeUtilTests | /24 返回 254 个 `.1~.254`、排除网络/广播、/30、非规范化输入 |
| SubnetSelectionTests | SubnetOf CIDR、APIPA/环回/网关过滤 |
| OemTableTests | 树莓派冒号键回归、小写 MAC、空/未知前缀 |
| NetViewParserTests | 中英文表头、含空格共享名、管理共享过滤 |

### Bug修复

| Bug | 原因 | 修复 |
|-----|------|------|
| 需密码设备显示"暂无共享" | TrySmbEnumShares 管理共享猜测普通用户必失败 | 移除，改用 NetShareEnum 主路径 |
| 1219 本地修复命令不显示 | UI 只渲染 TargetFixCommand | 修复卡按 Actions 统一渲染 |
| SMB1 文案误导 | 让用户启用 SMB Direct/RDMA（无关） | 重写为 SMB2/3 指引 + 本机 SMB1 不推荐 |
| 深色模式白边/滚动条未适配 | 主容器硬编码 White、滚动条样式局部 | 主题字典 + 全局隐式滚动条样式 |
| 中文共享名乱码 | net view 输出按 UTF8 错误解码 | GetOEMCP() 编码解码 |
| 扫描漏段 | Ping Take(100)/端口 Take(254) | GenerateHostIps 全段 + 限流 |

---

## Version 0.2.0 (2024-08-18)

### 新功能

#### 1. 设备昵称系统
**文件：** `YiziShare.Core/Device.cs`

```csharp
public class Device
{
    public string NickName { get; set; } = "";
    
    public string DisplayName => 
        string.IsNullOrEmpty(NickName) ? HostName : NickName;
}
```

**实现逻辑：**
- 添加NickName属性
- DisplayName计算属性优先返回昵称
- UI层点击✏️按钮弹出修改对话框

#### 2. 搜索过滤功能
**文件：** `YiziShare.App/MainWindow.xaml.cs`

```csharp
private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
{
    foreach (var child in DeviceListPanel.Children.OfType<Border>())
    {
        if (child.Tag is Device device)
        {
            var searchText = SearchBox.Text.ToLower();
            var match = device.DisplayName.ToLower().Contains(searchText) ||
                       device.IpAddress.ToLower().Contains(searchText);
            child.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
```

#### 3. 树形共享文件夹显示
**文件：** `YiziShare.App/MainWindow.xaml.cs`

```csharp
private void LoadSharesToDevice(Device device)
{
    var treeView = new TreeView();
    var rootItem = new TreeViewItem
    {
        Header = $"📁 {device.DisplayName} ({device.SharedFolders.Count} 个共享)",
        IsExpanded = true
    };

    foreach (var share in device.SharedFolders)
    {
        var shareItem = new TreeViewItem
        {
            Header = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "📂 " },
                    new TextBlock { Text = share.ShareName }
                }
            },
            Tag = share
        };
        rootItem.Items.Add(shareItem);
    }
    treeView.Items.Add(rootItem);
}
```

### 优化改进

#### 1. 扫描逻辑优化
**文件：** `YiziShare.Core/NetworkScanner.cs`

**变更前：**
```csharp
// 返回所有扫描到的设备
return [.. devices.Values];
```

**变更后：**
```csharp
// 只返回有共享的设备
var devicesWithShares = devices.Values
    .Where(d => d.HasShares || d.SharedFolders.Count > 0)
    .ToList();
return devicesWithShares;
```

#### 2. UI布局优化
**文件：** `YiziShare.App/MainWindow.xaml`

- 添加Window.Resources样式定义
- 圆角边框（CornerRadius=8）
- 边框颜色（#E0E0E0）
- 深色侧边栏（#2D2D2D）

### Bug修复

| Bug | 原因 | 修复 |
|-----|------|------|
| 共享文件夹重叠 | Grid缺少RowDefinitions | 添加RowDefinitions |
| 启动失败 | DropShadowEffect资源引用 | 改用BorderBrush |
| Thickness参数错误 | 使用了2参数构造函数 | 改用4参数构造函数 |
| TCP异步错误 | IAsyncResult没有GetAwaiter | 使用ConnectAsync+Task.WhenAny |

---

## Version 0.1.0 (2024-08-17)

### 核心功能

#### 1. 网络扫描器
**文件：** `YiziShare.Core/NetworkScanner.cs`

**技术实现：**

```csharp
// ARP缓存读取
private void ScanArpCache(ConcurrentDictionary<string, Device> devices)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = "arp",
            Arguments = "-a",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }
    };
    // 解析arp -a输出...
}

// TCP端口扫描
private async Task PortScanAsync(...)
{
    using var client = new TcpClient();
    var task = client.ConnectAsync(host, port);
    if (await Task.WhenAny(task, Task.Delay(timeoutMs)) == task)
    {
        return client.Connected;
    }
}
```

**扫描流程：**
1. 读取ARP缓存（快速）
2. ICMP Ping扫描（中等）
3. TCP 445端口扫描（准确）

#### 2. 共享文件夹发现
**文件：** `YiziShare.Core/ShareFinder.cs`

**方法1：net view命令**
```csharp
var startInfo = new ProcessStartInfo
{
    FileName = "net",
    Arguments = $"view \\\\{ip}",
    RedirectStandardOutput = true
};
```

**方法2：WMI查询（备用）**
```csharp
var scope = new ManagementScope($"\\\\{ip}\\root\\cimv2");
var query = new ObjectQuery("SELECT * FROM Win32_Share");
```

#### 3. WPF界面
**文件：** `YiziShare.App/MainWindow.xaml`

**布局结构：**
```
Window
├── Grid (两列)
│   ├── 左侧面板 (深色)
│   │   ├── 标题栏
│   │   ├── ScrollViewer
│   │   │   └── StackPanel (设备列表)
│   │   └── 状态栏
│   └── 右侧面板 (浅色)
│       ├── 标题卡片
│       └── 共享文件夹列表
```

### 项目结构

```
YiziShare/
├── YiziShare.Core/
│   ├── Device.cs           # 设备模型
│   ├── SharedFolder.cs     # 共享文件夹模型
│   ├── NetworkScanner.cs   # 网络扫描
│   └── ShareFinder.cs      # 共享发现
├── YiziShare.App/
│   ├── App.xaml            # 应用入口
│   ├── MainWindow.xaml     # 主窗口
│   └── MainWindow.xaml.cs  # 窗口逻辑
└── YiziShare.Tests/        # 单元测试
```

### 依赖项

| 包名 | 版本 | 用途 |
|------|------|------|
| .NET | 8.0 | 运行时 |
| WPF | 8.0 | UI框架 |
| xUnit | 2.0 | 测试框架 |

### 已知问题

1. **MaterialDesignThemes不兼容**
   - 问题：包在.NET 8上无法正常工作
   - 解决：移除依赖，使用原生WPF

2. **部分设备无法识别共享**
   - 问题：防火墙阻止ICMP
   - 解决：增加TCP端口扫描作为备用

---

## 版本对比

| 特性 | v0.1.0 | v0.2.0 | v0.3.0 | v0.4.0 |
|------|--------|--------|--------|--------|
| 设备扫描 | ✅ | ✅ | ✅ | ✅ |
| 共享发现 | ✅ | ✅ | ✅ | ✅ |
| 设备昵称 | ❌ | ✅ | ✅ | ✅ |
| 搜索过滤 | ❌ | ✅ | ✅ | ✅ |
| 树形显示 | ❌ | ✅ | ✅ | ✅ |
| 只显示有共享设备 | ❌ | ✅ | ✅ | ✅ |
| QQ风格界面 | ❌ | ✅ | ✅ | ✅ |
| 智能SMB诊断 | ❌ | ❌ | ✅ | ✅ |
| 一键修复（提权执行+复检） | ❌ | ❌ | ❌ | ✅ |
| 应用内凭据连接 | ❌ | ❌ | ❌ | ✅ |
| NetShareEnum 结构化枚举 | ❌ | ❌ | ❌ | ✅ |
| 黑金主题 | ❌ | ❌ | ❌ | ✅ |
| 明暗主题切换 | ❌ | ❌ | ❌ | ✅ |
| 单元测试 | ❌ | ❌ | ❌ | ✅ |
| 全段 IP 扫描覆盖 | ❌ | ❌ | ❌ | ✅ |

---

*文档持续更新中...*
