using YiziShare.Core;

namespace YiziShare.Tests;

public class HistoryStoreTests : IDisposable
{
    public HistoryStoreTests()
    {
        HistoryStore.Reset();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        var historyPath = Path.Combine(appFolder, "history.json");
        if (File.Exists(historyPath))
            File.Delete(historyPath);
    }

    public void Dispose()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        var historyPath = Path.Combine(appFolder, "history.json");
        if (File.Exists(historyPath))
            File.Delete(historyPath);
    }

    [Fact]
    public async Task RecordConnectionAsync_NewEntry_AddsToList()
    {
        await HistoryStore.LoadAsync();
        await HistoryStore.RecordConnectionAsync("192.168.1.100", "TestDevice");

        var entry = HistoryStore.GetEntry("192.168.1.100");
        Assert.NotNull(entry);
        Assert.Equal("TestDevice", entry.DeviceName);
        Assert.Equal(1, entry.ConnectCount);
    }

    [Fact]
    public async Task RecordConnectionAsync_ExistingEntry_IncrementsCount()
    {
        await HistoryStore.LoadAsync();
        await HistoryStore.RecordConnectionAsync("192.168.1.100", "TestDevice");
        await HistoryStore.RecordConnectionAsync("192.168.1.100", "TestDevice");

        var entry = HistoryStore.GetEntry("192.168.1.100");
        Assert.NotNull(entry);
        Assert.Equal(2, entry.ConnectCount);
    }

    [Fact]
    public async Task GetRecentConnections_ReturnsOrderedByLastSeen()
    {
        await HistoryStore.LoadAsync();
        await HistoryStore.RecordConnectionAsync("192.168.1.100", "Device1");
        await HistoryStore.RecordConnectionAsync("192.168.1.200", "Device2");

        var recent = HistoryStore.GetRecentConnections(10);
        Assert.Equal(2, recent.Count);
    }

    [Fact]
    public async Task GetRecentConnections_LimitsCount()
    {
        await HistoryStore.LoadAsync();
        for (int i = 0; i < 15; i++)
            await HistoryStore.RecordConnectionAsync($"192.168.1.{i}", $"Device{i}");

        var recent = HistoryStore.GetRecentConnections(5);
        Assert.Equal(5, recent.Count);
    }
}
