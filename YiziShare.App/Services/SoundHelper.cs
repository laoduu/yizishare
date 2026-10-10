using System.IO;
using System.Media;

namespace YiziShare.App.Services;

/// <summary>
/// 消息提示音：不依赖任何音频资源文件，首次播放时用代码合成一段
/// 约 350ms 的两音阶「叮咚」（22050Hz 16bit PCM 正弦波 + 淡出），
/// 缓存到 %LOCALAPPDATA%\YiziShare\message.wav，之后直接播放缓存。
/// 合成失败时回退到系统提示音。
/// </summary>
public static class SoundHelper
{
    private static readonly object Sync = new();
    private static SoundPlayer? _player;

    public static void PlayMessageSound()
    {
        try
        {
            lock (Sync)
            {
                if (_player == null)
                {
                    var path = GetOrCreateWavPath();
                    _player = path != null
                        ? new SoundPlayer(path)
                        : null;

                    if (_player == null)
                    {
                        PlaySystemFallback();
                        return;
                    }

                    try
                    {
                        _player.Load(); // 预加载，避免首播延迟
                    }
                    catch
                    {
                        // 预加载失败不影响后续 Play（Play 内部会再尝试）
                    }
                }
            }

            _player?.Play();
        }
        catch
        {
            PlaySystemFallback();
        }
    }

    private static void PlaySystemFallback()
    {
        try { System.Media.SystemSounds.Asterisk.Play(); } catch { /* 无声环境，忽略 */ }
    }

    private static string? GetOrCreateWavPath()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YiziShare");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "message.wav");

            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;

            File.WriteAllBytes(path, SynthesizeDingDong());
            return path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 合成「叮咚」：E6(1318Hz) 短音 + A6(1760Hz) 略长音，各带 60ms 淡出，
    /// 音量 0.35 避免刺耳。输出 22050Hz / 16bit / 单声道 PCM WAV。
    /// </summary>
    private static byte[] SynthesizeDingDong()
    {
        const int sampleRate = 22050;
        const double volume = 0.35;

        // (起始秒, 时长秒, 频率Hz)
        var notes = new[]
        {
            (start: 0.00, dur: 0.12, freq: 1318.5), // E6「叮」
            (start: 0.10, dur: 0.25, freq: 1760.0)  // A6「咚」
        };

        double totalSeconds = notes.Max(n => n.start + n.dur);
        int totalSamples = (int)(totalSeconds * sampleRate);
        var samples = new double[totalSamples];

        foreach (var (start, dur, freq) in notes)
        {
            int begin = (int)(start * sampleRate);
            int count = (int)(dur * sampleRate);
            int fadeOut = Math.Min((int)(0.06 * sampleRate), count / 2);

            for (int i = 0; i < count; i++)
            {
                double t = (double)i / sampleRate;
                double envelope = i > count - fadeOut
                    ? (double)(count - i) / fadeOut   // 淡出
                    : Math.Min(1.0, i / (0.005 * sampleRate)); // 5ms 淡入防爆音
                samples[begin + i] += Math.Sin(2 * Math.PI * freq * t) * envelope;
            }
        }

        // WAV 文件体：44 字节头 + 16bit PCM 数据
        using var ms = new MemoryStream(44 + totalSamples * 2);
        using (var writer = new BinaryWriter(ms))
        {
            int dataBytes = totalSamples * 2;
            int byteRate = sampleRate * 2;

            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16);              // fmt 块长度
            writer.Write((short)1);        // PCM
            writer.Write((short)1);        // 单声道
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)2);        // block align
            writer.Write((short)16);       // bits per sample
            writer.Write("data"u8);
            writer.Write(dataBytes);

            foreach (var s in samples)
            {
                short v = (short)Math.Round(Math.Clamp(s * volume, -1.0, 1.0) * short.MaxValue);
                writer.Write(v);
            }
        }

        return ms.ToArray();
    }
}
