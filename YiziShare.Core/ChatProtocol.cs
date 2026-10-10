using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YiziShare.Core;

/// <summary>局域网聊天消息种类</summary>
public enum ChatMessageKind : byte
{
    /// <summary>TCP 连接首帧：握手，声明协议版本与实际监听端口</summary>
    Hello = 0,
    /// <summary>握手成功应答</summary>
    HelloAck = 1,
    /// <summary>握手被拒（版本不符 / 连接数已满）</summary>
    HelloReject = 2,
    /// <summary>UDP 心跳宣告：我在这个端口上</summary>
    Announce = 3,
    /// <summary>聊天正文</summary>
    Chat = 4,
    /// <summary>已送达回执：我的进程收到了这条消息</summary>
    Delivered = 5,
    /// <summary>已读回执：RefId 指向已读到的最大 MsgId</summary>
    Read = 6,
    /// <summary>应用层保活探测</summary>
    Ping = 7,
    /// <summary>保活应答</summary>
    Pong = 8,
    /// <summary>图片消息：Data 为 base64 编码的图片字节，FileName 为原始文件名</summary>
    Image = 9,
    /// <summary>文件消息：Data 为 base64 编码的文件字节，FileName 为原始文件名</summary>
    File = 10
}

/// <summary>局域网聊天协议常量（端口与各类超时）</summary>
public static class ChatConstants
{
    /// <summary>UDP 心跳广播端口（与 WakeOnLan 的 9 端口隔离）</summary>
    public const int UdpPort = 45455;

    /// <summary>TCP 监听首选端口；被占用时向上顺延至 TcpPortScanEnd</summary>
    public const int TcpPort = 45402;

    /// <summary>TCP 监听端口顺延上限</summary>
    public const int TcpPortScanEnd = 45412;

    /// <summary>协议版本；不匹配时握手被拒，避免新旧版本互发看不懂的包</summary>
    public const string ProtocolVersion = "1";

    /// <summary>心跳广播间隔</summary>
    public static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(3);

    /// <summary>超过此时长未收到对端心跳即判定离线（容忍连续 4 次丢失）</summary>
    public static readonly TimeSpan PresenceTimeout = TimeSpan.FromSeconds(15);

    /// <summary>发出后未收到 Delivered 回执的超时</summary>
    public static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(8);

    /// <summary>收到 Delivered 后等待 Read 回执的时长（超时不降级状态）</summary>
    public static readonly TimeSpan ReadReceiptTimeout = TimeSpan.FromSeconds(20);

    /// <summary>链路空闲超过此时长即判定已死</summary>
    public static readonly TimeSpan LinkIdleTimeout = TimeSpan.FromSeconds(20);

    /// <summary>应用层保活探测间隔</summary>
    public static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

    /// <summary>TCP 连接建立后必须在此时间内完成握手</summary>
    public static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>主动 TCP 拨号探测的超时（绕开 UDP 的降级路径）</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>聊天附件（图片/文件）单文件大小上限（原始字节）</summary>
    public const long MaxAttachmentBytes = 5 * 1024 * 1024;
}

/// <summary>聊天消息信封（JSON 序列化后按长度前缀分帧传输）</summary>
public sealed class ChatEnvelope
{
    /// <summary>协议版本</summary>
    public string Version { get; set; } = ChatConstants.ProtocolVersion;

    /// <summary>本条消息唯一 ID（Guid），接收方据此幂等去重，回执据此关联</summary>
    public string MsgId { get; set; } = "";

    /// <summary>回执指向的原消息 MsgId（非回执消息为空）</summary>
    public string RefId { get; set; } = "";

    public ChatMessageKind Kind { get; set; }

    /// <summary>发送方的稳定设备标识（跨 DHCP 换 IP 仍可识别）</summary>
    public string NodeId { get; set; } = "";

    /// <summary>发送方昵称</summary>
    public string Name { get; set; } = "";

    /// <summary>Chat 的正文；Read 时携带"已读到的最大 MsgId"</summary>
    public string Text { get; set; } = "";

    /// <summary>图片/文件消息的 base64 载荷（Image/File 专用，其余为空）</summary>
    public string Data { get; set; } = "";

    /// <summary>图片/文件消息的原始文件名（含扩展名，用于落盘与展示）</summary>
    public string FileName { get; set; } = "";

    /// <summary>Unix 毫秒时间戳</summary>
    public long Timestamp { get; set; }

    /// <summary>发送方实际监听的 TCP 端口，供对端拨号</summary>
    public int TcpPort { get; set; }

    /// <summary>正文最大字符数，超出视为非法报文</summary>
    public const int MaxTextLength = 8192;

    /// <summary>附件（Data 字段 base64）最大字符数；对应原始字节约 5MB（base64 膨胀 4/3）</summary>
    public const int MaxDataLength = 7 * 1024 * 1024;

    /// <summary>校验报文合法性：类型在枚举范围内、MsgId 与 NodeId 非空、正文未超长</summary>
    public bool IsValid()
    {
        if (!Enum.IsDefined(Kind)) return false;
        if (string.IsNullOrWhiteSpace(MsgId)) return false;
        if (string.IsNullOrWhiteSpace(NodeId)) return false;
        if (Text.Length > MaxTextLength) return false;
        if (Data.Length > MaxDataLength) return false;
        return true;
    }
}

/// <summary>TCP 分帧编解码：4 字节大端长度前缀 + UTF-8 JSON 载荷</summary>
/// <remarks>不用分隔符——聊天正文必然含换行符，分隔符会被正文击穿</remarks>
public static class ChatFrame
{
    /// <summary>长度前缀字节数</summary>
    public const int HeaderSize = 4;

    /// <summary>载荷上限。LAN 可信环境 + 握手版本校验，允许内联传输图片/文件（base64 后 ≤7MB）</summary>
    public const int MaxPayloadBytes = 8 * 1024 * 1024;

    /// <summary>将载荷封装为带长度前缀的一帧</summary>
    public static byte[] Encode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length <= 0 || payload.Length > MaxPayloadBytes)
            throw new ArgumentOutOfRangeException(nameof(payload), payload.Length,
                $"载荷长度必须在 1..{MaxPayloadBytes} 字节之间");

        var frame = new byte[HeaderSize + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(0, HeaderSize), payload.Length);
        payload.CopyTo(frame.AsSpan(HeaderSize));
        return frame;
    }

    /// <summary>从长度前缀解析载荷长度；非法（≤0 或超限）返回 -1</summary>
    public static int DecodeLength(ReadOnlySpan<byte> header)
    {
        if (header.Length < HeaderSize) return -1;

        int length = BinaryPrimitives.ReadInt32BigEndian(header[..HeaderSize]);
        if (length <= 0 || length > MaxPayloadBytes) return -1;
        return length;
    }
}

/// <summary>信封序列化、反序列化与连接去重规则（纯函数，便于单测）</summary>
public static class ChatProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>序列化为 UTF-8 JSON 字节</summary>
    public static byte[] Serialize(ChatEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    /// <summary>反序列化并校验；任何非法输入都返回 false，绝不向调用方抛出</summary>
    public static bool TryDeserialize(ReadOnlySpan<byte> payload, out ChatEnvelope? envelope)
    {
        envelope = null;
        if (payload.Length == 0 || payload.Length > ChatFrame.MaxPayloadBytes) return false;

        ChatEnvelope? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ChatEnvelope>(payload, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed == null || !parsed.IsValid()) return false;

        envelope = parsed;
        return true;
    }

    /// <summary>
    /// 双向同时拨号会产生两条物理链路，必须让两端裁决出同一条连接，否则消息被投递两次。<br/>
    /// 规则：NodeId 较大的一方保留自己主动拨出的连接，较小的一方保留被接受的那条。
    /// 两端各自调用本方法时结论互补，最终收敛到同一条。
    /// </summary>
    public static bool ShouldKeepOutbound(string localNodeId, string remoteNodeId)
    {
        if (string.IsNullOrEmpty(localNodeId) || string.IsNullOrEmpty(remoteNodeId))
            return true;

        return string.CompareOrdinal(localNodeId, remoteNodeId) > 0;
    }

    /// <summary>
    /// 是否应保留这条被动接受（入站）的连接——等价于「远端 NodeId 更大」。
    /// </summary>
    public static bool ShouldKeepIncoming(string localNodeId, string remoteNodeId)
    {
        if (string.IsNullOrEmpty(localNodeId) || string.IsNullOrEmpty(remoteNodeId))
            return true;

        return string.CompareOrdinal(localNodeId, remoteNodeId) < 0;
    }
}
