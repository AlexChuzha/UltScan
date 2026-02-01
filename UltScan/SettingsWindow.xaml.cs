using System.Windows;
using System.Windows.Controls;

namespace UltScan;

public partial class SettingsWindow : Window
{
    private readonly App _app;

    public SettingsWindow()
    {
        InitializeComponent();

        _app = (App)System.Windows.Application.Current;
        HotKeyCombo.ItemsSource = _app.HotKeyPresetList;

        var current = HotKeyPresets.FindById(_app.Settings.HotKey.Id) ?? HotKeyPresets.Default;
        HotKeyCombo.SelectedItem = current;
        UpdateHotKeyTexts(current);
    }

    private void HotKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotKeyCombo.SelectedItem is HotKeyPreset preset)
        {
            UpdateHotKeyTexts(preset);
        }
    }

    private void UpdateHotKeyTexts(HotKeyPreset preset)
    {
        HintText.Text = preset.Hint;

        if (string.IsNullOrWhiteSpace(preset.Warning))
        {
            WarningText.Text = string.Empty;
            WarningText.Visibility = Visibility.Collapsed;
        }
        else
        {
            WarningText.Text = preset.Warning;
            WarningText.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (HotKeyCombo.SelectedItem is not HotKeyPreset preset)
        {
            return;
        }

        var config = preset.ToConfig();
        if (!_app.TryApplyHotKey(config, showError: true))
        {
            return;
        }

        _app.Settings.HotKey = config;
        _app.Settings.Save();
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
