using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace YiziShare.Core;

public class RepairStepResult
{
    public FixAction Action { get; set; } = null!;
    public bool Success { get; set; }
    public string Output { get; set; } = "";
}

public class RepairResult
{
    public bool Success { get; set; }
    public List<RepairStepResult> Steps { get; } = [];
    public string LogText { get; set; } = "";
}

/// <summary>
/// 本机修复动作执行器：按序执行 Cmd/PowerShell/DeleteNetSession 类动作。
/// OpenCredentialDialog / OpenExplorer 由界面层拦截处理，不经此执行器。
/// 宿主选择规则：reg/net/netsh/sc → cmd.exe；Set-*/Enable-WindowsOptionalFeature 等 → powershell。
/// </summary>
public class RepairService
{
    public event EventHandler<string>? LogMessage;

    private void Log(string msg) => LogMessage?.Invoke(this, msg);

    /// <summary>顺序执行动作列表，首个失败即停止；每步结果与完整日志记录在返回值中</summary>
    public Task<RepairResult> ExecuteAllAsync(IReadOnlyList<FixAction> actions)
    {
        return Task.Run(() =>
        {
            var result = new RepairResult();
            var log = new StringBuilder();

            foreach (var action in actions)
            {
                if (action.Kind is FixActionKind.OpenCredentialDialog or FixActionKind.OpenExplorer)
                    continue;

                Log($"▶ {action.Title}");
                var step = RunProcess(action);
                step.Action = action;
                result.Steps.Add(step);
                log.AppendLine($"[{(step.Success ? "OK" : "FAIL")}] {action.Title}");
                if (!string.IsNullOrEmpty(step.Output))
                    log.AppendLine(step.Output);
                Log(step.Success ? $"✅ {action.Title}" : $"❌ {action.Title}：{step.Output.Truncate(120)}");

                if (!step.Success)
                {
                    result.Success = false;
                    break;
                }
            }

            result.Success = result.Steps.Count == 0 || result.Steps.All(s => s.Success);
            result.LogText = log.ToString();
            return result;
        });
    }

    private static RepairStepResult RunProcess(FixAction action)
    {
        // DeleteNetSession（net use 删除连接）无需管理员权限，其余默认请求提权
        bool elevated = action.Kind != FixActionKind.DeleteNetSession;

        ProcessStartInfo psi = action.Kind switch
        {
            FixActionKind.Cmd or FixActionKind.DeleteNetSession => new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {action.Command}",
            },
            FixActionKind.PowerShell => new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{action.Command.Replace("\"", "\\\"")}\"",
            },
            _ => new ProcessStartInfo(),
        };

        try
        {
            if (!elevated)
            {
                // 免提权路径：可以重定向捕获输出
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;

                using var process = Process.Start(psi)!;
                string output = process.StandardOutput.ReadToEnd().TrimEnd();
                string error = process.StandardError.ReadToEnd().TrimEnd();
                process.WaitForExit();
                return new RepairStepResult
                {
                    Success = process.ExitCode == 0,
                    Output = string.IsNullOrEmpty(error) ? output : $"{output}\n{error}".Trim()
                };
            }

            // 提权路径：ShellExecute + runas，无法捕获输出，用退出码判断成败
            psi.UseShellExecute = true;
            psi.Verb = "runas";
            using var elevatedProcess = Process.Start(psi)!;
            elevatedProcess.WaitForExit();
            return new RepairStepResult
            {
                Success = elevatedProcess.ExitCode == 0,
                Output = elevatedProcess.ExitCode == 0 ? "" : $"命令退出码：{elevatedProcess.ExitCode}"
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new RepairStepResult { Success = false, Output = "已取消管理员授权" };
        }
        catch (Exception ex)
        {
            return new RepairStepResult { Success = false, Output = ex.Message };
        }
    }
}

/// <summary>
/// 字符串扩展
/// </summary>
public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
