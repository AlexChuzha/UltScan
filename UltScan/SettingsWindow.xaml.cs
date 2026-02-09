using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace UltScan;

public partial class SettingsWindow : Window
{
    private readonly App _app;
    private readonly LocalizationService _loc;
    private AppSettings _pendingSettings;
    private AppSettings _originalSettings;
    private bool _hasUnsavedChanges;
    private bool _suppressDirty;
    private bool _suppressLocaleChange;
    private bool _suppressTranslationChange;
    private bool _suppressAutoStartChange;
    private bool _suppressOverlayChange;
    private bool _suppressAppearanceChange;
    private bool _suppressFontChange;
    private bool _showAllLanguages;
    private bool _isCheckingUpdates;
    private readonly ProviderOption[] _providerOptions;
    private readonly OverlayOption[] _overlayOptions;
    private readonly ModeOption[] _modeOptions;
    private readonly ThemeOption[] _themeOptions;
    private readonly string _appVersionText;

    public SettingsWindow()
    {
        _app = (App)System.Windows.Application.Current;
        _loc = _app.Localization;
        _originalSettings = CloneSettings(_app.Settings);
        _pendingSettings = CloneSettings(_app.Settings);
        _appVersionText = BuildVersionText();
        _suppressOverlayChange = true;
        _suppressAppearanceChange = true;

        InitializeComponent();
        TrySetWindowIcon();
        ApplyButton.IsEnabled = false;
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

        PreviewKeyDown += SettingsWindow_PreviewKeyDown;
        Closing += SettingsWindow_Closing;
    }

    private void TrySetWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (!File.Exists(iconPath))
        {
            return;
        }

        Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
    }

    private void HotKeyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HotKeyCombo.SelectedItem is HotKeyPresetItem item)
        {
            UpdateHotKeyTexts(item.Preset);
            MarkDirty();
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

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var confirm = System.Windows.MessageBox.Show(
            _loc["Settings.ResetConfirm"],
            _loc["Message.Title"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var defaults = AppSettings.Default;
        if (string.IsNullOrWhiteSpace(defaults.LocaleId))
        {
            var osLocale = CultureInfo.CurrentUICulture.Name;
            defaults.LocaleId = _app.Localization.GetBestLocaleId(osLocale);
        }

        if (!_app.TryApplyHotKey(defaults.HotKey, showError: true))
        {
            return;
        }

        _pendingSettings = CloneSettings(defaults);
        ApplyPendingSettings();
        _originalSettings = CloneSettings(_pendingSettings);
        _hasUnsavedChanges = false;
        ApplyButton.IsEnabled = false;
        RefreshLocalization();
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

        _pendingSettings.HotKey = config;
        ApplyPendingSettings();
        _originalSettings = CloneSettings(_pendingSettings);
        _hasUnsavedChanges = false;
        ApplyButton.IsEnabled = false;
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

        _pendingSettings.LocaleId = option.Id;
        _app.Localization.SetLocale(option.Id);
        MarkDirty();
    }

    private void AutoStart_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAutoStartChange || AutoStartCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.AutoStart = AutoStartCheckBox.IsChecked.Value;
        MarkDirty();
    }

    private void TranslationEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (TranslationEnabledCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.Translation.Enabled = TranslationEnabledCheckBox.IsChecked.Value;
        UpdateTranslationWarnings();
        MarkDirty();
    }

    private void TranslationBold_Changed(object sender, RoutedEventArgs e)
    {
        if (TranslationBoldCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.Translation.TranslatedBold = TranslationBoldCheckBox.IsChecked.Value;
        MarkDirty();
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

        _pendingSettings.Translation.CaptionTextColor = option.CaptionHex;
        _pendingSettings.Translation.TranslatedTextColor = option.TextHex;
        MarkDirty();
    }
    private void TranslationFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFontChange)
        {
            return;
        }

        if (TranslationFontCombo.SelectedItem is not FontOption option)
        {
            return;
        }

        _pendingSettings.Translation.OverlayFontFamily = option.Id;
        MarkDirty();
    }

    
    private void TranslationFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFontChange)
        {
            return;
        }

        if (TranslationFontSizeCombo.SelectedItem is not FontSizeOption option)
        {
            return;
        }

        _pendingSettings.Translation.OverlayFontSize = option.Value;
        MarkDirty();
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

        _pendingSettings.Translation.Mode = option.Value;
        TranslationModeHint.Text = _loc[option.HintKey];
        MarkDirty();
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

        _pendingSettings.Translation.SourceLanguage = option.Code;
        MarkDirty();
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

        _pendingSettings.Translation.TargetLanguage = option.Code;
        MarkDirty();
    }

    private void TranslationProject_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _pendingSettings.Translation.ProjectId = TranslationProjectTextBox.Text.Trim();
        UpdateTranslationWarnings();
        MarkDirty();
    }

    private void TranslationApiKey_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _pendingSettings.Translation.ApiKey = TranslationApiKeyTextBox.Text.Trim();
        UpdateTranslationWarnings();
        MarkDirty();
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

        _pendingSettings.Translation.Provider = option.Id;
        UpdateTranslationWarnings();
        UpdateTranslationApiFields();
        MarkDirty();
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

        _pendingSettings.Overlay.Orientation = option.Value;
        MarkDirty();
    }

    private void OverlayOpacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressOverlayChange)
        {
            return;
        }

        var value = Math.Clamp(e.NewValue, 0.1, 1.0);
        _pendingSettings.Overlay.Opacity = value;
        OverlayOpacityValue.Text = $"{(int)Math.Round(value * 100)}%";
        MarkDirty();
    }

    private void OverlayCtrlResize_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressOverlayChange || OverlayCtrlResizeCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.Overlay.CtrlResizeEnabled = OverlayCtrlResizeCheckBox.IsChecked.Value;
        var showWarning = _pendingSettings.Overlay.CtrlResizeEnabled;
        OverlayCtrlResizeWarning.Visibility = showWarning
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverlayCtrlResizeHelpLink.Visibility = showWarning
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!showWarning)
        {
            OverlayCtrlResizeHelpText.Visibility = Visibility.Collapsed;
        }
        MarkDirty();
    }

    private void ExperimentalOverlayAutoHide_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressOverlayChange || ExperimentalOverlayAutoHideCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.Overlay.AutoHideWhenNoText = ExperimentalOverlayAutoHideCheckBox.IsChecked.Value;
        UpdateExperimentalOverlayAutoHideControlsState();
        MarkDirty();
    }

    private void ExperimentalOverlayDormantFrame_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressOverlayChange || ExperimentalOverlayDormantFrameCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.Overlay.AutoHideNoTextShowDormantFrame = ExperimentalOverlayDormantFrameCheckBox.IsChecked.Value;
        MarkDirty();
    }

    private void ExperimentalOverlayHideDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressOverlayChange)
        {
            return;
        }

        var value = (int)Math.Round(Math.Clamp(e.NewValue, 200, 1500));
        _pendingSettings.Overlay.AutoHideNoTextHideDelayMs = value;
        ExperimentalOverlayHideDelayValue.Text = $"{value} ms";
        MarkDirty();
    }

    private void ExperimentalOverlayShowDelay_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressOverlayChange)
        {
            return;
        }

        var value = (int)Math.Round(Math.Clamp(e.NewValue, 0, 800));
        _pendingSettings.Overlay.AutoHideNoTextShowDelayMs = value;
        ExperimentalOverlayShowDelayValue.Text = $"{value} ms";
        MarkDirty();
    }

    private void ExperimentalOverlayEmptyFrames_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressOverlayChange)
        {
            return;
        }

        var value = (int)Math.Round(Math.Clamp(e.NewValue, 1, 5));
        _pendingSettings.Overlay.AutoHideNoTextEmptyFrames = value;
        ExperimentalOverlayEmptyFramesValue.Text = value.ToString(CultureInfo.InvariantCulture);
        MarkDirty();
    }

    private void ExperimentalOverlayTestHide_Click(object sender, RoutedEventArgs e)
    {
        _app.SimulateOverlayAutoHideTextState(hasText: false);
    }

    private void ExperimentalOverlayTestShow_Click(object sender, RoutedEventArgs e)
    {
        _app.SimulateOverlayAutoHideTextState(hasText: true);
    }

    private void OverlayCtrlResizeHelp_Click(object sender, RoutedEventArgs e)
    {
        OverlayCtrlResizeHelpText.Visibility = OverlayCtrlResizeHelpText.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
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

        _pendingSettings.ExperimentalMode = ExperimentalModeCheckBox.IsChecked.Value;
        UpdateExperimentalWarning();
        MarkDirty();
    }

    private void ExperimentalPreprocess_Changed(object sender, RoutedEventArgs e)
    {
        if (ExperimentalPreprocessCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.ExperimentalImagePreprocessing = ExperimentalPreprocessCheckBox.IsChecked.Value;
        MarkDirty();
    }

    private void ExperimentalTranslationLog_Changed(object sender, RoutedEventArgs e)
    {
        if (ExperimentalTranslationLogCheckBox.IsChecked == null)
        {
            return;
        }

        _pendingSettings.ExperimentalTranslationLogging = ExperimentalTranslationLogCheckBox.IsChecked.Value;
        MarkDirty();
    }

    public void RefreshLocalization()
    {
        _suppressDirty = true;
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
        ExperimentalPreprocessCheckBox.Content = _loc["Settings.ExperimentalPreprocess"];
        ExperimentalPreprocessHint.Text = _loc["Settings.ExperimentalPreprocessHint"];
        ExperimentalTranslationLogCheckBox.Content = _loc["Settings.ExperimentalTranslationLog"];
        ExperimentalTranslationLogHint.Text = string.Format(
            _loc["Settings.ExperimentalTranslationLogHint"],
            TranslationLogger.LogsFolder);
        ExperimentalWarningText.Text = _loc["Settings.ExperimentalWarning"];
        OpenWelcomeButton.Content = _loc["Settings.OpenWelcome"];
        CheckUpdatesButton.Content = _loc["Settings.CheckUpdates"];
        VersionText.Text = string.Format(_loc["Settings.Version"], _appVersionText);
        SaveButton.Content = _loc["Settings.Save"];
        ApplyButton.Content = _loc["Settings.Apply"];
        ResetButton.Content = _loc["Settings.Reset"];
        CancelButton.Content = _loc["Settings.Cancel"];
        TranslationEnabledCheckBox.Content = _loc["Settings.TranslationEnabled"];
        TranslationBoldCheckBox.Content = _loc["Settings.TranslationBold"];
        TranslationColorLabel.Text = _loc["Settings.TranslationTextColor"];
        TranslationFontLabel.Text = _loc["Settings.TranslationFont"];
        TranslationFontSizeLabel.Text = _loc["Settings.TranslationFontSize"];
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
        OverlayCtrlResizeCheckBox.Content = _loc["Settings.OverlayCtrlResize"];
        OverlayCtrlResizeWarning.Text = _loc["Settings.OverlayCtrlResizeWarning"];
        OverlayCtrlResizeHelpLink.Text = _loc["Settings.OverlayCtrlResizeHelpLink"];
        OverlayCtrlResizeHelpText.Text = _loc["Settings.OverlayCtrlResizeHelpText"];
        ExperimentalOverlayGroupHeader.Text = _loc["Settings.ExperimentalOverlayGroupHeader"];
        ExperimentalOverlayAutoHideCheckBox.Content = _loc["Settings.ExperimentalOverlayAutoHideNoText"];
        ExperimentalOverlayOnlyTranslationHint.Text = _loc["Settings.ExperimentalOverlayAutoHideOnlyTranslation"];
        ExperimentalOverlayDormantFrameCheckBox.Content = _loc["Settings.ExperimentalOverlayDormantFrame"];
        ExperimentalOverlayHideDelayLabel.Text = _loc["Settings.ExperimentalOverlayHideDelay"];
        ExperimentalOverlayShowDelayLabel.Text = _loc["Settings.ExperimentalOverlayShowDelay"];
        ExperimentalOverlayEmptyFramesLabel.Text = _loc["Settings.ExperimentalOverlayEmptyFrames"];
        ExperimentalOverlayTestHideButton.Content = _loc["Settings.ExperimentalOverlayTestHide"];
        ExperimentalOverlayTestShowButton.Content = _loc["Settings.ExperimentalOverlayTestShow"];
        ResetHeaderText.Text = _loc["Settings.ResetHeader"];
        ResetHintText.Text = _loc["Settings.ResetHint"];

        RebuildHotKeyItems();
        RebuildTranslationLanguageLists();

        _suppressLocaleChange = true;
        var localeId = _pendingSettings.LocaleId;
        LocaleCombo.SelectedItem = _loc.Locales.FirstOrDefault(l => l.Id == localeId) ?? _loc.Locales.FirstOrDefault();
        _suppressLocaleChange = false;

        _suppressAutoStartChange = true;
        AutoStartCheckBox.IsChecked = _pendingSettings.AutoStart;
        _suppressAutoStartChange = false;

        ExperimentalModeCheckBox.IsChecked = _pendingSettings.ExperimentalMode;
        ExperimentalPreprocessCheckBox.IsChecked = _pendingSettings.ExperimentalImagePreprocessing;
        ExperimentalTranslationLogCheckBox.IsChecked = _pendingSettings.ExperimentalTranslationLogging;
        TranslationEnabledCheckBox.IsChecked = _pendingSettings.Translation.Enabled;
        TranslationBoldCheckBox.IsChecked = _pendingSettings.Translation.TranslatedBold;
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

        var currentCaption = _pendingSettings.Translation.CaptionTextColor;
        var currentText = _pendingSettings.Translation.TranslatedTextColor;
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
        RebuildFontOptions();
        RebuildFontSizeOptions();
        _suppressAppearanceChange = false;
        RebuildTranslationModes();
        TranslationProjectTextBox.Text = _pendingSettings.Translation.ProjectId;
        TranslationApiKeyTextBox.Text = _pendingSettings.Translation.ApiKey;
        UpdateExperimentalWarning();
        UpdateTranslationWarnings();
        UpdateTranslationApiFields();
        RebuildOverlayOptions();

        _suppressOverlayChange = true;
        OverlayOpacitySlider.Value = Math.Clamp(_pendingSettings.Overlay.Opacity, 0.1, 1.0);
        OverlayOpacityValue.Text = $"{(int)Math.Round(OverlayOpacitySlider.Value * 100)}%";
        OverlayCtrlResizeCheckBox.IsChecked = _pendingSettings.Overlay.CtrlResizeEnabled;
        ExperimentalOverlayAutoHideCheckBox.IsChecked = _pendingSettings.Overlay.AutoHideWhenNoText;
        ExperimentalOverlayDormantFrameCheckBox.IsChecked = _pendingSettings.Overlay.AutoHideNoTextShowDormantFrame;
        ExperimentalOverlayHideDelaySlider.Value = Math.Clamp(_pendingSettings.Overlay.AutoHideNoTextHideDelayMs, 200, 1500);
        ExperimentalOverlayHideDelayValue.Text = $"{(int)Math.Round(ExperimentalOverlayHideDelaySlider.Value)} ms";
        ExperimentalOverlayShowDelaySlider.Value = Math.Clamp(_pendingSettings.Overlay.AutoHideNoTextShowDelayMs, 0, 800);
        ExperimentalOverlayShowDelayValue.Text = $"{(int)Math.Round(ExperimentalOverlayShowDelaySlider.Value)} ms";
        ExperimentalOverlayEmptyFramesSlider.Value = Math.Clamp(_pendingSettings.Overlay.AutoHideNoTextEmptyFrames, 1, 5);
        ExperimentalOverlayEmptyFramesValue.Text = ((int)Math.Round(ExperimentalOverlayEmptyFramesSlider.Value)).ToString(CultureInfo.InvariantCulture);
        UpdateExperimentalOverlayAutoHideControlsState();
        var showWarning = _pendingSettings.Overlay.CtrlResizeEnabled;
        OverlayCtrlResizeWarning.Visibility = showWarning
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverlayCtrlResizeHelpLink.Visibility = showWarning
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (!showWarning)
        {
            OverlayCtrlResizeHelpText.Visibility = Visibility.Collapsed;
        }
        _suppressOverlayChange = false;
        _suppressDirty = false;
    }

    private static string BuildVersionText()
    {
        var version = typeof(App).Assembly.GetName().Version;
        return version == null ? "-" : $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
    }

    private void UpdateExperimentalWarning()
    {
        ExperimentalWarningText.Visibility = _pendingSettings.ExperimentalMode
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RebuildHotKeyItems()
    {
        var items = _app.HotKeyPresetList
            .Select(p => new HotKeyPresetItem(p, _loc[p.LabelKey]))
            .ToList();

        HotKeyCombo.ItemsSource = items;
        var currentId = _pendingSettings.HotKey.Id;
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

        var sourceCode = string.IsNullOrWhiteSpace(_pendingSettings.Translation.SourceLanguage)
            ? "auto"
            : _pendingSettings.Translation.SourceLanguage;

        var targetCode = _pendingSettings.Translation.TargetLanguage;

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
            .FirstOrDefault(p => p.Id == _pendingSettings.Translation.Provider)
            ?? TranslationProviderCombo.ItemsSource.Cast<ProviderOption>().FirstOrDefault();

        MoreLanguagesButton.Content = _showAllLanguages
            ? _loc["Settings.LanguagesLess"]
            : _loc["Settings.LanguagesMore"];

        _suppressTranslationChange = false;
    }
    private void RebuildFontOptions()
    {
        _suppressFontChange = true;

        var defaultLabel = _loc["Settings.TranslationFontDefault"];
        var options = Fonts.SystemFontFamilies
            .Select(f => new FontOption(f.Source, f) { DisplayName = f.Source })
            .OrderBy(o => o.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        options.Insert(0, new FontOption(string.Empty, System.Windows.SystemFonts.MessageFontFamily)
        {
            DisplayName = defaultLabel
        });

        var selected = EnsureSelectedFontOption(options, _pendingSettings.Translation.OverlayFontFamily);

        TranslationFontCombo.ItemsSource = options;
        TranslationFontCombo.SelectedItem = selected;

        _suppressFontChange = false;
    }

    
    private void RebuildFontSizeOptions()
    {
        _suppressFontChange = true;

        var sizes = new[]
        {
            10d, 11d, 12d, 13d, 14d, 16d, 18d, 20d, 22d, 24d, 28d, 32d, 64d, 96d, 128d, 160d
        };

        var options = sizes
            .Select(s => new FontSizeOption(s))
            .ToList();

        var selected = options.FirstOrDefault(o =>
            Math.Abs(o.Value - _pendingSettings.Translation.OverlayFontSize) < 0.01)
            ?? options.FirstOrDefault();

        TranslationFontSizeCombo.ItemsSource = options;
        TranslationFontSizeCombo.SelectedItem = selected ?? options.FirstOrDefault();

        _suppressFontChange = false;
    }
    private static FontOption EnsureSelectedFontOption(List<FontOption> options, string fontId)
    {
        if (string.IsNullOrWhiteSpace(fontId))
        {
            return options.FirstOrDefault() ?? new FontOption(string.Empty, System.Windows.SystemFonts.MessageFontFamily);
        }

        var existing = options.FirstOrDefault(o => string.Equals(o.Id, fontId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var fallbackFamily = TryCreateFontFamily(fontId, System.Windows.SystemFonts.MessageFontFamily);
        var inserted = new FontOption(fontId, fallbackFamily) { DisplayName = fontId };
        options.Insert(Math.Min(1, options.Count), inserted);
        return inserted;
    }

    private static System.Windows.Media.FontFamily TryCreateFontFamily(string fontId, System.Windows.Media.FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(fontId))
        {
            return fallback;
        }

        try
        {
            return new System.Windows.Media.FontFamily(fontId);
        }
        catch
        {
            return fallback;
        }
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
            .FirstOrDefault(m => m.Value == _pendingSettings.Translation.Mode)
            ?? TranslationModeCombo.ItemsSource.Cast<ModeOption>().FirstOrDefault();

        if (TranslationModeCombo.SelectedItem is ModeOption selected)
        {
            TranslationModeHint.Text = _loc[selected.HintKey];
        }

        _suppressTranslationChange = false;
    }

    private void UpdateTranslationWarnings()
    {
        var isApi = string.Equals(_pendingSettings.Translation.Provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase);
        var keyMissing = string.IsNullOrWhiteSpace(_pendingSettings.Translation.ApiKey)
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(TranslationService.ApiKeyEnvName));
        var projectMissing = string.IsNullOrWhiteSpace(_pendingSettings.Translation.ProjectId);
        var showWarning = _pendingSettings.Translation.Enabled && isApi && (keyMissing || projectMissing);

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
            .FirstOrDefault(o => o.Value == _pendingSettings.Overlay.Orientation)
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

    private void UpdateExperimentalOverlayAutoHideControlsState()
    {
        var enabled = ExperimentalOverlayAutoHideCheckBox.IsChecked == true;
        ExperimentalOverlayOnlyTranslationHint.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        ExperimentalOverlayDormantFrameCheckBox.IsEnabled = enabled;
        ExperimentalOverlayHideDelayLabel.IsEnabled = enabled;
        ExperimentalOverlayHideDelaySlider.IsEnabled = enabled;
        ExperimentalOverlayHideDelayValue.IsEnabled = enabled;
        ExperimentalOverlayShowDelayLabel.IsEnabled = enabled;
        ExperimentalOverlayShowDelaySlider.IsEnabled = enabled;
        ExperimentalOverlayShowDelayValue.IsEnabled = enabled;
        ExperimentalOverlayEmptyFramesLabel.IsEnabled = enabled;
        ExperimentalOverlayEmptyFramesSlider.IsEnabled = enabled;
        ExperimentalOverlayEmptyFramesValue.IsEnabled = enabled;
        ExperimentalOverlayTestHideButton.IsEnabled = enabled;
        ExperimentalOverlayTestShowButton.IsEnabled = enabled;
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
        var isApi = string.Equals(_pendingSettings.Translation.Provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase);
        TranslationProjectLabel.IsEnabled = isApi;
        TranslationProjectTextBox.IsEnabled = isApi;
        TranslationApiKeyLabel.IsEnabled = isApi;
        TranslationApiKeyTextBox.IsEnabled = isApi;
    }

    private void MarkDirty()
    {
        if (_suppressDirty)
        {
            return;
        }

        _hasUnsavedChanges = true;
        ApplyButton.IsEnabled = true;
    }

    private void ApplyPendingSettings()
    {
        if (!StartupManager.SetEnabled(_pendingSettings.AutoStart))
        {
            _suppressAutoStartChange = true;
            AutoStartCheckBox.IsChecked = _originalSettings.AutoStart;
            _pendingSettings.AutoStart = _originalSettings.AutoStart;
            _suppressAutoStartChange = false;
        }

        _app.Localization.SetLocale(_pendingSettings.LocaleId);
        CopySettings(_app.Settings, _pendingSettings);
        _app.Settings.Save();
        _app.UpdateTrayMenuText();
        _app.ApplyOverlayAppearance();
        _ = _app.ForceOverlayTranslationAsync();
    }

    private static AppSettings CloneSettings(AppSettings source)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(source);
        var clone = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);
        return clone ?? AppSettings.Default;
    }

    private static void CopySettings(AppSettings target, AppSettings source)
    {
        target.HotKey = new HotKeyConfig
        {
            Id = source.HotKey.Id,
            Modifiers = source.HotKey.Modifiers,
            VirtualKey = source.HotKey.VirtualKey
        };
        target.LocaleId = source.LocaleId;
        target.AutoStart = source.AutoStart;
        target.ExperimentalMode = source.ExperimentalMode;
        target.ExperimentalImagePreprocessing = source.ExperimentalImagePreprocessing;
        target.ExperimentalTranslationLogging = source.ExperimentalTranslationLogging;
        target.Overlay = new OverlaySettings
        {
            Orientation = source.Overlay.Orientation,
            Opacity = source.Overlay.Opacity,
            CtrlResizeEnabled = source.Overlay.CtrlResizeEnabled,
            AutoHideWhenNoText = source.Overlay.AutoHideWhenNoText,
            AutoHideNoTextHideDelayMs = source.Overlay.AutoHideNoTextHideDelayMs,
            AutoHideNoTextShowDelayMs = source.Overlay.AutoHideNoTextShowDelayMs,
            AutoHideNoTextEmptyFrames = source.Overlay.AutoHideNoTextEmptyFrames,
            AutoHideNoTextShowDormantFrame = source.Overlay.AutoHideNoTextShowDormantFrame
        };
        target.Translation = new TranslationSettings
        {
            Enabled = source.Translation.Enabled,
            TranslatedBold = source.Translation.TranslatedBold,
            TranslatedTextColor = source.Translation.TranslatedTextColor,
            CaptionTextColor = source.Translation.CaptionTextColor,
            OverlayFontFamily = source.Translation.OverlayFontFamily,
            OverlayFontSize = source.Translation.OverlayFontSize,
            Mode = source.Translation.Mode,
            SourceLanguage = source.Translation.SourceLanguage,
            TargetLanguage = source.Translation.TargetLanguage,
            StabilizationMs = source.Translation.StabilizationMs,
            MinChangeRatio = source.Translation.MinChangeRatio,
            PollIntervalMs = source.Translation.PollIntervalMs,
            ProjectId = source.Translation.ProjectId,
            ApiKey = source.Translation.ApiKey,
            Provider = source.Translation.Provider
        };
    }

    private void SettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_hasUnsavedChanges)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            _loc["Message.SettingsUnsaved"],
            _loc["Message.Title"],
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            if (!TryApplySettings())
            {
                e.Cancel = true;
                return;
            }
        }
        else if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
        }
        else
        {
            _pendingSettings = CloneSettings(_originalSettings);
            _app.Localization.SetLocale(_originalSettings.LocaleId);
        }
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
    private sealed class FontOption
    {
        public FontOption(string id, System.Windows.Media.FontFamily fontFamily)
        {
            Id = id;
            FontFamily = fontFamily;
            DisplayName = id;
        }

        public string Id { get; }
        public System.Windows.Media.FontFamily FontFamily { get; }
        public string DisplayName { get; set; }
    }

    private sealed class FontSizeOption
    {
        public FontSizeOption(double value)
        {
            Value = value;
            DisplayName = value % 1 == 0 ? ((int)value).ToString() : value.ToString("0.#");
        }

        public double Value { get; }
        public string DisplayName { get; }
        public override string ToString() => DisplayName;
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




