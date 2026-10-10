using System.Text.Json;

namespace YiziShare.Core;

/// <summary>常用设备（收藏）持久化：按 IP 存储</summary>
public static class FavoritesStore
{
    private static readonly string StoragePath;
    private static HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    static FavoritesStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = System.IO.Path.Combine(appData, "YiziShare");
        Directory.CreateDirectory(appFolder);
        StoragePath = System.IO.Path.Combine(appFolder, "favorites.json");
    }

    public static async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            if (File.Exists(StoragePath))
            {
                var json = await File.ReadAllTextAsync(StoragePath);
                _favorites = new HashSet<string>(
                    JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        _loaded = true;
    }

    /// <summary>测试隔离用：清空内存状态</summary>
    public static void Reset()
    {
        _favorites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _loaded = false;
    }

    public static bool IsFavorite(string ipAddress) => _favorites.Contains(ipAddress);

    public static List<string> GetAll() => _favorites.ToList();

    public static async Task ToggleAsync(string ipAddress)
    {
        if (!_favorites.Remove(ipAddress))
            _favorites.Add(ipAddress);
        await SaveAsync();
    }

    private static async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_favorites.ToList(), options);
            await File.WriteAllTextAsync(StoragePath, json);
        }
        catch { }
    }
}
