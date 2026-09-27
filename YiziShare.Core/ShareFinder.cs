using System.Diagnostics;
using System.Text.RegularExpressions;

namespace YiziShare.Core;

/// <summary>
/// SMB 共享文件夹发现：NetShareEnum 结构化枚举为主，net view 兜底
/// </summary>
public class ShareFinder
{
    /// <summary>
    /// 快速获取指定IP的所有磁盘类共享（不含管理共享）
    /// </summary>
    public async Task<List<SharedFolder>> GetSharesFromIpAsync(string ip)
    {
        // 主路径：NetShareEnum 结构化返回，无编码/语言/空格解析问题，
        // 且与 IPC$ 会话共用同一认证上下文（凭据连接成功后即可直接枚举）
        var shares = await SmbProbe.EnumSharesViaNetShareEnumAsync(ip);
        if (shares.Count > 0)
        {
            Logger.Info($"NetShareEnum 成功枚举 {ip} 的 {shares.Count} 个共享");
            return shares;
        }

        // 兜底：个别老旧 NAS/嵌入式设备 RPC 受限但响应 net view
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "net",
                Arguments = $"view \\\\{ip}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                // net 输出为 OEM 编码（中文系统 GBK），必须按 OEM 解码否则乱码
                StandardOutputEncoding = SmbProbe.GetOemEncoding(),
            };

            using var process = Process.Start(startInfo)!;
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                ParseNetViewOutput(output, ip, shares);
        }
        catch (Exception ex)
        {
            Logger.Error($"获取 {ip} 共享失败", ex);
        }

        return shares;
    }

    /// <summary>
    /// 语言无关的 net view 输出解析：
    /// 定位首个 "---" 分隔行，其后每行以「两个以上空格」切分共享名与备注（兼容含空格共享名）。
    /// </summary>
    public static void ParseNetViewOutput(string output, string ip, List<SharedFolder> shares)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');

        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimStart().StartsWith("---"))
            {
                start = i + 1;
                break;
            }
        }
        if (start < 0) return;

        for (int i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Trim().Length == 0) continue;

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("命令成功完成") ||
                trimmed.StartsWith("The command completed", StringComparison.OrdinalIgnoreCase))
                break;

            // 按「两个以上空格」切列：共享名 | 类型 | 使用为 | 备注（空列会塌缩）
            var columns = Regex.Split(trimmed, @"\s{2,}")
                .Select(c => c.Trim())
                .Where(c => c.Length > 0)
                .ToArray();
            if (columns.Length == 0) continue;

            var name = columns[0];
            if (name.EndsWith("$")) continue;   // 过滤管理共享

            // 备注为最后一列，且仅当第二列确为类型列时才取（避免把类型误当备注）
            string description = "";
            if (columns.Length >= 3 && IsShareTypeColumn(columns[1]))
                description = columns[^1];

            shares.Add(new SharedFolder
            {
                ShareName = name,
                Path = $"\\\\{ip}\\{name}",
                DeviceIp = ip,
                DeviceName = ip,
                Description = description,
                IsAccessible = true
            });
        }
    }

    private static bool IsShareTypeColumn(string value) =>
        value.Equals("Disk", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Print", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Pipe", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("IPC", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("磁盘") || value.Contains("打印") || value.Contains("设备");
}
