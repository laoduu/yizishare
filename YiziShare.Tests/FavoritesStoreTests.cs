using YiziShare.Core;

namespace YiziShare.Tests;

public class FavoritesStoreTests : IDisposable
{
    public FavoritesStoreTests()
    {
        FavoritesStore.Reset();
    }

    public void Dispose()
    {
        FavoritesStore.Reset();
    }

    [Fact]
    public async Task Toggle_AddsAndRemoves()
    {
        await FavoritesStore.ToggleAsync("192.168.1.10");
        Assert.True(FavoritesStore.IsFavorite("192.168.1.10"));

        await FavoritesStore.ToggleAsync("192.168.1.10");
        Assert.False(FavoritesStore.IsFavorite("192.168.1.10"));
    }

    [Fact]
    public void IsFavorite_NotLoaded_ReturnsFalse()
    {
        // Reset 后未 Load，不应报错，返回 false
        Assert.False(FavoritesStore.IsFavorite("10.0.0.1"));
    }

    [Fact]
    public void GetAll_ReturnsCurrentFavorites()
    {
        // 通过 Toggle 写入（会尝试持久化到用户目录，失败被吞掉，不影响内存断言）
        _ = FavoritesStore.ToggleAsync("192.168.1.20");
        _ = FavoritesStore.ToggleAsync("192.168.1.30");
        _ = FavoritesStore.ToggleAsync("192.168.1.20"); // 再 toggle 一次移除

        var all = FavoritesStore.GetAll();
        Assert.Contains("192.168.1.30", all);
        Assert.DoesNotContain("192.168.1.20", all);
    }
}
