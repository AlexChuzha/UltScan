using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace UltScan;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly LocalizationService _loc;
    private bool _suppressLocaleChange;
    private bool _suppressTranslationChange;
    private bool _suppressAutoStartChange;
    private bool _suppressOverlayChange;
    private bool _suppressAppearanceChange;
    private bool _showAllLanguages;
    private bool _isCheckingUpdates;
    private readonly ProviderOption[] _providerOptions;
    private readonly OverlayOption[] _overlayOptions;
    private readonly ModeOption[] _modeOptions;
    private readonly ThemeOption[] _themeOptions;

    public SettingsWindow()
    {
        _app = (App)System.Windows.Application.Current;
        _loc = _app.Localization;
        _suppressOverlayChange = true;
        _suppressAppearanceChange = true;

        InitializeComponent();
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
        _modeOptions = new[]
        {
            new ModeOption(TranslationMode.Standard, "Settings.TranslationModeStandard", "Settings.TranslationModeStandardHint"),
            new ModeOption(TranslationMode.VisualNovel, "Settings.TranslationModeVN", "Settings.TranslationModeVNHint")
        };
        _themeOptions = new[]
        {
            new ThemeOption("#C8FFD4", "#4CD964", "Settings.TranslationThemeEmerald"),
            new ThemeOption("#CFE8FF", "#64B5F6", "Settings.TranslationThemeSky"),
            new ThemeOption("#FFE8A3", "#FFD54F", "Settings.TranslationThemeAmber"),
            new ThemeOption("#FFD1D1", "#FF8A80", "Settings.TranslationThemeRose"),
            new ThemeOption("#E9D7F7", "#CE93D8", "Settings.TranslationThemeViolet"),
            new ThemeOption("#FFFFFF", "#F2F2F2", "Settings.TranslationThemeMono")
        };

        LocaleCombo.ItemsSource = _loc.Locales;
        RefreshLocalization();
        _suppressOverlayChange = false;
        _suppressAppearanceChange = false;
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
        if (!TryApplySettings())
        {
            return;
        }

        Close();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        TryApplySettings();
    }

    private bool TryApplySettings()
    {
        if (HotKeyCombo.SelectedItem is not HotKeyPresetItem item)
        {
            return false;
        }

        var config = item.Preset.ToConfig();
        if (!_app.TryApplyHotKey(config, showError: true))
        {
            return false;
        }

        _app.Settings.HotKey = config;
        _app.Settings.Save();
        _app.UpdateTrayMenuText();
        _app.ApplyOverlayAppearance();
        _ = _app.ForceOverlayTranslationAsync();
        return true;
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

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAutoStartChange || AutoStartCheckBox.IsChecked == null)
        {
            return;
        }

        var enabled = AutoStartCheckBox.IsChecked.Value;
        if (!StartupManager.SetEnabled(enabled))
        {
            _suppressAutoStartChange = true;
            AutoStartCheckBox.IsChecked = _app.Settings.AutoStart;
            _suppressAutoStartChange = false;
            return;
        }

        _app.Settings.AutoStart = enabled;
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

    private void TranslationColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAppearanceChange)
        {
            return;
        }

        if (TranslationColorCombo.SelectedItem is not ThemeOption option)
        {
            return;
        }

        _app.Settings.Translation.CaptionTextColor = option.CaptionHex;
        _app.Settings.Translation.TranslatedTextColor = option.TextHex;
        _app.Settings.Save();
    }

    private void TranslationMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTranslationChange)
        {
            return;
        }

        if (TranslationModeCombo.SelectedItem is not ModeOption option)
        {
            return;
        }

        _app.Settings.Translation.Mode = option.Value;
        _app.Settings.Save();
        TranslationModeHint.Text = _loc[option.HintKey];
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
        if (_suppressOverlayChange)
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

    private void OverlayOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressOverlayChange)
        {
            return;
        }

        var value = Math.Clamp(e.NewValue, 0.1, 1.0);
        _app.Settings.Overlay.Opacity = value;
        _app.Settings.Save();
        OverlayOpacityValue.Text = $"{(int)Math.Round(value * 100)}%";
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
        GeneralTab.Header = _loc["Settings.TabGeneral"];
        TranslationTab.Header = _loc["Settings.TabTranslation"];
        OverlayTab.Header = _loc["Settings.TabOverlay"];
        AdvancedTab.Header = _loc["Settings.TabAdvanced"];
        InterfaceLanguageHeaderText.Text = _loc["Settings.InterfaceLanguageHeader"];
        HotkeysGroupHeader.Text = _loc["Settings.HotkeysHeader"];
        HotkeysContext.Text = _loc["Settings.HotkeysContext"];
        HotkeysLabel.Text = _loc["Settings.HotkeysLabel"];
        AutoStartCheckBox.Content = _loc["Settings.AutoStart"];
        ExperimentalModeCheckBox.Content = _loc["Settings.ExperimentalMode"];
        ExperimentalWarningText.Text = _loc["Settings.ExperimentalWarning"];
        OpenWelcomeButton.Content = _loc["Settings.OpenWelcome"];
        CheckUpdatesButton.Content = _loc["Settings.CheckUpdates"];
        SaveButton.Content = _loc["Settings.Save"];
        ApplyButton.Content = _loc["Settings.Apply"];
        CancelButton.Content = _loc["Settings.Cancel"];
        TranslationEnabledCheckBox.Content = _loc["Settings.TranslationEnabled"];
        TranslationBoldCheckBox.Content = _loc["Settings.TranslationBold"];
        TranslationColorLabel.Text = _loc["Settings.TranslationTextColor"];
        TranslationModeLabel.Text = _loc["Settings.TranslationMode"];
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
        OverlayOpacityLabel.Text = _loc["Settings.OverlayOpacity"];

        RebuildHotKeyItems();
        RebuildTranslationLanguageLists();

        _suppressLocaleChange = true;
        var localeId = _app.Localization.CurrentLocaleId;
        LocaleCombo.SelectedItem = _loc.Locales.FirstOrDefault(l => l.Id == localeId) ?? _loc.Locales.FirstOrDefault();
        _suppressLocaleChange = false;

        _suppressAutoStartChange = true;
        AutoStartCheckBox.IsChecked = _app.Settings.AutoStart;
        _suppressAutoStartChange = false;

        ExperimentalModeCheckBox.IsChecked = _app.Settings.ExperimentalMode;
        TranslationEnabledCheckBox.IsChecked = _app.Settings.Translation.Enabled;
        TranslationBoldCheckBox.IsChecked = _app.Settings.Translation.TranslatedBold;
        _suppressAppearanceChange = true;
        var sampleCaption = _loc["Settings.TranslationThemeSampleCaption"];
        var sampleBody = _loc["Settings.TranslationThemeSampleBody"];
        var themeItems = _themeOptions
            .Select(o => new ThemeOption(o.CaptionHex, o.TextHex, o.NameKey)
            {
                DisplayName = _loc[o.NameKey],
                CaptionBrush = ToBrush(o.CaptionHex),
                TextBrush = ToBrush(o.TextHex),
                SampleCaption = sampleCaption,
                SampleBody = sampleBody
            })
            .ToList();

        var currentCaption = _app.Settings.Translation.CaptionTextColor;
        var currentText = _app.Settings.Translation.TranslatedTextColor;
        var selectedTheme = themeItems.FirstOrDefault(o =>
            string.Equals(o.CaptionHex, currentCaption, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(o.TextHex, currentText, StringComparison.OrdinalIgnoreCase));

        if (selectedTheme == null)
        {
            var custom = new ThemeOption(currentCaption, currentText, "Settings.TranslationThemeCustom")
            {
                DisplayName = _loc["Settings.TranslationThemeCustom"],
                CaptionBrush = ToBrush(currentCaption),
                TextBrush = ToBrush(currentText),
                SampleCaption = sampleCaption,
                SampleBody = sampleBody
            };
            themeItems.Insert(0, custom);
            selectedTheme = custom;
        }

        TranslationColorCombo.ItemsSource = themeItems;
        TranslationColorCombo.SelectedItem = selectedTheme ?? themeItems.FirstOrDefault();
        _suppressAppearanceChange = false;
        RebuildTranslationModes();
        TranslationProjectTextBox.Text = _app.Settings.Translation.ProjectId;
        TranslationApiKeyTextBox.Text = _app.Settings.Translation.ApiKey;
        UpdateExperimentalWarning();
        UpdateTranslationWarnings();
        UpdateTranslationApiFields();
        RebuildOverlayOptions();

        _suppressOverlayChange = true;
        OverlayOpacitySlider.Value = Math.Clamp(_app.Settings.Overlay.Opacity, 0.1, 1.0);
        OverlayOpacityValue.Text = $"{(int)Math.Round(OverlayOpacitySlider.Value * 100)}%";
        _suppressOverlayChange = false;
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

        var sourceList = TranslationLanguages.GetLanguages(_showAllLanguages, includeAuto: true).ToList();
        var targetList = TranslationLanguages.GetLanguages(_showAllLanguages, includeAuto: false).ToList();

        TranslationSourceCombo.ItemsSource = sourceList;
        TranslationTargetCombo.ItemsSource = targetList;

        var sourceCode = string.IsNullOrWhiteSpace(_app.Settings.Translation.SourceLanguage)
            ? "auto"
            : _app.Settings.Translation.SourceLanguage;

        var targetCode = _app.Settings.Translation.TargetLanguage;

        EnsureSelectedLanguage(sourceList, sourceCode, includeAuto: true);
        EnsureSelectedLanguage(targetList, targetCode, includeAuto: false);

        TranslationSourceCombo.ItemsSource = sourceList;
        TranslationTargetCombo.ItemsSource = targetList;
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

    private static void EnsureSelectedLanguage(List<LanguageOption> list, string code, bool includeAuto)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (!includeAuto && string.Equals(code, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (list.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var known = TranslationLanguages.FindByCode(code);
        if (known != null)
        {
            list.Add(known);
            return;
        }

        list.Add(new LanguageOption(code, code.ToUpperInvariant()));
    }

    private void RebuildTranslationModes()
    {
        _suppressTranslationChange = true;

        TranslationModeCombo.ItemsSource = _modeOptions
            .Select(m => new ModeOption(m.Value, m.NameKey, m.HintKey)
            {
                DisplayName = _loc[m.NameKey]
            })
            .ToList();

        TranslationModeCombo.SelectedItem = TranslationModeCombo.ItemsSource
            .Cast<ModeOption>()
            .FirstOrDefault(m => m.Value == _app.Settings.Translation.Mode)
            ?? TranslationModeCombo.ItemsSource.Cast<ModeOption>().FirstOrDefault();

        if (TranslationModeCombo.SelectedItem is ModeOption selected)
        {
            TranslationModeHint.Text = _loc[selected.HintKey];
        }

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
        _suppressOverlayChange = true;

        OverlayOrientationCombo.ItemsSource = _overlayOptions
            .Select(o => new OverlayOption(o.Value, o.NameKey) { DisplayName = _loc[o.NameKey] })
            .ToList();

        OverlayOrientationCombo.SelectedItem = OverlayOrientationCombo.ItemsSource
            .Cast<OverlayOption>()
            .FirstOrDefault(o => o.Value == _app.Settings.Overlay.Orientation)
            ?? OverlayOrientationCombo.ItemsSource.Cast<OverlayOption>().FirstOrDefault();

        _suppressOverlayChange = false;
    }

    private static bool TryParseColor(string value, out MediaColor color)
    {
        try
        {
            var converted = MediaColorConverter.ConvertFromString(value);
            if (converted is MediaColor parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch
        {
        }

        color = default;
        return false;
    }

    private static MediaBrush ToBrush(string hex)
    {
        return TryParseColor(hex, out var color) ? new SolidColorBrush(color) : MediaBrushes.Transparent;
    }

    private void OpenWelcome_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var welcome = new WelcomeWindow();
        welcome.ShowDialog();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        _isCheckingUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = _loc["Settings.CheckingUpdates"];
        CheckUpdatesProgress.Visibility = Visibility.Visible;

        try
        {
            await _app.CheckForUpdatesAsync(showNoUpdates: true);
        }
        finally
        {
            _isCheckingUpdates = false;
            CheckUpdatesButton.IsEnabled = true;
            CheckUpdatesButton.Content = _loc["Settings.CheckUpdates"];
            CheckUpdatesProgress.Visibility = Visibility.Collapsed;
        }
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

    private sealed class ModeOption
    {
        public ModeOption(TranslationMode value, string nameKey, string hintKey)
        {
            Value = value;
            NameKey = nameKey;
            HintKey = hintKey;
            DisplayName = nameKey;
        }

        public TranslationMode Value { get; }
        public string NameKey { get; }
        public string HintKey { get; }
        public string DisplayName { get; set; }
    }

    private sealed class ThemeOption
    {
        public ThemeOption(string captionHex, string textHex, string nameKey)
        {
            CaptionHex = captionHex;
            TextHex = textHex;
            NameKey = nameKey;
            DisplayName = nameKey;
            CaptionBrush = MediaBrushes.Transparent;
            TextBrush = MediaBrushes.Transparent;
            SampleCaption = string.Empty;
            SampleBody = string.Empty;
        }

        public string CaptionHex { get; }
        public string TextHex { get; }
        public string NameKey { get; }
        public string DisplayName { get; set; }
        public MediaBrush CaptionBrush { get; set; }
        public MediaBrush TextBrush { get; set; }
        public string SampleCaption { get; set; }
        public string SampleBody { get; set; }
    }
}
