using YiziShare.Core;

namespace YiziShare.Tests;

public class ChatDeliveryTests
{
    private static DateTimeOffset _now = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static DeliveryTracker NewTracker()
    {
        return new DeliveryTracker(() => _now);
    }

    [Fact]
    public void Register_StartsQueued()
    {
        var tracker = NewTracker();
        var pending = tracker.Register("m1", "peer1");

        Assert.Equal(MessageState.Queued, pending.State);
        Assert.Equal(MessageState.Queued, tracker.GetState("m1"));
        Assert.Equal(1, tracker.Count);
    }

    [Fact]
    public void NormalFlow_ReachesDeliveredThenRead()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");

        Assert.True(tracker.MarkSending("m1"));
        Assert.True(tracker.MarkSent("m1"));
        Assert.Equal(MessageState.Sent, tracker.GetState("m1"));

        var delivered = tracker.ApplyDelivered("m1");
        Assert.NotNull(delivered);
        Assert.Equal(MessageState.Delivered, tracker.GetState("m1"));

        tracker.ApplyRead("peer1", "m1");
        Assert.Equal(MessageState.Read, tracker.GetState("m1"));
    }

    [Fact]
    public void MarkSending_SameStateTwice_ReportsNoChange()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");

        Assert.True(tracker.MarkSending("m1"));
        Assert.False(tracker.MarkSending("m1"));
    }

    [Fact]
    public void ApplyDelivered_ForUnknownMsgId_IsIgnored()
    {
        var tracker = NewTracker();

        Assert.Null(tracker.ApplyDelivered("不存在的消息"));
    }

    [Fact]
    public void ApplyDelivered_IsIdempotent()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");

        Assert.NotNull(tracker.ApplyDelivered("m1"));
        Assert.Null(tracker.ApplyDelivered("m1"));
    }

    [Fact]
    public void ApplyDelivered_QueuedMessage_DoesNotSkipState()
    {
        // 还没写出去就不可能有 Delivered 回执，非法转换必须被拒绝
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");

        Assert.Null(tracker.ApplyDelivered("m1"));
        Assert.Equal(MessageState.Queued, tracker.GetState("m1"));
    }

    [Fact]
    public void SweepTimeouts_MarksFailedAfterDeliveryTimeout()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");

        _now = _now.AddSeconds(7);
        Assert.Empty(tracker.SweepTimeouts());

        _now = _now.AddSeconds(2);
        var failed = tracker.SweepTimeouts();
        Assert.Single(failed);
        Assert.Equal(MessageState.Failed, tracker.GetState("m1"));
    }

    [Fact]
    public void SweepTimeouts_IgnoresMessagesStillQueuedOrSending()
    {
        var tracker = NewTracker();
        tracker.Register("queued", "peer1");
        tracker.Register("sending", "peer1");
        tracker.MarkSending("sending");

        _now = _now.AddSeconds(60);

        Assert.Empty(tracker.SweepTimeouts());
    }

    [Fact]
    public void SweepTimeouts_DoesNotDowngradeDeliveredMessage()
    {
        // 对方没读不是失败——Delivered 超时不应降级
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");
        tracker.ApplyDelivered("m1");

        _now = _now.AddSeconds(300);
        Assert.Empty(tracker.SweepTimeouts());
        Assert.Equal(MessageState.Delivered, tracker.GetState("m1"));
    }

    [Fact]
    public void LateReceipt_UpgradesFailedMessage()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");

        _now = _now.AddSeconds(9);
        tracker.SweepTimeouts();
        Assert.Equal(MessageState.Failed, tracker.GetState("m1"));

        // 回执迟到，仍应把状态救回来
        var delivered = tracker.ApplyDelivered("m1");
        Assert.NotNull(delivered);
        Assert.Equal(MessageState.Delivered, tracker.GetState("m1"));
    }

    [Fact]
    public void Failed_CanBeRetriedBackToSending()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");
        _now = _now.AddSeconds(9);
        tracker.SweepTimeouts();

        Assert.True(tracker.MarkSending("m1"));
        Assert.Equal(MessageState.Sending, tracker.GetState("m1"));
    }

    [Fact]
    public void ApplyRead_MarksAllEarlierMessagesToSamePeer()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.Register("m2", "peer1");
        tracker.Register("m3", "peer1");
        foreach (var id in new[] { "m1", "m2", "m3" })
        {
            tracker.MarkSending(id);
            tracker.MarkSent(id);
            tracker.ApplyDelivered(id);
        }

        // 对端已读到 m2，m1/m2 应变 Read，m3 不受影响
        var changed = tracker.ApplyRead("peer1", "m2");

        Assert.Equal(2, changed.Count);
        Assert.Equal(MessageState.Read, tracker.GetState("m1"));
        Assert.Equal(MessageState.Read, tracker.GetState("m2"));
        Assert.Equal(MessageState.Delivered, tracker.GetState("m3"));
    }

    [Fact]
    public void ApplyRead_DoesNotTouchOtherPeers()
    {
        var tracker = NewTracker();
        tracker.Register("a1", "peer1");
        tracker.Register("a2", "peer1");
        tracker.Register("b1", "peer2");
        foreach (var id in new[] { "a1", "a2", "b1" })
        {
            tracker.MarkSending(id);
            tracker.MarkSent(id);
            tracker.ApplyDelivered(id);
        }

        tracker.ApplyRead("peer1", "a2");

        Assert.Equal(MessageState.Read, tracker.GetState("a1"));
        Assert.Equal(MessageState.Delivered, tracker.GetState("b1"));
    }

    [Fact]
    public void ApplyRead_ForUnknownAnchorMsgId_IsIgnored()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");
        tracker.ApplyDelivered("m1");

        Assert.Empty(tracker.ApplyRead("peer1", "从未收到过的消息"));
        Assert.Equal(MessageState.Delivered, tracker.GetState("m1"));
    }

    [Fact]
    public void ApplyRead_AlreadyReadMessages_NotReportedTwice()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");
        tracker.MarkSending("m1");
        tracker.MarkSent("m1");
        tracker.ApplyDelivered("m1");

        Assert.Single(tracker.ApplyRead("peer1", "m1"));
        Assert.Empty(tracker.ApplyRead("peer1", "m1"));
    }

    [Fact]
    public void Forget_RemovesTrackedMessage()
    {
        var tracker = NewTracker();
        tracker.Register("m1", "peer1");

        Assert.True(tracker.Forget("m1"));
        Assert.Equal(0, tracker.Count);
    }

    [Fact]
    public void ForgetPeer_RemovesOnlyThatPeer()
    {
        var tracker = NewTracker();
        tracker.Register("a1", "peer1");
        tracker.Register("a2", "peer1");
        tracker.Register("b1", "peer2");

        tracker.ForgetPeer("peer1");

        Assert.Equal(1, tracker.Count);
        Assert.Equal(MessageState.Queued, tracker.GetState("b1"));
    }

    [Fact]
    public void MarkQueued_SendingFallsBackToQueued()
    {
        // 对方离线时先尝试发送、失败后退回排队，UI 才能显示为「待发送」而非「正在发送」
        var tracker = NewTracker();
        tracker.Register("q1", "peer1");
        tracker.MarkSending("q1");
        Assert.Equal(MessageState.Sending, tracker.GetState("q1"));

        Assert.True(tracker.MarkQueued("q1"));
        Assert.Equal(MessageState.Queued, tracker.GetState("q1"));
    }

    [Fact]
    public void Queued_ResumesSendingWhenPeerComesBackOnline()
    {
        var tracker = NewTracker();
        tracker.Register("q2", "peer1");
        tracker.MarkSending("q2");
        tracker.MarkQueued("q2");

        // 补发时重新进入发送
        Assert.True(tracker.MarkSending("q2"));
        tracker.MarkSent("q2");
        tracker.ApplyDelivered("q2");

        Assert.Equal(MessageState.Delivered, tracker.GetState("q2"));
    }

    [Fact]
    public void Queued_IsNotSweptToFailed()
    {
        // 排队中的消息不应因超时被判失败——它会一直等到对方上线
        var tracker = NewTracker();
        tracker.Register("q3", "peer1");
        tracker.MarkSending("q3");
        tracker.MarkQueued("q3");

        _now = _now.AddSeconds(120);
        Assert.Empty(tracker.SweepTimeouts());
        Assert.Equal(MessageState.Queued, tracker.GetState("q3"));
    }

    [Fact]
    public void MarkSending_ForUnknownMessage_IsIgnored()
    {
        var tracker = NewTracker();
        Assert.False(tracker.MarkSending("从未登记"));
        Assert.False(tracker.MarkQueued("从未登记"));
    }

    [Fact]
    public void MarkQueued_SameStateTwice_ReportsNoChange()
    {
        var tracker = NewTracker();
        tracker.Register("q4", "peer1");
        tracker.MarkSending("q4");
        Assert.True(tracker.MarkQueued("q4"));
        Assert.False(tracker.MarkQueued("q4"));
    }

    [Fact]
    public void GetState_UnknownMessage_ReportsFailed()
    {
        var tracker = NewTracker();
        Assert.Equal(MessageState.Failed, tracker.GetState("从未存在"));
    }
}
