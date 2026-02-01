using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace UltScan;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly LocalizationService _loc;
    private bool _suppressLocaleChange;

    public SettingsWindow()
    {
        InitializeComponent();

        _app = (App)System.Windows.Application.Current;
        _loc = _app.Localization;

        LocaleCombo.ItemsSource = _loc.Locales;
        RefreshLocalization();
    }

    private void HotKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotKeyCombo.SelectedItem is HotKeyPresetItem item)
        {
            UpdateHotKeyTexts(item.Preset);
        }
    }

    private void UpdateHotKeyTexts(HotKeyPreset preset)
    {
        HintText.Text = _loc[preset.HintKey];

        if (string.IsNullOrWhiteSpace(preset.WarningKey))
        {
            WarningText.Text = string.Empty;
            WarningText.Visibility = Visibility.Collapsed;
        }
        else
        {
            WarningText.Text = _loc[preset.WarningKey];
            WarningText.Visibility = Visibility.Visible;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (HotKeyCombo.SelectedItem is not HotKeyPresetItem item)
        {
            return;
        }

        var config = item.Preset.ToConfig();
        if (!_app.TryApplyHotKey(config, showError: true))
        {
            return;
        }

        _app.Settings.HotKey = config;
        _app.Settings.Save();
        _app.UpdateTrayMenuText();
        Close();
    }

    private void LocaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLocaleChange)
        {
            return;
        }

        if (LocaleCombo.SelectedItem is not LocaleOption option)
        {
            return;
        }

        _app.Localization.SetLocale(option.Id);
        _app.Settings.LocaleId = option.Id;
        _app.Settings.Save();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ExperimentalMode_Changed(object sender, RoutedEventArgs e)
    {
        if (ExperimentalModeCheckBox.IsChecked == null)
        {
            return;
        }

        _app.Settings.ExperimentalMode = ExperimentalModeCheckBox.IsChecked.Value;
        _app.Settings.Save();
        UpdateExperimentalWarning();
    }

    public void RefreshLocalization()
    {
        Title = _loc["Settings.Title"];
        HotkeysHeader.Text = _loc["Settings.HotkeysHeader"];
        HotkeysContext.Text = _loc["Settings.HotkeysContext"];
        HotkeysLabel.Text = _loc["Settings.HotkeysLabel"];
        LanguageHeader.Text = _loc["Settings.LanguageHeader"];
        ExperimentalModeCheckBox.Content = _loc["Settings.ExperimentalMode"];
        ExperimentalWarningText.Text = _loc["Settings.ExperimentalWarning"];
        SaveButton.Content = _loc["Settings.Save"];
        CancelButton.Content = _loc["Settings.Cancel"];

        RebuildHotKeyItems();

        _suppressLocaleChange = true;
        var localeId = _app.Localization.CurrentLocaleId;
        LocaleCombo.SelectedItem = _loc.Locales.FirstOrDefault(l => l.Id == localeId) ?? _loc.Locales.FirstOrDefault();
        _suppressLocaleChange = false;

        ExperimentalModeCheckBox.IsChecked = _app.Settings.ExperimentalMode;
        UpdateExperimentalWarning();
    }

    private void UpdateExperimentalWarning()
    {
        ExperimentalWarningText.Visibility = _app.Settings.ExperimentalMode
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RebuildHotKeyItems()
    {
        var items = _app.HotKeyPresetList
            .Select(p => new HotKeyPresetItem(p, _loc[p.LabelKey]))
            .ToList();

        HotKeyCombo.ItemsSource = items;
        var currentId = _app.Settings.HotKey.Id;
        HotKeyCombo.SelectedItem = items.FirstOrDefault(i => i.Preset.Id == currentId) ?? items.FirstOrDefault();
        if (HotKeyCombo.SelectedItem is HotKeyPresetItem item)
        {
            UpdateHotKeyTexts(item.Preset);
        }
    }

    private sealed class HotKeyPresetItem
    {
        public HotKeyPresetItem(HotKeyPreset preset, string displayName)
        {
            Preset = preset;
            DisplayName = displayName;
        }

        public HotKeyPreset Preset { get; }
        public string DisplayName { get; }
    }
}
