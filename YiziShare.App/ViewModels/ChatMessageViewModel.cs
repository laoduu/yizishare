using System.Collections.Concurrent;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YiziShare.Core;

namespace YiziShare.App.ViewModels;

/// <summary>聊天气泡项</summary>
public class ChatMessageViewModel : ViewModelBase
{
    private static readonly ConcurrentDictionary<string, BitmapImage> ImageCache = new();

    private MessageState _state;
    private bool _showDivider;

    public ChatMessageViewModel(string msgId, bool isMine, string text, DateTime timestamp, MessageState state,
        bool isImage = false, string attachmentPath = "")
    {
        MsgId = msgId;
        IsMine = isMine;
        Text = text;
        Timestamp = timestamp;
        _state = state;
        IsImage = isImage;
        AttachmentPath = attachmentPath;
        ShellIcon = HasAttachment && !isImage
            ? Services.FileTypeIconCache.GetIconImage(attachmentPath)
            : null;
        RichText = BuildRichText(text);
    }

    /// <summary>按码点查 Twemoji 彩色图（编入程序集，缺失返回 null 回退文字）</summary>
    private static BitmapImage? GetEmojiImage(string emoji)
    {
        var name = Services.EmojiMap.Lookup(emoji);
        if (name == null) return null;

        return ImageCache.GetOrAdd(name, key =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri($"pack://application:,,,/Assets/Emoji/{key}.png");
            image.EndInit();
            image.Freeze();
            return image;
        });
    }

    /// <summary>
    /// 气泡富文本：表情以彩色图片内联（WPF 文字栈渲染 emoji 是黑白的，只能走图片），
    /// 其余文字原样输出；无表情时退化为普通文本块。
    /// </summary>
    public TextBlock RichText { get; }

    private static TextBlock BuildRichText(string text)
    {
        var block = new TextBlock
        {
            FontSize = 12,
            TextWrapping = System.Windows.TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["TextPrimaryBrush"]
        };

        int last = 0;
        foreach (System.Text.RegularExpressions.Match match in Services.EmojiMap.Regex.Matches(text))
        {
            if (match.Index > last)
                block.Inlines.Add(new System.Windows.Documents.Run(text[last..match.Index]));

            var image = GetEmojiImage(match.Value);
            if (image != null)
            {
                var emojiElement = new System.Windows.Controls.Image { Source = image, Width = 40, Height = 40 };
                block.Inlines.Add(new System.Windows.Documents.InlineUIContainer(emojiElement)
                {
                    BaselineAlignment = System.Windows.BaselineAlignment.Center
                });
            }
            else
            {
                block.Inlines.Add(new System.Windows.Documents.Run(match.Value));
            }

            last = match.Index + match.Length;
        }

        if (last < text.Length)
            block.Inlines.Add(new System.Windows.Documents.Run(text[last..]));

        return block;
    }

    public string MsgId { get; }
    public bool IsMine { get; }
    public string Text { get; }
    public DateTime Timestamp { get; set; }

    /// <summary>是否图片消息（气泡内直接显示图片）</summary>
    public bool IsImage { get; }

    /// <summary>附件本地路径（图片消息 = 可显示的图片；文件消息 = 原文件/落盘文件）</summary>
    public string AttachmentPath { get; } = "";

    /// <summary>是否附件消息（图片或文件）</summary>
    public bool HasAttachment => !string.IsNullOrEmpty(AttachmentPath);

    /// <summary>是否文件消息（非图片的附件）</summary>
    public bool IsFile => HasAttachment && !IsImage;

    /// <summary>是否纯文本消息（文本 + 内联彩色表情）</summary>
    public bool IsText => !HasAttachment;

    /// <summary>文件消息的 Shell 类型图标（资源管理器同源）；提取失败回退矢量</summary>
    public System.Windows.Media.ImageSource? ShellIcon { get; }
    public bool HasShellIcon => ShellIcon != null;

    public string TimeText => Timestamp.ToString("HH:mm");

    /// <summary>自己发送的消息才有投递状态标记</summary>
    public string StateText
    {
        get
        {
            if (!IsMine) return "";

            return State switch
            {
                // 待发送：对方离线，等其上线后自动补发
                MessageState.Queued => "⏳ 待发送",
                MessageState.Sending => "⏳",
                MessageState.Sent => "✓",
                MessageState.Delivered => "✓✓",
                MessageState.Read => "✓✓",
                MessageState.Failed => "❌",
                _ => ""
            };
        }
    }

    public bool IsFailed => IsMine && State == MessageState.Failed;

    /// <summary>对方离线期间积压的消息，可点击立即尝试重发</summary>
    public bool IsQueued => IsMine && State == MessageState.Queued;

    /// <summary>需要提示"点击重试"的状态（发送失败或积压未发）</summary>
    public bool IsRetryable => IsFailed || IsQueued;

    public bool ShowDivider
    {
        get => _showDivider;
        set => SetProperty(ref _showDivider, value);
    }

    public string TimeDividerText => Timestamp.ToString("HH:mm");

    public MessageState State
    {
        get => _state;
        set
        {
            if (!SetProperty(ref _state, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsFailed));
        }
    }

    /// <summary>由集合在追加时调用，判断是否需要时间分隔线（间隔超过 5 分钟）</summary>
    public bool NeedsDivider(DateTime? previousTimestamp)
    {
        if (previousTimestamp == null) return true;
        return Timestamp - previousTimestamp.Value > TimeSpan.FromMinutes(5);
    }
}
