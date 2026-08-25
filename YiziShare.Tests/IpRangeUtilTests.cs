using System.Net;
using YiziShare.Core;

namespace YiziShare.Tests;

public class IpRangeUtilTests
{
    [Fact]
    public void GenerateHostIps_Slash24_Returns254_Addresses()
    {
        var ips = IpRangeUtil.GenerateHostIps(IPAddress.Parse("192.168.1.0"), 24);
        Assert.Equal(254, ips.Count);
    }

    [Fact]
    public void GenerateHostIps_ExcludesNetworkAndBroadcastAddress()
    {
        var ips = IpRangeUtil.GenerateHostIps(IPAddress.Parse("192.168.1.0"), 24);
        Assert.Equal("192.168.1.1", ips[0].ToString());
        Assert.Equal("192.168.1.254", ips[^1].ToString());
        Assert.DoesNotContain(IPAddress.Parse("192.168.1.0"), ips);
        Assert.DoesNotContain(IPAddress.Parse("192.168.1.255"), ips);
    }

    [Fact]
    public void GenerateHostIps_NonNormalizedInput_StillCoversFullRange()
    {
        // 输入不是网络地址（主机位非零）时也应正确覆盖 .1~.254
        var ips = IpRangeUtil.GenerateHostIps(IPAddress.Parse("192.168.9.77"), 24);
        Assert.Equal(254, ips.Count);
        Assert.Equal("192.168.9.1", ips[0].ToString());
        Assert.Equal("192.168.9.254", ips[^1].ToString());
        Assert.Contains(IPAddress.Parse("192.168.9.200"), ips); // 回归：旧实现 Take(100) 漏掉的高段地址
    }

    [Fact]
    public void GenerateHostIps_Slash30_ReturnsTwoHosts()
    {
        var ips = IpRangeUtil.GenerateHostIps(IPAddress.Parse("10.0.0.0"), 30);
        Assert.Equal(2, ips.Count);
        Assert.Equal("10.0.0.1", ips[0].ToString());
        Assert.Equal("10.0.0.2", ips[1].ToString());
    }

    [Fact]
    public void ParseSubnet_WithPrefix_ParsesCorrectly()
    {
        var (network, prefix) = IpRangeUtil.ParseSubnet("192.168.5.0/24");
        Assert.Equal(IPAddress.Parse("192.168.5.0"), network);
        Assert.Equal(24, prefix);
    }

    [Fact]
    public void ParseSubnet_WithoutPrefix_DefaultsTo24()
    {
        var (_, prefix) = IpRangeUtil.ParseSubnet("192.168.5.0");
        Assert.Equal(24, prefix);
    }

    [Fact]
    public void SubnetOf_NormalIp_FormatsCidr()
    {
        Assert.Equal("192.168.1.0/24", IpRangeUtil.SubnetOf(IPAddress.Parse("192.168.1.37")));
    }
}
