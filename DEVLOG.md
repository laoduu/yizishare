# 开发日志

本文档记录了 YiziShare 项目的开发过程、技术决策和问题解决。

---

## 2024-08-24

### 一键诊断修复链路（诊断 → 修复 → 复检闭环）

**架构：**
```
SmbProbe（Win32 P/Invoke 探测）
  ├─ WNetAddConnection2 探测 \\ip\IPC$  → 真实 Win32 错误码
  ├─ WNetCancelConnection2 断开会话（1219 自动清理）
  └─ NetShareEnum 结构化枚举共享
        ↓
SmbDiagnostics（错误码 → 诊断结果映射）
  └─ DiagnosisResult：IssueType + FixScope(LocalElevated/Credentials/GuidedRemote) + Actions + ManualSteps
        ↓
RepairService（本机修复执行器）
  └─ cmd.exe / powershell.exe，Verb=runas 提权，逐条原子命令顺序执行
        ↓
DiagnosisCard（统一修复卡片）
  └─ 三种闭环：一键修复+自动复检 / 凭据对话框直连 / 分步指引+重新检测
```

**关键技术决策：**
1. **废弃 net view 文本解析**：本地化文本匹配有编码（OEM vs UTF8）+ 语言（中文/英文表头）双坑；改用 IPC$ 探测拿结构化 Win32 错误码（5/86/1219/1326/1244/53/1231/58/64…）
2. **NetShareEnum 为主、net view 兜底**：老 NAS/嵌入式设备 RPC 受限但响应 net view
3. **FixAction 单条原子命令**：禁止 `&&` 跨宿主拼接（cmd 与 PowerShell 语法不兼容）；宿主按命令类型选 `reg/net/netsh/sc → cmd`、`Set-*/Enable-WindowsOptionalFeature → powershell`
4. **密码只在内存传递**：作为 WNetAddConnection2 参数直传，不进命令行、不写凭据管理器；认证成功的会话刻意保留（explorer 免二次弹窗），失败的立即清理
5. **1244 来宾访问修复带风险确认**：明文传输风险提示 + NAS 侧替代方案 + 回滚命令（d=0）

**踩坑记录：**
- UAC 取消 = `Win32Exception.NativeErrorCode == 1223`，需单独文案，不能当程序故障
- `net view` 输出必须按 `GetOEMCP()` 解码（中文系统 GBK），否则共享名/错误文本乱码
- netsh 规则组名用 `@firewallapi.dll,-NNNNN` 资源 ID 形式（-28502 文件打印共享 / -28503 网络发现），保证语言无关
- NETRESOURCE 结构体的 Comment/Provider 字段必须声明为 string（int 会在 x64 下破坏布局）

### 黑金主题 + 明暗切换

**取色（来自 Assets/app.ico）：** 黑 `#0D0D0D` / 亮金 `#F0B429` / 操作金 `#C8912A` / 深藏蓝 `#1B2A4A`

**架构：**
```
ThemeColors.xaml       品牌层（渐变/金/藏蓝，双主题共用）
Surface.Light.xaml     浅色表面层（文字/卡片/语义色）
Surface.Dark.xaml      深色表面层（同键值）
ThemeManager           运行时替换 Surface 字典 + 偏好持久化 theme.txt
ThemeColors.cs         C# 侧动态读 Application 资源（代码拼 UI 用）
```

**要点：**
- XAML 表面色一律 `DynamicResource`（热切换立即生效）；代码拼装的 UI 经 `ThemeColors` 属性动态读取
- 细滚动条用**隐式全局样式**（去掉 x:Key），覆盖日志框等内嵌滚动条，颜色键随主题切换
- emoji 图标是固定彩色字体字形、不随主题 → 文件夹等换成 Segoe MDL2 Assets 单色字形 + 品牌金
- 主题切换按钮位于标题栏扫描键右侧（🌙/☀️），偏好持久化

**踩坑记录：**
- 主容器 `Background="White"` 硬编码 → 深色窄条模式下左列 300px 右侧 20px 露白（像素取样确认 x=301~319 纯白）
- logo 素材是黑底圆形徽章，在黑金标题栏上黑圈"隐身" → 加金圈描边 + 微亮底色
- WPF/WinForms 双命名空间歧义：Color/Brushes/Application/Clipboard 等需 using 别名
- 首次鼠标点击可能只激活窗口不触发按钮（连点两次更稳妥）

### 扫描层修复

1. **IP 范围全段覆盖**：`GenerateHostIps` 从 `.1~.254` 生成并排除网络/广播地址；原 Ping 只扫 `.0~.99`、端口漏 `.254`
2. **Ping 实例化**：每个并发任务独立 `new Ping()`（Ping 非线程安全），SemaphoreSlim 限流防大子网洪泛
3. **GetLocalSubnet 重写**：筛选「已启用 + 有 IPv4 网关 + 非 APIPA」网卡，按接口 Index 选取，排除 Hyper-V/WSL 虚拟网卡
4. **OEM 表**：键统一去冒号（修复 `DC:A6:32` 永不匹配）、扩充至约 45 条（群晖/威联通/小米/华为等）
5. **单元测试**：5 个测试类 38 个用例，全部纯函数可测（错误码映射/IP 生成/OEM/net view 解析/子网选择）

---

## 2024-08-20

### 全面UI重构：液态玻璃 + QQ风格

**核心改动：**
1. 无边框窗口设计（WindowStyle=None + WindowChrome）
2. 液态玻璃渐变蓝标题栏（1px半透明高光层）
3. QQ风格可折叠面板（默认窄条，点击展开）
4. 窗口自适应宽度（收起320px，展开900px）
5. 头尾统一圆角（顶部16,16,0,0，底部0,0,16,16）

**技术难点解决：**
- **DWM边框残留**：使用WindowChrome的GlassFrameThickness="0"完全禁用
- **头尾宽度不一致**：底部状态栏提升到外网格Row2，全宽布局
- **四角白边**：窗口背景设为Transparent，Border圆角裁剪
- **昵称弹窗崩溃**：使用this.FindResource从主窗口查找资源

**设计决策：**
- AllowsTransparency=True + 透明背景，Border圆角裁剪
- DropShadowEffect代替原生窗口阴影
- 关闭按钮行为改为最小化到托盘

### 清理废弃依赖
移除了3个未使用的NuGet包：MaterialDesignColors、MaterialDesignThemes、ModernWpf

### 系统托盘实现
Windows原生NotifyIcon + ContextMenuStrip

### 昵称持久化
JSON文件存储：%LocalAppData%\YiziShare\nicknames.json

### SMB诊断引擎
9种连接问题精准识别 + 中文解决方案 + 一键复制修复命令

---

## 2024-08-18

### 界面优化
- 仿QQ风格重新设计界面布局
- 修复共享文件夹显示
- 添加设备昵称功能

### 扫描功能优化
- 只显示有共享文件夹的设备
- 扫描流程：ARP缓存 → Ping → TCP端口扫描

---

## 技术决策记录

### 无边框窗口方案
WindowStyle=None + AllowsTransparency=True + WindowChrome + DropShadowEffect

### QQ风格面板
Grid列宽切换：收起 Column[1].Width=0，展开 Column[1].Width=Star
同步窗口宽度：320px ↔ 900px

### 扫描策略
| 阶段 | 方法 | 优点 | 缺点 |
|------|------|------|------|
| 1 | ARP缓存 | 快速准确 | 信息不全 |
| 2 | ICMP Ping | 简单通用 | 可能被防火墙阻止 |
| 3 | TCP端口扫描 | 准确检测SMB | 速度较慢 |

---

*文档持续更新中...*