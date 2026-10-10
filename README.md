# YiziShare - 局域网即时通讯与文件共享工具

<div align="center">

<img src="YiziShare.App/Assets/logo.png" width="96" alt="YiziShare">

**局域网里的 QQ —— 聊天、传文件、共享文件夹**

无需服务器 · 无需联网 · 数据不出内网

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Windows](https://img.shields.io/badge/Windows-WPF-0078D4)
![Protocol](https://img.shields.io/badge/协议-UDP%2045455%20·%20TCP%2045402-F0B429)
![License](https://img.shields.io/badge/License-MIT-green)

</div>

---

## 📖 简介

**YiziShare** 是一款运行在 Windows 上的局域网即时通讯与文件共享工具。

它把两件事做进了一个应用：

- **💬 聊天** —— 同一局域网内的设备自动互相发现，像 QQ 一样发文字、彩色表情、图片和文件；对方离线时消息自动排队，上线即补发；每条消息都有送达/已读回执。
- **📂 共享** —— 自动发现局域网内的 SMB 共享文件夹，应用内直接浏览、上传、下载；SMB 连不上时内置诊断与一键修复。

没有中心服务器，没有账号体系，没有云端中转——所有数据只在你自己的设备和局域网内流转。界面采用黑金主题（支持明暗切换），无边框设计。

---

## ✨ 功能总览

### 💬 局域网聊天（核心）

- **自动发现** —— UDP 心跳广播（每 3 秒）+ TCP 直连兜底，同网段装了 YiziShare 的设备自动上线，设备列表实时显示在线状态
- **消息类型** —— 文本（含彩色表情）、图片（气泡内直接显示、点击看大图）、文件（卡片式展示、右键另存）
- **彩色表情** —— 内置 200+ Twemoji 表情面板，消息内以彩色图片渲染
- **离线补发** —— 对方不在线时消息进入待发队列，重新上线自动补发（状态显示「⏳ 待发送」）
- **投递回执** —— 每条消息实时显示状态：发送中 → 待发送/已发送 → 已送达 → 已读
- **昵称体系** —— 显示优先级：我给对方设的备注 > 对方自己设置的昵称 > 主机名；对方改昵称数秒内自动同步
- **消息提醒** —— 后台收信时设备头像角标 + 横幅通知 + 提示音；正在查看的会话静默
- **剪贴板直发** —— 输入框内直接 Ctrl+V 截图或文件，作为消息发出

> 附件单文件上限 5MB（v1 内联传输）；文本与彩色表情无限制。

### 📂 局域网共享（核心）

- **设备扫描** —— ARP 缓存 + ICMP Ping + SMB 445 端口三合一探测，全段覆盖；支持指定网卡、后台自动刷新
- **共享发现** —— NetShareEnum 结构化枚举所有共享文件夹（过滤管理共享）
- **SMB 智能诊断与一键修复** —— 连不上时自动识别 10+ 种原因（登录失败、来宾访问被阻止、凭据冲突、防火墙拦截、公共网络…），能本机修的 UAC 提权自动修，需要凭据的应用内直连
- **应用内文件浏览** —— 点开共享即在面板内浏览，无需资源管理器：列表/图标双视图、右键菜单与快捷键（Ctrl+C/X/V、F2、Delete）、复制剪切粘贴重命名删除、文件按类型显示系统图标
- **拖放传输** —— 文件拖入列表即上传（带进度与冲突询问），拖出即下载
- **传输队列** —— 多任务顺序执行、实时进度、可取消、完成自动关窗

### 🖥 通用体验

- **黑金主题** —— 深色/浅色一键切换，无边框圆角设计
- **系统托盘** —— 最小化常驻、未读计数、快速访问最近设备
- **横幅通知** —— 微信式右下角横幅（头像/预览/点击直达会话），来消息有提示音
- **网络唤醒** —— 对设备发起 Wake-on-LAN 魔术包
- **多网卡适配** —— 自动识别物理网卡，网络变化自动重扫与重连

---

## 🚀 快速开始

### 方式一：下载便携版（推荐）

从 [Releases](https://github.com/laoduu/yizishare/releases) 下载 `YiziShare-vX.X.X-x64.zip`，解压后直接运行 `YiziShare.App.exe`。

- ✅ 自包含单文件，**无需安装 .NET 运行时**
- ✅ 绿色便携，不写注册表
- ⚠️ 首次运行如弹出防火墙提示，请选择**允许**（否则收不到聊天消息）

### 方式二：从源码构建

```powershell
# 克隆
git clone https://github.com/laoduu/yizishare.git
cd yizishare

# 编译运行（需要 .NET 8 SDK + Windows）
dotnet build YiziShare.App/YiziShare.App.csproj -c Debug
dotnet run --project YiziShare.App

# 运行单元测试
dotnet test YiziShare.Tests

# 发布自包含单文件版
dotnet publish YiziShare.App/YiziShare.App.csproj -c Release -r win-x64 `
  --self-contained true -p:PublishSingleFile=true -o publish
```

---

## 💬 使用指南

### 聊天

1. 两台设备都运行 YiziShare，设备列表会自动出现对方（绿色圆点 = 可聊天）
2. 点击设备 → 默认进入聊天页 → 输入消息回车发送
3. 表情面板 / 文件按钮 / 直接粘贴截图或文件均可发送
4. 对方离线？照发——消息进入待发队列，对方上线自动送达
5. 给常用设备设置备注昵称：右键设备 → 修改昵称

### 共享文件夹

1. 点击设备展开详情 → 「📁 共享」标签查看对方开放的文件夹
2. 点「打开」→ 在应用内浏览：双击进入、右键操作、拖放上传下载
3. 连不上？按诊断卡片提示操作（大多数问题可一键修复）

### 设置

- **我的昵称** —— 广播给局域网内所有人（优先级低于好友备注）
- **提示音 / 网卡 / 刷新间隔 / 开机自启** —— 常规 tab
- **应用信息与源码** —— 关于 tab

---

## ⚙️ 工作原理

```
┌─────────────┐   UDP :45455 心跳(3s)   ┌─────────────┐
│  设备 A      │ ──────────────────────► │  设备 B      │
│             │ ◄────────────────────── │             │
│  TCP :45402 │    在线表 + 昵称/状态    │             │
│  (顺延至    │ ──────────────────────► │             │
│   45412)    │   JSON 长度前缀帧        │             │
│             │ ◄────────────────────── │             │
└─────────────┘  聊天/图片/文件/回执/附件 └─────────────┘
```

- **发现**：UDP 心跳宣告 `NodeId`（持久化设备身份，重装可恢复）、昵称与 TCP 端口；15 秒无心跳判定离线
- **传输**：TCP 点对点，4 字节长度前缀 + UTF-8 JSON 帧；双向拨号用 NodeId 字典序仲裁，只保留一条链路
- **可靠性**：每条消息有 MsgId 幂等去重、Delivered/Read 回执、离线待发队列、投递状态机（含超时降级）
- **文件**：单帧内联 base64（≤5MB），接收方落盘至 `%LOCALAPPDATA%\YiziShare\Received\`

### 🔒 隐私与安全

- **数据不出网**：所有通信仅发生在局域网内的设备之间，无任何云端组件
- **凭据不落盘**：SMB 密码仅在内存中传递，不写入配置
- **已知限制**：聊天传输当前为明文（协议已预留版本字段，TLS/加密在规划中）；请勿在不可信网络中传输敏感内容

---

## 🏗️ 项目结构

```
YiziShare/
├── YiziShare.App/            # WPF 界面层
│   ├── MainWindow.xaml(.cs)  # 主窗口：设备列表 + 共享/聊天双面板
│   ├── MainWindow.Chat.cs    # 聊天界面逻辑（partial）
│   ├── TransferWindow.xaml   # 传输队列窗口
│   ├── NotificationBanner    # 右下角通知横幅
│   ├── ShareBrowserView      # 共享文件夹内置浏览
│   ├── ViewModels/           # MVVM 视图模型
│   ├── Services/             # 提示音/表情映射/文件图标
│   └── Assets/               # logo/应用图标/Twemoji 表情
├── YiziShare.Core/           # 核心逻辑层（纯 BCL，零 NuGet 依赖）
│   ├── ChatService.cs        # 聊天服务：UDP 发现 + TCP 传输 + 离线队列
│   ├── ChatProtocol.cs       # 消息信封与分帧协议
│   ├── ChatDelivery.cs       # 投递状态机与回执
│   ├── ChatPresence.cs       # 在线表
│   ├── ChatStore.cs          # 聊天历史持久化
│   ├── NetworkScanner.cs     # 局域网扫描
│   ├── SmbDiagnostics.cs     # SMB 诊断引擎
│   └── ...
├── YiziShare.Tests/          # 单元测试（172 个）
├── VERSION_LOG.md            # 版本详细记录
├── CHANGELOG.md              # 变更摘要
└── DEVLOG.md                 # 开发日志
```

---

## 🧩 第三方资源

| 资源 | 用途 | 许可证 |
|------|------|--------|
| [Twemoji](https://github.com/jdecked/twemoji) | 聊天彩色表情（`Assets/Emoji/`） | CC-BY 4.0 © Twitter/X |
| [Segoe MDL2 Assets](https://learn.microsoft.com/windows/apps/design/style/segoe-ui-symbol-font) | 界面矢量图标 | 随 Windows 分发 |

---

## 📄 许可证

[MIT](LICENSE)

## 📝 版本历史

查看 [VERSION_LOG.md](VERSION_LOG.md) 获取每个版本的详细变更，[CHANGELOG.md](CHANGELOG.md) 获取摘要。

## 🔮 路线图

- 🔐 聊天传输加密（AES-GCM，协议已预留版本字段）
- 📦 大文件分块传输与断点续传（当前单文件 ≤5MB）
- 🖼 图片消息缩略图与查看器
- 👥 群聊
