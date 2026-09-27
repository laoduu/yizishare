using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using YiziShare.Core;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace YiziShare.App;

/// <summary>
/// 统一诊断修复卡片：按 FixScope 渲染一键修复 / 凭据连接 / 分步指引三种操作闭环。
/// 状态机：Idle → Running(逐条日志+进度) → Success(自动复检) / Failed(日志保留可重试)
/// </summary>
public class DiagnosisCard : Border
{
    private readonly SmbDiagnostics.DiagnosisResult _diagnosis;
    private readonly string _deviceIp;
    private readonly StackPanel _actionsPanel = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly StackPanel _stepsPanel = new();
    private readonly TextBlock _progressText;
    private readonly TextBox _logBox;
    private readonly TextBlock _resultText;
    private readonly List<Button> _actionButtons = [];
    private int _runningStep;
    private int _totalSteps;
    private bool _stepsExpanded = false;
    private TextBlock? _stepsToggle;

    public event EventHandler? FixRequested;
    public event EventHandler? CredentialsRequested;
    public event EventHandler? RecheckRequested;

    public DiagnosisCard(SmbDiagnostics.DiagnosisResult diagnosis, string deviceIp)
    {
        _diagnosis = diagnosis;
        _deviceIp = deviceIp;

        Background = ThemeColors.DiagnosisBgBrush;
        BorderBrush = ThemeColors.DiagnosisBorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(16);
        Margin = new Thickness(0, 0, 0, 12);

        var root = new StackPanel();

        var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "⚠️",
            FontSize = 16,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = diagnosis.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeColors.SemanticOrangeBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(titlePanel);

        var descText = new TextBlock
        {
            Text = string.IsNullOrEmpty(diagnosis.Solution)
                ? diagnosis.Description
                : $"{diagnosis.Description}\n\n{diagnosis.Solution}",
            FontSize = 11,
            Foreground = ThemeColors.TextSecondaryBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        root.Children.Add(descText);

        BuildActionButtons();
        root.Children.Add(_actionsPanel);

        BuildManualSteps();
        root.Children.Add(_stepsPanel);
        UpdateStepsVisibility();

        _progressText = new TextBlock
        {
            FontSize = 11,
            Foreground = ThemeColors.AccentGoldBrush,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0)
        };
        root.Children.Add(_progressText);

        _logBox = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            IsReadOnly = true,
            Visibility = Visibility.Collapsed,
            MaxHeight = 120,
            Background = ThemeColors.InputBgBrush,
            Foreground = ThemeColors.TextPrimaryBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        root.Children.Add(_logBox);

        _resultText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        root.Children.Add(_resultText);

        var recheckBtn = MakeSecondaryButton("🔄 重新检测");
        recheckBtn.Click += (_, _) => RecheckRequested?.Invoke(this, EventArgs.Empty);
        var recheckWrap = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        recheckWrap.Children.Add(recheckBtn);
        root.Children.Add(recheckWrap);

        Child = root;
    }

    private void BuildActionButtons()
    {
        var executable = _diagnosis.Actions
            .Where(a => a.Kind is FixActionKind.Cmd or FixActionKind.PowerShell or FixActionKind.DeleteNetSession)
            .ToList();
        bool hasCredential = _diagnosis.Actions.Any(a => a.Kind == FixActionKind.OpenCredentialDialog);
        bool hasExplorer = _diagnosis.Actions.Any(a => a.Kind == FixActionKind.OpenExplorer);

        if (executable.Count > 0)
        {
            bool needsElevated = executable.Any(a => a.Kind is FixActionKind.Cmd or FixActionKind.PowerShell);
            var label = executable.Count == 1 && !needsElevated
                ? executable[0].Title
                : $"🛠 一键修复（{executable.Count} 步{(needsElevated ? "，需要管理员授权" : "")}）";
            var btn = MakePrimaryButton(label);
            btn.Click += (_, _) => FixRequested?.Invoke(this, EventArgs.Empty);
            _actionsPanel.Children.Add(btn);
            _actionButtons.Add(btn);
        }

        if (hasCredential)
        {
            var btn = MakePrimaryButton("🔑 输入用户名密码连接", ThemeColors.NavyHover);
            btn.Click += (_, _) => CredentialsRequested?.Invoke(this, EventArgs.Empty);
            _actionsPanel.Children.Add(btn);
            _actionButtons.Add(btn);
        }

        if (hasExplorer)
        {
            var btn = MakeSecondaryButton("📂 在资源管理器中打开目标设备");
            btn.Click += (_, _) => OpenExplorer();
            _actionsPanel.Children.Add(btn);
        }
    }

    private void BuildManualSteps()
    {
        if (_diagnosis.ManualSteps.Count == 0) return;

        _stepsPanel.Margin = new Thickness(0, 12, 0, 0);

        _stepsToggle = new TextBlock
        {
            Text = "📋 操作指引 ▶",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeColors.AccentGoldBrush,
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        _stepsToggle.MouseLeftButtonDown += (_, _) =>
        {
            _stepsExpanded = !_stepsExpanded;
            _stepsToggle.Text = _stepsExpanded ? "📋 操作指引 ▼" : "📋 操作指引 ▶";
            UpdateStepsVisibility();
        };
        _stepsPanel.Children.Add(_stepsToggle);

        foreach (var step in _diagnosis.ManualSteps)
        {
            _stepsPanel.Children.Add(new TextBlock
            {
                Text = step,
                FontSize = 11,
                Foreground = ThemeColors.TextSecondaryBrush,
                TextWrapping = TextWrapping.Wrap
            });
        }

        var copyAll = MakeSecondaryButton("📋 复制全部指引");
        copyAll.Click += (_, _) =>
        {
            Clipboard.SetText(BuildGuideText());
            copyAll.Content = "✅ 已复制";
        };
        _stepsPanel.Children.Add(copyAll);
    }

    private void UpdateStepsVisibility()
    {
        foreach (var child in _stepsPanel.Children)
        {
            if (child is TextBlock tb && tb != _stepsToggle)
                tb.Visibility = _stepsExpanded ? Visibility.Visible : Visibility.Collapsed;
            else if (child is Button btn)
                btn.Visibility = _stepsExpanded ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private string BuildGuideText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(_diagnosis.Title);
        sb.AppendLine(_diagnosis.Description);
        if (!string.IsNullOrEmpty(_diagnosis.Solution))
        {
            sb.AppendLine().AppendLine("解决方案：").AppendLine(_diagnosis.Solution);
        }
        if (_diagnosis.ManualSteps.Count > 0)
        {
            sb.AppendLine().AppendLine("操作指引：");
            foreach (var step in _diagnosis.ManualSteps)
                sb.AppendLine(step);
        }
        return sb.ToString();
    }

    public void SetRunning(int totalSteps)
    {
        _totalSteps = totalSteps;
        _runningStep = 0;
        foreach (var btn in _actionButtons) btn.IsEnabled = false;
        _progressText.Text = $"🛠 正在执行修复（0/{_totalSteps}）…";
        _progressText.Visibility = Visibility.Visible;
        _logBox.Text = "";
        _logBox.Visibility = Visibility.Visible;
        _resultText.Visibility = Visibility.Collapsed;
    }

    public void AppendLog(string line)
    {
        if (line.StartsWith("▶"))
        {
            _runningStep++;
            _progressText.Text = $"🛠 正在执行修复（{Math.Min(_runningStep, _totalSteps)}/{_totalSteps}）…";
        }
        _logBox.AppendText(line + Environment.NewLine);
        _logBox.ScrollToEnd();
    }

    public void SetFinished(bool success)
    {
        _progressText.Visibility = Visibility.Collapsed;
        _resultText.Visibility = Visibility.Visible;
        if (success)
        {
            _resultText.Text = "✅ 修复完成，正在重新检测…";
            _resultText.Foreground = ThemeColors.SemanticGreenBrush;
        }
        else
        {
            _resultText.Text = "❌ 修复未全部完成，请查看上方日志；可重试或按指引手动处理";
            _resultText.Foreground = ThemeColors.SemanticRedBrush;
            foreach (var btn in _actionButtons) btn.IsEnabled = true;
        }
    }

    private void OpenExplorer()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\\\\{_deviceIp}", UseShellExecute = true });
        }
        catch { /* 打开失败时静默，用户仍可复制路径 */ }
    }

    private static Button MakePrimaryButton(string content, Color? background = null)
    {
        return new Button
        {
            Content = content,
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(background ?? ThemeColors.AccentGold),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static Button MakeSecondaryButton(string content)
    {
        return new Button
        {
            Content = content,
            FontSize = 11,
            Cursor = System.Windows.Input.Cursors.Hand,
            Foreground = ThemeColors.TextSecondaryBrush,
            Background = ThemeColors.SecondaryBtnBgBrush,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 8, 8, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }
}
