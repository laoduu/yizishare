using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace YiziShare.App;

/// <summary>
/// 微信风格的新消息横幅：屏幕右下角滑入、悬停暂停自动关闭、点击直达会话。<br/>
/// 替代托盘气泡——系统气泡自带 Windows 提示音，会与本应用的提示音重叠，且样式不可控。<br/>
/// 全局单例：连续消息只刷新内容并重置计时，不叠出层叠窗口。
/// </summary>
public partial class NotificationBanner : Window
{
    private static NotificationBanner? _current;

    private readonly DispatcherTimer _closeTimer;
    private Action? _onActivate;

    private NotificationBanner()
    {
        InitializeComponent();

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _closeTimer.Tick += (_, _) => CloseAnimated();

        // WorkArea 已排除任务栏，且是 WPF 设备无关单位，DPI 缩放下位置正确
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 8;
        Top = work.Bottom - Height - 8;
    }

    /// <summary>显示（或刷新）一条新消息横幅</summary>
    /// <param name="peerName">发送方显示名</param>
    /// <param name="preview">消息预览</param>
    /// <param name="onActivate">点击横幅时的动作（打开对应会话）</param>
    /// <param name="avatarGlyph">自定义头像字符（null = 昵称首字符）</param>
    /// <param name="useLogoAvatar">true 时头像显示应用 logo（系统提醒类横幅）</param>
    public static void ShowBanner(string peerName, string preview, Action onActivate,
        string? avatarGlyph = null, bool useLogoAvatar = false)
    {
        if (_current == null || !_current.IsLoaded)
            _current = new NotificationBanner();

        _current._onActivate = onActivate;
        _current.SenderName.Text = peerName;
        _current.MessagePreview.Text = preview;

        if (useLogoAvatar)
        {
            _current.AvatarChar.Visibility = Visibility.Collapsed;
            _current.AvatarLogo.Visibility = Visibility.Visible;
        }
        else
        {
            _current.AvatarChar.Visibility = Visibility.Visible;
            _current.AvatarLogo.Visibility = Visibility.Collapsed;
            _current.AvatarChar.Text = avatarGlyph ?? FirstChar(peerName);
        }

        _current.ShowAnimated();
    }

    /// <summary>系统信息类横幅（扫描完成、最小化提示等）：头像用应用 logo，点击无动作仅关闭</summary>
    public static void ShowInfo(string title, string message)
        => ShowBanner(title, message, () => { }, useLogoAvatar: true);

    private static string FirstChar(string name)
    {
        name = name.Trim();
        return name.Length == 0 ? "?" : name[..1].ToUpperInvariant();
    }

    private void ShowAnimated()
    {
        // 未显示过才播滑入；已可见时只刷新内容并重置计时（对应连续消息合并）
        if (!IsVisible)
        {
            Slide.X = 380;
            BeginAnimation(OpacityProperty, FadeAnimation(0, 1, 220));
            Slide.BeginAnimation(TranslateTransform.XProperty, SlideAnimation(380, 0, 260, EasingMode.EaseOut));
            Show();
        }

        RestartCloseTimer();
    }

    private void RestartCloseTimer(int seconds = 5)
    {
        _closeTimer.Stop();
        _closeTimer.Interval = TimeSpan.FromSeconds(seconds);
        _closeTimer.Start();
    }

    private void CloseAnimated()
    {
        _closeTimer.Stop();
        BeginAnimation(OpacityProperty, FadeAnimation(Opacity, 0, 180));
        var slide = SlideAnimation(Slide.X, 380, 200, EasingMode.EaseIn);
        slide.Completed += (_, _) => Hide();
        Slide.BeginAnimation(TranslateTransform.XProperty, slide);
    }

    private static DoubleAnimation FadeAnimation(double from, double to, int ms) => new(from, to, TimeSpan.FromMilliseconds(ms))
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
    };

    private static DoubleAnimation SlideAnimation(double from, double to, int ms, EasingMode mode) => new(from, to, TimeSpan.FromMilliseconds(ms))
    {
        EasingFunction = new CubicEase { EasingMode = mode }
    };

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        var activate = _onActivate;
        CloseAnimated();
        activate?.Invoke();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        CloseAnimated();
    }

    /// <summary>悬停时不自动消失（微信同款行为），移开重新计时</summary>
    private void Banner_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _closeTimer.Stop();
    }

    private void Banner_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        RestartCloseTimer(3);
    }
}
