using System.Text.Json;

namespace YiziShare.Core;

/// <summary>持久化的一条聊天消息</summary>
public sealed class ChatMessageRecord
{
    public string MsgId { get; set; } = "";
    public bool IsMine { get; set; }
    public string Text { get; set; } = "";
    public long Timestamp { get; set; }
    public MessageState State { get; set; } = MessageState.Sent;
    public string PeerName { get; set; } = "";

    /// <summary>是否图片消息（决定气泡渲染方式）</summary>
    public bool IsImage { get; set; }

    /// <summary>附件原始文件名（文件消息展示用）</summary>
    public string AttachmentName { get; set; } = "";

    /// <summary>附件本地路径（接收方为落盘路径；发送方为原文件路径）</summary>
    public string AttachmentPath { get; set; } = "";
}

/// <summary>持久化的一个会话（与某个对端的全部聊天记录）</summary>
public sealed class ChatSessionRecord
{
    public string PeerId { get; set; } = "";
    public string PeerIp { get; set; } = "";
    public string PeerName { get; set; } = "";

    /// <summary>已读到的最大 MsgId，用于重连后判断哪些是未读</summary>
    public string LastReadMsgId { get; set; } = "";

    /// <summary>未读条数，持久化后重启仍能显示红点</summary>
    public int UnreadCount { get; set; }

    /// <summary>我读到的对方最后一条消息 Id，用于回执</summary>
    public string LastIncomingMsgId { get; set; } = "";

    /// <summary>会话排序用：最后一条消息时间（Unix 毫秒）</summary>
    public long LastActivityAt { get; set; }

    public List<ChatMessageRecord> Messages { get; set; } = new();
}

/// <summary>chat.json 的根结构</summary>
public sealed class ChatStoreFile
{
    public List<ChatSessionRecord> Sessions { get; set; } = new();
}

/// <summary>
/// 聊天历史本地持久化（%LOCALAPPDATA%\YiziShare\chat.json）。<br/>
/// 写入采用 2 秒防抖，避免每条消息都落盘；退出时调用 <see cref="FlushAsync"/> 强制落盘。
/// </summary>
public static class ChatStore
{
    /// <summary>单个会话保留的最大消息数</summary>
    public const int MaxMessagesPerSession = 200;

    /// <summary>会话总数上限</summary>
    public const int MaxSessions = 50;

    /// <summary>会话保留天数（按最后一条消息时间计）</summary>
    public const int MaxSessionAgeDays = 90;

    private static readonly string StoragePath;
    private static readonly List<ChatSessionRecord> _sessions = new();
    private static readonly object Sync = new();
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private static bool _loaded;
    private static bool _dirty;
    private static CancellationTokenSource? _flushCts;

    static ChatStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        Directory.CreateDirectory(appFolder);
        StoragePath = Path.Combine(appFolder, "chat.json");
    }

    public static async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            if (File.Exists(StoragePath))
            {
                var json = await File.ReadAllTextAsync(StoragePath).ConfigureAwait(false);
                var file = JsonSerializer.Deserialize<ChatStoreFile>(json);
                if (file?.Sessions != null)
                {
                    lock (Sync)
                    {
                        _sessions.Clear();
                        _sessions.AddRange(file.Sessions);
                        Prune(_sessions, DateTimeOffset.UtcNow);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 文件损坏时改名备份而不是直接丢弃，用户还能手工找回历史
            Logger.Error("读取聊天历史失败", ex);
            TryBackupCorruptedFile();
            lock (Sync)
            {
                _sessions.Clear();
            }
        }

        _loaded = true;
    }

    /// <summary>把损坏的历史文件改名留存，便于事后排查</summary>
    private static void TryBackupCorruptedFile()
    {
        try
        {
            if (!File.Exists(StoragePath)) return;

            var backup = StoragePath + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
            File.Move(StoragePath, backup);
        }
        catch
        {
            // 备份失败不影响启动
        }
    }

    /// <summary>启动防抖落盘循环</summary>
    public static void StartAutoFlush()
    {
        if (_flushCts != null) return;

        _flushCts = new CancellationTokenSource();
        _ = FlushLoopAsync(_flushCts.Token);
    }

    /// <summary>停止防抖落盘循环并立即落盘</summary>
    public static async Task StopAutoFlushAsync()
    {
        if (_flushCts != null)
        {
            _flushCts.Cancel();
            _flushCts.Dispose();
            _flushCts = null;
        }
        await FlushAsync();
    }

    /// <summary>取全部会话的快照副本（按最后活动时间倒序）</summary>
    public static List<ChatSessionRecord> GetSessions()
    {
        lock (Sync)
        {
            return _sessions.OrderByDescending(NewestTimestamp).ToList();
        }
    }

    public static ChatSessionRecord? GetSession(string peerId)
    {
        lock (Sync)
        {
            return _sessions.FirstOrDefault(s => string.Equals(s.PeerId, peerId, StringComparison.Ordinal));
        }
    }

    /// <summary>追加一条消息；同一 MsgId 重复追加会被忽略（幂等）</summary>
    /// <param name="isIncoming">true 表示这条是对方发来的（计入未读）</param>
    public static void AppendMessage(string peerId, string peerIp, string peerName, ChatMessageRecord message,
        bool isIncoming = false)
    {
        lock (Sync)
        {
            var session = GetOrCreateSession(peerId, peerIp, peerName);
            if (session.Messages.Any(m => string.Equals(m.MsgId, message.MsgId, StringComparison.Ordinal)))
                return;

            session.Messages.Add(message);
            session.LastActivityAt = message.Timestamp;

            if (isIncoming)
            {
                session.UnreadCount++;
                session.LastIncomingMsgId = message.MsgId;
            }

            Prune(_sessions, DateTimeOffset.UtcNow);
            _dirty = true;
        }
    }

    /// <summary>删除一条消息（重发时移除失败的那条旧记录）</summary>
    public static void RemoveMessage(string peerId, string msgId)
    {
        lock (Sync)
        {
            var session = _sessions.FirstOrDefault(s => string.Equals(s.PeerId, peerId, StringComparison.Ordinal));
            if (session == null) return;

            int removed = session.Messages.RemoveAll(m => string.Equals(m.MsgId, msgId, StringComparison.Ordinal));
            if (removed > 0) _dirty = true;
        }
    }

    /// <summary>更新消息的投递状态</summary>
    public static void UpdateState(string peerId, string msgId, MessageState state)
    {
        lock (Sync)
        {
            var session = _sessions.FirstOrDefault(s => string.Equals(s.PeerId, peerId, StringComparison.Ordinal));
            var message = session?.Messages.FirstOrDefault(m => string.Equals(m.MsgId, msgId, StringComparison.Ordinal));
            if (message == null) return;

            message.State = state;
            _dirty = true;
        }
    }

    /// <summary>记录已读到的最大 MsgId 并清零未读数</summary>
    public static void MarkRead(string peerId, string lastReadMsgId, int unreadCount = 0)
    {
        lock (Sync)
        {
            var session = _sessions.FirstOrDefault(s => string.Equals(s.PeerId, peerId, StringComparison.Ordinal));
            if (session == null) return;

            session.LastReadMsgId = lastReadMsgId;
            session.UnreadCount = unreadCount;
            _dirty = true;
        }
    }

    /// <summary>记录会话摘要信息（用于排序与列表展示）</summary>
    public static void UpdateSessionSummary(string peerId, DateTimeOffset lastActivityAt, string lastIncomingMsgId)
    {
        lock (Sync)
        {
            var session = _sessions.FirstOrDefault(s => string.Equals(s.PeerId, peerId, StringComparison.Ordinal));
            if (session == null) return;

            session.LastActivityAt = lastActivityAt.ToUnixTimeMilliseconds();
            if (!string.IsNullOrEmpty(lastIncomingMsgId))
                session.LastIncomingMsgId = lastIncomingMsgId;

            _dirty = true;
        }
    }

    /// <summary>把某个会话的历史迁移到新的对端标识（握手拿到真实标识后调用）</summary>
    public static void MigrateSessionKey(string oldPeerId, string newPeerId)
    {
        lock (Sync)
        {
            if (string.Equals(oldPeerId, newPeerId, StringComparison.Ordinal)) return;

            var session = _sessions.FirstOrDefault(s => string.Equals(s.PeerId, oldPeerId, StringComparison.Ordinal));
            if (session == null) return;

            session.PeerId = newPeerId;
            _dirty = true;
        }
    }

    public static async Task FlushAsync()
    {
        lock (Sync)
        {
            if (!_dirty) return;
            _dirty = false;
        }

        await SaveAsync();
    }

    public static void Reset()
    {
        lock (Sync)
        {
            _sessions.Clear();
            _dirty = false;
        }
        _loaded = false;
    }

    /// <summary>
    /// 保留策略（纯函数，不依赖存储路径）：丢弃空会话与过期会话，
    /// 每个会话只保留最新 <see cref="MaxMessagesPerSession"/> 条，会话总数超限时按最后活动时间丢最旧的。
    /// </summary>
    /// <returns>本次移除的会话与消息总数</returns>
    public static int Prune(List<ChatSessionRecord> sessions, DateTimeOffset now)
    {
        int removed = 0;
        var maxAge = TimeSpan.FromDays(MaxSessionAgeDays);

        removed += sessions.RemoveAll(s =>
            s.Messages.Count == 0 || now - NewestTimestamp(s) > maxAge);

        foreach (var session in sessions)
        {
            if (session.Messages.Count <= MaxMessagesPerSession) continue;

            // 消息按时间升序存放，丢最旧的
            int excess = session.Messages.Count - MaxMessagesPerSession;
            session.Messages.RemoveRange(0, excess);
            removed += excess;
        }

        if (sessions.Count > MaxSessions)
        {
            var byOldest = sessions.OrderBy(NewestTimestamp).ToList();
            var expired = byOldest.Take(sessions.Count - MaxSessions).ToList();
            foreach (var session in expired)
            {
                removed += session.Messages.Count;
                sessions.Remove(session);
            }
        }

        return removed;
    }

    private static ChatSessionRecord GetOrCreateSession(string peerId, string peerIp, string peerName)
    {
        var session = _sessions.FirstOrDefault(s => string.Equals(s.PeerId, peerId, StringComparison.Ordinal));
        if (session != null)
        {
            session.PeerIp = peerIp;
            if (!string.IsNullOrWhiteSpace(peerName)) session.PeerName = peerName;
            return session;
        }

        session = new ChatSessionRecord
        {
            PeerId = peerId,
            PeerIp = peerIp,
            PeerName = peerName
        };
        _sessions.Add(session);
        return session;
    }

    /// <summary>会话中最新一条消息的时间；无消息时返回 Unix 纪元</summary>
    private static DateTimeOffset NewestTimestamp(ChatSessionRecord session)
    {
        long newest = 0;
        foreach (var message in session.Messages)
        {
            if (message.Timestamp > newest) newest = message.Timestamp;
        }
        return DateTimeOffset.FromUnixTimeMilliseconds(newest);
    }

    private static async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, ct);
                await FlushAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 落盘失败不影响使用，等待下一个防抖周期重试
            }
        }
    }

    private static async Task SaveAsync()
    {
        try
        {
            ChatStoreFile snapshot;
            lock (Sync)
            {
                snapshot = new ChatStoreFile
                {
                    Sessions = _sessions.OrderByDescending(NewestTimestamp).ToList()
                };
            }

            var json = JsonSerializer.Serialize(snapshot);

            // 先写临时文件再原子替换：直接写目标文件时中途崩溃会留下半截 JSON，
            // 而 LoadAsync 的容错会把整份历史清空。
            var tempPath = StoragePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

            if (File.Exists(StoragePath))
                File.Replace(tempPath, StoragePath, null);
            else
                File.Move(tempPath, StoragePath);
        }
        catch
        {
            // 与 HistoryStore 一致：持久化失败静默吞掉，内存态仍可用
        }
    }
}
