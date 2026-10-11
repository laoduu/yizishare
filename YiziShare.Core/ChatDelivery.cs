namespace YiziShare.Core;

/// <summary>消息投递状态</summary>
/// <remarks>
/// Queued → Sending → Sent → Delivered → Read 为正常上升链路；
/// Sent 之后 8 秒未收到 Delivered 回执降级为 Failed。
/// Sent 与 Delivered 必须分开：Sent 只表示"已写入本机 socket"，
/// 不代表对方收到，合并两者就无法回答"消息到底送到没有"。
/// </remarks>
public enum MessageState
{
    /// <summary>已入本地历史，等待进入发送队列</summary>
    Queued = 0,
    /// <summary>已入发送队列，正在写入连接</summary>
    Sending = 1,
    /// <summary>帧已写入 socket（本地投递发出去了）</summary>
    Sent = 2,
    /// <summary>对端进程已确认收到</summary>
    Delivered = 3,
    /// <summary>对端已读</summary>
    Read = 4,
    /// <summary>发送失败</summary>
    Failed = 5
}

/// <summary>一条待投递消息的跟踪记录</summary>
public sealed class PendingMessage
{
    public string MsgId { get; init; } = "";

    /// <summary>归属对端；握手拿到真实 NodeId 后可被 Rebind 改写（IP 占位 → 真实键）</summary>
    public string PeerId { get; set; } = "";
    public long Seq { get; init; }
    public MessageState State { get; set; } = MessageState.Queued;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset SentAt { get; set; }
}

/// <summary>
/// 消息投递状态机与回执关联。<br/>
/// 构造函数注入时钟，使超时降级可在单测中通过推进时间验证，无需 sleep。
/// </summary>
public sealed class DeliveryTracker
{
    private readonly Dictionary<string, PendingMessage> _pending = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();
    private long _nextSeq;

    public DeliveryTracker(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int Count
    {
        get
        {
            lock (_sync) return _pending.Count;
        }
    }

    /// <summary>登记一条待发送消息，返回其跟踪记录</summary>
    public PendingMessage Register(string msgId, string peerId)
    {
        lock (_sync)
        {
            var pending = new PendingMessage
            {
                MsgId = msgId,
                PeerId = peerId,
                Seq = _nextSeq++,
                State = MessageState.Queued,
                CreatedAt = _clock()
            };
            _pending[msgId] = pending;
            return pending;
        }
    }

    /// <summary>
    /// 握手拿到对端真实标识后改写跟踪记录的归属键（IP 占位 → 真实 NodeId）。<br/>
    /// 不改写的话 ApplyRead 会按真实键匹配不到占位期间登记的消息，已读回执永远落空。
    /// </summary>
    public void Rebind(string msgId, string realPeerId)
    {
        if (string.IsNullOrEmpty(realPeerId)) return;

        lock (_sync)
        {
            if (_pending.TryGetValue(msgId, out var pending))
                pending.PeerId = realPeerId;
        }
    }

    public MessageState GetState(string msgId)
    {
        lock (_sync)
        {
            return _pending.TryGetValue(msgId, out var pending) ? pending.State : MessageState.Failed;
        }
    }

    public PendingMessage? Get(string msgId)
    {
        lock (_sync) return _pending.GetValueOrDefault(msgId);
    }

    /// <summary>Queued → Sending</summary>
    public bool MarkSending(string msgId)
    {
        lock (_sync) return Transition(msgId, MessageState.Sending);
    }

    /// <summary>标记发送失败（Sending/Sent → Failed；大文件传输中断等场景）</summary>
    public bool MarkFailed(string msgId)
    {
        lock (_sync) return Transition(msgId, MessageState.Failed);
    }

    /// <summary>
    /// 退回排队等待（对方离线）。<br/>
    /// 允许 Sending → Queued：先尝试发送、失败后转入待发队列，
    /// 这样 UI 能把它显示为「待发送」而不是「正在发送」或「失败」。
    /// </summary>
    public bool MarkQueued(string msgId)
    {
        lock (_sync) return Transition(msgId, MessageState.Queued);
    }

    /// <summary>Sending → Sent（帧已写入 socket）</summary>
    public bool MarkSent(string msgId)
    {
        lock (_sync)
        {
            var pending = _pending.GetValueOrDefault(msgId);
            if (pending == null) return false;

            bool changed = Transition(msgId, MessageState.Sent);
            if (changed)
                pending.SentAt = _clock();
            return changed;
        }
    }

    /// <summary>
    /// 处理 Delivered 回执：Sent → Delivered。<br/>
    /// 迟到的回执允许把 Failed 升级回来（幂等）。
    /// </summary>
    public PendingMessage? ApplyDelivered(string refId)
    {
        lock (_sync)
        {
            if (!Transition(refId, MessageState.Delivered)) return null;
            return _pending.GetValueOrDefault(refId);
        }
    }

    /// <summary>
    /// 处理 Read 回执：回执的 text 携带对端"已读到的最大 MsgId"，
    /// 一次性把该消息及其之前所有发给同一对端的消息标记为已读。
    /// </summary>
    /// <returns>状态发生变化的记录</returns>
    public List<PendingMessage> ApplyRead(string peerId, string lastReadMsgId)
    {
        lock (_sync)
        {
            var changed = new List<PendingMessage>();
            if (!_pending.TryGetValue(lastReadMsgId, out var anchor)) return changed;

            long targetSeq = anchor.Seq;
            foreach (var pending in _pending.Values)
            {
                if (!string.Equals(pending.PeerId, peerId, StringComparison.Ordinal)) continue;
                if (pending.Seq > targetSeq) continue;
                // 已读批量升级不得覆盖失败/取消的终态（如已取消的大文件传输被复活成 ✓✓）
                if (pending.State is MessageState.Read or MessageState.Failed) continue;

                if (Transition(pending.MsgId, MessageState.Read))
                    changed.Add(pending);
            }
            return changed;
        }
    }

    /// <summary>
    /// 扫描投递超时：处于 Sent 状态且超过 DeliveryTimeout 未收到 Delivered 的消息降级为 Failed。
    /// 已 Delivered 但未 Read 的消息不降级——对方没读不是失败。
    /// </summary>
    public List<PendingMessage> SweepTimeouts()
    {
        return SweepTimeouts(_clock());
    }

    /// <summary>指定时刻下的超时扫描（供单测注入固定时间）</summary>
    public List<PendingMessage> SweepTimeouts(DateTimeOffset now)
    {
        lock (_sync)
        {
            var changed = new List<PendingMessage>();
            foreach (var pending in _pending.Values)
            {
                if (pending.State != MessageState.Sent) continue;
                if (now - pending.SentAt <= ChatConstants.DeliveryTimeout) continue;

                if (Transition(pending.MsgId, MessageState.Failed))
                    changed.Add(pending);
            }
            return changed;
        }
    }

    /// <summary>消息淡出界面后移除跟踪记录</summary>
    public bool Forget(string msgId)
    {
        lock (_sync) return _pending.Remove(msgId);
    }

    /// <summary>移除某个对端的全部跟踪记录（连接断开时清理）</summary>
    public void ForgetPeer(string peerId)
    {
        lock (_sync)
        {
            var keys = new List<string>();
            foreach (var pending in _pending.Values)
            {
                if (string.Equals(pending.PeerId, peerId, StringComparison.Ordinal))
                    keys.Add(pending.MsgId);
            }
            foreach (var key in keys)
                _pending.Remove(key);
        }
    }

    public void Clear()
    {
        lock (_sync) _pending.Clear();
    }

    /// <summary>合法状态转换；重复置为同一状态不算变化</summary>
    private bool Transition(string msgId, MessageState to)
    {
        if (!_pending.TryGetValue(msgId, out var pending)) return false;
        if (pending.State == to) return false;
        if (!CanTransition(pending.State, to)) return false;

        pending.State = to;
        return true;
    }

    private static bool CanTransition(MessageState from, MessageState to)
    {
        return (from, to) switch
        {
            (MessageState.Queued, MessageState.Sending) => true,
            // 尝试发送失败后退回排队，等待对端上线后补发
            (MessageState.Sending, MessageState.Queued) => true,
            (MessageState.Sending, MessageState.Sent) => true,
            (MessageState.Sending, MessageState.Failed) => true,
            (MessageState.Sent, MessageState.Delivered) => true,
            (MessageState.Sent, MessageState.Failed) => true,
            (MessageState.Delivered, MessageState.Read) => true,
            // 迟到的回执可以救回已判失败的消息
            (MessageState.Failed, MessageState.Sending) => true,
            (MessageState.Failed, MessageState.Sent) => true,
            (MessageState.Failed, MessageState.Delivered) => true,
            (MessageState.Failed, MessageState.Read) => true,
            _ => false
        };
    }
}
