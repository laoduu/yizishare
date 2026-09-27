using System.Text.Json;

namespace YiziShare.Core;

public class HistoryEntry
{
    public string IpAddress { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public int ConnectCount { get; set; }
}

public static class HistoryStore
{
    private static readonly string StoragePath;
    private static List<HistoryEntry> _entries = new();
    private static bool _loaded;

    static HistoryStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        Directory.CreateDirectory(appFolder);
        StoragePath = Path.Combine(appFolder, "history.json");
    }

    public static async Task LoadAsync()
    {
        if (_loaded) return;

        try
        {
            if (File.Exists(StoragePath))
            {
                var json = await File.ReadAllTextAsync(StoragePath);
                _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json) ?? new List<HistoryEntry>();
            }
        }
        catch
        {
            _entries = new List<HistoryEntry>();
        }

        _loaded = true;
    }

    public static void Reset()
    {
        _entries = new List<HistoryEntry>();
        _loaded = false;
    }

    public static async Task RecordConnectionAsync(string ipAddress, string deviceName)
    {
        var entry = _entries.FirstOrDefault(e => e.IpAddress == ipAddress);
        if (entry == null)
        {
            entry = new HistoryEntry
            {
                IpAddress = ipAddress,
                DeviceName = deviceName,
                LastSeen = DateTime.Now,
                ConnectCount = 1
            };
            _entries.Add(entry);
        }
        else
        {
            entry.DeviceName = deviceName;
            entry.LastSeen = DateTime.Now;
            entry.ConnectCount++;
        }

        await SaveAsync();
    }

    public static List<HistoryEntry> GetRecentConnections(int count = 10)
    {
        return _entries
            .OrderByDescending(e => e.LastSeen)
            .Take(count)
            .ToList();
    }

    public static HistoryEntry? GetEntry(string ipAddress)
    {
        return _entries.FirstOrDefault(e => e.IpAddress == ipAddress);
    }

    private static async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_entries, options);
            await File.WriteAllTextAsync(StoragePath, json);
        }
        catch { }
    }
}
