using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using YiziShare.App.ViewModels;

namespace YiziShare.App;

public partial class NicknameDialog : Window
{
    private static readonly Regex ValidNamePattern = new(@"^[\w\u4e00-\u9fa5\-]*$", RegexOptions.Compiled);

    private readonly DeviceViewModel _device;

    /// <summary>提交成功后的昵称（可能为空字符串，表示清空昵称）</summary>
    public string? NicknameResult { get; private set; }

    public NicknameDialog(DeviceViewModel device)
    {
        InitializeComponent();
        _device = device;
        PromptText.Text = $"为 {device.IpAddress} 设置昵称，留空则恢复显示主机名";
        NicknameInput.Text = device.NickName;
        Loaded += (_, _) =>
        {
            NicknameInput.Focus();
            NicknameInput.SelectAll();
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Ok_Click(object sender, RoutedEventArgs e) => TrySubmit();

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TrySubmit();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void NicknameInput_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        CountText.Text = $"{NicknameInput.Text.Length}/20";
        HideHint();
    }

    private void TrySubmit()
    {
        var newName = NicknameInput.Text.Trim();

        if (newName.Length == 0)
        {
            NicknameResult = string.Empty;
            DialogResult = true;
            return;
        }

        if (newName.Length > 20)
        {
            ShowHint("昵称不能超过 20 个字符");
            return;
        }

        if (!ValidNamePattern.IsMatch(newName))
        {
            ShowHint("昵称只能包含字母、数字、中文、下划线和短横线");
            return;
        }

        NicknameResult = newName;
        DialogResult = true;
    }

    private void ShowHint(string message)
    {
        HintText.Text = message;
        HintText.Visibility = Visibility.Visible;
        NicknameInput.Focus();
    }

    private void HideHint()
    {
        HintText.Text = "";
        HintText.Visibility = Visibility.Collapsed;
    }
}
