using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace YiziShare.App;

/// <summary>
/// 黑金主题色（代码拼 UI 用）。属性动态读取 Application 资源，
/// ThemeManager 切换 Surface 字典后立即生效；与 ThemeColors.xaml/Surface.*.xlsx 保持键一致。
/// </summary>
public static class ThemeColors
{
    // ===== 品牌金（不随主题变）=====
    public static Color AccentGold => ReadColor("AccentGoldColor");
    public static Color AccentGoldBright => ReadColor("AccentGoldBrightColor");

    // ===== 深藏蓝（次强调，不随主题变）=====
    public static Color NavyPrimary => ReadColor("NavyPrimaryColor");
    public static Color NavyHover => ReadColor("NavyHoverColor");

    // ===== 表面与文字（随主题切换）=====
    public static Color ListBg => ReadColor("ListBgColor");
    public static Color PanelBg => ReadColor("PanelBgColor");
    public static Color CardBg => ReadColor("CardBgColor");
    public static Color CardHover => ReadColor("CardHoverColor");
    public static Color CardBorder => ReadColor("CardBorderColor");
    public static Color InputBg => ReadColor("InputBgColor");
    public static Color SecondaryBtnBg => ReadColor("SecondaryBtnBgColor");
    public static Color TextPrimary => ReadColor("TextPrimaryColor");
    public static Color TextSecondary => ReadColor("TextSecondaryColor");
    public static Color TextTertiary => ReadColor("TextTertiaryColor");
    public static Color TextFaint => ReadColor("TextFaintColor");
    public static Color AccentGoldSoft => ReadColor("AccentGoldSoftColor");
    public static Color AccentGoldSofter => ReadColor("AccentGoldSofterColor");
    public static Color SemanticGreen => ReadColor("SemanticGreenColor");
    public static Color SemanticGreenBg => ReadColor("SemanticGreenBgColor");
    public static Color SemanticOrange => ReadColor("SemanticOrangeColor");
    public static Color SemanticOrangeBg => ReadColor("SemanticOrangeBgColor");
    public static Color SemanticRed => ReadColor("SemanticRedColor");
    public static Color DiagnosisBg => ReadColor("DiagnosisBgColor");
    public static Color DiagnosisBorder => ReadColor("DiagnosisBorderColor");

    // ===== 画刷 =====
    public static SolidColorBrush AccentGoldBrush => ReadBrush("AccentGoldBrush");
    public static SolidColorBrush NavyPrimaryBrush => ReadBrush("NavyPrimaryBrush");
    public static SolidColorBrush NavyHoverBrush => ReadBrush("NavyHoverBrush");
    public static SolidColorBrush ListBgBrush => ReadBrush("ListBgBrush");
    public static SolidColorBrush PanelBgBrush => ReadBrush("PanelBgBrush");
    public static SolidColorBrush CardBgBrush => ReadBrush("CardBgBrush");
    public static SolidColorBrush CardHoverBrush => ReadBrush("CardHoverBrush");
    public static SolidColorBrush CardBorderBrush => ReadBrush("CardBorderBrush");
    public static SolidColorBrush InputBgBrush => ReadBrush("InputBgBrush");
    public static SolidColorBrush SecondaryBtnBgBrush => ReadBrush("SecondaryBtnBgBrush");
    public static SolidColorBrush TextPrimaryBrush => ReadBrush("TextPrimaryBrush");
    public static SolidColorBrush TextSecondaryBrush => ReadBrush("TextSecondaryBrush");
    public static SolidColorBrush TextTertiaryBrush => ReadBrush("TextTertiaryBrush");
    public static SolidColorBrush TextFaintBrush => ReadBrush("TextFaintBrush");
    public static SolidColorBrush AccentGoldSoftBrush => ReadBrush("AccentGoldSoftBrush");
    public static SolidColorBrush AccentGoldSofterBrush => ReadBrush("AccentGoldSofterBrush");
    public static SolidColorBrush SemanticGreenBrush => ReadBrush("SemanticGreenBrush");
    public static SolidColorBrush SemanticGreenBgBrush => ReadBrush("SemanticGreenBgBrush");
    public static SolidColorBrush SemanticOrangeBrush => ReadBrush("SemanticOrangeBrush");
    public static SolidColorBrush SemanticOrangeBgBrush => ReadBrush("SemanticOrangeBgBrush");
    public static SolidColorBrush SemanticRedBrush => ReadBrush("SemanticRedBrush");
    public static SolidColorBrush DiagnosisBgBrush => ReadBrush("DiagnosisBgBrush");
    public static SolidColorBrush DiagnosisBorderBrush => ReadBrush("DiagnosisBorderBrush");

    private static Color ReadColor(string key)
    {
        try { return (Color)Application.Current.Resources[key]; }
        catch { return Colors.Gray; }
    }

    private static SolidColorBrush ReadBrush(string key)
    {
        try { return (SolidColorBrush)Application.Current.Resources[key]; }
        catch { return Brushes.Gray; }
    }
}
