using YiziShare.Core;

namespace YiziShare.Tests;

public class WindowsCredentialManagerTests
{
    [Theory]
    [InlineData("192.168.1.5", "192.168.1.5\\alice", "alice")]
    [InlineData("192.168.1.5", ".\\alice", "alice")]
    [InlineData("192.168.1.5", "alice", "alice")]
    [InlineData("192.168.1.5", "  ALICE  ", "ALICE")]
    [InlineData("192.168.1.5", "MYDOMAIN\\bob", "MYDOMAIN\\bob")]
    [InlineData("NAS", "NAS\\media", "media")]
    [InlineData("NAS", "\\leading", "\\leading")]
    [InlineData("NAS", "trailing\\", "trailing\\")]
    public void NormalizeStoredUserName_StripsTargetDomainOnly(string target, string input, string expected)
    {
        Assert.Equal(expected, WindowsCredentialManager.NormalizeStoredUserName(target, input));
    }
}
