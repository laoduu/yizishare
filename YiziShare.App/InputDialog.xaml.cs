using System.Windows;

namespace YiziShare.App;

/// <summary>通用单行输入对话框（重命名等场景）</summary>
public partial class InputDialog : Window
{
    public InputDialog(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;
        ValueBox.SelectAll();
        ValueBox.Focus();
    }

    /// <summary>用户确认后的输入值</summary>
    public string Value => ValueBox.Text.Trim();

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            System.Windows.MessageBox.Show("名称不能为空", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
