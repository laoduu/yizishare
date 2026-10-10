using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Channels;

namespace YiziShare.Core;

/// <summary>入站聊天消息（携带来源 IP，供界面层把占位会话改写成正式会话）</summary>
public sealed class InboundChatEventArgs : EventArgs
{
    public ChatEnvelope Envelope { get; init; } = new();
    public string PeerIp { get; init; } = "";

    /// <summary>图片/文件消息落盘后的本地路径（Image/File 专用，其余为空）</summary>
    public string AttachmentPath { get; init; } = "";

    /// <summary>是否图片消息（决定气泡渲染方式）</summary>
    public bool IsImage { get; init; }
}

/// <summary>投递状态变化通知</summary>
public sealed class DeliveryChangedEventArgs : EventArgs
{
    public string PeerId { get; init; } = "";
    public string MsgId { get; init; } = "";
    public MessageState State { get; init; }
}

/// <summary>
/// 局域网聊天传输服务：UDP 广播心跳宣告在线，TCP 点对点传输消息与回执。<br/>
/// 这是仓库里首个长生命周期的有状态服务（其余均为一次性 IO），
/// 因此实现 IDisposable 以便在网络切换与进程退出时确定性释放持有的 socket。
/// </summary>
public sealed class ChatService : IDisposable
{
    private const int MaxConnections = 32;
    private const int HandshakeConcurrency = 8;

    private readonly string _nodeId = LoadOrCreateNodeId();
    private readonly PresenceTable _presence = new();
    private readonly DeliveryTracker _delivery = new();
    private readonly ConcurrentDictionary<string, PeerConnection> _connections = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ReconnectState> _reconnect = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _reportedOffline = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _handshakeGate = new(HandshakeConcurrency, HandshakeConcurrency);
    private readonly object _lifecycle = new();

    private CancellationTokenSource? _cts;
    private TcpListener? _listener;
    private UdpClient? _udpSend;
    private UdpClient? _udpListen;
    private int _localPort;
    private bool _disposed;

    /// <summary>入站聊天消息（PeerIp 为该消息实际来源地址）</summary>
    public event EventHandler<InboundChatEventArgs>? MessageReceived;

    /// <summary>对端在线状态跃迁（上线或掉线）</summary>
    public event EventHandler<PeerPresence>? PeerPresenceChanged;

    /// <summary>
    /// 主动拨号握手拿到对端真实 NodeId 时触发。<br/>
    /// UDP 广播被拦截时界面层只能靠此事件把 IP 占位会话改绑到真实标识。
    /// </summary>
    public event EventHandler<PeerPresence>? PeerResolved;

    /// <summary>对端在线期间修改了昵称（心跳/消息帧携带新名时触发；上线/掉线仍走 PeerPresenceChanged）</summary>
    public event EventHandler<PeerPresence>? PeerProfileUpdated;

    /// <summary>自身消息的投递状态发生变化</summary>
    public event EventHandler<DeliveryChangedEventArgs>? DeliveryChanged;

    /// <summary>与某个对端的连接已断开</summary>
    public event EventHandler<PeerPresence>? PeerDisconnected;

    public event EventHandler<string>? LogMessage;

    /// <summary>本机稳定标识</summary>
    public string LocalNodeId => _nodeId;

    /// <summary>本机展示名（由界面层设置为昵称或主机名）</summary>
    public string LocalName { get; set; } = Environment.MachineName;

    /// <summary>实际监听的 TCP 端口</summary>
    public int LocalPort => _localPort;

    public bool IsRunning
    {
        get
        {
            lock (_lifecycle) return _cts != null;
        }
    }

    /// <summary>当前在线的对端列表（界面层用它渲染在线状态）</summary>
    public List<PeerPresence> GetOnlinePeers()
    {
        return _presence.GetOnline();
    }

    /// <summary>查询指定对端的在线信息</summary>
    public PeerPresence? GetPeer(string peerId)
    {
        return _presence.Get(peerId);
    }

    /// <summary>查询自己某条消息当前的投递状态（界面层用它渲染 ✓/✓✓/❌）</summary>
    public MessageState GetDeliveryState(string msgId)
    {
        return _delivery.GetState(msgId);
    }

    #region 生命周期

    /// <summary>启动监听与心跳。重复调用无副作用。</summary>
    public async Task StartAsync()
    {
        lock (_lifecycle)
        {
            if (_disposed || _cts != null) return;
            _cts = new CancellationTokenSource();
        }

        var ct = _cts.Token;

        if (!TryStartTcpListener())
        {
            Log($"聊天端口 {ChatConstants.TcpPort}~{ChatConstants.TcpPortScanEnd} 均被占用，聊天功能不可用");
            return;
        }
        Log($"聊天服务已启动，监听 TCP 端口 {_localPort}，UDP 广播端口 {ChatConstants.UdpPort}");

        StartUdpSender();
        StartUdpListener(ct);

        _ = AcceptLoopAsync(_listener!, ct);
        _ = AnnounceLoopAsync(ct);
        _ = MaintenanceLoopAsync(ct);

        // 立即连发几个宣告，帮对端快速发现本机（而不是等一个心跳周期）
        await AnnounceBurstAsync(ct);
    }

    /// <summary>网络变化后重新绑定：换网卡后广播地址与监听端口都需重建</summary>
    public async Task RebindAsync()
    {
        CancellationToken ct;
        lock (_lifecycle)
        {
            if (_disposed || _cts == null) return;
            ct = _cts.Token;
        }

        foreach (var conn in _connections.Values)
            CloseConnection(conn);

        _connections.Clear();
        _presence.Clear();
        _reportedOffline.Clear();

        try
        {
            _udpSend?.Dispose();
            _udpSend = null;
            StartUdpSender();
            await AnnounceBurstAsync(ct);
            Log("网络已变化，聊天服务已重新绑定");
        }
        catch (Exception ex)
        {
            Logger.Error("聊天重绑失败", ex);
        }
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _cts?.Cancel();

        try
        {
            _listener?.Stop();
        }
        catch { }

        foreach (var conn in _connections.Values)
            CloseConnection(conn);
        _connections.Clear();

        _udpSend?.Dispose();
        _udpListen?.Dispose();
        _cts?.Dispose();
        _handshakeGate.Dispose();
    }

    private bool TryStartTcpListener()
    {
        for (int port = ChatConstants.TcpPort; port <= ChatConstants.TcpPortScanEnd; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                _listener = listener;
                _localPort = port;
                return true;
            }
            catch (SocketException)
            {
                // 端口被占用，顺延到下一个
            }
        }
        return false;
    }

    private void StartUdpSender()
    {
        var client = new UdpClient();
        client.EnableBroadcast = true;
        _udpSend = client;
    }

    private void StartUdpListener(CancellationToken ct)
    {
        try
        {
            var client = new UdpClient();
            client.EnableBroadcast = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            client.Client.Bind(new IPEndPoint(IPAddress.Any, ChatConstants.UdpPort));
            _udpListen = client;
            _ = UdpReceiveLoopAsync(client, ct);
        }
        catch (SocketException ex)
        {
            Logger.Error($"UDP 聊天端口 {ChatConstants.UdpPort} 绑定失败，广播发现不可用（仍可通过主动拨号连接）", ex);
            Log("⚠️ UDP 广播端口被占用，广播发现不可用，将改用主动拨号探测");
        }
    }

    #endregion

    #region UDP 心跳

    private async Task AnnounceLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(WithJitter(ChatConstants.AnnounceInterval));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await AnnounceAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task AnnounceBurstAsync(CancellationToken ct)
    {
        for (int i = 0; i < 3; i++)
        {
            await AnnounceAsync(ct);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task AnnounceAsync(CancellationToken ct)
    {
        var client = _udpSend;
        if (client == null) return;

        var envelope = new ChatEnvelope
        {
            Kind = ChatMessageKind.Announce,
            MsgId = Guid.NewGuid().ToString("N"),
            NodeId = _nodeId,
            Name = LocalName,
            TcpPort = _localPort,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var payload = ChatProtocol.Serialize(envelope);

        foreach (var address in GetBroadcastAddresses())
        {
            try
            {
                await client.SendAsync(payload, new IPEndPoint(address, ChatConstants.UdpPort), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// 逐网卡计算定向广播地址。不用 255.255.255.255 是因为它在多网卡 Windows 上路由不确定。
    /// </summary>
    private static List<IPAddress> GetBroadcastAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!NetworkScanner.IsUsableIpv4Nic(nic)) continue;

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IpRangeUtil.IsApipa(unicast.Address)) continue;

                    int prefix = unicast.PrefixLength;
                    result.Add(IpRangeUtil.BroadcastAddress(unicast.Address, prefix));
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("枚举网卡广播地址失败", ex);
        }

        return result.Distinct().ToList();
    }

    private async Task UdpReceiveLoopAsync(UdpClient client, CancellationToken ct)
    {
        var buffer = new byte[65536];
        var remote = new IPEndPoint(IPAddress.Any, 0);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 用底层 socket 收，才能拿到数据包的来源 IP（广播包自身不携带对端地址）
                var received = await client.Client
                    .ReceiveFromAsync(new Memory<byte>(buffer), SocketFlags.None, remote, ct)
                    .ConfigureAwait(false);

                if (!ChatProtocol.TryDeserialize(buffer.AsSpan(0, received.ReceivedBytes), out var envelope) || envelope == null)
                    continue;
                if (envelope.Kind != ChatMessageKind.Announce) continue;
                if (envelope.NodeId == _nodeId) continue;

                var peerIp = (received.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "";

                var previousPeer = _presence.Get(envelope.NodeId);
                bool becameOnline = _presence.Upsert(envelope.NodeId, peerIp, envelope.Name, envelope.TcpPort);
                if (becameOnline)
                    _reportedOffline.TryRemove(envelope.NodeId, out _);
                _reconnect.TryRemove(envelope.NodeId, out _);

                if (becameOnline)
                {
                    var peer = _presence.Get(envelope.NodeId);
                    if (peer != null)
                        PeerPresenceChanged?.Invoke(this, peer);
                }
                else if (previousPeer != null &&
                         !string.Equals(previousPeer.PeerName, envelope.Name, StringComparison.Ordinal))
                {
                    // 对端在线期间改了昵称：跃迁事件不会触发，需要单独通知（3 秒一次的心跳会立刻带来新名）
                    var peer = _presence.Get(envelope.NodeId);
                    if (peer != null)
                        PeerProfileUpdated?.Invoke(this, peer);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                // 一条脏报文绝不能打死收包循环
                Logger.Error("处理 UDP 心跳失败", ex);
            }
        }
    }

    #endregion

    #region TCP 接受与握手

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            _ = Task.Run(() => HandleIncomingAsync(client, ct), CancellationToken.None);
        }
    }

    private async Task HandleIncomingAsync(TcpClient client, CancellationToken ct)
    {
        await _handshakeGate.WaitAsync(ct).ConfigureAwait(false);
        PeerConnection? connection = null;

        try
        {
            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);

            var stream = client.GetStream();
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeCts.CancelAfter(ChatConstants.HandshakeTimeout);

            var hello = await ReadEnvelopeAsync(stream, handshakeCts.Token).ConfigureAwait(false);
            if (hello == null || hello.Kind != ChatMessageKind.Hello)
            {
                client.Dispose();
                return;
            }

            if (hello.Version != ChatConstants.ProtocolVersion)
            {
                await TrySendAsync(client, new ChatEnvelope
                {
                    Kind = ChatMessageKind.HelloReject,
                    MsgId = Guid.NewGuid().ToString("N"),
                    NodeId = _nodeId,
                    Text = "协议版本不一致"
                }).ConfigureAwait(false);
                client.Dispose();
                Log($"拒绝 {hello.Name}：协议版本不一致（对方 {hello.Version}，本机 {ChatConstants.ProtocolVersion}）");
                return;
            }

            if (_connections.Count >= MaxConnections)
            {
                await TrySendAsync(client, new ChatEnvelope
                {
                    Kind = ChatMessageKind.HelloReject,
                    MsgId = Guid.NewGuid().ToString("N"),
                    NodeId = _nodeId,
                    Text = "连接数已满"
                }).ConfigureAwait(false);
                client.Dispose();
                return;
            }

            var peerIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
            _presence.Upsert(hello.NodeId, peerIp, hello.Name, hello.TcpPort);

            connection = new PeerConnection(hello.NodeId, peerIp, hello.TcpPort, client, outbound: false);

            // 裁决必须在回 HelloAck 之前完成：被丢弃的一方要在握手阶段就知道，
            // 否则它会以为连接可用，随后把消息写进一条随时会被关闭的链路
            bool alreadyHaveLink = _connections.TryGetValue(hello.NodeId, out var existing) && !existing.IsClosed;
            bool keepOutbound = ChatProtocol.ShouldKeepOutbound(_nodeId, hello.NodeId);

            if (alreadyHaveLink && keepOutbound)
            {
                // 本机应保留自己拨出的连接，这条入站的是多余的。
                // 回 HelloReject 让对方回退到双方共同保留的那条，而不是把消息写进一条即将关闭的链路。
                Logger.Info($"对端 {hello.NodeId} 双向重复连接，拒绝多余入站连接");
                await TrySendAsync(client, new ChatEnvelope
                {
                    Kind = ChatMessageKind.HelloReject,
                    MsgId = Guid.NewGuid().ToString("N"),
                    NodeId = _nodeId,
                    Text = "DuplicateLink"
                }).ConfigureAwait(false);
                CloseConnection(connection);
                return;
            }

            await TrySendAsync(client, new ChatEnvelope
            {
                Kind = ChatMessageKind.HelloAck,
                MsgId = Guid.NewGuid().ToString("N"),
                NodeId = _nodeId,
                Name = LocalName,
                TcpPort = _localPort
            }).ConfigureAwait(false);

            if (!RegisterConnection(connection))
            {
                // 已被更合适的连接取代
                CloseConnection(connection);
                return;
            }

            _ = ReadLoopAsync(connection, ct);
            _ = WriteLoopAsync(connection, ct);

            // 连接刚建立，补发此前离线期间积压的消息
            FlushPending(connection, ct);
        }
        catch (Exception ex)
        {
            Logger.Error("处理入站连接失败", ex);
            if (connection == null)
                client.Dispose();
            else
                CloseConnection(connection);
        }
        finally
        {
            _handshakeGate.Release();
        }
    }

    /// <summary>
    /// 登记连接。<br/>
    /// 两端同时拨号会建立两条物理链路，必须让两端保留同一条，否则消息被投递两次。
    /// 裁决规则：NodeId 较大的一方保留自己拨出的（出站）连接，
    /// 较小的一方保留被接受的那条（入站）连接——两端用同一套字典序判断，结论必然互补。
    /// </summary>
    private bool RegisterConnection(PeerConnection connection)
    {
        if (_connections.TryGetValue(connection.PeerId, out var existing) && !existing.IsClosed)
            return KeepExisting(existing, connection);

        _connections[connection.PeerId] = connection;
        return true;
    }

    /// <summary>已有连接时按字典序裁决保留哪一条，返回 true 表示保留的是 incoming（新连接）</summary>
    private bool KeepExisting(PeerConnection existing, PeerConnection incoming)
    {
        // 本机 NodeId 更大 → 双方都应保留"出站"那条；否则应保留"入站"那条。
        // 无论 incoming 是出站还是入站，都按这个最终归属来判断。
        bool preferOutbound = ChatProtocol.ShouldKeepOutbound(_nodeId, incoming.PeerId);

        PeerConnection winner = preferOutbound
            ? (existing.IsOutbound ? existing : incoming)
            : (existing.IsOutbound ? incoming : existing);

        PeerConnection loser = ReferenceEquals(winner, existing) ? incoming : existing;

        if (!ReferenceEquals(winner, existing))
            _connections[incoming.PeerId] = winner;

        Logger.Info($"对端 {incoming.PeerId} 双向重复连接，保留{(winner.IsOutbound ? "出站" : "入站")}连接");
        CloseConnection(loser);

        return ReferenceEquals(winner, incoming);
    }

    #endregion

    #region TCP 收发

    private async Task ReadLoopAsync(PeerConnection connection, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !connection.IsClosed)
            {
                var (outcome, envelope) = await ReadFrameAsync(connection.Stream, ct).ConfigureAwait(false);

                // 坏帧只丢这一帧，连接继续
                if (outcome == ReadOutcome.BadFrame) continue;

                if (outcome == ReadOutcome.Closed || envelope == null)
                {
                    Logger.Debug($"[RX] 连接 {Shorten(connection.PeerId)} 读结束（流关闭或长度非法）");
                    break;
                }

                connection.LastSeen = DateTimeOffset.UtcNow;

                if (!string.Equals(envelope.NodeId, _nodeId, StringComparison.Ordinal))
                {
                    var previousPeer = _presence.Get(envelope.NodeId);
                    _presence.Upsert(envelope.NodeId, connection.PeerIp, envelope.Name, envelope.TcpPort);

                    // TCP 帧同样可能带来对端的改名（UDP 被拦截时这是唯一途径）
                    if (previousPeer != null &&
                        !string.Equals(previousPeer.PeerName, envelope.Name, StringComparison.Ordinal))
                    {
                        var renamed = _presence.Get(envelope.NodeId);
                        if (renamed != null)
                            PeerProfileUpdated?.Invoke(this, renamed);
                    }
                }

                HandleInboundEnvelope(connection, envelope);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Error($"读取 {connection.PeerId} 的数据失败", ex);
        }
        finally
        {
            OnConnectionLost(connection);
        }
    }

    private void HandleInboundEnvelope(PeerConnection connection, ChatEnvelope envelope)
    {
        // 逐条报文用 Debug 级别：Info 会被心跳和回执刷屏，几 MB 就触发日志轮转
        Logger.Debug($"[RX] 来自 {envelope.Name}({Shorten(envelope.NodeId)}) kind={envelope.Kind} " +
            $"refId={Shorten(envelope.RefId)} msgId={Shorten(envelope.MsgId)} " +
            $"via={(connection.IsOutbound ? "出站" : "入站")}");

        switch (envelope.Kind)
        {
            case ChatMessageKind.Chat:
                // Delivered 是传输层语义（我的进程收到了），自动回执，不依赖 UI
                connection.TryEnqueue(BuildReceipt(ChatMessageKind.Delivered, envelope.MsgId));
                MessageReceived?.Invoke(this, new InboundChatEventArgs
                {
                    Envelope = envelope,
                    PeerIp = connection.PeerIp
                });
                break;

            case ChatMessageKind.Image:
            case ChatMessageKind.File:
                var attachmentPath = SaveAttachment(envelope);
                if (attachmentPath == null)
                {
                    // 载荷损坏（base64 解码失败等）：回执拒绝语义没有专门的 Kind，直接丢弃该帧
                    Logger.Warning($"附件消息 {Shorten(envelope.MsgId)} 载荷损坏，已丢弃");
                    break;
                }

                connection.TryEnqueue(BuildReceipt(ChatMessageKind.Delivered, envelope.MsgId));
                MessageReceived?.Invoke(this, new InboundChatEventArgs
                {
                    Envelope = envelope,
                    PeerIp = connection.PeerIp,
                    AttachmentPath = attachmentPath,
                    IsImage = envelope.Kind == ChatMessageKind.Image
                });
                break;

            case ChatMessageKind.Delivered:
                var delivered = _delivery.ApplyDelivered(envelope.RefId);
                if (delivered != null)
                    RaiseDeliveryChanged(delivered.PeerId, delivered.MsgId, delivered.State);
                break;

            case ChatMessageKind.Read:
                foreach (var pending in _delivery.ApplyRead(connection.PeerId, envelope.Text))
                    RaiseDeliveryChanged(pending.PeerId, pending.MsgId, pending.State);
                break;

            case ChatMessageKind.Ping:
                connection.TryEnqueue(BuildEnvelope(ChatMessageKind.Pong));
                break;
        }
    }

    private async Task WriteLoopAsync(PeerConnection connection, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !connection.IsClosed)
            {
                var envelope = await connection.Outbox.Reader.ReadAsync(ct).ConfigureAwait(false);
                var frame = ChatFrame.Encode(ChatProtocol.Serialize(envelope));

                await connection.Stream.WriteAsync(frame, ct).ConfigureAwait(false);
                await connection.Stream.FlushAsync(ct).ConfigureAwait(false);

                // 帧写入成功 = 本地投递（Sent），不代表对方收到
                if (envelope.Kind == ChatMessageKind.Chat)
                {
                    _delivery.MarkSent(envelope.MsgId);
                    RaiseDeliveryChanged(connection.PeerId, envelope.MsgId, MessageState.Sent);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ChannelClosedException)
        {
            // 连接关闭时发送队列随之完结，属正常路径
        }
        catch (Exception ex)
        {
            Logger.Error($"向 {connection.PeerId} 发送数据失败", ex);
        }
        finally
        {
            OnConnectionLost(connection);
        }
    }

    private void OnConnectionLost(PeerConnection connection)
    {
        if (!_connections.TryGetValue(connection.PeerId, out var current) || ReferenceEquals(current, connection))
            _connections.TryRemove(connection.PeerId, out _);

        CloseConnection(connection);

        // 连接断了但心跳仍在继续（对方进程还活着，只是这条链路废了），交给重连处理
        var peer = _presence.Get(connection.PeerId);
        if (peer != null && !_presence.IsOnline(connection.PeerId) && _reportedOffline.TryAdd(connection.PeerId, true))
            PeerDisconnected?.Invoke(this, peer);
    }

    private static void CloseConnection(PeerConnection connection)
    {
        connection.MarkClosed();
        try
        {
            connection.Stream.Dispose();
        }
        catch { }
        try
        {
            connection.Client.Dispose();
        }
        catch { }
    }

    #endregion

    #region 发送

    /// <summary>
    /// 向指定对端发送一条消息。<b>立即返回</b>：消息登记为 Sending 并交由后台派发，
    /// UI 可马上把气泡显示出来；后续状态经 <see cref="DeliveryChanged"/> 实时流转——
    /// 连接建立 → 写循环标记 Sent，对端不可达 → 转入待发队列并标记 Queued，
    /// 上线后由 <see cref="FlushPending"/> 自动补发。
    /// </summary>
    /// <param name="targetNodeId">对端的稳定标识（尚未握手时可为 IP 占位）</param>
    /// <param name="targetIp">对端 IP</param>
    /// <param name="targetPort">对端实际监听的端口</param>
    /// <returns>新消息的 MsgId（必定非空；联网结果经事件通知，不经返回值）</returns>
    public string SendChatAsync(string targetNodeId, string targetIp, int targetPort, string text)
    {
        var envelope = BuildEnvelope(ChatMessageKind.Chat, text);

        // 先登记再派发：App 层用返回的 MsgId 上屏，状态从 Sending 起步
        _delivery.Register(envelope.MsgId, targetNodeId);
        _delivery.MarkSending(envelope.MsgId);

        // 拨号可能含端口区间探测（十几个超时周期），必须放后台，不能拖住 UI
        _ = DispatchChatAsync(targetNodeId, targetIp, targetPort, envelope);

        return envelope.MsgId;
    }

    /// <summary>后台派发：拨号 → 直发或入待发队列。失败兜底入队，消息不丢。</summary>
    private async Task DispatchChatAsync(string targetNodeId, string targetIp, int targetPort, ChatEnvelope envelope)
    {
        try
        {
            var connection = await GetOrConnectAsync(targetNodeId, targetIp, targetPort, CancellationToken.None).ConfigureAwait(false);
            if (connection == null)
            {
                // 对方离线：消息照常保留，由待发队列在对方上线后补发
                EnqueuePending(targetNodeId, targetIp, targetPort, envelope);
                _delivery.MarkQueued(envelope.MsgId);
                RaiseDeliveryChanged(targetNodeId, envelope.MsgId, MessageState.Queued);
                Log($"{envelope.Name}（{targetIp}）当前不可达，消息已进入待发队列，上线后自动补发");
                return;
            }

            // 握手拿到真实 NodeId：跟踪记录改挂真实键，否则 ApplyRead 匹配不到
            _delivery.Rebind(envelope.MsgId, connection.PeerId);

            if (!connection.TryEnqueue(envelope))
            {
                EnqueuePending(connection.PeerId, connection.PeerIp, connection.PeerPort, envelope);
                _delivery.MarkQueued(envelope.MsgId);
                RaiseDeliveryChanged(connection.PeerId, envelope.MsgId, MessageState.Queued);
                Log("发送队列已满，消息转入待发队列");
                return;
            }

            // 帧写入成功后由写循环把状态推进到 Sent
        }
        catch (Exception ex)
        {
            Logger.Error($"派发消息 {Shorten(envelope.MsgId)} 失败，转入待发队列兜底", ex);
            EnqueuePending(targetNodeId, targetIp, targetPort, envelope);
            _delivery.MarkQueued(envelope.MsgId);
            RaiseDeliveryChanged(targetNodeId, envelope.MsgId, MessageState.Queued);
        }
    }

    #region 待发队列（对方离线时暂存，上线后补发）

    /// <summary>待发的一条消息及其目标</summary>
    private sealed class PendingMessage
    {
        public required ChatEnvelope Envelope { get; init; }
        public required string PeerIp { get; init; }
        public required int PeerPort { get; init; }
    }

    // 按对端标识分组，保持组内 FIFO
    private readonly ConcurrentDictionary<string, List<PendingMessage>> _pendingOutbox = new(StringComparer.Ordinal);
    private readonly object _pendingSync = new();

    /// <summary>单个对端最多暂存多少条离线消息，超出则丢弃最旧的</summary>
    private const int MaxPendingPerPeer = 200;

    /// <summary>把发送失败的消息暂存，等对方上线后补发</summary>
    private void EnqueuePending(string peerId, string peerIp, int peerPort, ChatEnvelope envelope)
    {
        var list = _pendingOutbox.GetOrAdd(peerId, _ => new List<PendingMessage>());

        lock (_pendingSync)
        {
            list.Add(new PendingMessage { Envelope = envelope, PeerIp = peerIp, PeerPort = peerPort });

            // 防止离线期间无限堆积
            if (list.Count > MaxPendingPerPeer)
                list.RemoveRange(0, list.Count - MaxPendingPerPeer);
        }
    }

    /// <summary>
    /// 取出并清空某个对端的待发消息。<br/>
    /// 离线期间只能按 IP 占位标识入队，而连接建立后拿到的是真实 NodeId，
    /// 因此这里同时按「真实标识」和「IP 占位」两种键查找，避免积压的消息永远发不出去。
    /// </summary>
    private List<PendingMessage> TakePending(string peerId, string peerIp)
    {
        var result = new List<PendingMessage>();

        var keys = new List<string> { peerId };
        var placeholder = $"ip-{peerIp}";
        if (!string.Equals(placeholder, peerId, StringComparison.OrdinalIgnoreCase))
            keys.Add(placeholder);

        foreach (var key in keys)
        {
            if (!_pendingOutbox.TryRemove(key, out var list)) continue;

            lock (_pendingSync)
            {
                result.AddRange(list);
                list.Clear();
            }
        }

        return result;
    }

    /// <summary>该对端当前是否有待发消息</summary>
    public bool HasPending(string peerId, string peerIp)
    {
        if (_pendingOutbox.TryGetValue(peerId, out var list) && list.Count > 0) return true;

        var placeholder = $"ip-{peerIp}";
        return _pendingOutbox.TryGetValue(placeholder, out var byIp) && byIp.Count > 0;
    }

    /// <summary>
    /// 对端连上后补发积压的消息。<br/>
    /// 入队是非阻塞操作（Channel.TryWrite），故不必是异步方法。
    /// </summary>
    private void FlushPending(PeerConnection connection, CancellationToken ct)
    {
        var pending = TakePending(connection.PeerId, connection.PeerIp);
        if (pending.Count == 0) return;

        Logger.Info($"向 {Shorten(connection.PeerId)} 补发 {pending.Count} 条离线消息");

        foreach (var item in pending)
        {
            if (ct.IsCancellationRequested || connection.IsClosed) break;

            // 补发保留原 MsgId：接收端据此幂等去重，重复投递也不会显示两次
            if (!connection.TryEnqueue(item.Envelope))
            {
                // 队列又满了，放回去下次再试
                lock (_pendingSync)
                {
                    _pendingOutbox.GetOrAdd(connection.PeerId, _ => new List<PendingMessage>()).Add(item);
                }
                break;
            }

            // 离线期间按占位键登记的跟踪记录改挂真实键，否则已读回执匹配不到
            _delivery.Rebind(item.Envelope.MsgId, connection.PeerId);
            _delivery.MarkSending(item.Envelope.MsgId);
            RaiseDeliveryChanged(connection.PeerId, item.Envelope.MsgId, MessageState.Sending);
        }
    }

    #endregion

    /// <summary>发送回执（Delivered 或 Read）。回执发送失败不影响主流程，仅记录日志。</summary>
    public async Task SendReceiptAsync(string targetNodeId, string targetIp, int targetPort,
        ChatMessageKind kind, string refId, string text = "")
    {
        var envelope = BuildEnvelope(kind, text);
        envelope.RefId = refId;

        var connection = await GetOrConnectAsync(targetNodeId, targetIp, targetPort, CancellationToken.None).ConfigureAwait(false);
        if (connection == null) return;

        connection.TryEnqueue(envelope);
    }

    /// <summary>
    /// 发送图片/文件消息：读文件 → base64 → 单帧内联发送，复用聊天连接与投递状态机。<br/>
    /// 超过 <see cref="ChatConstants.MaxAttachmentBytes"/> 返回 null 并由调用方提示。
    /// </summary>
    public async Task<string?> SendAttachmentAsync(
        string targetNodeId, string targetIp, int targetPort, string filePath, bool isImage)
    {
        if (!File.Exists(filePath)) return null;

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length > ChatConstants.MaxAttachmentBytes) return null;

        var bytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);

        var envelope = BuildEnvelope(isImage ? ChatMessageKind.Image : ChatMessageKind.File, "");
        envelope.FileName = Path.GetFileName(filePath);
        envelope.Data = Convert.ToBase64String(bytes);

        _delivery.Register(envelope.MsgId, targetNodeId);
        _delivery.MarkSending(envelope.MsgId);
        _ = DispatchChatAsync(targetNodeId, targetIp, targetPort, envelope);

        return envelope.MsgId;
    }

    /// <summary>
    /// 把附件消息的 base64 载荷落盘到 %LOCALAPPDATA%\YiziShare\Received\{月份}\；失败返回 null。
    /// MsgId 前 8 位做前缀，天然去重且避免同名覆盖。
    /// </summary>
    private static string? SaveAttachment(ChatEnvelope envelope)
    {
        try
        {
            var bytes = Convert.FromBase64String(envelope.Data);
            if (bytes.Length == 0) return null;

            var safeName = string.Join("_", envelope.FileName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "attachment";

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YiziShare", "Received", DateTime.Now.ToString("yyyyMM"));
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"{envelope.MsgId[..8]}_{safeName}");
            File.WriteAllBytes(path, bytes);
            return path;
        }
        catch (Exception ex)
        {
            Logger.Warning($"附件落盘失败 ({envelope.FileName}): {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 主动拨号探测：完全绕开 UDP，广播被防火墙拦截时仍能建立连接。
    /// </summary>
    public async Task<bool> ProbeAsync(string peerId, string peerIp, int peerPort)
    {
        if (!IsRunning || _disposed) return false;

        var connection = await GetOrConnectAsync(peerId, peerIp, peerPort, CancellationToken.None).ConfigureAwait(false);
        return connection != null;
    }

    private async Task<PeerConnection?> GetOrConnectAsync(string peerId, string peerIp, int peerPort, CancellationToken ct)
    {
        if (_connections.TryGetValue(peerId, out var existing) && !existing.IsClosed)
            return existing;

        // 已用真实 NodeId 连过这个对端（peerId 可能只是 IP 占位），不要重复拨号
        var known = _presence.Get(peerId);
        if (!string.IsNullOrEmpty(known?.PeerId) && !string.Equals(known.PeerId, peerId, StringComparison.Ordinal))
        {
            if (_connections.TryGetValue(known.PeerId, out var realExisting) && !realExisting.IsClosed)
                return realExisting;
        }

        if (peerPort <= 0 || !IPAddress.TryParse(peerIp, out var address)) return null;

        // 先按已知端口连；失败则按端口区间逐个试探——服务端被占用时会顺延到 45403+，
        // 而对端的实际端口只有 UDP 心跳能告知，广播被拦时必须靠探测兜底。
        var connection = await DialAsync(peerId, peerIp, address, peerPort, ct).ConfigureAwait(false);
        if (connection != null) return connection;

        if (peerPort == ChatConstants.TcpPort)
        {
            Logger.Info($"对端 {Shorten(peerId)} 的 {peerPort} 端口不可达，按区间探测");
            for (int port = ChatConstants.TcpPort + 1; port <= ChatConstants.TcpPortScanEnd; port++)
            {
                connection = await DialAsync(peerId, peerIp, address, port, ct).ConfigureAwait(false);
                if (connection != null)
                {
                    Logger.Info($"探测到对端 {Shorten(peerId)} 的实际端口 {port}");
                    return connection;
                }
            }
        }

        return null;
    }

    /// <summary>按指定端口拨号并完成握手，失败返回 null</summary>
    private async Task<PeerConnection?> DialAsync(string peerId, string peerIp, IPAddress address,
        int peerPort, CancellationToken ct)
    {
        try
        {
            var client = new TcpClient();
            client.NoDelay = true;
            client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
            client.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 2);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(ChatConstants.ProbeTimeout);

            await client.ConnectAsync(address, peerPort, connectCts.Token).ConfigureAwait(false);

            await TrySendAsync(client, BuildEnvelope(ChatMessageKind.Hello)).ConfigureAwait(false);

            var ack = await ReadEnvelopeAsync(client.GetStream(), connectCts.Token).ConfigureAwait(false);
            if (ack == null || string.IsNullOrEmpty(ack.NodeId))
            {
                client.Dispose();
                return null;
            }

            // 对端因双向重复连接拒绝了这条拨号——回退到已有的那条，它才是双方共同保留的链路
            if (ack.Kind == ChatMessageKind.HelloReject)
            {
                client.Dispose();
                if (_connections.TryGetValue(ack.NodeId, out var fallback) && !fallback.IsClosed)
                    return fallback;
                return null;
            }

            if (ack.Kind != ChatMessageKind.HelloAck)
            {
                client.Dispose();
                return null;
            }

            // 握手应答里才有对方的真实标识，必须用它统一登记：
            // 若沿用调用方传入的 IP 占位，同一对端会被登记成两个条目、各建一条连接，
            // 导致消息在两条链路上各投递一次而重复显示。
            var realPeerId = ack.NodeId;
            var connection = new PeerConnection(realPeerId, peerIp, ack.TcpPort, client, outbound: true);

            _presence.Upsert(realPeerId, peerIp, ack.Name, ack.TcpPort);

            // 界面层可能只拿得到 IP 占位标识，握手出真实 NodeId 后要通知它改绑会话
            if (!string.Equals(peerId, realPeerId, StringComparison.Ordinal))
                PeerResolved?.Invoke(this, new PeerPresence
                {
                    PeerId = realPeerId,
                    PeerIp = peerIp,
                    PeerName = ack.Name,
                    TcpPort = ack.TcpPort
                });

            // 与入站侧用同一套字典序裁决，保证两端保留的是同一条物理连接
            if (_connections.TryGetValue(realPeerId, out var current) && !current.IsClosed)
            {
                bool keepNew = KeepExisting(current, connection);
                return keepNew ? connection : current;
            }

            if (!_connections.TryAdd(realPeerId, connection))
            {
                CloseConnection(connection);
                return _connections[realPeerId];
            }

            // 占位条目与真实条目指向同一个人，登记连接后立即丢弃占位，避免维护循环再拨一次
            if (!string.Equals(peerId, realPeerId, StringComparison.Ordinal))
            {
                _presence.Remove(peerId);
                if (_connections.TryGetValue(peerId, out var stale) && !stale.IsClosed)
                {
                    _connections.TryRemove(peerId, out _);
                    CloseConnection(stale);
                }
            }

            var token = _cts?.Token ?? CancellationToken.None;
            _ = ReadLoopAsync(connection, token);
            _ = WriteLoopAsync(connection, token);
            FlushPending(connection, token);

            return connection;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }

    private ChatEnvelope BuildEnvelope(ChatMessageKind kind, string text = "")
    {
        return new ChatEnvelope
        {
            Kind = kind,
            MsgId = Guid.NewGuid().ToString("N"),
            NodeId = _nodeId,
            Name = LocalName,
            Text = text,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TcpPort = _localPort
        };
    }

    private ChatEnvelope BuildReceipt(ChatMessageKind kind, string refId, string text = "")
    {
        var envelope = BuildEnvelope(kind, text);
        envelope.RefId = refId;
        return envelope;
    }

    #endregion

    #region 维护循环

    private async Task MaintenanceLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                RunMaintenance(ct);
            }
        }
        catch (OperationCanceledException) { }
    }

    private void RunMaintenance(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var pending in _delivery.SweepTimeouts(now))
            RaiseDeliveryChanged(pending.PeerId, pending.MsgId, pending.State);

        // 只在"由在线转为离线"的那一刻上报一次，避免每轮维护循环重复通知 UI
        foreach (var stale in _presence.SweepStale(now))
        {
            if (!_reportedOffline.TryAdd(stale.PeerId, true)) continue;
            PeerDisconnected?.Invoke(this, stale);
        }

        foreach (var connection in _connections.Values)
        {
            if (connection.IsClosed)
            {
                _connections.TryRemove(connection.PeerId, out _);
                continue;
            }

            if (now - connection.LastSeen > ChatConstants.LinkIdleTimeout)
            {
                Logger.Info($"连接 {connection.PeerId} 空闲超时，断开");
                OnConnectionLost(connection);
                continue;
            }

            if (now - connection.LastPingAt > ChatConstants.KeepAliveInterval)
            {
                connection.LastPingAt = now;
                connection.TryEnqueue(BuildEnvelope(ChatMessageKind.Ping));
            }
        }

        TryReconnect(ct);
        TryFlushPendingMessages(ct);
    }

    /// <summary>
    /// 待发队列的主动投递尝试。<br/>
    /// 补发只在连接建立时触发，而离线对端既不在在线表里、也不会主动连过来，
    /// 因此必须有独立的重试循环——否则队列里的消息会永远躺在那里发不出去。
    /// </summary>
    private void TryFlushPendingMessages(CancellationToken ct)
    {
        if (_pendingOutbox.IsEmpty) return;

        foreach (var peerId in _pendingOutbox.Keys)
        {
            if (ct.IsCancellationRequested) return;

            // 已有活跃连接时，连接建立那一刻就已补发过
            if (_connections.TryGetValue(peerId, out var existing) && !existing.IsClosed) continue;

            // 取一条样本拿到目标地址（队列内其余消息的目标相同）
            if (!TryPeekPendingTarget(peerId, out var peerIp, out var peerPort)) continue;
            if (!IPAddress.TryParse(peerIp, out _)) continue;

            // 与重连共用退避节奏，避免对离线设备高频拨号
            var now = DateTimeOffset.UtcNow;
            if (!_reconnect.TryGetValue(peerId, out var state))
            {
                state = new ReconnectState { NextAttemptAt = now };
                _reconnect[peerId] = state;
            }

            if (now < state.NextAttemptAt) continue;
            state.NextAttemptAt = now.AddSeconds(BackoffSeconds(state.Attempts));

            _ = Task.Run(async () =>
            {
                var connection = await GetOrConnectAsync(peerId, peerIp, peerPort, ct).ConfigureAwait(false);
                if (connection != null)
                    _reconnect.TryRemove(peerId, out _);
            }, CancellationToken.None);
        }
    }

    /// <summary>查看某对端待发消息的目标地址</summary>
    private bool TryPeekPendingTarget(string peerId, out string peerIp, out int peerPort)
    {
        peerIp = "";
        peerPort = 0;

        if (!_pendingOutbox.TryGetValue(peerId, out var list)) return false;

        lock (_pendingSync)
        {
            if (list.Count == 0) return false;
            peerIp = list[0].PeerIp;
            peerPort = list[0].PeerPort;
            return true;
        }
    }

    private void TryReconnect(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var peer in _presence.GetOnline())
        {
            if (_connections.TryGetValue(peer.PeerId, out var existing) && !existing.IsClosed) continue;
            if (!IPAddress.TryParse(peer.PeerIp, out _)) continue;

            if (!_reconnect.TryGetValue(peer.PeerId, out var state))
            {
                state = new ReconnectState { NextAttemptAt = now };
                _reconnect[peer.PeerId] = state;
            }

            if (now < state.NextAttemptAt) continue;

            state.Attempts++;
            state.NextAttemptAt = now.AddSeconds(BackoffSeconds(state.Attempts));

            _ = Task.Run(async () =>
            {
                var connection = await GetOrConnectAsync(peer.PeerId, peer.PeerIp, peer.TcpPort, ct).ConfigureAwait(false);
                if (connection != null)
                    _reconnect.TryRemove(peer.PeerId, out _);
            }, CancellationToken.None);
        }
    }

    /// <summary>指数退避 1s → 2s → 4s → 8s → 15s 封顶</summary>
    private static double BackoffSeconds(int attempt)
    {
        return attempt switch
        {
            1 => 1,
            2 => 2,
            3 => 4,
            4 => 8,
            _ => 15
        };
    }

    #endregion

    #region 辅助

    /// <summary>读取结果：区分「连接结束」与「单个坏帧」，后者不应拆链</summary>
    private enum ReadOutcome
    {
        /// <summary>成功读到一帧</summary>
        Received,
        /// <summary>流已结束或发生 I/O 错误</summary>
        Closed,
        /// <summary>帧损坏（长度非法或解析失败），可继续读下一帧</summary>
        BadFrame
    }

    /// <summary>
    /// 读取一个完整帧（4 字节长度前缀 + 载荷）。<br/>
    /// 单个坏帧只丢这一帧而不拆链：对端发一条超长或格式错误的报文不该让整条连接失效。
    /// </summary>
    private static async Task<(ReadOutcome Outcome, ChatEnvelope? Envelope)> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[ChatFrame.HeaderSize];
        try
        {
            await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EndOfStreamException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return (ReadOutcome.Closed, null);
        }

        int length = ChatFrame.DecodeLength(header);
        if (length <= 0)
        {
            // 长度非法意味着后续字节已无法定位边界，只能断链
            Logger.Warning($"聊天报文长度非法：{length}，断开连接");
            return (ReadOutcome.Closed, null);
        }

        var payload = new byte[length];
        try
        {
            await stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is EndOfStreamException or OperationCanceledException or IOException or ObjectDisposedException)
        {
            return (ReadOutcome.Closed, null);
        }

        // 长度合法但内容解析失败：边界仍然完整，丢这一帧继续读
        if (!ChatProtocol.TryDeserialize(payload, out var envelope) || envelope == null)
        {
            Logger.Warning("丢弃一个无法解析的聊天报文，连接保持");
            return (ReadOutcome.BadFrame, null);
        }

        return (ReadOutcome.Received, envelope);
    }

    /// <summary>读取一个完整帧的便捷包装（握手阶段使用，失败一律视为结束）</summary>
    private static async Task<ChatEnvelope?> ReadEnvelopeAsync(Stream stream, CancellationToken ct)
    {
        var (outcome, envelope) = await ReadFrameAsync(stream, ct).ConfigureAwait(false);
        return outcome == ReadOutcome.Received ? envelope : null;
    }

    /// <summary>握手阶段的直发（此时连接尚未纳入连接表，不走发送队列）</summary>
    private async Task TrySendAsync(TcpClient client, ChatEnvelope envelope)
    {
        try
        {
            var frame = ChatFrame.Encode(ChatProtocol.Serialize(envelope));
            var stream = client.GetStream();
            await stream.WriteAsync(frame).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException)
        {
            // 握手失败由调用方处理
        }
    }

    private void RaiseDeliveryChanged(string peerId, string msgId, MessageState state)
    {
        DeliveryChanged?.Invoke(this, new DeliveryChangedEventArgs
        {
            PeerId = peerId,
            MsgId = msgId,
            State = state
        });
    }

    /// <summary>给固定间隔加 ±20% 抖动，避免同网段所有机器同秒广播</summary>
    private static TimeSpan WithJitter(TimeSpan interval)
    {
        double factor = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromMilliseconds(interval.TotalMilliseconds * factor);
    }

    /// <summary>日志里用的短标识，避免刷屏</summary>
    private static string Shorten(string id)
    {
        return string.IsNullOrEmpty(id) ? "-" : id[..Math.Min(6, id.Length)];
    }

    private void Log(string message)
    {
        Logger.Info(message);
        LogMessage?.Invoke(this, message);
    }

    /// <summary>
    /// 读取本机的稳定标识，首次运行时生成并持久化。<br/>
    /// 必须持久化：若每次启动都换 ID，对端会把重启后的本机当成新设备，
    /// 历史会话无法归并；DHCP 换了 IP 之后也更难识别为同一台机器。<br/>
    /// 双写备份：主文件在 <c>%LOCALAPPDATA%\YiziShare\node-id.txt</c>，
    /// 备份在 <c>%APPDATA%\YiziShare\node-id.txt</c>。任一生效即复用同一份标识，
    /// 即使用户清了 Local（部分卸载器会这么做），只要 Roaming 还在就认得出旧设备。
    /// </summary>
    private static string LoadOrCreateNodeId()
    {
        try
        {
            var primary = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YiziShare", "node-id.txt");
            var backup = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "YiziShare", "node-id.txt");

            foreach (var path in new[] { primary, backup })
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length == 32)
                    {
                        // 主文件丢了但备份还在，或反之——读到一个就双写补齐
                        PersistNodeId(primary, existing);
                        PersistNodeId(backup, existing);
                        return existing;
                    }
                }
                catch (Exception inner)
                {
                    Logger.Warning($"读取 NodeId 失败：{path} ({inner.Message})");
                }
            }

            var created = Guid.NewGuid().ToString("N");
            PersistNodeId(primary, created);
            PersistNodeId(backup, created);
            return created;
        }
        catch
        {
            // 落盘失败时退化为进程级标识，功能仍可用，只是重启后会被当作新设备
            return Guid.NewGuid().ToString("N");
        }
    }

    /// <summary>把 NodeId 写到指定路径，自动建目录；失败仅记录不抛出（NodeId 仍在内存里有效）</summary>
    private static void PersistNodeId(string path, string nodeId)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, nodeId);
        }
        catch (Exception ex)
        {
            Logger.Warning($"写入 NodeId 备份失败：{path} ({ex.Message})");
        }
    }

    private sealed class ReconnectState
    {
        public int Attempts;
        public DateTimeOffset NextAttemptAt;
    }

    #endregion
}

/// <summary>与单个对端的连接：发送队列 + 空闲跟踪</summary>
internal sealed class PeerConnection
{
    private const int OutboxCapacity = 200;
    private int _closed;

    public PeerConnection(string peerId, string peerIp, int peerPort, TcpClient client, bool outbound)
    {
        PeerId = peerId;
        PeerIp = peerIp;
        PeerPort = peerPort;
        Client = client;
        Stream = client.GetStream();
        IsOutbound = outbound;
        Outbox = Channel.CreateBounded<ChatEnvelope>(new BoundedChannelOptions(OutboxCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public string PeerId { get; }
    public string PeerIp { get; }
    public int PeerPort { get; }
    public TcpClient Client { get; }
    public NetworkStream Stream { get; }
    public bool IsOutbound { get; }
    public Channel<ChatEnvelope> Outbox { get; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastPingAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    public void MarkClosed()
    {
        Interlocked.Exchange(ref _closed, 1);
        Outbox.Writer.TryComplete();
    }

    public bool TryEnqueue(ChatEnvelope envelope)
    {
        if (IsClosed) return false;
        return Outbox.Writer.TryWrite(envelope);
    }
}

