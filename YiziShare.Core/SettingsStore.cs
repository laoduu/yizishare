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
                    var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
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
    }
}
