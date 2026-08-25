using YiziShare.Core;

namespace YiziShare.Tests;

public class NetViewParserTests
{
    private const string Ip = "192.168.1.60";

    [Fact]
    public void ParsesChineseHeader_SampleOutput()
    {
        const string output = """
            \\192.168.1.60 的共享资源

            共享名  类型  使用为  注释

            -------------------------------------
            公共文件  Disk          家庭共享资料
            backup   Disk          备份用
            命令成功完成。

            """;
        var shares = new List<SharedFolder>();
        ShareFinder.ParseNetViewOutput(output, Ip, shares);

        Assert.Equal(2, shares.Count);
        Assert.Equal("公共文件", shares[0].ShareName);
        Assert.Equal($"\\\\{Ip}\\公共文件", shares[0].Path);
        Assert.Equal("家庭共享资料", shares[0].Description);
        Assert.Equal(Ip, shares[0].DeviceIp);
    }

    [Fact]
    public void ParsesEnglishHeader_SampleOutput()
    {
        // 回归：旧实现只认中文表头"共享名"，英文系统解析失败
        const string output = """

            Shared resources at \\192.168.1.60

            Share name  Type  Used as  Comment

            -------------------------------------
            Media       Disk           Movies
            Photos      Disk
            The command completed successfully.

            """;
        var shares = new List<SharedFolder>();
        ShareFinder.ParseNetViewOutput(output, Ip, shares);

        Assert.Equal(2, shares.Count);
        Assert.Equal("Media", shares[0].ShareName);
        Assert.Equal("Photos", shares[1].ShareName);
    }

    [Fact]
    public void ShareName_WithSpaces_KeptIntact()
    {
        // 回归：旧实现按单个空格 Split，含空格的共享名被拆碎
        const string output = """
            共享名  类型  使用为  注释

            -------------------------------------
            我的 共享 文件夹  Disk          带空格名称
            命令成功完成。

            """;
        var shares = new List<SharedFolder>();
        ShareFinder.ParseNetViewOutput(output, Ip, shares);

        var share = Assert.Single(shares);
        Assert.Equal("我的 共享 文件夹", share.ShareName);
    }

    [Fact]
    public void AdminShares_EndingWithDollar_AreFiltered()
    {
        const string output = """
            共享名  类型  使用为  注释

            -------------------------------------
            IPC$      Disk          远程 IPC
            C$        Disk          默认共享
            data      Disk
            命令成功完成。

            """;
        var shares = new List<SharedFolder>();
        ShareFinder.ParseNetViewOutput(output, Ip, shares);

        var share = Assert.Single(shares);
        Assert.Equal("data", share.ShareName);
    }
}
