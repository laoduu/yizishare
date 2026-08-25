using System.IO;
using System.Windows;
using Application = System.Windows.Application;

namespace YiziShare.App;

/// <summary>
/// 明暗主题切换：替换 Application 资源中的 Surface 字典（品牌黑金层两种主题共用）。
/// XAML 侧表面色一律 DynamicResource 引用；C# 侧经 ThemeColors 动态读取。
/// </summary>
public static class ThemeManager
{
    private static readonly string PrefPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YiziShare", "theme.txt");

    public static bool IsDark { get; private set; }

    /// <summary>启动时恢复上次的主题偏好（须在主窗口渲染前调用）</summary>
    public static void Load()
    {
        bool dark = false;
        try
        {
            if (File.Exists(PrefPath))
                dark = File.ReadAllText(PrefPath).Trim() == "dark";
        }
        catch { }
        Apply(dark);
    }

    public static void Toggle() => Apply(!IsDark);

    public static void Apply(bool dark)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app == null) return;

        var merged = app.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
        {
            if (merged[i].Source?.OriginalString.Contains("Surface.") == true)
                merged.RemoveAt(i);
        }
        merged.Add(new ResourceDictionary
        {
            Source = new Uri(dark ? "Surface.Dark.xaml" : "Surface.Light.xaml", UriKind.Relative)
        });

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PrefPath)!);
            File.WriteAllText(PrefPath, dark ? "dark" : "light");
        }
        catch { }
    }
}
