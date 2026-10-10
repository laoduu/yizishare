using YiziShare.Core;

namespace YiziShare.Tests;

public class SettingsStoreTests : IDisposable
{
    public SettingsStoreTests()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        var settingsPath = Path.Combine(appFolder, "settings.json");
        if (File.Exists(settingsPath))
            File.Delete(settingsPath);
    }

    public void Dispose()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        var settingsPath = Path.Combine(appFolder, "settings.json");
        if (File.Exists(settingsPath))
            File.Delete(settingsPath);

        // 开机自启测试会写真实注册表，退出时还原，避免在开发机留下自启项
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.DeleteValue("YiziShare", false);
        }
        catch { }
    }

    [Fact]
    public void GetAutoStart_DefaultsToFalse()
    {
        Assert.False(SettingsStore.GetAutoStart());
    }

    [Fact]
    public void SetAutoStart_True_Persists()
    {
        SettingsStore.SetAutoStart(true);
        Assert.True(SettingsStore.GetAutoStart());
    }

    [Fact]
    public void SetAutoStart_False_Persists()
    {
        SettingsStore.SetAutoStart(true);
        SettingsStore.SetAutoStart(false);
        Assert.False(SettingsStore.GetAutoStart());
    }

    [Fact]
    public void SetAutoStart_True_WritesExecutableNotDll()
    {
        // 回归：曾用 Assembly.GetExecutingAssembly().Location 取路径，而本方法位于 Core，
        // 写入的是 YiziShare.Core.dll；单文件打包下更会返回空字符串。
        // .dll 没有可执行入口，开机自启会静默失效。
        SettingsStore.SetAutoStart(true);

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", false);
        var value = key?.GetValue("YiziShare") as string;

        Assert.False(string.IsNullOrEmpty(value), "开机自启未写入注册表");

        var path = value!.Trim('"');
        Assert.EndsWith(".exe", path, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(path), $"自启指向的文件不存在: {path}");
    }

    [Fact]
    public void SetAutoStart_False_RemovesRegistryEntry()
    {
        SettingsStore.SetAutoStart(true);
        SettingsStore.SetAutoStart(false);

        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run", false);
        Assert.Null(key?.GetValue("YiziShare"));
    }

    [Fact]
    public void GetRememberUsername_DefaultsToFalse()
    {
        Assert.False(SettingsStore.GetRememberUsername());
    }

    [Fact]
    public void SetRememberUsername_True_Persists()
    {
        SettingsStore.SetRememberUsername(true);
        Assert.True(SettingsStore.GetRememberUsername());
    }

    [Fact]
    public void GetSelectedAdapter_DefaultsToNull()
    {
        Assert.Null(SettingsStore.GetSelectedAdapter());
    }

    [Fact]
    public void SetSelectedAdapter_Persists()
    {
        SettingsStore.SetSelectedAdapter("Ethernet (192.168.1.100)");
        Assert.Equal("Ethernet (192.168.1.100)", SettingsStore.GetSelectedAdapter());
    }
}
