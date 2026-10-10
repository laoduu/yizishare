using System.Net;
using System.Net.NetworkInformation;

namespace YiziShare.Core;

/// <summary>
/// IP 范围与子网计算工具（纯函数，便于单测）
/// </summary>
public static class IpRangeUtil
{
    /// <summary>
    /// 生成子网内所有可用主机地址（排除网络地址与广播地址）
    /// 例如 /24 → x.x.x.1 ~ x.x.x.254 共 253 个
    /// </summary>
    public static List<IPAddress> GenerateHostIps(IPAddress network, int prefixLen)
    {
        var result = new List<IPAddress>();
        var bytes = NormalizeNetwork(network, prefixLen).GetAddressBytes();
        uint networkInt = ToUInt(bytes);
        int hostBits = 32 - prefixLen;
        if (hostBits <= 0 || hostBits >= 32) return result;

        ulong total = 1UL << hostBits;      // 地址总数
        for (uint i = 1; i < total - 1; i++) // 跳过网络地址(.0)与广播地址(.255)
        {
            uint ipInt = networkInt + i;
            result.Add(FromUInt(ipInt));
        }
        return result;
    }

    /// <summary>
    /// 解析 "a.b.c.d/前缀"，无前缀时默认 /24
    /// </summary>
    public static (IPAddress Network, int PrefixLen) ParseSubnet(string subnet)
    {
        if (subnet.Contains('/'))
        {
            var parts = subnet.Split('/');
            return (IPAddress.Parse(parts[0]), int.Parse(parts[1]));
        }
        return (IPAddress.Parse(subnet), 24);
    }

    /// <summary>将任意 IPv4 归一化为其所在网络的网络地址（按掩码清零主机位）</summary>
    public static IPAddress NormalizeNetwork(IPAddress ip, int prefixLen)
    {
        var bytes = ip.GetAddressBytes();
        uint ipInt = ToUInt(bytes);
        return FromUInt(ipInt & ToMask(prefixLen));
    }

    /// <summary>
    /// 计算子网的定向广播地址（网络地址 OR 按位取反的掩码）
    /// 例如 192.168.1.0/24 → 192.168.1.255
    /// </summary>
    public static IPAddress BroadcastAddress(IPAddress network, int prefixLen)
    {
        var bytes = NormalizeNetwork(network, prefixLen).GetAddressBytes();
        uint ipInt = ToUInt(bytes);
        return FromUInt(ipInt | ~ToMask(prefixLen));
    }

    /// <summary>IPv4 → CIDR 子网字符串，如 192.168.1.37 → "192.168.1.0/24"</summary>
    public static string SubnetOf(IPAddress ipv4)
    {
        var net = NormalizeNetwork(ipv4, 24);
        return $"{net}/24";
    }

    /// <summary>是否为链路本地自动配置地址(APIPA) 169.254.*.*</summary>
    public static bool IsApipa(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }

    /// <summary>是否包含可用的 IPv4 网关（排除空值与 APIPA 网关）</summary>
    public static bool HasIpv4Gateway(IEnumerable<IPAddress?> gateways)
        => gateways.Any(g => g != null
                             && g.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                             && !IsApipa(g));

    private static uint ToUInt(byte[] b) => (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);

    private static uint ToMask(int prefixLen)
        => prefixLen <= 0 ? 0 : prefixLen >= 32 ? 0xFFFFFFFFu : 0xFFFFFFFFu << (32 - prefixLen);

    private static IPAddress FromUInt(uint v) => new([(byte)((v >> 24) & 0xFF), (byte)((v >> 16) & 0xFF),
                                                     (byte)((v >> 8) & 0xFF), (byte)(v & 0xFF)]);
}
