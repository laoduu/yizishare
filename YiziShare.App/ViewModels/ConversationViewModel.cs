using System.Collections.ObjectModel;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

/// <summary>会话列表项</summary>
public class ConversationViewModel : ViewModelBase
{
    private bool _isOnline;
    private int _unreadCount;
    private string _peerName;
    private string _lastPreview = "";
    private string _lastTimeText = "";
    private string _lastIncomingMsgId = "";

    public ConversationViewModel(string peerId, string peerIp, string peerName)
    {
        PeerId = peerId;
        PeerIp = peerIp;
        _peerName = peerName;
    }

    /// <summary>
    /// 会话标识。首次从设备列表发起时用 IP 占位，握手拿到对方真实 NodeId 后改写，
    /// 保证同一台机器始终只有一个会话。
    /// </summary>
    public string PeerId { get; private set; }
    public string PeerIp { get; private set; }

    public string PeerName
    {
        get => string.IsNullOrWhiteSpace(_peerName) ? PeerIp : _peerName;
        private set => SetProperty(ref _peerName, value);
    }

    /// <summary>对端当前监听的端口，用于拨号</summary>
    public int PeerPort { get; set; }

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    public bool IsOnline
    {
        get => _isOnline;
        set => SetProperty(ref _isOnline, value);
    }

    /// <summary>对方未读（已读回执由本机发出）</summary>
    public int PeerUnreadCount { get; set; }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            if (SetProperty(ref _unreadCount, value)) OnPropertyChanged(nameof(HasUnread));
        }
    }

    public bool HasUnread => UnreadCount > 0;

    public string UnreadCountText => UnreadCount > 99 ? "99+" : UnreadCount.ToString();

    /// <summary>会话列表里的最后一条消息摘要（自己发的加「我:」前缀）</summary>
    public string LastPreview
    {
        get => _lastPreview;
        private set => SetProperty(ref _lastPreview, value);
    }

    /// <summary>相对时间：今天显示 HH:mm，昨天显示「昨天」，更早显示 MM-dd</summary>
    public string LastTimeText
    {
        get => _lastTimeText;
        private set => SetProperty(ref _lastTimeText, value);
    }

    /// <summary>排序用：最后一条消息的时间，没有消息时为 DateTime.MinValue</summary>
    public DateTime LastActivityAt { get; private set; }

    /// <summary>我发出的最后一条消息 Id，用于向对方回执「已送达/已读」</summary>
    public string LastOutgoingMsgId { get; set; } = "";

    /// <summary>我读到的对方最后一条消息 Id，用于回复「我已读到你的哪一条」</summary>
    public string LastIncomingMsgId
    {
        get => _lastIncomingMsgId;
        set
        {
            if (SetProperty(ref _lastIncomingMsgId, value))
                OnPropertyChanged(nameof(HasUnreadReceiptTarget));
        }
    }

    public bool HasUnreadReceiptTarget => !string.IsNullOrEmpty(_lastIncomingMsgId);

    /// <summary>已经发出过已读回执的游标，避免窗口反复激活时重复回执</summary>
    public string LastReceiptSent { get; set; } = "";

    /// <summary>占位标识前缀：以 IP 预建的会话在握手拿到真实 NodeId 前使用</summary>
    public bool IsPlaceholderId => PeerId.StartsWith("ip-", StringComparison.Ordinal);

    /// <summary>握手拿到对方真实标识后改写会话键，返回旧标识（供调用方迁移持久化数据）</summary>
    public string? RebindPeerId(string realPeerId)
    {
        if (string.IsNullOrWhiteSpace(realPeerId) || string.Equals(PeerId, realPeerId, StringComparison.Ordinal))
            return null;

        var oldPeerId = PeerId;
        PeerId = realPeerId;
        OnPropertyChanged(nameof(PeerId));
        OnPropertyChanged(nameof(IsPlaceholderId));
        return oldPeerId;
    }

    public void UpdatePeerInfo(string peerIp, string peerName, int peerPort)
    {
        if (!string.IsNullOrWhiteSpace(peerIp) && PeerIp != peerIp)
        {
            PeerIp = peerIp;
            OnPropertyChanged(nameof(PeerIp));
            OnPropertyChanged(nameof(PeerName));
        }

        if (!string.IsNullOrWhiteSpace(peerName) && _peerName != peerName)
            PeerName = peerName;

        if (peerPort > 0 && PeerPort != peerPort)
            PeerPort = peerPort;
    }

    /// <summary>追加一条消息并刷新列表摘要</summary>
    /// <returns>false 表示该 MsgId 已存在，已忽略（同一链路重复投递时的兜底）</returns>
    public bool AppendMessage(ChatMessageViewModel message)
    {
        foreach (var existing in Messages)
        {
            if (string.Equals(existing.MsgId, message.MsgId, StringComparison.Ordinal))
                return false;
        }

        DateTime? previous = Messages.Count > 0 ? Messages[^1].Timestamp : null;
        message.ShowDivider = message.NeedsDivider(previous);
        Messages.Add(message);

        if (message.IsMine)
        {
            LastOutgoingMsgId = message.MsgId;
            PeerUnreadCount++;
        }
        else
        {
            LastIncomingMsgId = message.MsgId;
        }

        RefreshSummary(message);
        return true;
    }

    /// <summary>刷新会话列表的摘要与时间</summary>
    public void RefreshSummary()
    {
        if (Messages.Count == 0) return;
        RefreshSummary(Messages[^1]);
    }

    private void RefreshSummary(ChatMessageViewModel last)
    {
        LastPreview = BuildPreview(last);
        LastTimeText = FormatTime(last.Timestamp);
        LastActivityAt = last.Timestamp;
    }

    public void MarkAllRead()
    {
        UnreadCount = 0;
    }

    /// <summary>清空某条消息的未读状态（用于加载历史时按 LastReadMsgId 精确恢复）</summary>
    public void RestoreUnread(int count)
    {
        UnreadCount = count;
    }

    /// <summary>汇总摘要预览：我发的加前缀并标状态，对方发的直接取内容</summary>
    private static string BuildPreview(ChatMessageViewModel message)
    {
        var text = message.Text.Replace("\n", " ").Replace("\r", " ").Trim();
        if (text.Length > 30) text = text[..30] + "…";

        if (!message.IsMine) return text;

        var mark = message.State switch
        {
            MessageState.Failed => "！",
            MessageState.Read => " ",
            MessageState.Delivered or MessageState.Sent => " ",
            _ => " "
        };

        return $"我: {text}{mark}";
    }

    /// <summary>相对时间显示</summary>
    public static string FormatTime(DateTime time)
    {
        if (time == default) return "";

        var now = DateTime.Now;
        if (time.Date == now.Date) return time.ToString("HH:mm");
        if (time.Date == now.Date.AddDays(-1)) return "昨天";
        if (time.Date > now.Date.AddDays(-7)) return time.ToString("ddd");
        return time.ToString("MM-dd");
    }
}
