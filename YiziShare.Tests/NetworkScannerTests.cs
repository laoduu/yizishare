using YiziShare.Core;

namespace YiziShare.Tests;

public class NetworkScannerTests
{
    [Fact]
    public void GetLocalSubnet_ReturnsValidSubnet()
    {
        var scanner = new NetworkScanner();
        var subnet = scanner.GetLocalSubnet();

        Assert.False(string.IsNullOrEmpty(subnet));
        Assert.Contains("/", subnet);
    }

    [Fact]
    public void GetManufacturer_KnownOui_ReturnsManufacturer()
    {
        var manufacturer = NetworkScanner.GetManufacturer("B8:27:EB:12:34:56");
        Assert.Equal("Raspberry Pi", manufacturer);
    }

    [Fact]
    public void GetManufacturer_UnknownOui_ReturnsEmpty()
    {
        var manufacturer = NetworkScanner.GetManufacturer("AA:BB:CC:DD:EE:FF");
        Assert.Equal("", manufacturer);
    }

    [Fact]
    public void GetManufacturer_EmptyMac_ReturnsEmpty()
    {
        var manufacturer = NetworkScanner.GetManufacturer("");
        Assert.Equal("", manufacturer);
    }

    [Fact]
    public void IsUsableIpv4Nic_Loopback_ReturnsFalse()
    {
        var loopback = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(ni => ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback);

        if (loopback != null)
            Assert.False(NetworkScanner.IsUsableIpv4Nic(loopback));
    }

    [Fact]
    public void GetUsableNetworkAdapters_ReturnsList()
    {
        var adapters = NetworkScanner.GetUsableNetworkAdapters();
        Assert.NotNull(adapters);
    }
}
