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
        bool isAttachment = false, bool isImage = false, string attachmentPath = "")
    {
        MsgId = msgId;
        IsMine = isMine;
        Text = text;
        Timestamp = timestamp;
        _state = state;
        IsAttachment = isAttachment;
        IsImage = isImage;
        AttachmentPath = attachmentPath;
        // Shell 图标按扩展名提取（SHGFI_USEFILEATTRIBUTES 只读名字不碰磁盘）：
        // 文件消息用展示文本（=文件名，接收中取自 FileOffer），完成后 CompleteAttachment 按真实路径刷新；
        // 图片消息无需类型图标（气泡直接显示图片）
        ShellIcon = IsAttachment && !IsImage && !string.IsNullOrWhiteSpace(text)
            ? Services.FileTypeIconCache.GetIconImage(text)
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

    /// <summary>是否附件类消息（图片或文件；接收中大文件路径尚为空但已算附件）</summary>
    public bool IsAttachment { get; }

    /// <summary>是否图片消息（气泡内直接显示图片；大文件接收完成时按扩展名确定）</summary>
    public bool IsImage { get; private set; }

    /// <summary>附件本地路径（图片消息 = 可显示的图片；文件消息 = 原文件/落盘文件）；接收中为空，完成时填充</summary>
    public string AttachmentPath { get; private set; } = "";

    /// <summary>失败原因（取消/中断时的状态栏文案）；普通失败为空显示 ❌</summary>
    private string _failureReason = "";

    public string FailureReason
    {
        get => _failureReason;
        set
        {
            if (!SetProperty(ref _failureReason, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(ShowStateText));
        }
    }

    /// <summary>是否附件消息（图片或文件）</summary>
    public bool HasAttachment => !string.IsNullOrEmpty(AttachmentPath);

    /// <summary>是否文件消息（非图片的附件）</summary>
    public bool IsFile => IsAttachment && !IsImage;

    /// <summary>
    /// 文件卡片副标题：随传输阶段变化，指导用户当下能做什么。<br/>
    /// 待接收/传送中的卡片不能双击打开，写死"双击打开 · 右键更多"会误导。
    /// </summary>
    public string FileCardHint => !IsFile
        ? ""
        : AwaitingAcceptance ? "文件 · 请在卡片上选择接收或拒绝"
        : State == MessageState.Failed ? "文件 · 右键可另存为"
        : IsMine && IsLargeFileAwaitingAcceptance ? "文件 · 已发出，等待对方接收 · 右键可取消"
        : IsTransferring ? "文件 · 传输中 · 右键可取消"
        : HasAttachment ? "文件 · 双击打开 · 右键更多"
        : "文件 · 等待对方处理";

    /// <summary>是否纯文本消息（文本 + 内联彩色表情）</summary>
    public bool IsText => !IsAttachment;

    /// <summary>是否等待用户确认接收（大文件 FileOffer 到达后、用户选择前）</summary>
    private bool _awaitingAcceptance;
    public bool AwaitingAcceptance
    {
        get => _awaitingAcceptance;
        set
        {
            if (!SetProperty(ref _awaitingAcceptance, value)) return;
            OnPropertyChanged(nameof(IsTransferring));
            OnPropertyChanged(nameof(ShowTransferProgress));
            OnPropertyChanged(nameof(FileCardHint));
        }
    }

    /// <summary>是否显示传输进度条（附件传输中且已开始拉取）</summary>
    public bool ShowTransferProgress => IsFile && IsTransferring;

    /// <summary>
    /// 是否走流式大文件通道（>5MB，控制/数据通道分离）。<br/>
    /// 用于区分「内联附件的瞬时 ⏳」与「大文件提议的等待/传输阶段」——两者 State 都是 Sending，但语义不同。
    /// </summary>
    private bool _isLargeFileOffer;
    public bool IsLargeFileOffer
    {
        get => _isLargeFileOffer;
        set
        {
            if (!SetProperty(ref _isLargeFileOffer, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(FileCardHint));
            OnPropertyChanged(nameof(ShowTransferProgress));
        }
    }

    /// <summary>大文件提议是否已开始实际传输（对端确认并建立数据连接后，进度 &gt; 0）</summary>
    public bool IsLargeFileAwaitingAcceptance => IsLargeFileOffer && _progressBytes <= 0;

    /// <summary>是否显示投递状态：发送方总是显示；接收方仅在附件失败/被取消时显示（如「对方已取消传送」）</summary>
    public bool ShowStateText => IsMine || (IsFile && State == MessageState.Failed);

    /// <summary>文件消息的 Shell 类型图标（资源管理器同源）；提取失败回退矢量</summary>
    public System.Windows.Media.ImageSource? ShellIcon { get; private set; }
    public bool HasShellIcon => ShellIcon != null;

    /// <summary>接收完成后由服务层填充附件路径与类型（大文件接收完成时调用）</summary>
    public void CompleteAttachment(string path, bool isImage)
    {
        AttachmentPath = path;
        IsImage = isImage;
        AwaitingAcceptance = false;
        ShellIcon = !isImage ? Services.FileTypeIconCache.GetIconImage(path) : null;
        // 无参 OnPropertyChanged 在普通方法里等于通知方法名（UI 不刷新），必须逐一显式通知
        OnPropertyChanged(nameof(AttachmentPath));
        OnPropertyChanged(nameof(IsImage));
        OnPropertyChanged(nameof(HasAttachment));
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsFile));
        OnPropertyChanged(nameof(ShellIcon));
        OnPropertyChanged(nameof(HasShellIcon));
        OnPropertyChanged(nameof(ShowTransferProgress));
        OnPropertyChanged(nameof(ShowStateText));
        OnPropertyChanged(nameof(FileCardHint));
    }

    #region 大文件传输进度

    private long _progressBytes;
    private long _progressTotal;

    /// <summary>进度百分比 0~100（未在传输中为 0）</summary>
    public double ProgressPercent => _progressTotal > 0
        ? Math.Min(100, _progressBytes * 100.0 / _progressTotal)
        : 0;

    /// <summary>附件正在传输中（含收发两个方向）</summary>
    public bool IsTransferring => _progressTotal > 0 && State == MessageState.Sending;

    /// <summary>服务层按分块进度调用（收发双向）</summary>
    public void ReportProgress(long bytes, long total)
    {
        _progressTotal = total;
        _progressBytes = bytes;
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(IsTransferring));
        OnPropertyChanged(nameof(ShowTransferProgress));
        OnPropertyChanged(nameof(FileCardHint));
        OnPropertyChanged(nameof(IsLargeFileAwaitingAcceptance));
        OnPropertyChanged(nameof(StateText));
    }

    #endregion

    public string TimeText => Timestamp.ToString("HH:mm");

    /// <summary>自己发送的消息才有投递状态标记</summary>
    public string StateText
    {
        get
        {
            // 接收方的大文件失败/被取消也要显示状态（如「对方已取消传送」）；其他状态仍是发送方专属
            if (!IsMine && State != MessageState.Failed) return "";

            return State switch
            {
                // 待发送：对方离线，等其上线后自动补发
                MessageState.Queued => "⏳ 待发送",
                // 大文件提议：等待对方确认接收 与 实际传输 是两个阶段，文案要分开
                MessageState.Sending => IsLargeFileAwaitingAcceptance ? "⏳ 等待对方接收" : "⏳",
                MessageState.Sent => "✓",
                MessageState.Delivered => "✓✓",
                MessageState.Read => "✓✓",
                MessageState.Failed => string.IsNullOrEmpty(FailureReason) ? "❌" : $"🚫 {FailureReason}",
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
            OnPropertyChanged(nameof(ShowStateText));
            OnPropertyChanged(nameof(IsTransferring));
            OnPropertyChanged(nameof(ShowTransferProgress));
            OnPropertyChanged(nameof(FileCardHint));
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
