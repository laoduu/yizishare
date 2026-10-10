using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiziShare.App.ViewModels;
using YiziShare.Core;
using InlineUIContainer = System.Windows.Documents.InlineUIContainer;
using Image = System.Windows.Controls.Image;

namespace YiziShare.App;

/// <summary>聊天功能的界面逻辑：标签切换、会话选择、发送与滚动</summary>
public partial class MainWindow
{
    private const string TabShare = "share";
    private const string TabChat = "chat";

    /// <summary>默认落在聊天页（QQ 形态：聊天为主，共享是副功能）</summary>
    private string _currentTab = TabChat;

    /// <summary>共享文件夹内置浏览进行中：共享 tab 显示文件浏览而不是共享列表</summary>
    private bool _isBrowsingFiles;

    /// <summary>输入法是否正在组词（组词时按 Enter 是确认选词，不应发送消息）</summary>
    private bool _isImeComposing;

    /// <summary>当前展开的标签页，聊天页收到消息时才知道该不该直接标已读</summary>
    public string CurrentTab => _currentTab;

    #region 标签切换
    private void TabShare_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(TabShare);
    }

    private void TabChat_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(TabChat);
    }

    private void SwitchTab(string tab)
    {
        _currentTab = tab;

        bool isChat = tab == TabChat;
        SharePane.Visibility = isChat ? Visibility.Collapsed : Visibility.Visible;
        ChatPane.Visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        FilePane.Visibility = (!isChat && _isBrowsingFiles) ? Visibility.Visible : Visibility.Collapsed;
        // 浏览进行中共享 tab 的内容在 FilePane 里，避免和文件视图叠在一起
        if (_isBrowsingFiles && !isChat)
            SharePane.Visibility = Visibility.Collapsed;

        UpdateTabIndicator();
        UpdateChatVisibilityState();

        if (isChat)
        {
            // 聊天对象始终跟随设备列表的选中项：选中谁就跟谁聊天
            SyncChatTargetToSelectedDevice();
            UpdateChatEmptyState();

            var active = _viewModel.Chat.SelectedConversation;
            if (active != null)
            {
                _viewModel.Chat.MarkRead(active);
                ScrollChatToBottom();
            }

            // 标已读后托盘"未读 N 条"文案要跟着清零
            UpdateTrayUnreadState();
        }

        // 离开聊天页不清空选中会话：消息一直在接收和落盘，
        // 切回来时应恢复到同一台设备的对话，而不是要求用户重新选择
    }

    /// <summary>
    /// 让聊天对象跟随设备列表的选中项。<br/>
    /// 聊天入口只有这一条：设备列表点选设备 → 切到聊天标签页即与该设备对话。
    /// </summary>
    private void SyncChatTargetToSelectedDevice()
    {
        var device = _viewModel.SelectedDevice;
        if (device == null)
        {
            _viewModel.Chat.SelectedConversation = null;
            return;
        }

        // 会话按设备隔离：按 IP 找到已有会话就复用，保留消息与未读状态
        _viewModel.Chat.OpenConversation($"ip-{device.IpAddress}", device.IpAddress,
            device.DisplayName, ChatConstants.TcpPort);
    }

    /// <summary>把"面板是否展开 + 是否在聊天页 + 窗口是否前台"同步给 ViewModel，供未读判定使用</summary>
    private void UpdateChatVisibilityState()
    {
        _viewModel.Chat.IsChatTabActive = _currentTab == TabChat;
        _viewModel.Chat.IsChatVisible = RightPanel.Visibility == Visibility.Visible;
        _viewModel.Chat.IsActive = IsActive && Visibility == Visibility.Visible;
    }

    /// <summary>窗口重新获得焦点时，把当前会话标记为已读</summary>
    private void MarkActiveConversationRead()
    {
        var active = _viewModel.Chat.SelectedConversation;
        if (active != null && _currentTab == TabChat)
            _viewModel.Chat.MarkRead(active);

        UpdateTrayUnreadState();
    }

    /// <summary>打开共享文件夹的内置浏览：面板内切换到文件视图（不弹窗）</summary>
    private void ShowShareBrowser(string rootPath)
    {
        _isBrowsingFiles = true;
        ShareBrowser.Open(rootPath);

        if (_currentTab != TabShare)
            SwitchTab(TabShare);
        else
            UpdatePaneVisibility();

        UpdateChatVisibilityState();
    }

    /// <summary>按当前 tab 与浏览状态同步三个内容面板的可见性</summary>
    private void UpdatePaneVisibility()
    {
        bool isChat = _currentTab == TabChat;
        ChatPane.Visibility = isChat ? Visibility.Visible : Visibility.Collapsed;
        FilePane.Visibility = (!isChat && _isBrowsingFiles) ? Visibility.Visible : Visibility.Collapsed;
        SharePane.Visibility = (!isChat && !_isBrowsingFiles) ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region 聊天对象
    /// <summary>从设备列表发起会话（右键菜单 → 发起聊天）</summary>
    private void StartChatWithDevice(DeviceViewModel device)
    {
        // 会话按设备隔离：每台设备一个独立会话，聊天内容互不混淆。
        // 设备刚被发现时还不知道对方的稳定标识，先用 IP 占位，
        // 握手拿到真实标识后 EnsureConversation 会改写。
        _viewModel.Chat.OpenConversation($"ip-{device.IpAddress}", device.IpAddress,
            device.DisplayName, ChatConstants.TcpPort);

        UpdateChatEmptyState();
        SwitchTab(TabChat);
        ScrollChatToBottom();
    }

    /// <summary>设备卡片右键 → 发起聊天</summary>
    private void StartChat_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DeviceViewModel device })
            StartChatWithDevice(device);
    }

    /// <summary>文件浏览视图请求返回 → 回到共享列表</summary>
    private void ShareBrowser_BackRequested(object? sender, EventArgs e)
    {
        _isBrowsingFiles = false;

        if (_currentTab != TabShare)
            SwitchTab(TabShare);
        else
            UpdatePaneVisibility();

        UpdateChatVisibilityState();
    }

    /// <summary>空态下的「返回设备列表」：回到共享页去挑设备</summary>
    private void BackToShare_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab(TabShare);
    }

    /// <summary>把金色指示条移到当前激活的标签下方</summary>
    private void UpdateTabIndicator()
    {
        if (TabIndicator == null) return;

        var parent = TabIndicator.Parent as Grid;
        if (parent == null) return;

        TabIndicator.SetValue(Grid.ColumnProperty, _currentTab == TabChat ? 0 : 1);
    }

    private void UpdateChatEmptyState()
    {
        bool hasConversation = _viewModel.Chat.HasActiveConversation;
        ChatEmptyHint.Visibility = hasConversation ? Visibility.Collapsed : Visibility.Visible;
        ChatHeader.Visibility = hasConversation ? Visibility.Visible : Visibility.Collapsed;
        ChatInputArea.Visibility = hasConversation ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region 发送

    /// <summary>表情面板数据项：字符 + 对应的 Twemoji 彩色图</summary>
    public sealed record EmojiItem(string Emoji, ImageSource? Image);

    private void BtnEmoji_Click(object sender, RoutedEventArgs e)
    {
        if (EmojiPanel.ItemsSource == null)
        {
            var items = Services.EmojiMap.All
                .Select(emoji => new EmojiItem(emoji, FindEmojiImage(emoji)))
                .ToArray();
            EmojiPanel.ItemsSource = items;
        }

        EmojiPopup.IsOpen = !EmojiPopup.IsOpen;
    }

    private static ImageSource? FindEmojiImage(string emoji)
    {
        var name = Services.EmojiMap.Lookup(emoji);
        if (name == null) return null;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri($"pack://application:,,,/Assets/Emoji/{name}.png");
        image.EndInit();
        image.Freeze();
        return image;
    }

    /// <summary>点击表情插入到输入框光标处，面板自动收起（用户能确认表情已填入）</summary>
    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string emoji }) return;

        var image = FindEmojiImage(emoji);
        var container = new InlineUIContainer(new Image
        {
            Source = image,
            Width = 22,
            Height = 22,
            Tag = emoji,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(1, 0, 1, 0)
        })
        { BaselineAlignment = BaselineAlignment.Bottom };

        var caret = ChatInputBox.CaretPosition;
        var paragraph = caret.Paragraph ?? new Paragraph();
        if (paragraph != caret.Paragraph)
            ChatInputBox.Document.Blocks.Add(paragraph);

        if (caret.Parent is Inline anchor)
            paragraph.Inlines.InsertBefore(anchor, container);
        else
            paragraph.Inlines.Add(container);

        ChatInputBox.CaretPosition = container.ElementEnd;
        EmojiPopup.IsOpen = false;
        ChatInputBox.Focus();
    }

    /// <summary>提取输入内容：Run 贡献文字，表情图片按 Tag 还原为原始字符</summary>
    private string ExtractInputText()
    {
        var sb = new StringBuilder();
        foreach (var block in ChatInputBox.Document.Blocks)
        {
            if (block is not Paragraph paragraph) continue;
            foreach (var inline in paragraph.Inlines)
            {
                switch (inline)
                {
                    case Run run:
                        sb.Append(run.Text);
                        break;
                    case InlineUIContainer { Child: FrameworkElement { Tag: string emoji } }:
                        sb.Append(emoji);
                        break;
                    case LineBreak:
                        sb.Append('\n');
                        break;
                }
            }
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>
    /// 粘贴处理：文件 → 直接作为消息发送；位图（截图）→ 存临时 PNG 作为图片消息发送；
    /// 纯文本 → 正常插入输入框。微信同款行为。
    /// </summary>
    private async void ChatInputBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        // 1) 文件（资源管理器里 Ctrl+C 的文件）
        if (e.DataObject.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            e.CancelCommand();
            if (e.DataObject.GetData(System.Windows.DataFormats.FileDrop) is string[] files)
            {
                foreach (var file in files)
                    await _viewModel.Chat.SendAttachmentAsync(file);
                ScrollChatToBottom();
            }
            return;
        }

        // 2) 位图（截图工具 / 网页复制图片）
        if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Bitmap))
        {
            e.CancelCommand();
            try
            {
                if (e.DataObject.GetData(System.Windows.DataFormats.Bitmap) is System.Windows.Media.Imaging.BitmapSource source)
                {
                    var tempPath = Path.Combine(
                        System.IO.Path.GetTempPath(), $"yizishare_{Guid.NewGuid():N}.png");

                    using var stream = new FileStream(tempPath, FileMode.Create);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(source));
                    encoder.Save(stream);

                    await _viewModel.Chat.SendAttachmentAsync(tempPath);
                    ScrollChatToBottom();
                }
            }
            catch (Exception ex)
            {
                _viewModel.StatusMessage = $"图片粘贴失败: {ex.Message}";
            }
            return;
        }

        // 3) 纯文本：正常插入输入框
        if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
        {
            e.FormatToApply = System.Windows.DataFormats.Text;
            return;
        }

        e.CancelCommand();
    }

    /// <summary>选择文件/图片作为消息发送（按扩展名决定渲染方式）</summary>
    private async void BtnAttach_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择要发送的文件" };
        if (dialog.ShowDialog() != true) return;

        await _viewModel.Chat.SendAttachmentAsync(dialog.FileName);
        ScrollChatToBottom();
    }

    /// <summary>点击图片消息：用系统看图器打开</summary>
    private void BubbleImage_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChatMessageViewModel msg } && msg.IsImage && File.Exists(msg.AttachmentPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = msg.AttachmentPath,
                UseShellExecute = true
            });
        }
    }

    /// <summary>气泡右键菜单打开前：按消息种类启用"复制文本/复制图片/另存为"</summary>
    private void Bubble_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessageViewModel msg, ContextMenu: ContextMenu menu })
            return;

        foreach (var item in menu.Items.OfType<System.Windows.Controls.MenuItem>())
        {
            item.Visibility = item.Header switch
            {
                "复制文本" => msg is { IsImage: false, IsFile: false } ? Visibility.Visible : Visibility.Collapsed,
                "复制图片" => msg.IsImage ? Visibility.Visible : Visibility.Collapsed,
                "另存为..." => msg.HasAttachment ? Visibility.Visible : Visibility.Collapsed,
                _ => item.Visibility
            };
        }
    }

    private void BubbleCopyText_Click(object sender, RoutedEventArgs e)
    {
        var msg = (sender as FrameworkElement)?.DataContext as ChatMessageViewModel;
        if (msg is not { IsImage: false, IsFile: false }) return;
        System.Windows.Clipboard.SetText(msg.Text);
    }

    private void BubbleCopyImage_Click(object sender, RoutedEventArgs e)
    {
        var msg = (sender as FrameworkElement)?.DataContext as ChatMessageViewModel;
        if (msg is not { IsImage: true } imageMessage) return;
        if (!File.Exists(imageMessage.AttachmentPath)) return;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(imageMessage.AttachmentPath);
        image.EndInit();
        if (image.CanFreeze) image.Freeze();

        System.Windows.Clipboard.SetImage(image);
    }

    private void BubbleSaveAs_Click(object sender, RoutedEventArgs e)
    {
        var msg = (sender as FrameworkElement)?.DataContext as ChatMessageViewModel;
        if (msg is not { HasAttachment: true } attachmentMessage) return;
        if (!File.Exists(attachmentMessage.AttachmentPath)) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Path.GetFileName(attachmentMessage.AttachmentPath)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.Copy(attachmentMessage.AttachmentPath, dialog.FileName, overwrite: true);
            _viewModel.StatusMessage = $"已保存到 {dialog.FileName}";
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"保存失败: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>拖动输入区顶部横条调整输入框高度：向上拖加高，向下拖降低</summary>
    private void InputResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        // 拖动后转为手动固定高度（覆盖随内容自增长），范围 [52, 280]
        double newHeight = ChatInputBox.ActualHeight - e.VerticalChange;
        ChatInputBox.Height = Math.Clamp(newHeight, ChatInputBox.MinHeight, ChatInputBox.MaxHeight);
    }

    private void ChatSend_Click(object sender, RoutedEventArgs e)
    {
        SendChatInputAsync();
    }

    private void ChatInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        // Shift+Enter 换行（TextBox 已设 AcceptsReturn），不拦截
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            return;

        // 中文输入法组词过程中按 Enter 是确认选词，此时不能发送，否则会发出半成品。
        // WPF 没有直接的 IsInComposition 属性，改为跟踪 IME 组合事件。
        if (_isImeComposing)
            return;

        // Enter 发送，必须拦截以阻止插入换行
        e.Handled = true;
        SendChatInputAsync();
    }

    private void SendChatInputAsync()
    {
        var text = ExtractInputText();
        if (string.IsNullOrWhiteSpace(text)) return;

        ChatInputBox.Document.Blocks.Clear();
        ChatInputBox.Document.Blocks.Add(new Paragraph());
        ChatInputBox.CaretPosition = ChatInputBox.Document.ContentStart;
        _viewModel.Chat.Send(text);
        ScrollChatToBottom();
    }

    /// <summary>点击失败/待发送标记重发该条消息</summary>
    private void ChatRetry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ChatMessageViewModel message })
            return;
        if (!message.IsRetryable) return;

        _viewModel.Chat.Retry(message);
        ScrollChatToBottom();
    }

    #endregion

    #region 滚动与提醒

    private void ScrollChatToBottom()
    {
        if (ChatScrollViewer == null) return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            ChatScrollViewer.ScrollToEnd();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>后台收到新消息：播放提示音、右下角横幅提醒（微信样式）、更新托盘未读态</summary>
    private void OnMessageReceivedInBackground(object? sender, ConversationViewModel conversation)
    {
        UpdateTrayUnreadState();

        // 正在查看的会话走 MessageReceivedWhileViewing，不会到这里；
        // 后台到达才响铃，避免用户盯着窗口还被反复提示
        if (SettingsStore.GetChatSoundEnabled())
            Services.SoundHelper.PlayMessageSound();

        // 横幅单例自动合并连续消息，无需按对端节流；
        // 不用托盘气泡：系统气泡自带 Windows 提示音，会与本应用的提示音叠加
        NotificationBanner.ShowBanner(
            conversation.PeerName,
            conversation.LastPreview,
            () => OpenChatFromBanner(conversation.PeerIp));

        _viewModel.StatusMessage = $"🔔 {conversation.PeerName}: {conversation.LastPreview}";
    }

    /// <summary>点击横幅：唤出主窗口并直接跳到该设备的聊天会话</summary>
    private void OpenChatFromBanner(string peerIp)
    {
        ShowFromTray(balloon: false);

        var device = _viewModel.AllDevices.FirstOrDefault(d =>
            string.Equals(d.IpAddress, peerIp, StringComparison.OrdinalIgnoreCase));
        if (device == null)
        {
            _viewModel.StatusMessage = $"⚠️ {peerIp} 当前不在设备列表中，试试重新扫描";
            return;
        }

        SelectDevice(device);   // 已默认落在聊天页
        ScrollChatToBottom();
    }

    /// <summary>正在查看会话时收到消息：自动滚动到底部</summary>
    private void OnMessageReceivedWhileViewing(object? sender, ConversationViewModel conversation)
    {
        ScrollChatToBottom();
    }

    /// <summary>托盘图标显示未读角标（文字形式，避免额外图片资源）</summary>
    private void UpdateTrayUnreadState()
    {
        if (_notifyIcon == null) return;

        var total = _viewModel.Chat.TotalUnread;
        _notifyIcon.Text = total > 0
            ? $"YiziShare（{total} 条未读）"
            : "YiziShare";
    }

    #endregion

    #region 输入法
    /// <summary>
    /// 跟踪输入法的组词状态，避免中文选词时误触发发送：
    /// 组词过程中按 Enter 是「确认选词」，若同时发消息会送出半成品。
    /// </summary>
    private void TrackImeComposition()
    {
        TextCompositionEventHandler started = (_, _) => _isImeComposing = true;
        TextCompositionEventHandler updated = (_, _) => _isImeComposing = true;
        TextCompositionEventHandler ended = (_, _) => _isImeComposing = false;

        System.Windows.Input.TextCompositionManager.AddPreviewTextInputStartHandler(ChatInputBox, started);
        System.Windows.Input.TextCompositionManager.AddPreviewTextInputUpdateHandler(ChatInputBox, updated);
        System.Windows.Input.TextCompositionManager.AddPreviewTextInputHandler(ChatInputBox, ended);
    }
    #endregion
}
