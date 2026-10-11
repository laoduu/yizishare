using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using YiziShare.App.ViewModels;
using YiziShare.Core;

namespace YiziShare.App;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _viewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        VersionText.Text = $"V{App.Version}";
        LoadSettings();
    }

    #region 标签切换（常规 / 关于）

    private void BtnTabGeneral_Click(object sender, RoutedEventArgs e) => ShowTab(general: true);

    private void BtnTabAbout_Click(object sender, RoutedEventArgs e) => ShowTab(general: false);

    private void ShowTab(bool general)
    {
        TabGeneral.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        TabAbout.Visibility = general ? Visibility.Collapsed : Visibility.Visible;
        TabIndicator.SetValue(Grid.ColumnProperty, general ? 0 : 1);
        BtnTabGeneral.Foreground = general
            ? (System.Windows.Media.Brush)FindResource("AccentGoldBrush")
            : FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
        BtnTabAbout.Foreground = !general
            ? (System.Windows.Media.Brush)FindResource("AccentGoldBrush")
            : FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
    }

    private void GitHubLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/laoduu/yizishare",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"无法打开链接: {ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    private void LoadSettings()
    {
        ChkAutoStart.IsChecked = SettingsStore.GetAutoStart();
        ChkAutoRefresh.IsChecked = _viewModel.AutoRefreshEnabled;
        TxtRefreshInterval.Text = _viewModel.AutoRefreshIntervalMinutes.ToString();
        ChkRememberUsername.IsChecked = SettingsStore.GetRememberUsername();
        ChkChatSound.IsChecked = SettingsStore.GetChatSoundEnabled();
        TxtMyNickname.Text = SettingsStore.GetMyNickname();
        TxtReceiveDir.Text = SettingsStore.GetReceivedDirectory(); // 显示当前实际路径（含默认）
        UpdateReceiveDirHint();

        LoadNetworkAdapters();
    }

    private void UpdateReceiveDirHint()
    {
        ReceiveDirHint.Text = string.IsNullOrWhiteSpace(TxtReceiveDir.Text.Trim())
            ? "留空使用默认位置（C 盘应用数据目录）"
            : "聊天接收的图片和文件将保存到此目录";
    }

    /// <summary>浏览选择接收目录</summary>
    private void BtnBrowseReceiveDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择聊天接收文件的保存位置",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(TxtReceiveDir.Text.Trim()))
            dialog.SelectedPath = TxtReceiveDir.Text.Trim();

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        TxtReceiveDir.Text = dialog.SelectedPath;
        UpdateReceiveDirHint();
    }

    /// <summary>恢复默认接收目录：直接填入默认路径（避免清空导致用户困惑）</summary>
    private void BtnResetReceiveDir_Click(object sender, RoutedEventArgs e)
    {
        TxtReceiveDir.Text = YiziShare.Core.SettingsStore.DefaultReceivedDirectory;
        UpdateReceiveDirHint();
    }

    private void LoadNetworkAdapters()
    {
        CmbNetworkAdapter.Items.Clear();
        var adapters = NetworkScanner.GetUsableNetworkAdapters();
        foreach (var adapter in adapters)
        {
            CmbNetworkAdapter.Items.Add(adapter);
        }
        if (CmbNetworkAdapter.Items.Count > 0)
            CmbNetworkAdapter.SelectedIndex = 0;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        SettingsStore.SetAutoStart(ChkAutoStart.IsChecked == true);
        _viewModel.AutoRefreshEnabled = ChkAutoRefresh.IsChecked == true;

        if (int.TryParse(TxtRefreshInterval.Text, out int interval) && interval > 0)
            _viewModel.AutoRefreshIntervalMinutes = interval;

        SettingsStore.SetRememberUsername(ChkRememberUsername.IsChecked == true);
        SettingsStore.SetChatSoundEnabled(ChkChatSound.IsChecked == true);

        // 我的昵称：保存后立即生效（下一条心跳即广播新名）
        var myNick = TxtMyNickname.Text.Trim();
        SettingsStore.SetMyNickname(myNick);
        _viewModel.SetLocalChatName(myNick);

        // 文件接收目录：留空 = 默认位置；目录变更后即刻对之后的接收生效
        SettingsStore.SetReceivedDirectory(TxtReceiveDir.Text.Trim());

        if (CmbNetworkAdapter.SelectedItem is string adapter)
            SettingsStore.SetSelectedAdapter(adapter);

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>标题栏拖动（自绘标题栏，无系统边框）；点在交互控件上时不拖动</summary>
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox ||
            e.OriginalSource is System.Windows.Controls.CheckBox ||
            e.OriginalSource is System.Windows.Controls.Button ||
            e.OriginalSource is System.Windows.Controls.ComboBox) return;

        if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
