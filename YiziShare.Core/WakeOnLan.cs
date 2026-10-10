using System.Net;
using System.Net.Sockets;

namespace YiziShare.Core;

/// <summary>Wake-on-LAN：向局域网广播发送魔术包以唤醒离线设备</summary>
public static class WakeOnLan
{
    /// <summary>向指定 MAC 发送魔术包（UDP 广播，默认端口 9）</summary>
    public static async Task WakeAsync(string macAddress, int port = 9, CancellationToken ct = default)
    {
        byte[] packet = BuildMagicPacket(macAddress);
        using var client = new UdpClient();
        client.EnableBroadcast = true;
        await client.SendAsync(packet, new IPEndPoint(IPAddress.Broadcast, port), ct).ConfigureAwait(false);
    }

    /// <summary>构造魔术包：6 字节 0xFF + 16 次重复的 MAC（6 字节）</summary>
    public static byte[] BuildMagicPacket(string macAddress)
    {
        string hex = macAddress.Replace(":", "").Replace("-", "").Replace(" ", "").Trim();
        if (hex.Length != 12 || !IsHex(hex))
            throw new ArgumentException("MAC 地址格式无效", nameof(macAddress));

        var mac = new byte[6];
        for (int i = 0; i < 6; i++)
            mac[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

        var packet = new byte[6 + 16 * 6];
        Array.Fill(packet, (byte)0xFF, 0, 6);
        for (int i = 0; i < 16; i++)
            Array.Copy(mac, 0, packet, 6 + i * 6, 6);
        return packet;
    }

    private static bool IsHex(string value)
    {
        foreach (char c in value)
        {
            bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }
}
