using YiziShare.Core;

namespace YiziShare.Tests;

public class ChatPresenceTests
{
    private static DateTimeOffset _now = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static PresenceTable NewTable()
    {
        return new PresenceTable(() => _now);
    }

    [Fact]
    public void Upsert_NewPeer_CreatesEntryAndReportsTransition()
    {
        var table = NewTable();

        bool changed = table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        Assert.True(changed);
        Assert.Equal(1, table.Count);
        Assert.Equal("书房电脑", table.Get("peer1")!.PeerName);
        Assert.Equal("192.168.1.10", table.Get("peer1")!.PeerIp);
        Assert.Equal(45402, table.Get("peer1")!.TcpPort);
    }

    [Fact]
    public void Upsert_AlreadyOnlinePeer_DoesNotReportTransition()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        _now = _now.AddSeconds(3);

        bool changed = table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);
        Assert.False(changed);
        Assert.Equal(1, table.Count);
    }

    [Fact]
    public void Upsert_RefreshesLastSeenAndUpdatedName()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "旧名字", 45402);
        _now = _now.AddSeconds(5);

        table.Upsert("peer1", "192.168.1.10", "新名字", 45403);

        var peer = table.Get("peer1")!;
        Assert.Equal("新名字", peer.PeerName);
        Assert.Equal(45403, peer.TcpPort);
        Assert.Equal(_now, peer.LastSeen);
    }

    [Fact]
    public void Upsert_EmptyPeerId_IsIgnored()
    {
        var table = NewTable();

        Assert.False(table.Upsert("", "192.168.1.10", "无名", 45402));
        Assert.Equal(0, table.Count);
    }

    [Fact]
    public void IsOnline_WithinTimeout_IsTrue()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        _now = _now.AddSeconds(3);
        Assert.True(table.IsOnline("peer1"));

        // 恰好在阈值上仍算在线
        _now = _now.AddSeconds(12);
        Assert.True(table.IsOnline("peer1"));
    }

    [Fact]
    public void IsOnline_AfterTimeout_IsFalse()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        _now = _now.AddSeconds(16);
        Assert.False(table.IsOnline("peer1"));
    }

    [Fact]
    public void IsOnline_UnknownPeer_IsFalse()
    {
        var table = NewTable();
        Assert.False(table.IsOnline("从未见过"));
    }

    [Fact]
    public void IsOnline_ToleratesFourMissedHeartbeats()
    {
        // 心跳 3 秒、阈值 15 秒 → 应能容忍连续 4 次丢失不误判离线
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        for (int i = 0; i < 4; i++)
        {
            _now = _now.AddSeconds(ChatConstants.AnnounceInterval.TotalSeconds);
        }

        Assert.True(table.IsOnline("peer1"));
    }

    [Fact]
    public void SweepStale_ReturnsPeersPastTimeout()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);
        table.Upsert("peer2", "192.168.1.11", "客厅电视", 45402);

        _now = _now.AddSeconds(10);
        table.Upsert("peer2", "192.168.1.11", "客厅电视", 45402);

        _now = _now.AddSeconds(10);

        var stale = table.SweepStale();
        Assert.Single(stale);
        Assert.Equal("peer1", stale[0].PeerId);
    }

    [Fact]
    public void Upsert_AfterGoingOffline_ReportsTransitionAgain()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        _now = _now.AddSeconds(30);
        Assert.False(table.IsOnline("peer1"));

        bool changed = table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);
        Assert.True(changed);
        Assert.True(table.IsOnline("peer1"));
    }

    [Fact]
    public void GetOnline_ExcludesStalePeers()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);
        table.Upsert("peer2", "192.168.1.11", "客厅电视", 45402);

        _now = _now.AddSeconds(20);
        table.Upsert("peer2", "192.168.1.11", "客厅电视", 45402);

        var online = table.GetOnline();
        Assert.Single(online);
        Assert.Equal("peer2", online[0].PeerId);
    }

    [Fact]
    public void FindByIp_LocatesPeerCaseInsensitively()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        Assert.NotNull(table.FindByIp("192.168.1.10"));
        Assert.NotNull(table.FindByIp("192.168.1.10"));
        Assert.Null(table.FindByIp("192.168.1.99"));
    }

    [Fact]
    public void RemoveAndClear_DropEntries()
    {
        var table = NewTable();
        table.Upsert("peer1", "192.168.1.10", "书房电脑", 45402);

        Assert.True(table.Remove("peer1"));
        Assert.Equal(0, table.Count);

        table.Upsert("peer2", "192.168.1.11", "客厅电视", 45402);
        table.Clear();
        Assert.Equal(0, table.Count);
    }
}
