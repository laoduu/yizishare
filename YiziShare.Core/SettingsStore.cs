using System.Text.Json;

namespace YiziShare.Core;

public static class SettingsStore
{
    private static readonly string SettingsPath;

    static SettingsStore()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "YiziShare");
        Directory.CreateDirectory(appFolder);
        SettingsPath = Path.Combine(appFolder, "settings.json");
    }

    public static bool GetAutoStart()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                return settings?.AutoStart ?? false;
            }
        }
        catch { }
        return false;
    }

    public static void SetAutoStart(bool enabled)
    {
        var settings = LoadSettings();
        settings.AutoStart = enabled;
        SaveSettings(settings);

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enabled)
                {
                    // 必须指向主程序 exe：Assembly.Location 在单文件打包下返回空字符串
                    // 会让开机自启写入残缺路径而静默失效；且本方法位于 Core，
                    // GetExecutingAssembly() 返回的是 Core.dll 而非主程序。
                    var exePath = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(exePath))
                        key.SetValue("YiziShare", $"\"{exePath}\"");
                }
                else
                {
                    key.DeleteValue("YiziShare", false);
                }
            }
        }
        catch { }
    }

    public static bool GetRememberUsername()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                return settings?.RememberUsername ?? false;
            }
        }
        catch { }
        return false;
    }

    public static void SetRememberUsername(bool enabled)
    {
        var settings = LoadSettings();
        settings.RememberUsername = enabled;
        SaveSettings(settings);
    }

    public static string? GetSelectedAdapter()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                return settings?.SelectedAdapter;
            }
        }
        catch { }
        return null;
    }

    public static void SetSelectedAdapter(string adapter)
    {
        var settings = LoadSettings();
        settings.SelectedAdapter = adapter;
        SaveSettings(settings);
    }

    /// <summary>收到新消息时是否播放提示音（默认开）</summary>
    public static bool GetChatSoundEnabled()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                return settings?.ChatSoundEnabled ?? true;
            }
        }
        catch { }
        return true;
    }

    public static void SetChatSoundEnabled(bool enabled)
    {
        var settings = LoadSettings();
        settings.ChatSoundEnabled = enabled;
        SaveSettings(settings);
    }

    /// <summary>共享文件浏览默认使用图标模式（false = 列表模式，默认）</summary>
    public static bool GetFileBrowseIconMode()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                return settings?.FileBrowseIconMode ?? false;
            }
        }
        catch { }
        return false;
    }

    public static void SetFileBrowseIconMode(bool iconMode)
    {
        var settings = LoadSettings();
        settings.FileBrowseIconMode = iconMode;
        SaveSettings(settings);
    }

    /// <summary>我的昵称（广播给局域网对端显示）；未设置时对端显示本机机器名。<br/>
    /// 双写备份：主存在 Local 的 settings.json，备份在 Roaming 的 my-nickname.txt
    /// （与 NodeId 同策略，部分卸载器只清 Local，备份仍在即可恢复昵称）。</summary>
    private static string NicknameBackupPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "YiziShare", "my-nickname.txt");

    public static string GetMyNickname()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                var nickname = settings?.MyNickname ?? "";
                if (!string.IsNullOrWhiteSpace(nickname))
                    return nickname;
            }

            // 主值丢失（清 Local / 手动删字段）→ 从 Roaming 备份回读并补写主文件
            if (File.Exists(NicknameBackupPath))
            {
                var backup = File.ReadAllText(NicknameBackupPath).Trim();
                if (!string.IsNullOrWhiteSpace(backup))
                {
                    var settings = LoadSettings();
                    settings.MyNickname = backup;
                    SaveSettings(settings);
                    return backup;
                }
            }
        }
        catch { }
        return "";
    }

    public static void SetMyNickname(string nickname)
    {
        var trimmed = nickname?.Trim() ?? "";

        var settings = LoadSettings();
        settings.MyNickname = trimmed;
        SaveSettings(settings);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(NicknameBackupPath)!);
            File.WriteAllText(NicknameBackupPath, trimmed);
        }
        catch { }
    }

    /// <summary>聊天文件接收目录；空 = 默认 %LOCALAPPDATA%\YiziShare\Received</summary>
    public static string DefaultReceivedDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YiziShare", "Received");

    public static string GetReceivedDirectory()
    {
        var custom = "";
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<SettingsData>(json);
                custom = settings?.ReceivedDirectory?.Trim() ?? "";
            }
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(custom))
        {
            try { Directory.CreateDirectory(custom); return custom; } catch { /* 目录建不了（盘不在了）回默认 */ }
        }

        try { Directory.CreateDirectory(DefaultReceivedDirectory); } catch { }
        return DefaultReceivedDirectory;
    }

    public static void SetReceivedDirectory(string directory)
    {
        var settings = LoadSettings();
        settings.ReceivedDirectory = directory?.Trim() ?? "";
        SaveSettings(settings);
    }

    private static SettingsData LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
        }
        catch { }
        return new SettingsData();
    }

    private static void SaveSettings(SettingsData settings)
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(settings, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private class SettingsData
    {
        public bool AutoStart { get; set; }
        public bool RememberUsername { get; set; }
        public string? SelectedAdapter { get; set; }
        public bool ChatSoundEnabled { get; set; } = true;
        public bool FileBrowseIconMode { get; set; }
        public string MyNickname { get; set; } = "";
        public string? ReceivedDirectory { get; set; }
    }
}
