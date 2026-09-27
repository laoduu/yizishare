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
