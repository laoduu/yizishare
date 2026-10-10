using System.Text;
using System.Text.RegularExpressions;

namespace YiziShare.App.Services;

/// <summary>表情清单（Unicode 文本形态）与 Twemoji 彩色资源的映射中心。</summary>
/// <remarks>
/// 资源：Twemoji 72px（CC-BY 4.0，twitter/twemoji），编入程序集 Assets/Emoji/{码点}.png。<br/>
/// WPF 文字栈不支持彩色字体（COLR），文字渲染表情必然黑白，彩色只能走图片内联。
/// </remarks>
public static class EmojiMap
{
    /// <summary>表情文本清单（保留 FE0F 变体选择符，与输入/传输内容一致）</summary>
    public static readonly string[] All =
    [
        "😀","😃","😄","😁","😆","😅","🤣","😂","🙂","🙃","😉","😊","😇","🥰","😍","🤩",
        "😘","😗","😚","😙","😋","😛","😜","🤪","🤨","🧐","🤓","😎","🥳","😏","😒","😞",
        "😔","😟","😕","🙁","😣","😖","😫","😩","🥺","😢","😭","😤","😠","😡","🤯","😳",
        "🥵","🥶","😱","😨","😰","😥","😓","🤗","🤔","🤭","🤫","🤥","😶","😐","😑","😬",
        "🙄","😯","😴","🤤","😪","😵","🤐","🥴","🤢","🤮","🤧","😷","🤒","🤕","🤑","🤠",
        "👍","👎","👌","✌️","🤞","🤟","🤘","🤙","👈","👉","👆","👇","☝️","✋","🤚","🖖",
        "👋","🤝","💪","🙏","✍️","👏","🙌","🤲","👐","🫶",
        "❤️","🧡","💛","💚","💙","💜","🖤","🤍","💔","❣️","💕","💞","💓","💗","💖","💘",
        "🐶","🐱","🐭","🐹","🐰","🦊","🐻","🐼","🐨","🐯","🦁","🐮","🐷","🐸","🐵","🐔",
        "🐧","🐦","🐤","🦆","🦉","🦄","🐝","🦋","🐢","🐠",
        "🍎","🍐","🍊","🍋","🍌","🍉","🍇","🍓","🍒","🍑","🥭","🍍","🥝","🍅","🥑","🌽",
        "☕","🍵","🍺","🍻","🥂","🍷","🎂","🍰","🍫","🍬","🍩","🍪",
        "⚽","🏀","🎾","🏐","🏓","🏸","🎮","🎲","🎯","🎸","🎧","🎤",
        "⭐","🌟","✨","⚡","🔥","💥","❄️","🌈","☀️","🌙","💧","🎵","💡","🔔","🎉","🎊","🎁","🏆","🌹","🍀"
    ];

    /// <summary>emoji 文本（保留 FE0F）→ 资源文件名（小写码点序列，剔除 FE0F，Twemoji 命名规则）</summary>
    private static readonly Dictionary<string, string> Map = new();

    /// <summary>匹配任意已知表情的正则（长序列优先，避免前缀截断）</summary>
    public static readonly Regex Regex;

    static EmojiMap()
    {
        foreach (var emoji in All)
        {
            var name = ToFileName(emoji);
            if (name != null) Map[emoji] = name;
        }

        var pattern = string.Join("|", Map.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        Regex = new Regex(pattern, RegexOptions.Compiled);
    }

    /// <summary>查彩色资源文件名；未知表情返回 null（回退文字渲染）</summary>
    public static string? Lookup(string emoji) => Map.TryGetValue(emoji, out var name) ? name : null;

    private static string? ToFileName(string emoji)
    {
        var sb = new StringBuilder();
        foreach (var rune in emoji.EnumerateRunes())
        {
            if (rune.Value == 0xFE0F) continue;
            if (sb.Length > 0) sb.Append('-');
            sb.Append(rune.Value.ToString("x4"));
        }
        return sb.Length == 0 ? null : sb.ToString();
    }
}
