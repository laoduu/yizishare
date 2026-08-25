using System.Net;
using YiziShare.Core;

namespace YiziShare.Tests;

public class SubnetSelectionTests
{
    [Fact]
    public void IsApipa_DetectsLinkLocalAddress()
    {
        Assert.True(IpRangeUtil.IsApipa(IPAddress.Parse("169.254.10.20")));
    }

    [Fact]
    public void IsApipa_RejectsNormalAddress()
    {
        Assert.False(IpRangeUtil.IsApipa(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void HasIpv4Gateway_Empty_ReturnsFalse()
    {
        Assert.False(IpRangeUtil.HasIpv4Gateway(Array.Empty<IPAddress?>()));
    }

    [Fact]
    public void HasIpv4Gateway_NormalIpv4_ReturnsTrue()
    {
        Assert.True(IpRangeUtil.HasIpv4Gateway([IPAddress.Parse("192.168.1.1")]));
    }

    [Fact]
    public void HasIpv4Gateway_OnlyApipa_ReturnsFalse()
    {
        Assert.False(IpRangeUtil.HasIpv4Gateway([IPAddress.Parse("169.254.1.1")]));
    }

    [Fact]
    public void HasIpv4Gateway_NullEntries_Ignored()
    {
        Assert.True(IpRangeUtil.HasIpv4Gateway([null, IPAddress.Parse("192.168.1.1"), null]));
    }
}
