using System.Windows;
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
        LoadSettings();
    }

    private void LoadSettings()
    {
        ChkAutoStart.IsChecked = SettingsStore.GetAutoStart();
        ChkAutoRefresh.IsChecked = _viewModel.AutoRefreshEnabled;
        TxtRefreshInterval.Text = _viewModel.AutoRefreshIntervalMinutes.ToString();
        ChkRememberUsername.IsChecked = SettingsStore.GetRememberUsername();

        LoadNetworkAdapters();
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
}
