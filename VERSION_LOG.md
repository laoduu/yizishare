# 版本详细记录

本文档记录了每个版本的详细技术变更。

---

## Version 0.9.34 (2026-10-10)

### 界面微调（验收反馈）

- 设置窗口固定高度（560px），常规/关于 tab 切换不再跳动
- 关于页排版宽松居中：logo 36→**72px**、简介居中加行距、GitHub 行加**章鱼猫标志**（矢量 Path，主题色渲染）且整行可点击

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.33 (2026-10-10)

### 界面改进

#### 1. 设置窗口改 tab 布局
- 「常规 / 关于」双 tab 切换，金色指示条随选中移动
- 常规 tab：原有全部设置项 + 我的昵称 + 保存按钮
- **关于 tab（新增）**：logo + 应用名 + 版本号、应用简介、GitHub 仓库链接（点击浏览器打开）

#### 2. 我的昵称双写备份（Roaming）
- 主值存 Local 的 settings.json，备份写 `%APPDATA%\YiziShare\my-nickname.txt`
- 主值丢失（清 Local）时自动从 Roaming 回读并补写主文件——与 NodeId 同策略，跨重装保留昵称

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.32 (2026-10-10)

### 界面微调

- 输入框"发送文件"按钮图标：回形针（E8A5）→ **文档页（E7C3 Page）**——回形针语义是"附件"，文档页直观表达"发文件"（对齐微信的文件图标语义），保持 MDL2 图标体系一致

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.31 (2026-10-10)

### 界面微调（双机验收反馈）

- 连续消息间距：气泡上下各加 4px，多条消息不再糊在一起
- 文件卡片放大：Shell 图标 26→**32px**（1:1 像素显示系统图标源，消除缩放发糊）、卡片最小宽 180→210、文件名字号 12→13

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.30 (2026-10-10)

### 修复（双机实测反馈）

#### 1. 接收端文字/表情消息空气泡 + 横幅预览为空
接收端构造展示文本时误用了空附件路径的文件名：`Path.GetFileName("")` 返回空字符串 → 接收端文本消息内容为空（发送端正常，因为走的是原文本）。横幅预览取自消息文本，同样为空。

**修复：** 文本消息展示文本改用 `envelope.Text` 原文；图片统一 `[图片]`；文件用文件名。

#### 2. 传输窗口完成后不自动关闭（跨线程 bug）
`ProcessQueueAsync` 的 await 带 `ConfigureAwait(false)`，`OnQueueDrained` 跑在后台线程——里面创建 DispatcherTimer、改 FooterText 全部跨线程抛异常，且异常被 fire-and-forget 吞掉，自动关闭从未生效。

**修复：** `OnQueueDrained` 全体切回 UI 线程（`Dispatcher.Invoke`）。

#### 3. 文件消息卡片图标未取系统类型图标
文件卡片使用固定矢量。改为 Shell 图标优先（按扩展名，资源管理器同源），矢量回退。

#### 4. 拖放上传静默覆盖
拖文件进共享列表同样存在静默覆盖。补齐与粘贴一致的冲突询问：**覆盖 / 跳过重名 / 取消**。

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.29 (2026-10-10)

### 修复

#### 1. 气泡样式恢复（移除诊断标记）
v0.9.28 附带的诊断样式（文本红底黑字 20 号、容器黄底）移除，恢复正常 12 号主题色文字 + 彩色内联表情。三分支渲染（文本/图片/文件）经双机验收通过。

### 测试

全量 **172 个用例**通过；文字/表情/文件/图片发送接收人工验收通过。

---

## Version 0.9.28 (2026-10-10)

### 修复

#### 1. 对端改昵称后本机不显示新昵称（显示回退到 IP/机器名）
`PeerPresenceChanged` 是**在线状态跃迁**事件——只在上线/掉线瞬间触发。对方改昵称时它一直在线，事件永不触发，设备卡的 `PeerName` 得不到同步。

**修复：** 新增 `PeerProfileUpdated` 事件——UDP 心跳与 TCP 消息帧两条路径都会比对 presence 里的旧名与新名，**不一致即通知**（心跳 3 秒一轮，改名后数秒内生效）。链路：ChatService → ChatViewModel（更新聊天头，备注优先）→ MainViewModel（更新设备卡显示名）。

#### 2. 气泡内容不显示的诊断澄清
经排查确认：三分支修复（v0.9.27）本身有效，此前两轮误测了旧 publish 包。诊断标记（红底/黄底）已移除，恢复正式样式。

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.27 (2026-10-10)

### 修复

#### 1. 气泡内容不显示（三分支切换方案缺陷）
`ContentControl + ContentTemplate 触发器切换` 结构下 Content 为 null，模板内绑定未按预期取到数据，所有消息只剩空气泡。

**修复：** 改为**三元素叠放 + Visibility 切换**（文本 ContentPresenter / 图片 Image / 文件卡片 StackPanel，全部由 Style 驱动，规避本地值与模板作用域两类坑；新增 `ChatMessageViewModel.IsText` 属性驱动文本分支）。

#### 2. 表情面板点击后不消失
改为选中即收起（插入输入框后关闭弹窗），用户可明确确认表情已填入。

### 测试

全量 **172 个用例**通过。

---

## Version 0.9.26 (2026-10-10)

### 新功能：聊天图片与文件传输（v1）

#### 协议与传输
- 消息种类新增 `Image` / `File`：`Data` 字段承载 base64 载荷、`FileName` 原始文件名；JSON 信封新字段对旧版本**向前兼容**（旧端忽略未知字段，不会崩）
- 帧载荷上限 64KB → **8MB**（LAN 可信环境 + 握手版本校验；护栏语义保留）；单附件上限 **5MB**，超限明确提示
- 接收方落盘 `%LOCALAPPDATA%\YiziShare\Received\{月份}\{MsgId前8}_{文件名}`（天然去重、防同名覆盖），自动回 Delivered 回执
- 发送复用既有连接、投递状态机与离线队列：**离线时图片/文件同样进待发队列**，上线自动补发

#### 消息渲染与操作
- 气泡三分支：文本（彩色表情内联）/ **图片**（最大 220px 等比，点击用系统看图器打开）/ **文件卡片**（图标 + 文件名）
- 气泡右键菜单（按种类启停）：复制文本 / 复制图片 / **另存为...**

#### 输入区（微信式布局）
- 表情、文件（📎）图标移入输入框内**靠左**，发送按钮**靠右**；默认高约 5 行
- **粘贴直发**：输入框内 Ctrl+V 文件（资源管理器复制）→ 直接作为文件消息发送；粘贴截图/位图 → 存临时 PNG 作为图片消息发送

### 新功能：昵称体系

- 显示优先级：**本机备注昵称 > 对方自报昵称 > 主机名**
- 对端自报昵称随 UDP 心跳同步到设备卡（`Device.PeerName` 新字段）
- 设置窗口新增「我的昵称」：保存后立即生效（下一条心跳广播新名）

### 界面

- **设置窗口改金色 UI**：无边框圆角 + 深色标题栏（自绘拖动/关闭），去系统边框
- 底部状态栏左下角显示版本号（V0.9.26，`App.Version` 常量随发版更新）
- 系统提醒横幅（最小化/扫描完成）头像改用**应用 logo**（不用 ⓘ 字形）

### 测试

全量 **172 个用例**通过（新增 8MB 帧上限边界用例；旧 64KB 断言随协议升级更新）。

---

## Version 0.9.25 (2026-10-10)

### 修复

#### 1. 传输完成后需要手动关闭窗口
原自动关闭逻辑要求"全部 Completed"才触发——任何失败/取消都会让窗口永久停留；且旧批次已结束的条目残留在列表里持续污染判断，新传输完成后同样关不掉。

**修复：**
- 新传输开始时清掉上一批已结束条目，列表只反映当前批次
- **全部成功 → 2.5 秒自动关闭；有失败/取消 → 12 秒自动关闭**（留时间读错误，不再要求手动关）
- 状态栏按批次统计（"已粘贴 N 项"式的计数风格）

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.24 (2026-10-10)

### 界面统一

#### 1. 共享卡片列表换用金色矢量文件夹
共享 tab 首页的共享卡片原先用 MDL2 单色字形（E8B7），与内置文件浏览的金色矢量文件夹不一致。

**修复：** 文件夹/文件矢量模板提升为**应用级资源**（ThemeColors），共享卡片列表（34×29 放大档）与内置文件浏览（26×22 + 图标模式 45×38 Viewbox）共用同一套矢量，样式完全统一。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.23 (2026-10-10)

### 修复

#### 1. 列表模式行空白（列绑定语法错误）
`GridViewRowPresenter.Columns` 用了错误的 Binding 路径（附加属性未加括号），绑定失败 → Columns=null → 无列可渲染 → 整行空白。改为 `{TemplateBinding GridView.ColumnCollection}`（Aero2 默认模板同款写法）。

#### 2. 图标模式文件夹高度偏低
文件夹矢量为宽扁形（26×22），按宽缩放后高度仅 34px，比 38px 的文件图标矮一截。改为按高度 38 等比放大（宽 45），容器 48×44。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.22 (2026-10-10)

### 界面改进

#### 1. 全新 logo（logonew2，浅色设计）
- 应用内头部：logo 原样展示——**保留自然形状，无圆形裁切、无底板徽章**（浅色 logo 在深色头部上直接可读）
- exe / 任务栏 / 托盘图标：从 logonew2 重新生成多尺寸 ICO（16~256px）
- 尺寸分析驱动底板决策：新 logo 平均亮度 179（浅色），若套用旧白底徽章会隐形，故按用户要求彻底去除徽章形态

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.21 (2026-10-10)

### 修复

#### 1. 粘贴同名文件静默覆盖（数据丢失风险）
粘贴时 `File.Copy(overwrite: true)` 直接覆盖目标同名文件，无任何提示——对方共享目录里的旧文件被悄悄换掉，状态栏一闪而过。

**修复：** 粘贴前做同名冲突检测，冲突时询问三选一（资源管理器语义）：
- **是**：全部覆盖
- **否**：跳过重名项（其余正常粘贴）
- **取消**：中止

状态栏反馈计数：「已粘贴 N 项，跳过 M 个重名项」。冲突预览列出前 5 个重名文件名。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.20 (2026-10-10)

### 修复

#### 1. 共享浏览"上级"按钮在根目录时看似不可点击
打开共享即落在根目录，"上级"（进入子目录后回退一级用）在根目录无意义却被禁用，观感如同坏按钮。

**修复：** 根目录时直接隐藏，进入子目录后才出现。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.19 (2026-10-10)

### 修复

#### 1. 图标模式 Shell 图标底部裁切
格子 40×34 装不下 38×38 的图标。格子改 44×44，图标本体 38 居中。

### 界面统一：MDL2 矢量图标体系

全应用图标从 emoji/文本杂混统一为 **Segoe MDL2 Assets**（Win10+ 内置矢量字体，主题色渲染、缩放不糊、语义标准），分三档：

- **幽灵工具按钮**（透明底 hover 高亮）：图标+文字（返回/上传/资源管理器）或纯图标（上级/刷新）
- **紧凑图标按钮**（头部）：扫描 E72C、设置 E713、主题 E708/E706、最小化 E921、最大化 E922、还原 E923
- **菜单小图标**：共享浏览右键菜单与设备卡右键菜单全部改为 MenuItem.Icon + MDL2 字形（打开 E8E5、下载 E896、复制 E8C8、剪切 E8C6、粘贴 E77F、重命名 E8AC、删除 E74D、发起聊天 E8BD、唤醒 E7E8 等）

其他统一项：
- 收藏星标：空心 E734 / 实心 E735（DeviceViewModel.FavoriteIcon 改字形）
- 修改昵称铅笔 E70F、表情按钮 E76E、唤醒电源 E7E8
- 金色填充按钮只保留给主操作（聊天"发送"、对话框"保存"）

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.18 (2026-10-10)

### 修复

#### 1. 文件图标全空白、目录显示成文件页（WPF 优先级规则第三踩）
模板里 `Visibility="Collapsed"` 与 `ContentTemplate="文件页"` 写成了**本地属性值**——WPF 优先级中本地值压过 Style 触发器，导致：
- Shell 图标的"有图标时显示"触发器失效 → 文件全空白
- IsDirectory 换金色文件夹的模板切换失效 → 目录显示文件页矢量

**修复：** 可见性与模板全部改由 Style Setter/Trigger 驱动，元素上不写任何本地值（与聊天圆点"常绿"、头部 Fill 同源教训，模板内已加注释）。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.17 (2026-10-10)

### 界面改进

#### 1. 文件类型识别为系统图标（资源管理器同源）
列表与图标模式下，文件显示其扩展名在 Windows 里关联的程序图标（.txt 记事本、.mp3 播放器、.exe 应用图标……），与本地资源管理器观感一致。

- 新增 `Services/FileTypeIconCache`：`SHGetFileInfo` + `SHGFI_USEFILEATTRIBUTES` 按扩展名提取 Shell 图标——**只读扩展名不触碰文件**，UNC 不可达也不会卡顿；按扩展名缓存 + Freeze，跨线程安全
- 目录保持金色文件夹矢量（品牌色）；提取失败回退文件页矢量
- 两套模板（列表/图标模式）统一为「Shell 图标优先，矢量兜底」的三态结构

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.16 (2026-10-10)

### 修复

#### 1. 图标模式仍横向排列不换行
上一版从视觉树捞内部 ScrollViewer 改属性的方式受模板应用时机影响不可靠。改为 WPF 惯用法：直接在 ListView 上设置 `ScrollViewer.HorizontalScrollBarVisibility` **附加属性**（内部 ScrollViewer 模板自动读取宿主的该属性）——图标模式 Disabled（WrapPanel 拿到有限宽度，正常换行），列表模式 Auto。

### 界面改进

#### 1. 列表模式列头点击排序
- 名称 / 大小 / 修改时间三列均可点击：同列再点切换升降序，新列默认升序，列头显示 ▲/▼ 指示
- 大小列按**真实字节**排序（"1.2 MB" 这类字符串直接排会乱），目录恒排在文件之前
- 图标模式共享同一集合视图，排序同步生效

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.15 (2026-10-10)

### 修复

#### 1. 文件列表全部显示 "FileItem"（详情视图失效）
`ApplyViewMode` 从 `Resources["DetailsView"]` 取 GridView——但 GridView 定义在 `ListView.View` 里而非 UserControl.Resources，取值为 null，`FileList.View = null` 使 ListView 回退到默认模板（渲染 `ToString()` = "FileItem"）。

**修复：** GridView 移入 UserControl.Resources 作为键控资源，两种模式都由 `ApplyViewMode` 正确赋值。

#### 2. 图标模式出现横向滚动条
WrapPanel 在 ListView 默认 ScrollViewer（水平 Auto）下拿到无限宽度，永远不换行。图标模式下禁用内部 ScrollViewer 的水平滚动（`Disabled`），列表模式恢复 Auto，实现自动换行。

### 调整

- 聊天气泡表情 30px → **40px**

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.14 (2026-10-10)

### 界面改进

#### 1. 输入框彩色表情（TextBox → RichTextBox）
输入框内也以彩色图片显示表情（与面板同风格）。实现：
- `ChatInputBox` 换 RichTextBox（TextBox 无法内嵌图像）；表情以 `InlineUIContainer` 彩色图（22px）插入，Tag 保存原字符
- 发送时遍历流文档提取文本：Run 贡献文字、表情图按 Tag 还原字符——**协议仍是纯文本，双向兼容**
- 粘贴只接受纯文本（`DataObject.Pasting` 拦截，拒绝 RTF/图片），IME 组词拦截逻辑不变

#### 2. 气泡表情放大 18px → 30px
#### 3. 输入表情 22px（与正文比例协调）

### 共享浏览增强

#### 1. 金色矢量图标（弃用 Twemoji 棕色文件夹）
文件夹/文件改为 **XAML 矢量图形**：金色文件夹（AccentGold 同色）+ 白纸页金折角，风格与应用主题一致，且矢量缩放不糊。

#### 2. 列表模式 / 图标模式可切换
- 工具栏新增「⊞ 图标 / ☰ 列表」切换按钮
- 图标模式：WrapPanel 大图标 + 名称居中（资源管理器样式）
- 偏好持久化到 `SettingsStore.FileBrowseIconMode`

#### 3. 拖放传输
- **拖入**：从本机/资源管理器拖文件到列表 → 加入传输队列上传到当前目录（复用 TransferWindow，含进度）
- **拖出**：从列表拖文件到本地/桌面 → 复制语义下载（FileDropList 承载 UNC 路径，资源管理器原生接住）

#### 4. 「📂 资源管理器」按钮
上传按钮右侧新增，在 Windows 资源管理器中打开当前共享目录。

#### 5. 观感放大
行高 38→**44px**、字号 12.5→**13.5**、名称列 320→**330px**。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.13 (2026-10-10)

### 修复

#### 1. 表情面板空白
v0.9.12 的 Twemoji 图片只是下载到了 Assets 目录，**csproj 里漏加 Resource 声明**——208 张 PNG 没编入程序集，pack URI 全部加载失败，面板呈现一排空按钮。

**修复：** `<Resource Include="Assets\Emoji\**" />`；已验证 g.resources 中含 emoji 资源键（UTF-16）。同时补上目录/文件图标的资源（1f4c1/1f4c4）。

#### 2. 文件列表过于抽象
- 名称列加**彩色图标**（📁 目录 / 📄 文件，复用 Twemoji 资源，风格与表情一致）
- 行高 22→**38px**、字号 12→**12.5**，名称列 280→**320px**

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.12 (2026-10-10)

### 界面改进

#### 1. 表情彩色化（Twemoji 图片方案）
WPF 文字栈不支持彩色字体（COLR），文字渲染 emoji 必然黑白——彩色只能走图片。

- 引入 **Twemoji 72px**（twitter/twemoji，CC-BY 4.0 开源），208 张按码点命名编入程序集（`Assets/Emoji/`，共 183KB）
- 表情面板放大（38×40 按钮、26px 彩图、380px 宽面板）
- 聊天气泡内联渲染：`ChatMessageViewModel.RichText` 将消息文本按表情切分，表情以内联彩色图片（18px）呈现，普通文字照旧；未知表情回退文字渲染

#### 2. 共享文件夹浏览内嵌面板（替代弹窗）
- 新增 `ShareBrowserView` UserControl 嵌在详情面板内：点共享"打开" → 面板原地切换为文件浏览（不弹任何窗口），"← 返回"回共享列表；共享/聊天 tab 照常可用
- 完整文件操作语义对齐资源管理器：**右键菜单**（打开/下载到本地/复制/剪切/粘贴/重命名/删除/刷新）+ **快捷键** Ctrl+C/X/V、Delete、F2 + 工具栏上传
- 剪贴板走 FileDropList：本应用内复制粘贴互通，也能与真实资源管理器互相粘贴
- 删除 `FileBrowserWindow`（逻辑已全部移植；其 XAML 笔误与同步冻结缺陷由新视图根治）

#### 3. 窗口宽度限制按语义反转
收起态（仅设备列表）`MaxWidth` = 320 × 1.5 = **480px**；展开态**无限制**、可最大化。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.11 (2026-10-10)

### 修复

#### 1. 聊天头部在线圆点常绿
与设备卡同款根因：`Fill="{DynamicResource ChatOnlineDotBrush}"` 作为**本地值**写在元素上，本地值优先级高于 Style 默认值，离线灰色永远不生效。移除本地 Fill，由样式触发器全权接管（绿=在线，灰=离线）。

#### 2. "打开共享"点击即崩（FileBrowserWindow 的双重缺陷）
- **XAML 笔误**：下载/上传按钮的 `Margin="8,0,0,0}"` 多了 `}`，解析直接抛 XamlParseException——这就是当初"点浏览就崩"的元凶
- **同步枚举 UNC 路径**：目标不可达时 `Directory.GetDirectories` 冻结 UI 线程

**修复：** 修正笔误；目录枚举移入后台任务（`_loadSeq` 防竞态）；新增"⬆️ 上级"导航（限制在共享根之内，不越出本共享范围）。

### 界面改进

#### 1. 表情选择器
输入区新增 😊 按钮，弹出 190+ 常用表情面板（Unicode 开放标准，以纯文本收发，**协议零改动**）；插入到光标处，面板不关闭可连续选。图片贴图包（Twemoji 等）留作后续：需在消息协议中新增图片类型。

#### 2. 共享文件夹内置浏览
"打开"共享不再弹资源管理器——改用应用内 `FileBrowserWindow`（列表/上传/下载/刷新/上级导航），托盘快速访问同路径受益。右键"打开全部共享"改为打开第一个共享的内置浏览器。

#### 3. 展开态窗口宽度上限
详情面板展开时 `MaxWidth` = 默认宽度 × 1.5（1350px），拖拽不会无限拉宽；最大化与收起态不设上限。

### 测试

全量 **171 个用例**通过。

**待规划（下一轮）：** 聊天内发送图片/文件——现有 `FileTransferService` 是本地 SMB 复制（写入对方共享目录），不适用于聊天级传输。需要消息协议扩展：`Image/File` 消息类型 + TCP 分块传输 + 进度/取消 + 缩略图，涉及 `ChatProtocol`/`ChatService`/`ChatStore`/UI 全链路，单独成轮实施。

---

## Version 0.9.10 (2026-10-10)

### 修复

#### 1. 设备列表无法分辨能否聊天
头像右下角的在线圆点是**硬编码绿色**（无绑定），只代表"扫描到了设备"，与 YiziShare 是否运行无关——用户无法判断对方可否聊天。

**修复：** 圆点改三态绑定 + 悬停提示，数据来自聊天模块的 UDP 心跳级在线状态（`ConversationViewModel.IsOnline`，经 MainViewModel 按 IP 桥接到设备卡，扫描建卡/聊天建卡均回填）：

| 颜色 | 含义 |
|------|------|
| 🟢 绿 | YiziShare 在线，可直接聊天 |
| ⚪ 灰 | 设备在线（未检测到 YiziShare），仅可共享文件 |
| ⚪ 浅灰 | 设备离线 |

触发器按「离线 < 仅设备在线 < 可聊天」排序（WPF 后者覆盖前者），聊天在线必胜出——即使扫描探测失败（防火墙拦 Ping）而心跳正常。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.9 (2026-10-10)

### 修复

#### 1. 默认宽度下右上角关闭按钮被挤出窗口
窗口默认 320px，头部 6 个 30px 图标按钮 + logo + 标题 ≈ 347px——必然溢出。新增 **HeaderIconButton** 紧凑样式（24×24，基于 IconButton），头部按钮全部换用，总宽降到 ~280px，关闭键恢复可见。

#### 2. 收起状态下拖宽窗口留大片空白
设备列表列固定 300px + 右列 0：拖宽窗口时列表不动、右侧空白。

**修复：** 收起状态下设备列表列改为**星号铺满整窗**（初始 XAML 与 `CollapseRightPanel` 同步），展开时切回固定 300 + 星号（`ExpandRightPanel` 显式恢复）。QQ 同款行为：列表形态自适应窗口宽度。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.8 (2026-10-10)

### 修复

#### 1. 消息横幅显示设备名而非备注名
横幅/聊天头部取的是 `conversation.PeerName`——来源于**对端自报的机器名**（消息信封与心跳里的 Name），本机为该 IP 设置的备注名（NicknameStore）从未被聊天模块查询。

**修复：** `ChatViewModel` 统一加显示名解析 `ResolveDisplayName(ip, name)`——**备注名优先、自报名兜底**，覆盖全部入口：消息到达（`EnsureConversation`）、心跳上线（含显式 `UpdatePeerInfo`，防止覆盖昵称）、拨号握手（PeerResolved）、历史加载（含落盘记录改存备注名，重启后列表同名）。

#### 2. 系统托盘相关仍是系统气泡
最小化到托盘、扫描完成、窗口打开等仍弹 Windows 系统气泡（样式不可控）。

**修复：** 横幅增加信息型变体 `NotificationBanner.ShowInfo(title, message)`（金色圆内 "i"，点击仅关闭），四处系统气泡全部替换：最小化到托盘、扫描完成（手动/托盘两条路径）、从托盘恢复窗口。

#### 3. 最大化/最小化按钮位置不符 Windows 惯例
调整为「─ 最小化 → ▢ 最大化 → ✕ 关闭」；最大化状态下按钮 hover 提示由"最大化"改为**"还原"**（字形 ❐ 不变）。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.7 (2026-10-10)

### 界面修复

#### 1. 输入区顶部出现两条线
输入区 Border 自带 `0,1,0,0` 分割线，下方紧贴 3px 拖拽条——线叠线。分割线职责交给拖拽条，移除 Border 顶线。

#### 2. 共享文件夹卡片阴影发虚
`DropShadowEffect` 在圆角卡片上是一团灰雾，且与统一的"平底色 + 细线"语言冲突。移除阴影，改为细描边 + hover 背景反馈（卡片含"打开"按钮，hover 提供可点击暗示）。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.6 (2026-10-10)

### 修复

#### 1. 头部 logo 空白（根因：Content 资源在单文件发布下加载不可靠）
v0.9.5 换了浅色底板后仍空白——资产文件本身合规（PNG 8-bit RGBA 非隔行、内容完整），但 `logo.png` 以 **Content 构建动作**依赖运行时相对路径解析，单文件发布下 pack URI 不可靠（推断旧 logo 在发布版中从未真正显示过，此前"黑圈"实为深色底板本身）。

**修复：** `logo.png` 改为 **Resource 构建动作编入程序集**（已验证 `g.resources` 中包含该条目），任何打包形式下 `pack://application` 必然可加载；头部徽章同步改为 `ImageBrush` 直绘（去掉 Image+Clip 组合）。`app.ico` 保持松散文件（托盘按文件路径加载）。

#### 2. 输入框拖高后文字垂直居中
`VerticalContentAlignment` 改为 `Top`——多行编辑器行为，文字从顶部开始排。

#### 3. 发送按钮随输入框等比增高
`VerticalAlignment="Stretch"` 改为 `Bottom + 固定 36px 高`——微信式布局，输入框再高按钮也贴底不变形。

### 界面统一

#### 右侧面板背景色规划
此前三段三种底色（亮色主题：头部/标签条/输入区纯白 #FFFFFF、消息区浅灰 #F8F9FA），观感割裂。**统一为同一底色（PanelBg）+ 细分割线区分区域**：设备信息头、标签条、消息区、输入区浑然一体，仅以 1px 边线分层，消息气泡自带层次。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.5 (2026-10-10)

### 修复

#### 1. 头部 logo 显示为纯黑圆圈
新 logo 为深色设计（像素采样平均亮度 89/255，主体炭灰），叠在头部近黑圆底（#1C1913）上深底叠深图 = 黑圈。**改为浅色底板**（暖白 #F5F2EA + 金边），深色 logo 在浅底上正常显形。

#### 2. 未读数字未按会话隔离
聊天 tab 旁的红色数字绑定的是**全部会话的总未读**（`Chat.HasUnread`），查看 B 的聊天时显示的是 A 的未读数，归属不明。**移除 tab 旁的总数角标**——未读数只在各设备头像上按会话显示（设备卡角标）+ 托盘汇总，语义清晰无歧义。

### 界面改进

#### 1. 聊天输入框支持手动拖高
输入区顶部新增拖拽横条：向上拖加高、向下拖降低（52~280px）。拖动后转为手动固定高度；拖拽条 hover 可见、低调不喧宾。

#### 2. 支持窗口最大化（含面板展开状态）
- 接入 `WindowChrome`：无边框窗口获得拖边缘缩放能力，最大化正确停靠工作区（不盖任务栏）
- 标题栏新增最大化按钮（▢/❐ 随状态切换），**双击标题栏**切换最大化/还原
- 最大化时自动去除窗口圆角（圆角缺口会露出透明背景），还原时恢复
- 展开/收起详情面板的宽度动画在最大化状态下自动跳过，避免与铺满尺寸冲突；从最大化还原按面板状态恢复宽度

#### 3. 设备列表：长主机名防挤出 + 图标 hover 渐显
- 昵称/主机名超长时省略号截断，完整名放 tooltip——修复图标被挤出窗口无法点击的问题
- 收藏 ⭐ 与改昵称 ✏️ 图标改为 **hover 卡片时渐显**（0.15s 淡入），不再常驻挤占行宽

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.4 (2026-10-10) 紧急修复

### 修复

#### 1. 对端一上线，本机进程静默崩溃（v0.9.3 引入，致命）
**现象：** 本机应用运行中，局域网另一台机器启动应用后本机应用无声挂掉；反过来重启本机又把对方挂掉——看起来像"新实例杀死旧实例"。

**根因：** Windows 事件日志（ID 1026）实锤——`System.Reflection.TargetParameterCountException: Parameter count mismatch` 导致进程终止。v0.9.3 在 `ChatViewModel.PostToUi` 加固异常隔离时写错了一行：

```csharp
dispatcher.Invoke(RunGuarded);   // RunGuarded 是"接收 Action 参数"的方法组
```

方法组被绑定到废弃的 `Invoke(Delegate, params object[])` 重载、`params` 为空表，运行时 `DynamicInvoke` 发现参数数量不匹配直接抛异常 → 未处理 → 进程终止。新实例启动即广播在线状态，对端收到 presence 事件进入 `PostToUi` 的非 UI 线程分支触发——**谁收到事件谁死**，双向对称。

**修复：** 改为 `dispatcher.Invoke(() => RunGuarded(action))`，并注释说明方法组陷阱。

#### 2. 兜底防线：UI 未处理异常不再无声杀进程
`App.OnStartup` 挂接 `DispatcherUnhandledException`：未处理的 UI 异常记入 `app.log` 后标记 Handled，应用继续运行。此前任何 UI 异常都直接终止进程，只能靠事件查看器排查（本次事故即如此）。

### 测试

全量 **171 个用例**通过。

**运维提醒：** v0.9.3 双端都会触发本问题，两台设备必须同步更新到 v0.9.4。

---

## Version 0.9.3 (2026-10-10) 紧急修复

### 修复

#### 1. 一条消息炸断一条 TCP 连接（v0.9.2 引入，致命）
**现象：** 第一条消息能到达；对方不回复的情况下继续发消息，全部变成「发送失败」。日志中每条消息到达后紧跟 `读取 xxx 的数据失败`，且双端每 5 秒一次重连风暴。

**根因链（三层叠加）：**
1. v0.9.2 的横幅窗口引用了 `Style="{StaticResource EditIconButton}"`——该样式定义在 MainWindow 的 Window.Resources，**横幅是独立窗口看不到它**，构造即抛 `XamlParseException`；单例未建成，每条消息都重试重炸
2. `ChatViewModel.PostToUi` 用 `Dispatcher.Invoke` 派发——**UI 处理器的异常会原样抛回 socket 线程**
3. `ReadLoopAsync` 的 catch 把这个 UI 异常当成读错误 → 关闭 TCP 连接

于是：每条来消息 → 对端读循环死亡 → 后续回执丢失 → 我方消息卡在 `Sent` → 8 秒超时判定 `Failed`。第一条「能到达」只是因为消息上屏发生在异常抛出之前。双端都跑 v0.9.2 时互相炸，形成重连风暴。

**修复：**
- 横幅 XAML 移除跨窗口 `StaticResource`，关闭按钮改内联模板
- **`PostToUi` 系统性加固**：所有聊天 UI 事件处理器的异常就地捕获并记日志，绝不再飞回 socket 线程杀连接（该隐患自 v0.6 起就存在，任何 UI bug 都可能断链，现在从结构上堵死）

**运维提醒：** 此问题双端对称，两台设备必须同步更新；单边更新时未更新一端仍会因自身 UI 异常断链。

### 测试

Chat 相关 80 个用例通过。

---

## Version 0.9.2 (2026-10-10)

### 修复

#### 1. ComboBox 选中值永远显示空白（模板级 bug，全局生效）
`ThemeColors.xaml` 的 ComboBox 模板漏了选中值显示层——ContentPresenter 没绑定任何内容，导致「筛选/排序」与设置里的「网卡选择」全部显示空白。

**修复：** 模板中补上绑定 `SelectionBoxItem` 的 ContentPresenter（`IsHitTestVisible=False` 不挡点击），所有下拉框正常显示当前选中值。

#### 2. 新消息提示音重叠
托盘气泡 `ShowBalloonTip` 是系统组件，**自带 Windows 通知音且无法关闭**，与本应用的提示音必然叠加。

**修复：** 自绘「微信式横幅」替代托盘气泡（见下）——气泡移除后系统提示音随之消失，只剩应用自己的提示音。

### 界面改进

#### 1. 微信式新消息横幅（新增 `NotificationBanner`）
- 屏幕右下角滑入（微信同款位置），深色卡片 + 金色圆头像（昵称首字符）+ 发送者 + 消息预览，滑入淡出动效
- 悬停暂停自动关闭（移开 3 秒后消失）；点击直达会话：唤出主窗口 → 选中该设备 → 落在聊天页并滚到底
- 全局单例：连续消息只刷新内容并重置计时，不叠窗口；删除原托盘气泡的 60 秒节流逻辑（单例合并使节流不再必要）
- 托盘图标"未读 N 条"文案、扫描完成等非消息类系统气泡保持不变

#### 2. 新应用图标
- `Assets/logo.png`：新设计图标高质量缩放至 128×128（主窗口头部徽标）
- `Assets/app.ico`：从新图标重生成多尺寸 ICO（16~256px，PNG 压缩带透明通道）——exe 图标、任务栏、托盘全部更新

#### 3. 聊天输入框加高
MinHeight 40→52、MaxHeight 100→160：默认更高，多行输入随内容自然长高，超出后内部滚动。

#### 4. 搜索框占位提示
空搜索框增加"搜索设备名称或 IP..."水印，输入即隐。

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.1 (2026-10-10)

### 修复

#### 1. 离线发送消息"消失"几十秒
**现象：** 对方离线时发消息，气泡长时间不出现；部分情况下消息彻底不显示。

**根因：** `SendChatAsync` 要**等拨号完成才返回**，而对端不可达时会做最多 10 个端口的区间探测（每个都带超时周期），期间 UI 只能干等；极端时返回 null，消息直接丢失。

**修复：** 发送改为「立即受理 + 后台派发」——
- `ChatService.SendChatAsync` 改为同步返回 MsgId：登记为 `Sending` 即交还 UI，**气泡瞬间上屏**
- 拨号/探测移入后台 `DispatchChatAsync`：连上 → 写循环推进 `Sent`；不可达 → 入待发队列、推进 `Queued`
- 状态实时流转：`⏳发送中 → ⏳待发送(对方离线) → ✓ → ✓✓ → 已读`，每步经 `DeliveryChanged` 推到气泡
- 派发异常兜底转入待发队列，消息不再丢失
- 新增 `ChatService.PeerResolved` 事件：UDP 广播被拦截时，主动拨号握手拿到对端真实 NodeId 后通知界面层改绑会话（原实现靠返回值传递，异步化后必须走事件）

#### 2. 离线补发消息的已读回执永远落空
离线期间消息按 IP 占位键登记投递跟踪，补发后 `ApplyRead` 仍按占位键匹配，而对端回执按真实 NodeId 查找——永远失配，气泡不显示已读。

**修复：** `DeliveryTracker` 新增 `Rebind(msgId, realPeerId)`；`DispatchChatAsync` 拿到连接后、`FlushPending` 补发前都把跟踪记录改挂真实键。

### 界面重设计（顶部设备详情面板）

| 之前 | 之后 |
|------|------|
| 唤醒 / 浏览 / 全部打开三按钮常驻 | 只留**唤醒**（仅离线且有 MAC 时出现） |
| 「浏览」同步遍历 UNC 路径，目标不可达时窗口冻结假死 | **删除入口**（`FileBrowserWindow` 代码保留） |
| 「全部打开」一次弹 N 个资源管理器窗口 | **删除**（共享列表每项自带「打开」） |
| 状态徽标独占一行、长文字撑爆 | **小徽标紧跟昵称**：🟢已连接 / 橙不可访问(悬停显示诊断详情) / 灰扫描中 |
| ☆ 收藏、✏️ 改名常驻 | **hover 渐显**（0.15s 淡入 / 0.3s 淡出） |
| IP、MAC、共享数分散多行 | **一行元信息**：`📌 IP · 🔗 MAC · 📂 N 个共享` |
| 最后连接独占一行 | 有历史记录才显示第三行 |

### 测试

全量 **171 个用例**通过。

---

## Version 0.9.0 (2026-10-10)

### 修复

#### 1. 收到消息窗口不显示（角标有、内容空）
**现象：** 对端发来消息，Windows 通知与未读角标都正常，但聊天窗口里始终看不到消息。

**根因：** `EnsureConversation` 的「同 IP 双标识」分支把方向判断反了——从设备列表打开会话时传的是 `ip-<IP>` 占位符，而历史会话已按真实 NodeId 建立，占位（新）与真实（旧）不相等即被当成「对方换机了」，**另建了一个空会话**返回并把选中指向它。后续消息按真实 NodeId 全部落进旧会话（角标、通知都来自旧会话），UI 绑定的却是空会话。

**修复（`ChatViewModel.EnsureConversation`）：** 按「占位/真实」方向分派——
- 占位 → 真实：直接复用真实会话（设备列表打开的常态路径）
- 真实 → 真实且不一致：才认定换机，另建会话
- 其余（真实 → 占位 / 占位 → 占位 / 同 ID）：走原 rebind 合并

#### 2. NodeId 只存一份，清 Local 即丢
`node-id.txt` 仅在 `%LOCALAPPDATA%`，卸载器清理该目录后重装会被对端当成新设备，历史会话接不上。

**修复（`ChatService.LoadOrCreateNodeId`）：** 双位置备份——主文件 `%LOCALAPPDATA%\YiziShare\node-id.txt`、备份 `%APPDATA%\YiziShare\node-id.txt`，按序读取任一生效即复用并**双写补齐**另一个；首次生成同步写两处。覆盖「卸载器只清 Local」场景（重装系统仍无法覆盖）。

### QQ 风格聊天体验

#### 1. 设备卡片未读角标 + 头像脉冲
原先未读数只存在于会话（`ConversationViewModel.UnreadCount`），设备卡片毫无反应。

- `DeviceViewModel` 新增 `UnreadCount` / `HasUnread` / `UnreadBadgeText`（>99 显示 `99+`）/ `UnreadPulse`（上升沿触发后 450ms 自动复位，保证下一条消息可再次触发）
- `MainViewModel` 桥接：监听会话集合增删与单个会话未读变化，以 IP 为公共键同步到设备卡；`MergeDevices` / `EnsureChatPeer` 新建卡片时回填历史未读
- 设备卡头像右上角红色数字角标（QQ 红点样式），新消息到达头像缩放脉冲一次（1→1.14→1）

#### 2. 消息提示音（零资源文件）
全项目原先无任何声音代码。

- 新增 `YiziShare.App/Services/SoundHelper.cs`：首次播放时**纯代码合成**约 350ms 两音阶「叮咚」（E6→A6，22050Hz/16bit/单声道 PCM，淡入淡出防爆音），缓存到 `%LOCALAPPDATA%\YiziShare\message.wav`，`SoundPlayer` 播放；合成失败回退系统提示音
- 仅**后台到达**的消息响铃（正在查看的会话经 `MessageReceivedWhileViewing`，不响）
- 设置窗口新增「聊天」分区 →「收到消息时播放提示音」开关（`SettingsStore.ChatSoundEnabled`，默认开）

#### 3. 聊天标签页默认在左、点设备直接进聊天
- 标签条对调：聊天列 0（默认）、共享列 1；金色指示条与未读红点随移
- `SelectDevice`：点击设备 → 展开右面板**直接落在聊天页**（先定设备再切页，`SwitchTab(聊天)` 链路自带选会话、标已读、滚到底，角标随之清零）
- 收起面板后重置为聊天页；托盘「快速访问」显式切回共享页（其用途是打开共享）；空态文案改为「点击左侧设备即可开始聊天」
- 标已读后同步刷新托盘「未读 N 条」文案

#### 4. 清理
- 删除 `ChatViewModel.NotificationRequested` 事件：职责已被 `MessageReceivedInBackground` 覆盖，且 MainWindow 订阅处是空 lambda（死代码）

### 测试

全量 **171 个用例**通过；提示音合成算法以独立脚本复算验证（合法 RIFF/WAVE 头，SoundPlayer 可加载）。

---

## Version 0.8.0 (2026-10-02)

### 新功能

#### 1. 离线消息队列与自动补发
原先对方离线时消息直接丢弃，用户必须保持对方在线才能发消息。

**实现：**
- `ChatService` 新增 `_pendingOutbox`（按对端分组、组内 FIFO，单对端上限 200 条）
- 发送时若对端不可达，消息进入待发队列，状态标记为 `Queued`（UI 显示「⏳ 待发送」并标橙色，可点击催发），消息同时正常落库
- 连接建立时（入站接受与主动拨号两条路径）触发 `FlushPending()` 补发，**保留原 MsgId** 以便接收端幂等去重
- **独立重试循环** `TryFlushPendingMessages()`：补发只在连接建立时触发，而离线对端既不在在线表里也不会主动连过来，必须由维护循环周期性主动拨号（与重连共用指数退避节奏）
- 取队列时同时按「真实 NodeId」和「IP 占位」两种键查找——离线期间只能按 IP 占位入队，否则积压的消息永远发不出去
- 投递状态机新增 `Sending → Queued` 转换与 `MarkQueued()`；`Queued` 状态不会被超时扫描误判为失败

**验证：** 双实例端到端——A 启动后向尚未上线的 B 发送（状态 `Queued`）→ B 启动占用顺延端口 → A 主动重连并补发 → 状态变 `Delivered`，B 收到消息。

#### 2. 移除聊天页的多目标会话侧栏
会话侧栏与设备列表构成两套并行的"选择对象"入口：选了设备再在侧栏里换人，与"选中谁就跟谁聊天"的线性关系冲突（用户反馈：选了一家饺子馆，打开菜单却能点火锅西餐）。

**改为：** 聊天对象只由设备列表的选中项决定——点选设备 → 切到聊天标签页即与该设备对话。消息仍在后台无条件接收落盘，切回聊天页恢复到同一台设备的对话。

### 修复
- 修正 `FlushPending` 声明为 async 却无 await 触发的 CS1998 警告（入队是非阻塞操作，改为同步方法）

### 测试
新增 5 个用例覆盖离线状态流转（`Sending → Queued` 退回、补发后恢复发送、排队中不被超时判失败等），全量 **171 个用例**通过。

---

## Version 0.7.0 (2026-10-02)

### 缺陷修复

#### 1. 消息重复显示（根因在 XAML 而非网络）
`ChatMessageTemplate` 写了**两个气泡**：「对方气泡」无条件靠左、「自己气泡」无条件靠右，二者绑定同一 `Text`，`DataTrigger` 只切配色与圆角、没有任何一处用 `IsMine` 控制显隐——每条消息都被渲染两次。
**修复：** 改为单个气泡，按 `IsMine` 切换对齐、边距、配色、圆角；投递状态标记区分颜色（已读金色、失败红色），失败时可点击重发。

#### 2. 会话标识被反向污染
`EnsureConversation` 的「按 IP 兜底」分支会把已用真实 NodeId 建好的会话**降级**成 `ip-` 占位符（打开聊天页时触发），并反向改写 `ChatStore` 主键，导致 `DeliveryChanged` 按真实标识查找落空、状态图标冻结。
**修复：** 只允许「占位 → 真实」单向升级；反向情形另建会话。同时 `NodeId` 改为落盘持久化（`node-id.txt`），此前每次进程启动重新生成，与「稳定设备标识」的设计意图相悖。

#### 3. 双向同时拨号产生两条物理链路
两端同时拨号时，`RegisterConnection` 与 `GetOrConnectAsync` 的去重规则自相矛盾，各保留了自己拨出的连接，形成双向各一条、互相看不见对方，消息被投递两次、另一条链路的发送队列被 Ping 塞满。
**修复：** 两端统一用 NodeId 字典序裁决（较大方保留出站、较小方保留入站），并在**回 HelloAck 之前**完成裁决——被拒一方回 `HelloReject` 并回退到共同保留的链路。

#### 4. 退出必挂死
`MainWindow_Closing` 在 UI 线程 `GetAwaiter().GetResult()`，而 `ChatStore` 内部 `await` 捕获 WPF DispatcherSynchronizationContext，构成自死锁——有未落盘消息时从托盘退出必然卡死并被强杀丢消息。
**修复：** 改为 `e.Cancel = true` + 异步退出流程；`SaveAsync` 加 `ConfigureAwait(false)`。

#### 5. 后台消息被静默吞掉
窗口隐藏到托盘时未同步可见状态，`IsActive` 停留在 true，导致后台消息被判定为「正在查看」而直接标已读、不弹气泡。
**修复：** 新增 `HideToTray()` 统一处理隐藏，`Activated`/`Deactivated` 都同步状态，`IsActive` 计入 `Visibility`。

#### 6. 已读回执指向错误
`LastReadMsgId => LastOutgoingMsgId` 取的是**自己**发出的最后一条，导致对方发来的消息永远收不到已读；且窗口每次激活都无条件重发回执。
**修复：** 改用 `LastIncomingMsgId`（我读到的对方最后一条），并以 `LastReceiptSent` 游标去重。

### 功能补全

- **会话列表**（聊天页左侧）：在线状态圆点、最后一条消息摘要、相对时间、未读角标（>99 显示 `99+`）、有未读的会话置顶、按最后活动时间排序；选中会话会同步选中对应设备
- **切标签不再清空选中会话**（此前离开聊天页即置 null，重新进入丢失上下文）
- **未读数持久化**：新增 `ChatSessionRecord.UnreadCount` / `LastIncomingMsgId` / `LastActivityAt`，重启后红点不再归零
- **消息无条件落盘**：收到消息立即写入 `chat.json`，不再依赖聊天面板是否打开
- **新消息提醒**：托盘气泡按 PeerId 节流（60 秒，原按昵称分组会让同名设备互相吞消息）、连续多条合并为「N 条」、托盘图标 ToolTip 显示未读总数
- **自动滚动**：正在查看的会话收到消息时自动滚到底部
- **失败重试入口**：气泡下方的失败标记可点击重发，重发会删除旧的失败记录
- **端口探测**：对端 45402 不可达时自动按 45403~45412 逐个试探——服务端端口顺延时也能连通（UDP 广播被拦时的兜底）
- **中文输入法修复**：输入法组词过程中按 Enter 是确认选词，误发会送出半成品；改为跟踪 IME 组合事件拦截

### 健壮性

- **坏帧不再拆链**：长度合法但 JSON 解析失败时只丢该帧（新增 `ReadOutcome` 区分「流结束」与「坏帧」），原先一律 `break`，对端发一条格式错误报文就会断链
- **落盘原子化**：改用临时文件 + `File.Replace`，避免崩溃留下半截 JSON；历史损坏时改名备份而非静默丢弃
- **修正 `ReferenceEquals` 比较字符串**（恒为 false，导致条件恒真）
- **逐条报文日志降为 Debug**：心跳与回执会以 Info 级别刷屏，几 MB 即触发 5MB 日志轮转
- **修复合并残留的 XML 注释未闭合**

### 测试

新增 5 个用例（`ChatStoreTests` 覆盖未读累计/清零、`LastActivityAt`、会话键迁移），全量 **166 个用例**通过。

---

## Version 0.6.0 (2026-10-02)

### 新功能

#### 1. 局域网聊天（类 QQ）
**背景：** Windows 已无可用的局域网发消息命令——`net send` 在 Vista 被移除，`msg` 仅对远程桌面会话主机生效。因此本功能从零实现网络层。

**新增文件（Core，纯 BCL 零 NuGet 依赖）：**
- `YiziShare.Core/ChatProtocol.cs` - 消息种类枚举、消息信封、4 字节大端长度前缀分帧、连接去重规则
- `YiziShare.Core/ChatPresence.cs` - 在线表（IP/昵称/端口/最后可见时间），注入时钟以便可测
- `YiziShare.Core/ChatDelivery.cs` - 投递状态机与回执关联，注入时钟以便可测
- `YiziShare.Core/ChatStore.cs` - 聊天历史持久化与保留策略（2 秒防抖落盘）
- `YiziShare.Core/ChatService.cs` - UDP 广播发现 + TCP 点对点传输（仓库首个长生命周期有状态服务，实现 IDisposable）

**新增文件（App）：**
- `YiziShare.App/ViewModels/ChatViewModel.cs` - 服务包装、跨线程封送、未读管理
- `YiziShare.App/ViewModels/ConversationViewModel.cs` - 会话列表项
- `YiziShare.App/ViewModels/ChatMessageViewModel.cs` - 聊天气泡项
- `YiziShare.App/MainWindow.Chat.cs` - partial 块：标签切换、会话选择、发送与滚动

**新增测试（4 个测试类 79 个用例）：**
- `ChatProtocolTests`（22 例）- 分帧往返、长度防御、脏报文不抛异常
- `ChatPresenceTests`（14 例）- 离线阈值判定、容忍连续 4 次心跳丢失
- `ChatDeliveryTests`（17 例）- 状态流转、超时降级、迟到回执升级
- `ChatStoreTests`（15 例）- 保留策略、幂等追加、已读位持久化
- `IpRangeUtilTests` 补充 `BroadcastAddress` 5 例

#### 2. 界面与交互改进
- **共享/聊天双标签页** - 右侧详情面板新增 34px 标签条（金色指示条），聊天页内嵌「会话列表 + 聊天区」
- **设备卡右键菜单** - 新增「💬 发起聊天」
- **未读提醒** - 标签页红点 + 会话列表角标（>99 显示 99+）+ 托盘气泡（同一对端 60 秒节流）
- **在线状态** - 会话列表与聊天头部在线圆点，连接建立/断开实时更新

#### 3. 视觉修复
- **头像配色统一为品牌金** - 设备列表头像与详情面板图标由深藏蓝改为金色，收藏态亮金 `#F0B429`、未选中浅金 `#D9A441`
- **收藏星标放大** - 统一使用文字字形（未收藏 `☆` / 收藏 `★`），字号 11→15/16px，取消 0.6 透明度衰减
- **按钮 hover 重设计** - `GlassButton` 原本 hover 时强制覆盖背景为半透明白，亮色模式下白底白字导致按钮消失；改为叠加 8%/16% 黑色遮罩加深，对任意背景色成立
- **ComboBox 黑角修复** - Popup 补 `AllowsTransparency="True"`，圆角外不再露出黑色角落
- **窗口圆角透明** - 移除三个贴边根 Border 的 `DropShadowEffect`（阴影被窗口边界裁切，只在四个圆角缺口处露出灰雾）
- **设备选中态修复** - `IsSelected` 变化时补发派生画刷属性变更通知，背景/描边/头像/文字随选中切换
- **筛选与排序重构** - 「分组」改为「筛选（所有/收藏）」，排序精简为 IP/名称；主列表与显示列表分离，扫描/定时刷新不再重置筛选排序

#### 4. 基础设施
- `IpRangeUtil.BroadcastAddress()` - 纯函数计算子网定向广播地址（多网卡逐个发送，绕开 255.255.255.255 路由不确定问题）
- `MainViewModel.EnsureChatPeer()` - 聊天认识但扫描未发现的设备补进主列表

### 已知限制
- **Windows 防火墙可能拦截 TCP 入站**（应用为 asInvoker 非管理员）。主动拨号降级路径可覆盖本端主动连接场景，但对方入站放行仍需用户手动允许
- **企业网 VLAN / AP 客户端隔离**下 UDP 广播会被交换机丢弃，此时需从设备列表手动发起聊天（惰性拨号仍可连通）
- **传输未加密**，同网段可抓包读取明文。协议已预留 `Version` 字段，后续可在 `ChatFrame` 单点封装 AES-GCM
- 对端未安装本应用时会显示离线，不会误判

---

## Version 0.5.0 (2024-08-27)

### 新功能

#### 1. MVVM 架构重构
**新增文件：**
- `YiziShare.App/ViewModels/ViewModelBase.cs` - INotifyPropertyChanged 基类
- `YiziShare.App/ViewModels/RelayCommand.cs` - ICommand 实现
- `YiziShare.App/ViewModels/MainViewModel.cs` - 主窗口 ViewModel
- `YiziShare.App/ViewModels/DeviceViewModel.cs` - 设备 ViewModel（含主题感知属性）
- `YiziShare.App/Converters/Converters.cs` - 值转换器（背景/边框/头像/文字颜色）

**架构改进：**
- 设备列表改用 `ItemsControl` + `DataTemplate` 数据绑定
- 共享卡片改用 `DataTemplate` 数据绑定
- 主题切换时自动刷新所有绑定属性
- 移除代码拼装 UI 逻辑，改用声明式 XAML

#### 2. 设置窗口
**新增文件：**
- `YiziShare.App/SettingsWindow.xaml` - 设置界面
- `YiziShare.App/SettingsWindow.xaml.cs` - 设置逻辑
- `YiziShare.Core/SettingsStore.cs` - 设置持久化（JSON）

**功能：**
- 开机自启动（注册表 Run 键）
- 后台自动刷新开关
- 刷新间隔配置（分钟）
- 记住用户名选项
- 网卡选择

#### 3. 文件浏览器
**新增文件：**
- `YiziShare.App/FileBrowserWindow.xaml` - 文件浏览界面
- `YiziShare.App/FileBrowserWindow.xaml.cs` - 文件浏览逻辑

**功能：**
- 目录浏览（双击进入）
- 文件下载（选择目标目录）
- 文件上传（多选文件）
- 刷新目录

#### 4. 历史记录
**新增文件：**
- `YiziShare.Core/HistoryStore.cs` - 连接历史持久化（JSON）

**功能：**
- 记录设备最后连接时间
- 记录连接次数
- 在设备详情面板显示

#### 5. 日志系统
**新增文件：**
- `YiziShare.Core/Logger.cs` - 文件日志

**功能：**
- 多级别日志（Debug/Info/Warning/Error）
- 自动日志轮转（5MB）
- 保存到 `%LocalAppData%\YiziShare\app.log`

#### 6. 高 DPI 适配
**新增文件：**
- `YiziShare.App/app.manifest` - DPI 感知声明

**功能：**
- PerMonitorV2 DPI 感知
- 支持多显示器不同 DPI

#### 7. CI/CD
**新增文件：**
- `.github/workflows/build.yml` - GitHub Actions 工作流

**功能：**
- 自动构建
- 自动测试
- 自动发布

#### 8. 代码规范
**新增文件：**
- `.editorconfig` - 编辑器配置

### 修复

| Bug | 原因 | 修复 |
|-----|------|------|
| 主题切换后设备卡片颜色不更新 | 转换器缓存 | 改用 ViewModel 属性绑定 |
| 弹窗阴影畸形 | AllowsTransparency=true | 移除阴影效果 |
| 按钮 hover 不可见 | 未设置背景色 | 添加 CardHoverBrush 背景 |
| 设备列表昵称按钮点击无效 | PreviewMouseLeftButtonDown 拦截 | 添加 Button 类型检查 |
| 状态栏颜色不随主题变化 | 使用静态资源 | 改用 DynamicResource |
| 构建时 Brush 命名空间冲突 | WinForms 混用 | 添加 using 别名 |

### 测试

**新增测试文件：**
- `YiziShare.Tests/RepairServiceTests.cs` - 修复服务测试（5 用例）
- `YiziShare.Tests/HistoryStoreTests.cs` - 历史记录测试（4 用例）
- `YiziShare.Tests/NetworkScannerTests.cs` - 网络扫描测试（6 用例）
- `YiziShare.Tests/SettingsStoreTests.cs` - 设置存储测试（7 用例）

**测试总数：** 60 用例全部通过

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
