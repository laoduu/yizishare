using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace YiziShare.Core;

/// <summary>
/// 昵称持久化存储
/// </summary>
public static class NicknameStore
{
    private static readonly string StoragePath;
    private static Dictionary<string, string> _nicknames = new();
    private static bool _loaded;

    static NicknameStore()
    {
        // 存储在应用数据目录下
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        Directory.CreateDirectory(appFolder);
        StoragePath = Path.Combine(appFolder, "nicknames.json");
    }

    /// <summary>
    /// 加载昵称数据
    /// </summary>
    public static async Task LoadAsync()
    {
        if (_loaded) return;
        
        try
        {
            if (File.Exists(StoragePath))
            {
                var json = await File.ReadAllTextAsync(StoragePath);
                _nicknames = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
            }
        }
        catch
        {
            _nicknames = new();
        }
        
        _loaded = true;
    }

    /// <summary>
    /// 获取设备昵称
    /// </summary>
    public static string? GetNickname(string ipAddress)
    {
        return _nicknames.TryGetValue(ipAddress, out var nickname) ? nickname : null;
    }

    /// <summary>
    /// 设置设备昵称
    /// </summary>
    public static async Task SetNickname(string ipAddress, string nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
            _nicknames.Remove(ipAddress);
        else
            _nicknames[ipAddress] = nickname;
        
        await SaveAsync();
    }

    /// <summary>
    /// 保存到文件
    /// </summary>
    private static async Task SaveAsync()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(_nicknames, options);
            await File.WriteAllTextAsync(StoragePath, json);
        }
        catch { }
    }
}
