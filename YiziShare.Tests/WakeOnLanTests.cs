using YiziShare.Core;

namespace YiziShare.Tests;

public class WakeOnLanTests
{
    [Fact]
    public void BuildMagicPacket_StandardFormat()
    {
        byte[] packet = WakeOnLan.BuildMagicPacket("B8:27:EB:12:34:56");

        Assert.Equal(102, packet.Length);
        // 前 6 字节全 FF
        for (int i = 0; i < 6; i++)
            Assert.Equal(0xFF, packet[i]);
        // 后 96 字节为 MAC 重复 16 次
        byte[] mac = { 0xB8, 0x27, 0xEB, 0x12, 0x34, 0x56 };
        for (int rep = 0; rep < 16; rep++)
        {
            for (int i = 0; i < 6; i++)
                Assert.Equal(mac[i], packet[6 + rep * 6 + i]);
        }
    }

    [Theory]
    [InlineData("B8-27-EB-12-34-56")]
    [InlineData("b827eb123456")]
    [InlineData("B8:27:EB:12:34:56")]
    [InlineData(" b8:27:eb:12:34:56 ")]
    public void BuildMagicPacket_AcceptsCommonFormats(string mac)
    {
        byte[] packet = WakeOnLan.BuildMagicPacket(mac);
        Assert.Equal(102, packet.Length);
        Assert.Equal(0xB8, packet[6]);
        Assert.Equal(0x56, packet[11]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-mac")]
    [InlineData("B8:27:EB:12:34")]
    [InlineData("ZZ:27:EB:12:34:56")]
    public void BuildMagicPacket_RejectsInvalidMacs(string mac)
    {
        Assert.Throws<ArgumentException>(() => WakeOnLan.BuildMagicPacket(mac));
    }
}
