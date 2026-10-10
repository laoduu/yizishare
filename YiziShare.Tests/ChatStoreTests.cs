using YiziShare.Core;

namespace YiziShare.Tests;

public class ChatStoreTests
{
    private static ChatSessionRecord MakeSession(string peerId, int messageCount, DateTimeOffset newest)
    {
        var session = new ChatSessionRecord { PeerId = peerId, PeerName = peerId };

        for (int i = 0; i < messageCount; i++)
        {
            session.Messages.Add(new ChatMessageRecord
            {
                MsgId = $"{peerId}-{i}",
                IsMine = i % 2 == 0,
                Text = $"消息 {i}",
                Timestamp = newest.AddMinutes(-messageCount + i).ToUnixTimeMilliseconds(),
                State = MessageState.Sent
            });
        }
        return session;
    }

    [Fact]
    public void Prune_KeepsSessionWithinLimits()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var sessions = new List<ChatSessionRecord> { MakeSession("a", 10, now) };

        ChatStore.Prune(sessions, now);

        Assert.Single(sessions);
        Assert.Equal(10, sessions[0].Messages.Count);
    }

    [Fact]
    public void Prune_DropsEmptySession()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var sessions = new List<ChatSessionRecord> { new ChatSessionRecord { PeerId = "空会话" } };

        ChatStore.Prune(sessions, now);

        Assert.Empty(sessions);
    }

    [Fact]
    public void Prune_KeepsNewestMaxMessagesPerSession()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var sessions = new List<ChatSessionRecord>
        {
            MakeSession("a", ChatStore.MaxMessagesPerSession + 50, now)
        };

        ChatStore.Prune(sessions, now);

        Assert.Equal(ChatStore.MaxMessagesPerSession, sessions[0].Messages.Count);
        // 丢的是最旧的，保留最新的那条
        Assert.Equal("a-249", sessions[0].Messages[^1].MsgId);
    }

    [Fact]
    public void Prune_DropsSessionOlderThanRetention()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var old = now.AddDays(-(ChatStore.MaxSessionAgeDays + 1));
        var sessions = new List<ChatSessionRecord> { MakeSession("过期", 5, old) };

        ChatStore.Prune(sessions, now);

        Assert.Empty(sessions);
    }

    [Fact]
    public void Prune_KeepsSessionJustInsideRetention()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var recent = now.AddDays(-(ChatStore.MaxSessionAgeDays - 1));
        var sessions = new List<ChatSessionRecord> { MakeSession("刚好", 5, recent) };

        ChatStore.Prune(sessions, now);

        Assert.Single(sessions);
    }

    [Fact]
    public void Prune_CapsSessionCount()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var sessions = new List<ChatSessionRecord>();
        for (int i = 0; i < ChatStore.MaxSessions + 20; i++)
        {
            sessions.Add(MakeSession($"peer{i}", 3, now.AddMinutes(-i)));
        }

        ChatStore.Prune(sessions, now);

        Assert.Equal(ChatStore.MaxSessions, sessions.Count);
        // 按最后活动时间丢最旧的，最新的 peer0 必须还在
        Assert.Contains(sessions, s => s.PeerId == "peer0");
        Assert.DoesNotContain(sessions, s => s.PeerId == $"peer{ChatStore.MaxSessions + 19}");
    }

    [Fact]
    public void Prune_ReturnsRemovedCount()
    {
        var now = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var sessions = new List<ChatSessionRecord>
        {
            MakeSession("a", 10, now),
            new ChatSessionRecord { PeerId = "空" }
        };

        int removed = ChatStore.Prune(sessions, now);

        Assert.Equal(1, removed);
    }

    [Fact]
    public void AppendMessage_CreatesSessionAndDeduplicates()
    {
        ChatStore.Reset();

        var message = new ChatMessageRecord
        {
            MsgId = "m1",
            IsMine = true,
            Text = "你好",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        ChatStore.AppendMessage("peer1", "192.168.1.10", "书房电脑", message);
        ChatStore.AppendMessage("peer1", "192.168.1.10", "书房电脑", message);

        var session = ChatStore.GetSession("peer1");
        Assert.NotNull(session);
        Assert.Single(session!.Messages);
        Assert.Equal("书房电脑", session.PeerName);
        Assert.Equal("192.168.1.10", session.PeerIp);

        ChatStore.Reset();
    }

    [Fact]
    public void UpdateState_ChangesPersistedState()
    {
        ChatStore.Reset();

        var message = new ChatMessageRecord
        {
            MsgId = "m2",
            Text = "在吗",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            State = MessageState.Sent
        };
        ChatStore.AppendMessage("peer2", "192.168.1.11", "客厅电视", message);

        ChatStore.UpdateState("peer2", "m2", MessageState.Read);

        Assert.Equal(MessageState.Read, ChatStore.GetSession("peer2")!.Messages[0].State);

        ChatStore.Reset();
    }

    [Fact]
    public void UpdateState_UnknownMessage_IsIgnored()
    {
        ChatStore.Reset();
        ChatStore.UpdateState("不存在的会话", "m", MessageState.Read);
        Assert.Null(ChatStore.GetSession("不存在的会话"));
        ChatStore.Reset();
    }

    [Fact]
    public void MarkRead_StoresLastReadMsgId()
    {
        ChatStore.Reset();

        ChatStore.AppendMessage("peer3", "192.168.1.12", "书房电脑", new ChatMessageRecord
        {
            MsgId = "m3",
            Text = "收到",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        ChatStore.MarkRead("peer3", "m3");

        Assert.Equal("m3", ChatStore.GetSession("peer3")!.LastReadMsgId);

        ChatStore.Reset();
    }

    [Fact]
    public void MarkRead_UnknownSession_IsIgnored()
    {
        ChatStore.Reset();
        ChatStore.MarkRead("从未存在", "m1");
        Assert.Null(ChatStore.GetSession("从未存在"));
    }

    [Fact]
    public void GetSessions_ReturnsNewestActivityFirst()
    {
        ChatStore.Reset();
        var baseTime = DateTimeOffset.UtcNow;

        ChatStore.AppendMessage("旧", "192.168.1.20", "旧设备", new ChatMessageRecord
        {
            MsgId = "old",
            Text = "很久以前",
            Timestamp = baseTime.AddHours(-5).ToUnixTimeMilliseconds()
        });
        ChatStore.AppendMessage("新", "192.168.1.21", "新设备", new ChatMessageRecord
        {
            MsgId = "new",
            Text = "刚刚",
            Timestamp = baseTime.ToUnixTimeMilliseconds()
        });

        var sessions = ChatStore.GetSessions();
        Assert.Equal(2, sessions.Count);
        Assert.Equal("新", sessions[0].PeerId);

        ChatStore.Reset();
    }

    [Fact]
    public void Reset_ClearsState()
    {
        ChatStore.Reset();
        ChatStore.AppendMessage("peer4", "192.168.1.13", "设备", new ChatMessageRecord
        {
            MsgId = "m4",
            Text = "内容",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        Assert.Single(ChatStore.GetSessions());

        ChatStore.Reset();
        Assert.Empty(ChatStore.GetSessions());
    }

    [Fact]
    public void AppendMessage_Incoming_AccumulatesUnreadCount()
    {
        ChatStore.Reset();

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ChatStore.AppendMessage("peerU", "192.168.1.20", "设备", new ChatMessageRecord
        {
            MsgId = "u1",
            Text = "在吗",
            Timestamp = now
        }, isIncoming: true);
        ChatStore.AppendMessage("peerU", "192.168.1.20", "设备", new ChatMessageRecord
        {
            MsgId = "u2",
            Text = "在不在",
            Timestamp = now + 1
        }, isIncoming: true);

        var session = ChatStore.GetSession("peerU")!;
        Assert.Equal(2, session.UnreadCount);
        Assert.Equal("u2", session.LastIncomingMsgId);

        ChatStore.Reset();
    }

    [Fact]
    public void AppendMessage_Outgoing_DoesNotCountAsUnread()
    {
        ChatStore.Reset();

        ChatStore.AppendMessage("peerO", "192.168.1.21", "设备", new ChatMessageRecord
        {
            MsgId = "o1",
            IsMine = true,
            Text = "我发的",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        Assert.Equal(0, ChatStore.GetSession("peerO")!.UnreadCount);
        ChatStore.Reset();
    }

    [Fact]
    public void MarkRead_ClearsUnreadCount()
    {
        ChatStore.Reset();

        ChatStore.AppendMessage("peerR", "192.168.1.22", "设备", new ChatMessageRecord
        {
            MsgId = "r1",
            Text = "消息",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        }, isIncoming: true);
        Assert.Equal(1, ChatStore.GetSession("peerR")!.UnreadCount);

        ChatStore.MarkRead("peerR", "r1");

        var session = ChatStore.GetSession("peerR")!;
        Assert.Equal(0, session.UnreadCount);
        Assert.Equal("r1", session.LastReadMsgId);

        ChatStore.Reset();
    }

    [Fact]
    public void AppendMessage_UpdatesLastActivityAt()
    {
        ChatStore.Reset();

        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ChatStore.AppendMessage("peerA", "192.168.1.23", "设备", new ChatMessageRecord
        {
            MsgId = "a1",
            Text = "消息",
            Timestamp = ts
        });

        Assert.Equal(ts, ChatStore.GetSession("peerA")!.LastActivityAt);
        ChatStore.Reset();
    }

    [Fact]
    public void MigrateSessionKey_RewritesPrimaryKey()
    {
        ChatStore.Reset();

        ChatStore.AppendMessage("ip-192.168.1.30", "192.168.1.30", "设备", new ChatMessageRecord
        {
            MsgId = "m1",
            Text = "占位时期的消息",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        ChatStore.MigrateSessionKey("ip-192.168.1.30", "real-node-id");

        Assert.Null(ChatStore.GetSession("ip-192.168.1.30"));
        Assert.NotNull(ChatStore.GetSession("real-node-id"));

        ChatStore.Reset();
    }

    [Fact]
    public async Task FlushAsync_WhenClean_DoesNotThrow()
    {
        ChatStore.Reset();
        ChatStore.AppendMessage("peer5", "192.168.1.14", "设备", new ChatMessageRecord
        {
            MsgId = "m5",
            Text = "落盘",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        await ChatStore.FlushAsync();

        // 真实落盘到 %LOCALAPPDATA%，随后清理
        ChatStore.Reset();
    }
}
