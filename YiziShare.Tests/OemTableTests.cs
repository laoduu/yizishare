using YiziShare.Core;

namespace YiziShare.Tests;

public class OemTableTests
{
    [Theory]
    [InlineData("DC:A6:32:11:22:33")] // 回归：原表键带冒号导致永不匹配
    [InlineData("DCA632111122")]
    public void GetManufacturer_RaspberryPiMac_MatchesRegardlessOfColons(string mac)
    {
        Assert.Equal("Raspberry Pi", NetworkScanner.GetManufacturer(mac));
    }

    [Fact]
    public void GetManufacturer_LowercaseMac_StillMatches()
    {
        Assert.Equal("VMware", NetworkScanner.GetManufacturer("00:0c:29:aa:bb:cc"));
    }

    [Fact]
    public void GetManufacturer_EmptyMac_ReturnsEmptyString()
    {
        Assert.Equal("", NetworkScanner.GetManufacturer(""));
    }

    [Fact]
    public void GetManufacturer_ShortMac_DoesNotThrow()
    {
        Assert.Equal("", NetworkScanner.GetManufacturer("AB:CD"));
    }

    [Fact]
    public void GetManufacturer_UnknownPrefix_ReturnsEmptyString()
    {
        Assert.Equal("", NetworkScanner.GetManufacturer("001122334455"));
    }

    [Theory]
    [InlineData("001132000000", "Synology")]
    [InlineData("245EBE000000", "QNAP")]
    [InlineData("00155D000000", "Microsoft Hyper-V")]
    public void GetManufacturer_NasAndVirtualizationOuis_AreRecognized(string mac, string expected)
    {
        Assert.Equal(expected, NetworkScanner.GetManufacturer(mac));
    }
}
