using System.Text;
using YiziShare.Core;

namespace YiziShare.Tests;

public class ChatProtocolTests
{
    private static ChatEnvelope NewEnvelope(ChatMessageKind kind, string text)
    {
        return new ChatEnvelope
        {
            Kind = kind,
            MsgId = Guid.NewGuid().ToString("N"),
            NodeId = "node-1234",
            Name = "测试昵称",
            Text = text,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TcpPort = ChatConstants.TcpPort
        };
    }

    [Fact]
    public void Serialize_TryDeserialize_RoundTripsChatMessage()
    {
        var original = NewEnvelope(ChatMessageKind.Chat, "你好，局域网！");
        var payload = ChatProtocol.Serialize(original);

        Assert.True(ChatProtocol.TryDeserialize(payload, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(ChatMessageKind.Chat, parsed!.Kind);
        Assert.Equal(original.MsgId, parsed.MsgId);
        Assert.Equal(original.NodeId, parsed.NodeId);
        Assert.Equal(original.Name, parsed.Name);
        Assert.Equal(original.Text, parsed.Text);
        Assert.Equal(original.TcpPort, parsed.TcpPort);
        Assert.Equal(original.Timestamp, parsed.Timestamp);
    }

    [Fact]
    public void Serialize_TryDeserialize_PreservesMultilineAndUnicode()
    {
        // 正文含换行、中文、emoji、引号与反斜杠，验证 JSON 转义与分帧不会截断
        var original = NewEnvelope(ChatMessageKind.Chat, "第一行\n第二行 😀 \"引号\" \\ 反斜杠");
        var payload = ChatProtocol.Serialize(original);

        Assert.True(ChatProtocol.TryDeserialize(payload, out var parsed));
        Assert.Equal(original.Text, parsed!.Text);
    }

    [Fact]
    public void Serialize_TryDeserialize_PreservesReceiptRefId()
    {
        var original = NewEnvelope(ChatMessageKind.Read, "abc123");
        original.RefId = "被确认消息的Id";

        var payload = ChatProtocol.Serialize(original);
        Assert.True(ChatProtocol.TryDeserialize(payload, out var parsed));
        Assert.Equal("abc123", parsed!.Text);
        Assert.Equal("被确认消息的Id", parsed.RefId);
    }

    [Fact]
    public void TryDeserialize_EmptyPayload_ReturnsFalse()
    {
        Assert.False(ChatProtocol.TryDeserialize(ReadOnlySpan<byte>.Empty, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryDeserialize_GarbagePayload_ReturnsFalseWithoutThrowing()
    {
        // 一条脏报文绝不能打死收包循环，解析必须吞掉异常
        var garbage = Encoding.UTF8.GetBytes("这不是 JSON {{{");
        Assert.False(ChatProtocol.TryDeserialize(garbage, out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void TryDeserialize_JsonNull_ReturnsFalse()
    {
        var payload = Encoding.UTF8.GetBytes("null");
        Assert.False(ChatProtocol.TryDeserialize(payload, out _));
    }

    [Fact]
    public void TryDeserialize_UnknownKind_ReturnsFalse()
    {
        var payload = Encoding.UTF8.GetBytes("""{"Version":"1","MsgId":"m1","NodeId":"n1","Kind":99,"Text":""}""");
        Assert.False(ChatProtocol.TryDeserialize(payload, out _));
    }

    [Fact]
    public void TryDeserialize_EmptyMsgId_ReturnsFalse()
    {
        var payload = Encoding.UTF8.GetBytes("""{"Version":"1","MsgId":"","NodeId":"n1","Kind":4,"Text":""}""");
        Assert.False(ChatProtocol.TryDeserialize(payload, out _));
    }

    [Fact]
    public void TryDeserialize_EmptyNodeId_ReturnsFalse()
    {
        var payload = Encoding.UTF8.GetBytes("""{"Version":"1","MsgId":"m1","NodeId":"","Kind":4,"Text":""}""");
        Assert.False(ChatProtocol.TryDeserialize(payload, out _));
    }

    [Fact]
    public void IsValid_TextOverMaxLength_ReturnsFalse()
    {
        var envelope = NewEnvelope(ChatMessageKind.Chat, new string('字', ChatEnvelope.MaxTextLength + 1));
        Assert.False(envelope.IsValid());
    }

    [Fact]
    public void IsValid_TextAtMaxLength_ReturnsTrue()
    {
        var envelope = NewEnvelope(ChatMessageKind.Chat, new string('字', ChatEnvelope.MaxTextLength));
        Assert.True(envelope.IsValid());
    }

    [Fact]
    public void Encode_WritesBigEndianLengthPrefix()
    {
        var payload = new byte[] { 0xAA, 0xBB, 0xCC };
        var frame = ChatFrame.Encode(payload);

        Assert.Equal(ChatFrame.HeaderSize + payload.Length, frame.Length);
        Assert.Equal(0, frame[0]);
        Assert.Equal(0, frame[1]);
        Assert.Equal(0, frame[2]);
        Assert.Equal(payload.Length, frame[3]);
        Assert.Equal(0xAA, frame[4]);
        Assert.Equal(0xCC, frame[6]);
    }

    [Fact]
    public void EncodeDecodeLength_RoundTrips()
    {
        var payload = new byte[1234];
        var frame = ChatFrame.Encode(payload);

        var length = ChatFrame.DecodeLength(frame.AsSpan(0, ChatFrame.HeaderSize));
        Assert.Equal(1234, length);
    }

    [Fact]
    public void DecodeLength_TooLarge_ReturnsMinus1()
    {
        // 8MB 帧上限（附件 base64 内联需要）；9MB 超限
        var header = new byte[] { 0x00, 0x90, 0x00, 0x00 }; // 9_437_184 > 8MB
        Assert.Equal(-1, ChatFrame.DecodeLength(header));
    }

    [Fact]
    public void DecodeLength_MaxPayload_ReturnsLength()
    {
        // 恰好 8MB 上限应被接受（附件内联依赖此上限）
        Assert.Equal(8 * 1024 * 1024, ChatFrame.DecodeLength(new byte[] { 0x00, 0x80, 0x00, 0x00 }));
    }

    [Fact]
    public void DecodeLength_ZeroOrNegative_ReturnsMinus1()
    {
        Assert.Equal(-1, ChatFrame.DecodeLength(new byte[] { 0x00, 0x00, 0x00, 0x00 }));
        Assert.Equal(-1, ChatFrame.DecodeLength(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }));
    }

    [Fact]
    public void DecodeLength_HeaderTooShort_ReturnsMinus1()
    {
        Assert.Equal(-1, ChatFrame.DecodeLength(new byte[] { 0x00, 0x00 }));
        Assert.Equal(-1, ChatFrame.DecodeLength(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Encode_EmptyPayload_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatFrame.Encode(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void Encode_OversizedPayload_Throws()
    {
        var payload = new byte[ChatFrame.MaxPayloadBytes + 1];
        Assert.Throws<ArgumentOutOfRangeException>(() => ChatFrame.Encode(payload));
    }

    [Fact]
    public void ShouldKeepIncoming_BothDirections_AreComplementary()
    {
        // 去重规则必须保证两端最终保留的是同一条物理连接
        Assert.True(ChatProtocol.ShouldKeepIncoming("aaa", "bbb"));
        Assert.False(ChatProtocol.ShouldKeepIncoming("bbb", "aaa"));
    }

    [Fact]
    public void ShouldKeepOutbound_ComplementsShouldKeepIncoming()
    {
        // 不变式：两端裁决必须互补，且恰好一方保留出站、另一方保留入站，
        // 双方才会收敛到同一条物理链路（否则消息被投递两次）。
        var a = "aaa";
        var b = "bbb";

        Assert.False(ChatProtocol.ShouldKeepOutbound(a, b));   // a 较小 → 保留入站
        Assert.True(ChatProtocol.ShouldKeepIncoming(a, b));

        Assert.True(ChatProtocol.ShouldKeepOutbound(b, a));    // b 较大 → 保留出站
        Assert.False(ChatProtocol.ShouldKeepIncoming(b, a));

        // 严格互补
        Assert.NotEqual(ChatProtocol.ShouldKeepOutbound(a, b), ChatProtocol.ShouldKeepOutbound(b, a));
        Assert.NotEqual(ChatProtocol.ShouldKeepIncoming(a, b), ChatProtocol.ShouldKeepIncoming(b, a));
    }

    [Fact]
    public void ConnectionArbitration_BothSidesSelectSamePhysicalLink()
    {
        // 模拟双向同时拨号：A 与 B 各拨出一条链路后，双方必须做出互补决策：
        // 一方保留自己的出站连接，另一方保留对应的入站连接——
        // 指的是同一条物理链路，因此消息只投递一次。
        var aId = "1111";
        var bId = "9999";

        bool aKeepsOutbound = ChatProtocol.ShouldKeepOutbound(aId, bId);
        bool bKeepsOutbound = ChatProtocol.ShouldKeepOutbound(bId, aId);

        // a 较小（1 < 9）→ a 保留入站，b 较大 → b 保留出站
        Assert.False(aKeepsOutbound);
        Assert.True(bKeepsOutbound);

        // 恰好一方保留出站、另一方保留入站，指的才是同一条物理链路
        Assert.NotEqual(aKeepsOutbound, bKeepsOutbound);

        // 反向调用时结论翻转：b 认为该留出站，a 认为该留入站
        Assert.True(ChatProtocol.ShouldKeepOutbound(bId, aId));
        Assert.True(ChatProtocol.ShouldKeepIncoming(aId, bId));
        Assert.False(ChatProtocol.ShouldKeepIncoming(bId, aId));
    }

    [Fact]
    public void ShouldKeepOutbound_EmptyNodeId_DefaultsToOutbound()
    {
        Assert.True(ChatProtocol.ShouldKeepOutbound("", "bbb"));
        Assert.True(ChatProtocol.ShouldKeepOutbound("aaa", ""));
    }

    [Fact]
    public void ShouldKeepIncoming_SameNodeId_IsDeterministic()
    {
        Assert.False(ChatProtocol.ShouldKeepIncoming("same", "same"));
    }

    [Fact]
    public void ShouldKeepIncoming_EmptyNodeId_ConservativelyKeeps()
    {
        Assert.True(ChatProtocol.ShouldKeepIncoming("", "bbb"));
        Assert.True(ChatProtocol.ShouldKeepIncoming("aaa", ""));
    }
}
