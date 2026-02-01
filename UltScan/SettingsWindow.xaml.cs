using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace UltScan;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly LocalizationService _loc;
    private bool _suppressLocaleChange;
    private bool _suppressTranslationChange;
    private bool _showAllLanguages;
    private readonly ProviderOption[] _providerOptions;
    private readonly OverlayOption[] _overlayOptions;

    public SettingsWindow()
    {
        InitializeComponent();

        _app = (App)System.Windows.Application.Current;
        _loc = _app.Localization;
        _providerOptions = new[]
        {
            new ProviderOption(TranslationService.ProviderApi, "Settings.TranslationProviderApi"),
            new ProviderOption(TranslationService.ProviderWeb, "Settings.TranslationProviderWeb")
        };
        _overlayOptions = new[]
        {
            new OverlayOption(OverlayOrientation.Right, "Settings.OverlayRight"),
            new OverlayOption(OverlayOrientation.Bottom, "Settings.OverlayBottom"),
            new OverlayOption(OverlayOrientation.Left, "Settings.OverlayLeft"),
            new OverlayOption(OverlayOrientation.Top, "Settings.OverlayTop")
        };

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

    private void TranslationEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (TranslationEnabledCheckBox.IsChecked == null)
        {
            return;
        }

        _app.Settings.Translation.Enabled = TranslationEnabledCheckBox.IsChecked.Value;
        _app.Settings.Save();
        UpdateTranslationWarnings();
    }

    private void TranslationBold_Changed(object sender, RoutedEventArgs e)
    {
        if (TranslationBoldCheckBox.IsChecked == null)
        {
            return;
        }

        _app.Settings.Translation.TranslatedBold = TranslationBoldCheckBox.IsChecked.Value;
        _app.Settings.Save();
    }

    private void TranslationSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationChange)
        {
            return;
        }

        if (TranslationSourceCombo.SelectedItem is not LanguageOption option)
        {
            return;
        }

        _app.Settings.Translation.SourceLanguage = option.Code;
        _app.Settings.Save();
    }

    private void TranslationTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationChange)
        {
            return;
        }

        if (TranslationTargetCombo.SelectedItem is not LanguageOption option)
        {
            return;
        }

        _app.Settings.Translation.TargetLanguage = option.Code;
        _app.Settings.Save();
    }

    private void TranslationProject_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _app.Settings.Translation.ProjectId = TranslationProjectTextBox.Text.Trim();
        _app.Settings.Save();
        UpdateTranslationWarnings();
    }

    private void TranslationApiKey_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _app.Settings.Translation.ApiKey = TranslationApiKeyTextBox.Text.Trim();
        _app.Settings.Save();
        UpdateTranslationWarnings();
    }

    private void MoreLanguages_Click(object sender, RoutedEventArgs e)
    {
        _showAllLanguages = !_showAllLanguages;
        RebuildTranslationLanguageLists();
    }

    private void TranslationProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationChange)
        {
            return;
        }

        if (TranslationProviderCombo.SelectedItem is not ProviderOption option)
        {
            return;
        }

        _app.Settings.Translation.Provider = option.Id;
        _app.Settings.Save();
        UpdateTranslationWarnings();
        UpdateTranslationApiFields();
    }

    private void OverlayOrientation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationChange)
        {
            return;
        }

        if (OverlayOrientationCombo.SelectedItem is not OverlayOption option)
        {
            return;
        }

        _app.Settings.Overlay.Orientation = option.Value;
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
        InterfaceLanguageHeaderText.Text = _loc["Settings.InterfaceLanguageHeader"];
        HotkeysGroupHeader.Text = _loc["Settings.HotkeysHeader"];
        HotkeysContext.Text = _loc["Settings.HotkeysContext"];
        HotkeysLabel.Text = _loc["Settings.HotkeysLabel"];
        ExperimentalModeCheckBox.Content = _loc["Settings.ExperimentalMode"];
        ExperimentalWarningText.Text = _loc["Settings.ExperimentalWarning"];
        OpenWelcomeButton.Content = _loc["Settings.OpenWelcome"];
        SaveButton.Content = _loc["Settings.Save"];
        CancelButton.Content = _loc["Settings.Cancel"];
        TranslationEnabledCheckBox.Content = _loc["Settings.TranslationEnabled"];
        TranslationBoldCheckBox.Content = _loc["Settings.TranslationBold"];
        TranslationSourceLabel.Text = _loc["Settings.TranslationSource"];
        TranslationTargetLabel.Text = _loc["Settings.TranslationTarget"];
        TranslationProjectLabel.Text = _loc["Settings.TranslationProject"];
        TranslationApiKeyLabel.Text = _loc["Settings.TranslationApiKeyLabel"];
        TranslationProviderLabel.Text = _loc["Settings.TranslationProvider"];
        TranslationApiKeyWarning.Text = string.Format(
            _loc["Settings.TranslationApiKeyWarning"],
            TranslationService.ApiKeyEnvName);
        TranslationLanguageHeaderText.Text = _loc["Settings.TranslationLanguagesHeader"];
        TranslationConnectionHeaderText.Text = _loc["Settings.TranslationConnectionHeader"];
        ExperimentalHeaderText.Text = _loc["Settings.ExperimentalHeader"];
        OverlayHeaderText.Text = _loc["Settings.OverlayHeader"];
        OverlayOrientationLabel.Text = _loc["Settings.OverlayOrientation"];

        RebuildHotKeyItems();
        RebuildTranslationLanguageLists();

        _suppressLocaleChange = true;
        var localeId = _app.Localization.CurrentLocaleId;
        LocaleCombo.SelectedItem = _loc.Locales.FirstOrDefault(l => l.Id == localeId) ?? _loc.Locales.FirstOrDefault();
        _suppressLocaleChange = false;

        ExperimentalModeCheckBox.IsChecked = _app.Settings.ExperimentalMode;
        TranslationEnabledCheckBox.IsChecked = _app.Settings.Translation.Enabled;
        TranslationBoldCheckBox.IsChecked = _app.Settings.Translation.TranslatedBold;
        TranslationProjectTextBox.Text = _app.Settings.Translation.ProjectId;
        TranslationApiKeyTextBox.Text = _app.Settings.Translation.ApiKey;
        UpdateExperimentalWarning();
        UpdateTranslationWarnings();
        UpdateTranslationApiFields();
        RebuildOverlayOptions();
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

    private void RebuildTranslationLanguageLists()
    {
        _suppressTranslationChange = true;

        var sourceList = TranslationLanguages.GetLanguages(_showAllLanguages, includeAuto: true);
        var targetList = TranslationLanguages.GetLanguages(_showAllLanguages, includeAuto: false);

        TranslationSourceCombo.ItemsSource = sourceList;
        TranslationTargetCombo.ItemsSource = targetList;

        var sourceCode = string.IsNullOrWhiteSpace(_app.Settings.Translation.SourceLanguage)
            ? "auto"
            : _app.Settings.Translation.SourceLanguage;

        var targetCode = _app.Settings.Translation.TargetLanguage;

        TranslationSourceCombo.SelectedItem = sourceList.FirstOrDefault(l => l.Code == sourceCode) ?? sourceList.FirstOrDefault();
        TranslationTargetCombo.SelectedItem = targetList.FirstOrDefault(l => l.Code == targetCode) ?? targetList.FirstOrDefault();

        TranslationProviderCombo.ItemsSource = _providerOptions
            .Select(p => new ProviderOption(p.Id, p.NameKey) { DisplayName = _loc[p.NameKey] })
            .ToList();
        TranslationProviderCombo.SelectedItem = TranslationProviderCombo.ItemsSource
            .Cast<ProviderOption>()
            .FirstOrDefault(p => p.Id == _app.Settings.Translation.Provider)
            ?? TranslationProviderCombo.ItemsSource.Cast<ProviderOption>().FirstOrDefault();

        MoreLanguagesButton.Content = _showAllLanguages
            ? _loc["Settings.LanguagesLess"]
            : _loc["Settings.LanguagesMore"];

        _suppressTranslationChange = false;
    }

    private void UpdateTranslationWarnings()
    {
        var isApi = string.Equals(_app.Settings.Translation.Provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase);
        var keyMissing = string.IsNullOrWhiteSpace(_app.Settings.Translation.ApiKey)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TranslationService.ApiKeyEnvName));
        var projectMissing = string.IsNullOrWhiteSpace(_app.Settings.Translation.ProjectId);
        var showWarning = _app.Settings.Translation.Enabled && isApi && (keyMissing || projectMissing);

        TranslationApiKeyWarning.Visibility = showWarning ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildOverlayOptions()
    {
        _suppressTranslationChange = true;

        OverlayOrientationCombo.ItemsSource = _overlayOptions
            .Select(o => new OverlayOption(o.Value, o.NameKey) { DisplayName = _loc[o.NameKey] })
            .ToList();

        OverlayOrientationCombo.SelectedItem = OverlayOrientationCombo.ItemsSource
            .Cast<OverlayOption>()
            .FirstOrDefault(o => o.Value == _app.Settings.Overlay.Orientation)
            ?? OverlayOrientationCombo.ItemsSource.Cast<OverlayOption>().FirstOrDefault();

        _suppressTranslationChange = false;
    }

    private void OpenWelcome_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var welcome = new WelcomeWindow();
        welcome.ShowDialog();
    }

    private void UpdateTranslationApiFields()
    {
        var isApi = string.Equals(_app.Settings.Translation.Provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase);
        TranslationProjectLabel.IsEnabled = isApi;
        TranslationProjectTextBox.IsEnabled = isApi;
        TranslationApiKeyLabel.IsEnabled = isApi;
        TranslationApiKeyTextBox.IsEnabled = isApi;
    }

    private sealed class ProviderOption
    {
        public ProviderOption(string id, string nameKey)
        {
            Id = id;
            NameKey = nameKey;
            DisplayName = nameKey;
        }

        public string Id { get; }
        public string NameKey { get; }
        public string DisplayName { get; set; }
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

    private sealed class OverlayOption
    {
        public OverlayOption(OverlayOrientation value, string nameKey)
        {
            Value = value;
            NameKey = nameKey;
            DisplayName = nameKey;
        }

        public OverlayOrientation Value { get; }
        public string NameKey { get; }
        public string DisplayName { get; set; }
    }
}
