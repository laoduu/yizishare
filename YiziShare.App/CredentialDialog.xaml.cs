using System.Windows;
using System.Windows.Controls;

namespace YiziShare.App;

public partial class CredentialDialog : Window
{
    public string UserName => UserNameBox.Text.Trim();
    public string Password => PasswordBox.Password;

    public CredentialDialog(string deviceIp, string deviceName,
        string initialUserName = "", string? errorMessage = null)
    {
        InitializeComponent();
        Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);

        TargetText.Text = $"🔗 {deviceName}（{deviceIp}）";
        UserNameBox.Text = initialUserName;

        if (!string.IsNullOrEmpty(errorMessage))
        {
            ErrorText.Text = "⚠️ " + errorMessage;
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            Loaded += (_, _) => PasswordBox.Focus();
        }
    }

    private void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserName))
        {
            ShowError("请输入用户名");
            return;
        }
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = "⚠️ " + message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
