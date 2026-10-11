using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

/// <summary>
/// 聊天界面的服务包装层：持有 <see cref="ChatService"/>，把所有来自 socket 线程的事件
/// 统一封送到 UI 线程，并管理会话集合与未读计数。
/// </summary>
public class ChatViewModel : ViewModelBase
{
    private readonly ChatService _service = new();
    private ConversationViewModel? _selectedConversation;

    public ChatViewModel()
    {
        _service.MessageReceived += OnMessageReceived;
        _service.PeerPresenceChanged += OnPeerPresenceChanged;
        _service.PeerProfileUpdated += OnPeerProfileUpdated;
        _service.PeerResolved += OnPeerResolved;
        _service.PeerDisconnected += OnPeerDisconnected;
        _service.DeliveryChanged += OnDeliveryChanged;
        _service.FileTransferProgress += OnFileTransferProgress;
        _service.FileTransferCancelled += OnFileTransferCancelled;
        _service.FileOfferReceived += OnFileOfferReceived;
    }

    /// <summary>待用户确认的大文件接收请求：FileId → (Offer 信封, 对端地址)，供「接收/拒绝」执行</summary>
    private readonly ConcurrentDictionary<string, (ChatEnvelope Offer, string PeerIp, int PeerPort)> _pendingIncomingFiles = new(StringComparer.Ordinal);

    /// <summary>收到大文件接收提议：创建「待接收」气泡（含 接收/拒绝 按钮），不自动拉取</summary>
    private void OnFileOfferReceived(object? sender, InboundChatEventArgs args)
    {
        var offer = args.Envelope;

        PostToUi(() =>
        {
            var conversation = EnsureConversation(offer.NodeId, args.PeerIp, offer.Name);
            if (conversation.PeerPort <= 0) conversation.PeerPort = ChatConstants.TcpPort;

            _pendingIncomingFiles[offer.FileId] = (offer, args.PeerIp, args.PeerPort);

            var displayName = string.IsNullOrEmpty(offer.FileName) ? "[大文件]" : offer.FileName;
            // 传输期一律按「文件卡片」渲染（即使是大图片）：卡片才承载 接收/拒绝 按钮与进度条；
            // 完成后 CompleteAttachment(path, isImage) 再按真实类型切到图片展示
            var message = new ChatMessageViewModel(offer.MsgId, false, displayName,
                DateTime.Now, MessageState.Sending, isAttachment: true, isImage: false, attachmentPath: "");
            conversation.AppendMessage(message);
            // 待用户确认：显示「接收 / 拒绝」按钮（漏设则按钮组永不显示——已实测踩坑）
            message.AwaitingAcceptance = true;
            message.IsLargeFileOffer = true;

            ChatStore.AppendMessage(conversation.PeerId, conversation.PeerIp, conversation.PeerName,
                new ChatMessageRecord
                {
                    MsgId = offer.MsgId,
                    IsMine = false,
                    Text = displayName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    State = MessageState.Sending,
                    IsImage = false, // 同上：接收中按文件卡片落盘，避免重载后出现空图片气泡
                    AttachmentName = offer.FileName
                },
                isIncoming: true);

            SortConversations();
            StatusRequested?.Invoke(this, $"📥 {conversation.PeerName} 发来文件 {displayName}，请在卡片上选择接收或拒绝");
            // 待接收卡片进入视野（若是当前会话则自动下滚，避免按钮在窗口外被遮挡）
            RaiseAttachmentStateChanged(message);
            MessageReceivedInBackground?.Invoke(this, conversation);
        });
    }

    /// <summary>用户确认接收：回拨发送方拉取（进度条在原气泡上继续显示）</summary>
    public void AcceptFile(string msgId)
    {
        if (!_pendingIncomingFiles.TryRemove(msgId, out var pending))
            return;

        var conversation = Conversations.FirstOrDefault(c =>
            c.Messages.Any(m => string.Equals(m.MsgId, msgId, StringComparison.Ordinal)));
        // 清除待确认态（按钮组收起，进度条接管）；附件路径在完成时由服务层填充
        if (conversation?.Messages.FirstOrDefault(m => string.Equals(m.MsgId, msgId, StringComparison.Ordinal))
            is { } receivingMsg)
        {
            receivingMsg.AwaitingAcceptance = false;
            RaiseAttachmentStateChanged(receivingMsg);
        }

        _ = _service.AcceptLargeFileAsync(msgId);
    }

    /// <summary>
    /// 用户拒绝接收：本端气泡显示「已拒绝接收」（我方动作），对端显示「对方拒绝接收」（peerReason）。<br/>
    /// 拒绝后按钮组立即收起——拒绝是终态，不允许「先拒绝后同意」的恢复路径（需发送方重新发起）。
    /// </summary>
    public void RejectFile(string msgId, string peerReason)
    {
        if (_pendingIncomingFiles.TryRemove(msgId, out _))
        {
            if (FindMessageById(msgId) is { } msg)
            {
                msg.AwaitingAcceptance = false; // 收起「接收/拒绝」按钮组，防止拒绝后仍可点接收
                msg.FailureReason = "已拒绝接收";
                msg.State = MessageState.Failed;
                RaiseAttachmentStateChanged(msg);
            }
            _ = _service.RejectLargeFileAsync(msgId, peerReason);
        }
    }

    /// <summary>
    /// 对端中止了大文件传输：文案取自对端实报——「对方拒绝接收」（开场即拒绝）或「对方已取消传送」（传送中取消）。<br/>
    /// 两者语义不同（拒绝=未开始，取消=过程中中断），必须区分，不能本端硬编码。
    /// </summary>
    private void OnFileTransferCancelled(object? sender, FileAbortEventArgs e)
    {
        PostToUi(() =>
        {
            if (FindMessageById(e.FileId) is not { } msg) return;
            msg.AwaitingAcceptance = false;
            msg.FailureReason = string.IsNullOrEmpty(e.Reason) ? "对方已取消传送" : e.Reason;
            msg.State = MessageState.Failed;
            RaiseAttachmentStateChanged(msg);
            StatusRequested?.Invoke(this, $"🚫 {msg.FailureReason}");
        });
    }

    /// <summary>附件消息进入终态（完成/失败/取消）：供 UI 自动下滚显示最新状态</summary>
    public event EventHandler<string>? AttachmentStateChanged;

    private void RaiseAttachmentStateChanged(ChatMessageViewModel msg) => AttachmentStateChanged?.Invoke(this, msg.MsgId);

    /// <summary>按 MsgId 在全部会话中定位消息（进度事件高频，遍历成本可接受）</summary>
    private ChatMessageViewModel? FindMessageById(string msgId)
        => Conversations.SelectMany(c => c.Messages)
            .FirstOrDefault(m => string.Equals(m.MsgId, msgId, StringComparison.Ordinal));

    /// <summary>消息是否属于当前查看的会话（供 UI 决定是否自动下滚）</summary>
    public bool IsMessageInSelectedConversation(string msgId)
        => SelectedConversation?.Messages.Any(m => string.Equals(m.MsgId, msgId, StringComparison.Ordinal)) == true;

    /// <summary>大文件分块进度 → 对应气泡的进度条（事件在后台线程，需切 UI）</summary>
    private void OnFileTransferProgress(object? sender, FileTransferProgressEventArgs e)
    {
        PostToUi(() =>
        {
            FindMessageById(e.FileId)?.ReportProgress(e.BytesDone, e.TotalBytes);
        });
    }

    /// <summary>取消进行中的大文件传输（本地气泡立即显示"已取消传送"，对端显示"对方已取消传送"）</summary>
    public void CancelFileTransfer(string fileId)
    {
        if (FindMessageById(fileId) is { } msg)
        {
            msg.FailureReason = "已取消传送";
            msg.State = MessageState.Failed;
            RaiseAttachmentStateChanged(msg);
        }
        _service.CancelFileTransfer(fileId);
    }

    /// <summary>登记定向单播心跳目标（扫描发现的设备 IP，广播被防火墙拦截时的兜底发现途径）</summary>
    public void RegisterUnicastPeer(string ip) => _service.RegisterUnicastPeer(ip);

    /// <summary>界面显示名（昵称优先，回退主机名）</summary>
    public string LocalName
    {
        get => _service.LocalName;
        set => _service.LocalName = value;
    }

    public ObservableCollection<ConversationViewModel> Conversations { get; } = new();

    /// <summary>全部会话的未读总数，供标签页红点使用</summary>
    public int TotalUnread
    {
        get
        {
            int total = 0;
            foreach (var conversation in Conversations)
                total += conversation.UnreadCount;
            return total;
        }
    }

    public bool HasUnread => TotalUnread > 0;

    public string UnreadBadgeText => TotalUnread > 99 ? "99+" : TotalUnread.ToString();

    /// <summary>对端在线状态跃迁（转发自 ChatService，供设备列表同步"YiziShare 在线"与对端昵称）</summary>
    public event EventHandler<PeerPresence>? PeerPresenceChanged;

    /// <summary>对端在线期间修改了昵称（供设备列表即时更新显示名）</summary>
    public event EventHandler<PeerPresence>? PeerProfileUpdated;

    /// <summary>会话列表是否为空（用于显示「还没有会话」提示）</summary>
    public bool HasConversations => Conversations.Count > 0;

    public ConversationViewModel? SelectedConversation
    {
        get => _selectedConversation;
        set
        {
            if (_selectedConversation == value) return;

            if (SetProperty(ref _selectedConversation, value) && value != null)
                MarkRead(value);

            OnPropertyChanged(nameof(ActivePeerIp));
            OnPropertyChanged(nameof(ActivePeerName));
            OnPropertyChanged(nameof(HasActiveConversation));
        }
    }

    public bool HasActiveConversation => SelectedConversation != null;

    public string ActivePeerIp => SelectedConversation?.PeerIp ?? "";

    public string ActivePeerName => SelectedConversation?.PeerName ?? "";

    /// <summary>窗口当前是否在前台可见（决定收消息时是否弹气泡、是否直接标已读）</summary>
    public bool IsChatVisible { get; set; }

    /// <summary>聊天标签页是否处于激活状态</summary>
    public bool IsChatTabActive { get; set; }

    /// <summary>后台收到新消息时触发（用于播放提示音、更新托盘图标与设备卡角标）</summary>
    public event EventHandler<ConversationViewModel>? MessageReceivedInBackground;

    /// <summary>正在查看的会话收到新消息时触发（用于自动滚动到底部）</summary>
    public event EventHandler<ConversationViewModel>? MessageReceivedWhileViewing;

    /// <summary>需要更新底部状态栏时触发</summary>
    public event EventHandler<string>? StatusRequested;

    /// <summary>发现扫描结果里没有的新对端时触发（对方装了本应用但没共享任何目录）</summary>
    public event EventHandler<(string Ip, string Name)>? PeerDiscovered;

    public async Task StartAsync()
    {
        await ChatStore.LoadAsync();
        ChatStore.StartAutoFlush();

        LoadHistory();

        await _service.StartAsync();
        StatusRequested?.Invoke(this, $"💬 局域网聊天已开启（UDP {ChatConstants.UdpPort} / TCP {_service.LocalPort}）");
    }

    public async Task StopAsync()
    {
        await ChatStore.StopAutoFlushAsync();
        _service.Dispose();
    }

    public async Task RebindAsync()
    {
        await _service.RebindAsync();
    }

    /// <summary>发起会话：设备列表点「聊天」时调用</summary>
    public void OpenConversation(string peerId, string peerIp, string peerName, int peerPort)
    {
        var conversation = EnsureConversation(peerId, peerIp, peerName);
        conversation.UpdatePeerInfo(peerIp, peerName, peerPort);
        SelectedConversation = conversation;

        // 重新进入某个会话时刷新状态栏，避免残留上一次的"发送失败"提示
        if (SelectedConversation == conversation)
            StatusRequested?.Invoke(this, conversation.IsOnline
                ? $"💬 与 {conversation.PeerName} 的会话"
                : $"⚠️ {conversation.PeerName} 当前离线，消息可能发送失败");
    }

    /// <summary>
    /// 发送一条聊天消息，返回是否受理。<br/>
    /// 消息<b>立即上屏</b>（状态 Sending），联网发送由服务在后台完成，
    /// 状态经 DeliveryChanged 实时流转：对端离线 → Queued「待发送」，连上 → Sent/Delivered/Read。
    /// </summary>
    public bool Send(string text)
    {
        var conversation = SelectedConversation;
        if (conversation == null || string.IsNullOrWhiteSpace(text))
            return false;

        var content = text.Trim();
        var msgId = _service.SendChatAsync(conversation.PeerId, conversation.PeerIp, conversation.PeerPort, content);

        var message = new ChatMessageViewModel(msgId, true, content, DateTime.Now, MessageState.Sending);
        conversation.AppendMessage(message);

        // 登记到上屏之间存在同步快路径（已有连接时），状态可能已流转到 Queued/Sent，
        // 补读一次真实状态，避免气泡卡在「⏳」
        message.State = _service.GetDeliveryState(msgId);

        ChatStore.AppendMessage(conversation.PeerId, conversation.PeerIp, conversation.PeerName,
            new ChatMessageRecord
            {
                MsgId = msgId,
                IsMine = true,
                Text = content,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                State = message.State
            });

        NotifyUnreadChanged();
        StatusRequested?.Invoke(this, $"📤 正在发送到 {conversation.PeerName}...");
        return true;
    }

    /// <summary>发送图片/文件消息（超过 5MB 或文件不存在时拒绝并提示）</summary>
    public async Task<bool> SendAttachmentAsync(string filePath)
    {
        var conversation = SelectedConversation;
        if (conversation == null || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        var isImage = IsImageFile(filePath);
        var name = Path.GetFileName(filePath);
        var size = new FileInfo(filePath).Length;

        string? msgId;
        if (size <= ChatConstants.MaxAttachmentBytes)
        {
            // ≤5MB：单帧内联（支持离线补发）
            msgId = await _service.SendAttachmentAsync(
                conversation.PeerId, conversation.PeerIp, conversation.PeerPort, filePath, isImage);
            if (msgId == null)
            {
                StatusRequested?.Invoke(this, $"❌ 发送 {name} 失败");
                return false;
            }
        }
        else
        {
            // >5MB：流式传输，需对方在线（P2P 无离线暂存）
            if (!conversation.IsOnline)
            {
                StatusRequested?.Invoke(this, $"❌ {conversation.PeerName} 离线，大于 5MB 的文件需要对方在线才能发送");
                return false;
            }

            msgId = await _service.SendFileLargeAsync(
                conversation.PeerId, conversation.PeerIp, conversation.PeerPort, filePath);
            if (msgId == null)
            {
                StatusRequested?.Invoke(this, $"❌ 发送 {name} 失败");
                return false;
            }
        }

        // 预览文本：图片统一显示 [图片]，文件显示文件名
        var previewText = isImage ? "[图片]" : name;
        // 大文件走流式通道：传输期按文件卡片渲染（承载进度条 + 取消），完成后按真实类型切回图片
        var message = new ChatMessageViewModel(msgId, true, previewText, DateTime.Now, MessageState.Sending,
            isAttachment: true, isImage: isImage && size <= ChatConstants.MaxAttachmentBytes, attachmentPath: filePath);
        conversation.AppendMessage(message);
        message.State = _service.GetDeliveryState(msgId);
        // 大文件：标记流式通道，并补发 Offer 阶段丢失的 0% 进度事件（事件先于气泡创建）；
        // 内联附件不初始化进度——否则 5MB 以下文件也会闪出一条无意义的进度条
        message.IsLargeFileOffer = size > ChatConstants.MaxAttachmentBytes;
        if (message.IsLargeFileOffer) message.ReportProgress(0, size);

        ChatStore.AppendMessage(conversation.PeerId, conversation.PeerIp, conversation.PeerName,
            new ChatMessageRecord
            {
                MsgId = msgId,
                IsMine = true,
                Text = previewText,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                State = message.State,
                IsImage = isImage,
                AttachmentName = name,
                AttachmentPath = filePath
            });

        NotifyUnreadChanged();
        StatusRequested?.Invoke(this, isImage ? $"📤 正在发送图片 {name}" : $"📤 正在发送文件 {name}");
        return true;
    }

    /// <summary>按扩展名判断是否图片</summary>
    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
    }

    /// <summary>重发一条失败的消息</summary>
    public void Retry(ChatMessageViewModel message)
    {
        var conversation = SelectedConversation;
        if (conversation == null || message == null) return;

        // 附件消息（图片/文件）重试走 SendAttachmentAsync（自动按大小分流内联/流式）
        if (message.HasAttachment && File.Exists(message.AttachmentPath))
        {
            conversation.Messages.Remove(message);
            ChatStore.RemoveMessage(conversation.PeerId, message.MsgId);
            _ = SendAttachmentAsync(message.AttachmentPath);
            return;
        }

        var msgId = _service.SendChatAsync(conversation.PeerId, conversation.PeerIp, conversation.PeerPort, message.Text);

        conversation.Messages.Remove(message);

        // 重发会产生新的 MsgId，旧记录必须删掉，否则历史里会同时留下失败的那条
        ChatStore.RemoveMessage(conversation.PeerId, message.MsgId);

        var retried = new ChatMessageViewModel(msgId, true, message.Text, DateTime.Now, MessageState.Sending);
        conversation.AppendMessage(retried);
        retried.State = _service.GetDeliveryState(msgId);

        ChatStore.AppendMessage(conversation.PeerId, conversation.PeerIp, conversation.PeerName,
            new ChatMessageRecord
            {
                MsgId = msgId,
                IsMine = true,
                Text = message.Text,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                State = retried.State
            });

        SortConversations();
        StatusRequested?.Invoke(this, $"📤 正在重新发送到 {conversation.PeerName}...");
    }

    /// <summary>当前会话变为可见时标记已读并回执</summary>
    public void MarkRead(ConversationViewModel conversation)
    {
        if (conversation.UnreadCount > 0)
        {
            conversation.MarkAllRead();
            NotifyUnreadChanged();
        }

        // 回执必须指向"我读到的对方最后一条消息"。
        // 原实现取的是自己发出的最后一条，导致对方发来的消息永远收不到已读。
        var lastIncoming = conversation.LastIncomingMsgId;
        if (string.IsNullOrEmpty(lastIncoming)) return;

        // 同一游标不重复发回执：窗口每次激活都会走到这里
        if (string.Equals(conversation.LastReceiptSent, lastIncoming, StringComparison.Ordinal)) return;
        conversation.LastReceiptSent = lastIncoming;

        ChatStore.MarkRead(conversation.PeerId, lastIncoming, 0);
        _ = _service.SendReceiptAsync(conversation.PeerId, conversation.PeerIp, conversation.PeerPort,
            ChatMessageKind.Read, lastIncoming, lastIncoming);
    }

    /// <summary>界面判定"当前正在看这个会话"时调用：可见则标已读，否则计未读</summary>
    public void OnConversationVisible(ConversationViewModel conversation)
    {
        if (IsChatVisible && IsChatTabActive && IsActive && ReferenceEquals(SelectedConversation, conversation))
            MarkRead(conversation);
    }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// 显示名解析：本机为该 IP 保存的备注名（NicknameStore）优先，
    /// 对端自报的机器名只作回退——否则横幅/聊天头部显示的是设备名而不是备注名。
    /// </summary>
    private static string ResolveDisplayName(string peerIp, string peerName)
    {
        if (!string.IsNullOrWhiteSpace(peerIp))
        {
            var nick = NicknameStore.GetNickname(peerIp);
            if (!string.IsNullOrWhiteSpace(nick)) return nick;
        }
        return peerName;
    }

    private ConversationViewModel EnsureConversation(string peerId, string peerIp, string peerName)
    {
        peerName = ResolveDisplayName(peerIp, peerName);

        // 先按对端标识找；找不到再按 IP 找——从设备列表发起时可能还没有对方标识
        var conversation = Conversations.FirstOrDefault(c => string.Equals(c.PeerId, peerId, StringComparison.Ordinal));

        if (conversation == null && !string.IsNullOrWhiteSpace(peerIp))
        {
            var byIp = Conversations.FirstOrDefault(c =>
                string.Equals(c.PeerIp, peerIp, StringComparison.OrdinalIgnoreCase));

            if (byIp != null)
            {
                var incomingIsPlaceholder = peerId.StartsWith("ip-", StringComparison.Ordinal);

                // 占位 → 真实：直接复用真实会话。这是最常见路径——从设备列表打开时
                // 还没有对端 NodeId 而传 ip-<IP>，但历史已按真实 ID 存好。
                // 若反向改写为「新建会话」，UI 选中的是空会话，而新消息仍进真实会话，
                // 导致角标/通知正常但窗口看不到消息。
                if (incomingIsPlaceholder && !byIp.IsPlaceholderId)
                {
                    byIp.UpdatePeerInfo(peerIp, peerName, 0);
                    return byIp;
                }

                // 两端都是真实 ID 且不一致：对方真的重装或换机了，才允许另建会话。
                if (!byIp.IsPlaceholderId && !incomingIsPlaceholder
                    && !string.Equals(byIp.PeerId, peerId, StringComparison.Ordinal))
                {
                    conversation = new ConversationViewModel(peerId, peerIp, peerName);
                    Conversations.Add(conversation);
                    return conversation;
                }

                // 其他情况：占位 → 占位 / 真实 → 占位 / 同 ID，走正常 rebind 或复用
                var oldKey = byIp.RebindPeerId(peerId);
                if (oldKey != null) ChatStore.MigrateSessionKey(oldKey, peerId);
                byIp.UpdatePeerInfo(peerIp, peerName, 0);
                return byIp;
            }
        }

        if (conversation != null)
        {
            conversation.UpdatePeerInfo(peerIp, peerName, 0);
            return conversation;
        }

        conversation = new ConversationViewModel(peerId, peerIp, peerName);
        Conversations.Add(conversation);

        // 对方可能没共享任何目录，扫描结果里根本没有它，补报给主列表
        if (!string.IsNullOrWhiteSpace(peerIp))
            PeerDiscovered?.Invoke(this, (peerIp, peerName));

        return conversation;
    }

    private void LoadHistory()
    {
        foreach (var session in ChatStore.GetSessions())
        {
            var conversation = new ConversationViewModel(session.PeerId, session.PeerIp,
                ResolveDisplayName(session.PeerIp, session.PeerName))
            {
                PeerPort = ChatConstants.TcpPort,
                LastIncomingMsgId = session.LastIncomingMsgId
            };

            foreach (var record in session.Messages)
            {
                var message = new ChatMessageViewModel(record.MsgId, record.IsMine, record.Text,
                    DateTimeOffset.FromUnixTimeMilliseconds(record.Timestamp).LocalDateTime, record.State,
                    isAttachment: record.IsImage || !string.IsNullOrEmpty(record.AttachmentPath),
                    isImage: record.IsImage, attachmentPath: record.AttachmentPath);
                conversation.AppendMessage(message);
            }

            // 恢复持久化的未读数：原来的无条件 MarkAllRead 会让重启后红点全部归零
            if (session.UnreadCount > 0)
                conversation.RestoreUnread(session.UnreadCount);

            // 已经回过执的游标不要重复发送
            conversation.LastReceiptSent = session.LastReadMsgId;

            // 旧版本可能因占位 id 与真实 id 并存而存下同 IP 的重复会话，这里合并保留最新一个
            var duplicate = Conversations.FirstOrDefault(c =>
                string.Equals(c.PeerIp, session.PeerIp, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.PeerId, session.PeerId, StringComparison.Ordinal));

            if (duplicate != null)
            {
                var index = Conversations.IndexOf(duplicate);
                if (conversation.LastActivityAt >= duplicate.LastActivityAt)
                    Conversations[index] = conversation;
                continue;
            }

            Conversations.Add(conversation);
        }

        SortConversations();
        NotifyUnreadChanged();
    }

    /// <summary>按最后活动时间倒序排列，有未读的会话优先置顶</summary>
    private void SortConversations()
    {
        var sorted = Conversations
            .OrderByDescending(c => c.UnreadCount > 0)
            .ThenByDescending(c => c.LastActivityAt)
            .ToList();

        for (int i = 0; i < sorted.Count; i++)
        {
            if (!ReferenceEquals(Conversations[i], sorted[i]))
            {
                var target = sorted[i];
                var currentIndex = Conversations.IndexOf(target);
                Conversations.Move(currentIndex, i);
            }
        }
    }

    private void OnMessageReceived(object? sender, InboundChatEventArgs args)
    {
        var envelope = args.Envelope;

        PostToUi(() =>
        {
            var conversation = EnsureConversation(envelope.NodeId, args.PeerIp, envelope.Name);

            if (conversation.PeerPort <= 0) conversation.PeerPort = ChatConstants.TcpPort;
            conversation.IsOnline = true;
            if (conversation.PeerPort <= 0) conversation.PeerPort = ChatConstants.TcpPort;

            var attachmentPath = args.AttachmentPath;
            // 大文件接收中：FileOffer 到达（无落盘路径），状态保持 Sending 并显示进度条
            var isReceivingStart = envelope.Kind == ChatMessageKind.FileOffer && string.IsNullOrEmpty(attachmentPath);
            // 传输期一律按文件卡片渲染（承载进度条/取消）；大图片也在完成时由 CompleteAttachment 切回图片
            var isImage = args.IsImage && !isReceivingStart;
            var displayName = isImage ? "[图片]"
                : !string.IsNullOrEmpty(attachmentPath) ? Path.GetFileName(attachmentPath)
                : !string.IsNullOrEmpty(envelope.FileName) ? envelope.FileName
                : envelope.Text;

            var state = isReceivingStart ? MessageState.Sending : MessageState.Read;

            var message = new ChatMessageViewModel(envelope.MsgId, false, displayName,
                DateTime.Now, state, isAttachment: isImage || !string.IsNullOrEmpty(attachmentPath) || isReceivingStart,
                isImage: isImage, attachmentPath: attachmentPath);
            // 接收中：0% 进度初始化（Offer 事件先于气泡创建的时序问题，同发送方）
            if (isReceivingStart)
                message.ReportProgress(0, 1); // total 仅用于占比 0%，total=1 避免 0/0

            // 同一 MsgId 只显示一次：传输层已修正双连接问题，这里做幂等兜底；
            // 大文件接收中气泡已存在（MsgId 相同）→ 此处为完成回调：填充附件路径并转为可保存
            if (!conversation.AppendMessage(message))
            {
                // 「接收起始」回调（AttachmentPath 为空）只表示开始拉取：气泡早已由 FileOffer 建好并带进度条，
                // 若在此按"完成"处理会把卡片置为 Read → 进度条与取消按钮永久消失（已实测踩坑）
                if (isReceivingStart) return;

                var existing = conversation.Messages
                    .FirstOrDefault(m => string.Equals(m.MsgId, envelope.MsgId, StringComparison.Ordinal));
                if (existing != null)
                {
                    existing.CompleteAttachment(attachmentPath, isImage);
                    existing.State = MessageState.Read;
                }
                ChatStore.UpdateState(conversation.PeerId, envelope.MsgId, MessageState.Read);
                ChatStore.UpdateAttachmentPath(conversation.PeerId, envelope.MsgId, attachmentPath);
                return;
            }

            // 无论聊天面板是否打开都立即落盘，否则关掉应用就丢了
            ChatStore.AppendMessage(conversation.PeerId, conversation.PeerIp, conversation.PeerName,
                new ChatMessageRecord
                {
                    MsgId = envelope.MsgId,
                    IsMine = false,
                    Text = displayName,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    State = MessageState.Read,
                    PeerName = conversation.PeerName,
                    IsImage = isImage,
                    AttachmentName = Path.GetFileName(attachmentPath),
                    AttachmentPath = attachmentPath
                },
                isIncoming: true);

            SortConversations();

            bool isActive = ReferenceEquals(SelectedConversation, conversation);
            if (isActive && IsChatVisible && IsChatTabActive && IsActive)
            {
                MarkRead(conversation);
                // 正在看的会话有消息进来，状态栏也要跟着刷新
                StatusRequested?.Invoke(this, $"💬 {conversation.PeerName}: {Preview(envelope.Text)}");
                MessageReceivedWhileViewing?.Invoke(this, conversation);
                return;
            }

            conversation.UnreadCount++;
            ChatStore.MarkRead(conversation.PeerId, conversation.LastReceiptSent, conversation.UnreadCount);
            NotifyUnreadChanged();
            MessageReceivedInBackground?.Invoke(this, conversation);
        });
    }

    private void OnPeerPresenceChanged(object? sender, PeerPresence peer)
    {
        // 先原样转发给设备列表（同步对端自报昵称与在线态），再做聊天侧处理
        PeerPresenceChanged?.Invoke(this, peer);

        Logger.Info($"对端上线: {peer.PeerName}({peer.PeerId}) ip={peer.PeerIp}");

        PostToUi(() =>
        {
            var conversation = EnsureConversation(peer.PeerId, peer.PeerIp, peer.PeerName);
            // 心跳携带的是对端自报名，显式更新时也要过一遍备注名解析，否则会覆盖掉昵称
            conversation.UpdatePeerInfo(peer.PeerIp, ResolveDisplayName(peer.PeerIp, peer.PeerName), peer.TcpPort);
            conversation.IsOnline = true;
            StatusRequested?.Invoke(this, $"✅ {conversation.PeerName} 已上线");
        });
    }

    /// <summary>
    /// 拨号握手解析出对端真实 NodeId：按 IP 找到占位会话并改绑，
    /// 后续回执与状态事件都按真实键查找。UDP 广播被拦截时这是唯一的改绑途径。
    /// </summary>
    private void OnPeerResolved(object? sender, PeerPresence peer)
    {
        PeerProfileUpdated?.Invoke(this, peer); // 建卡/改名信息同步给设备列表

        PostToUi(() =>
        {
            var conversation = EnsureConversation(peer.PeerId, peer.PeerIp, peer.PeerName);
            if (conversation.PeerPort <= 0) conversation.PeerPort = peer.TcpPort;
            conversation.IsOnline = true;
        });
    }

    /// <summary>对端在线期间改了昵称：更新聊天头显示，并通知设备列表刷新</summary>
    private void OnPeerProfileUpdated(object? sender, PeerPresence peer)
    {
        PeerProfileUpdated?.Invoke(this, peer);

        PostToUi(() =>
        {
            var conversation = Conversations.FirstOrDefault(c =>
                string.Equals(c.PeerId, peer.PeerId, StringComparison.Ordinal));
            if (conversation == null) return;

            // 备注优先；无备注时更新为对端新昵称
            conversation.UpdatePeerInfo(peer.PeerIp, ResolveDisplayName(peer.PeerIp, peer.PeerName), 0);
        });
    }

    private void OnPeerDisconnected(object? sender, PeerPresence peer)
    {
        PostToUi(() =>
        {
            var conversation = Conversations.FirstOrDefault(c => string.Equals(c.PeerId, peer.PeerId, StringComparison.Ordinal));
            if (conversation == null) return;

            conversation.IsOnline = false;
            StatusRequested?.Invoke(this, $"⚠️ {conversation.PeerName} 已离线");
        });
    }

    private void OnDeliveryChanged(object? sender, DeliveryChangedEventArgs args)
    {
        PostToUi(() =>
        {
            var conversation = Conversations.FirstOrDefault(c => string.Equals(c.PeerId, args.PeerId, StringComparison.Ordinal));
            if (conversation == null) return;

            var message = conversation.Messages.FirstOrDefault(m => string.Equals(m.MsgId, args.MsgId, StringComparison.Ordinal));
            if (message == null) return;

            message.State = args.State;
            ChatStore.UpdateState(args.PeerId, args.MsgId, args.State);

            // 大文件（含图片）送达后：发送方也切回图片展示（传输期统一按文件卡片渲染）
            if (message.IsMine && message.IsLargeFileOffer && !message.IsImage
                && args.State is MessageState.Delivered or MessageState.Read
                && IsImageFile(message.AttachmentPath))
                message.CompleteAttachment(message.AttachmentPath, true);

            // 附件消息进入终态（完成/失败/取消）：通知 UI 自动下滚显示最新状态
            if (message.HasAttachment && args.State is MessageState.Failed or MessageState.Delivered or MessageState.Read)
                RaiseAttachmentStateChanged(message);

            // 回执到达时同步状态栏：失败要报错，成功则把上一次的失败提示冲掉
            switch (args.State)
            {
                case MessageState.Queued:
                    StatusRequested?.Invoke(this, $"⏳ {conversation.PeerName} 当前离线，消息待发送");
                    break;
                case MessageState.Delivered:
                    StatusRequested?.Invoke(this, $"✅ {conversation.PeerName} 已收到");
                    break;
                case MessageState.Read:
                    StatusRequested?.Invoke(this, $"👁 {conversation.PeerName} 已读");
                    break;
                case MessageState.Failed:
                    // 有明确原因（主动取消/被拒绝/被取消）时如实显示；无原因才是网络未响应
                    StatusRequested?.Invoke(this, string.IsNullOrEmpty(message.FailureReason)
                        ? $"❌ 发送失败：{conversation.PeerName} 未响应"
                        : $"🚫 {message.FailureReason}");
                    break;
            }
        });
    }

    private void NotifyUnreadChanged()
    {
        OnPropertyChanged(nameof(TotalUnread));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadBadgeText));
        OnPropertyChanged(nameof(HasConversations));
    }

    private static string Preview(string text)
    {
        var normalized = text.Replace("\n", " ").Replace("\r", " ");
        return normalized.Length <= 40 ? normalized : normalized[..40] + "…";
    }

    /// <summary>
    /// Core 事件在 socket 线程触发，操作 ObservableCollection 前必须切回 UI 线程。
    /// 集中放在这里，不散落到 code-behind。<br/>
    /// UI 处理器的异常必须就地吃掉并记日志：Dispatcher.Invoke 会把异常原样抛回调用方，
    /// 若放任其飞回事件源，socket 读循环会把它当成读错误，<b>直接杀掉 TCP 连接</b>
    /// （曾经一个窗口 XAML 解析失败导致每条消息都断一次链）。
    /// </summary>
    private void PostToUi(Action action)
    {
        var dispatcher = WpfApplication.Current?.Dispatcher;
        if (dispatcher == null)
        {
            RunGuarded(action);
            return;
        }

        if (dispatcher.CheckAccess())
            RunGuarded(action);
        else
            // 注意必须包一层 lambda：直接传 RunGuarded 方法组会绑定到废弃的
            // Invoke(Delegate, params object[]) 重载，空参数表触发
            // TargetParameterCountException 并崩掉整个进程（v0.9.3 事故）
            dispatcher.Invoke(() => RunGuarded(action));
    }

    private static void RunGuarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Logger.Error("聊天 UI 事件处理异常（已隔离，不影响连接）", ex);
        }
    }
}
