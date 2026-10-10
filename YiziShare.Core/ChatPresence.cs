namespace YiziShare.Core;

/// <summary>对端在线信息快照</summary>
public sealed class PeerPresence
{
    public string PeerId { get; init; } = "";
    public string PeerIp { get; init; } = "";
    public string PeerName { get; init; } = "";
    public int TcpPort { get; init; }
    public DateTimeOffset LastSeen { get; init; }
}

/// <summary>
/// 局域网在线表：记录每个对端的最后一次可见时间，超过 <see cref="ChatConstants.PresenceTimeout"/> 即判定离线。
/// 构造函数注入时钟，使超时逻辑无需 sleep 即可单测。
/// </summary>
public sealed class PresenceTable
{
    private readonly Dictionary<string, PeerPresence> _peers = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();

    public PresenceTable(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public int Count
    {
        get
        {
            lock (_sync) return _peers.Count;
        }
    }

    /// <summary>记录一次心跳；返回 true 表示该对端的在线状态发生了跃迁（仅跃迁才应通知 UI）</summary>
    public bool Upsert(string peerId, string peerIp, string peerName, int tcpPort)
    {
        if (string.IsNullOrWhiteSpace(peerId)) return false;

        lock (_sync)
        {
            var now = _clock();
            bool wasOnline = IsOnlineLocked(peerId, now);

            _peers[peerId] = new PeerPresence
            {
                PeerId = peerId,
                PeerIp = peerIp,
                PeerName = peerName,
                TcpPort = tcpPort,
                LastSeen = now
            };

            return !wasOnline;
        }
    }

    /// <summary>对端是否在线（未记录过视为离线）</summary>
    public bool IsOnline(string peerId)
    {
        lock (_sync) return IsOnlineLocked(peerId, _clock());
    }

    /// <summary>指定时刻下对端是否在线（供超时扫描与单测使用）</summary>
    public bool IsOnline(string peerId, DateTimeOffset now)
    {
        lock (_sync) return IsOnlineLocked(peerId, now);
    }

    private bool IsOnlineLocked(string peerId, DateTimeOffset now)
    {
        if (!_peers.TryGetValue(peerId, out var peer)) return false;
        return now - peer.LastSeen <= ChatConstants.PresenceTimeout;
    }

    public PeerPresence? Get(string peerId)
    {
        lock (_sync) return _peers.GetValueOrDefault(peerId);
    }

    public PeerPresence? FindByIp(string peerIp)
    {
        lock (_sync)
        {
            foreach (var peer in _peers.Values)
            {
                if (string.Equals(peer.PeerIp, peerIp, StringComparison.OrdinalIgnoreCase))
                    return peer;
            }
        }
        return null;
    }

    /// <summary>所有在线对端</summary>
    public List<PeerPresence> GetOnline()
    {
        lock (_sync)
        {
            var now = _clock();
            var result = new List<PeerPresence>();
            foreach (var peer in _peers.Values)
            {
                if (now - peer.LastSeen <= ChatConstants.PresenceTimeout)
                    result.Add(peer);
            }
            return result;
        }
    }

    /// <summary>
    /// 找出已超时未再出现过的对端（保留记录，以便其短暂掉线后恢复时仍能识别为同一设备）。
    /// </summary>
    public List<PeerPresence> SweepStale()
    {
        return SweepStale(_clock());
    }

    /// <summary>指定时刻下的过期检测（供单测注入固定时间）</summary>
    public List<PeerPresence> SweepStale(DateTimeOffset now)
    {
        lock (_sync)
        {
            var expired = new List<PeerPresence>();
            foreach (var peer in _peers.Values)
            {
                if (now - peer.LastSeen > ChatConstants.PresenceTimeout)
                    expired.Add(peer);
            }
            return expired;
        }
    }

    /// <summary>彻底移除对端（连接断开且不再可达时调用）</summary>
    public bool Remove(string peerId)
    {
        lock (_sync) return _peers.Remove(peerId);
    }

    public void Clear()
    {
        lock (_sync) _peers.Clear();
    }
}
