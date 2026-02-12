using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UltScan;

public partial class TranslationSetupWizardWindow : Window
{
    private const int StepMode = 1;
    private const int StepSetup = 2;
    private const int StepCheck = 3;

    private readonly LocalizationService _loc;
    private readonly AppSettings _workingSettings;
    private bool _isChecking;
    private bool _suppressUiEvents;
    private int _currentStep = StepMode;
    private bool _lastCheckSuccess;

    public TranslationSetupWizardWindow(AppSettings sourceSettings)
    {
        InitializeComponent();

        _loc = ((App)System.Windows.Application.Current).Localization;
        _workingSettings = CloneSettings(sourceSettings);
        ResultSettings = CloneSettings(sourceSettings);

        ApplyLocalization();
        BuildProviderList();
        BindFromSettings();
        UpdateWizardUi();
    }

    public AppSettings ResultSettings { get; private set; }

    private void ApplyLocalization()
    {
        Title = _loc["Wizard.Title"];
        HeaderText.Text = _loc["Wizard.Header"];
        BodyText.Text = _loc["Wizard.Body"];

        Step1TitleText.Text = _loc["Wizard.Step1Title"];
        Step1HintText.Text = _loc["Wizard.Step1Hint"];
        ProviderLabel.Text = _loc["Wizard.Provider"];

        Step2TitleText.Text = _loc["Wizard.Step2Title"];
        Step2HintText.Text = _loc["Wizard.Step2Hint"];
        OpenEnableApiButton.Content = _loc["Wizard.OpenEnableApi"];
        OpenCredentialsButton.Content = _loc["Wizard.OpenCredentials"];
        OpenServiceAccountsButton.Content = _loc["Wizard.OpenServiceAccounts"];
        OpenCredentialsHelpText.Text = string.Empty;
        ApiKeyLabel.Text = _loc["Wizard.ApiKey"];
        ProjectIdLabel.Text = _loc["Wizard.ProjectId"];
        ServiceAccountLabel.Text = _loc["Wizard.ServiceAccount"];
        BrowseServiceAccountButton.Content = _loc["Wizard.Browse"];

        Step3TitleText.Text = _loc["Wizard.Step3Title"];
        Step3HintText.Text = _loc["Wizard.Step3Hint"];
        CheckButton.Content = _loc["Wizard.Check"];
        ResultText.Text = _loc["Wizard.NoChecks"];

        BackButton.Content = _loc["Wizard.Back"];
        NextButton.Content = _loc["Wizard.Next"];
        ApplyButton.Content = _loc["Wizard.Apply"];
        CloseButton.Content = _loc["Wizard.Close"];
    }

    private void BuildProviderList()
    {
        ProviderCombo.ItemsSource = new[]
        {
            new ProviderItem(TranslationService.ProviderApi, _loc["Settings.TranslationProviderApi"]),
            new ProviderItem(TranslationService.ProviderApiV3OAuth, _loc["Settings.TranslationProviderApiV3OAuth"]),
            new ProviderItem(TranslationService.ProviderWeb, _loc["Settings.TranslationProviderWeb"]),
            new ProviderItem(TranslationService.ProviderWebSiteExperimental, _loc["Settings.TranslationProviderWebSiteExperimental"])
        };
    }

    private void BindFromSettings()
    {
        _suppressUiEvents = true;
        try
        {
            var selectedProvider = _workingSettings.Translation.Provider;
            var item = ProviderCombo.ItemsSource
                .Cast<ProviderItem>()
                .FirstOrDefault(p => string.Equals(p.Id, selectedProvider, StringComparison.OrdinalIgnoreCase))
                ?? ProviderCombo.ItemsSource.Cast<ProviderItem>().FirstOrDefault();
            ProviderCombo.SelectedItem = item;

            ApiKeyTextBox.Text = _workingSettings.Translation.ApiKey;
            ProjectIdTextBox.Text = _workingSettings.Translation.ProjectId;
            ServiceAccountTextBox.Text = _workingSettings.Translation.ServiceAccountJsonPath;
        }
        finally
        {
            _suppressUiEvents = false;
        }

        UpdateProviderDescription();
        UpdateSetupStepByProvider();
    }

    private void ProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUiEvents || ProviderCombo.SelectedItem is not ProviderItem provider)
        {
            return;
        }

        _workingSettings.Translation.Provider = provider.Id;
        _lastCheckSuccess = false;
        ResultText.Text = _loc["Wizard.NoChecks"];
        ResultText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(68, 68, 68));
        UpdateProviderDescription();
        UpdateSetupStepByProvider();
        UpdateWizardUi();
    }

    private void ApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _workingSettings.Translation.ApiKey = ApiKeyTextBox.Text.Trim();
        InvalidateCheckState();
    }

    private void ProjectIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _workingSettings.Translation.ProjectId = ProjectIdTextBox.Text.Trim();
        InvalidateCheckState();
    }

    private void ServiceAccountTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _workingSettings.Translation.ServiceAccountJsonPath = ServiceAccountTextBox.Text.Trim();
        InvalidateCheckState();
    }

    private void BrowseServiceAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ServiceAccountTextBox.Text = dialog.FileName;
        TryFillProjectIdFromServiceAccount(dialog.FileName);
    }

    private void OpenEnableApiButton_Click(object sender, RoutedEventArgs e)
        => OpenExternal("https://console.cloud.google.com/apis/library/translate.googleapis.com");

    private void OpenCredentialsButton_Click(object sender, RoutedEventArgs e)
        => OpenExternal("https://console.cloud.google.com/apis/credentials");

    private void OpenServiceAccountsButton_Click(object sender, RoutedEventArgs e)
        => OpenExternal("https://console.cloud.google.com/iam-admin/serviceaccounts");

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > StepMode)
        {
            _currentStep--;
            UpdateWizardUi();
        }
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < StepCheck)
        {
            _currentStep++;
            UpdateWizardUi();
        }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isChecking)
        {
            return;
        }

        _isChecking = true;
        CheckButton.IsEnabled = false;
        CheckButton.Content = _loc["Wizard.CheckInProgress"];
        ResultText.Text = _loc["Wizard.CheckInProgress"];
        ResultText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(85, 85, 85));

        try
        {
            var diagnostics = await TranslationConnectionDiagnostics.RunAsync(_workingSettings.Translation);
            _lastCheckSuccess = diagnostics.IsSuccess;

            var lines = diagnostics.Checks
                .Select(c =>
                {
                    var marker = c.Ok ? "OK" : "FAIL";
                    var message = _loc[c.MessageKey];
                    return string.IsNullOrWhiteSpace(c.Details)
                        ? $"[{marker}] {message}"
                        : $"[{marker}] {message}: {c.Details}";
                })
                .ToList();

            var summaryKey = diagnostics.IsSuccess ? "Wizard.CheckSuccess" : "Wizard.CheckFailed";
            lines.Insert(0, _loc[summaryKey]);
            ResultText.Text = string.Join(Environment.NewLine, lines);
            ResultText.Foreground = diagnostics.IsSuccess
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(46, 125, 50))
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(176, 0, 32));
        }
        finally
        {
            _isChecking = false;
            CheckButton.IsEnabled = true;
            CheckButton.Content = _loc["Wizard.Check"];
            UpdateWizardUi();
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var provider = _workingSettings.Translation.Provider;
        var isCloud = string.Equals(provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(provider, TranslationService.ProviderApiV3OAuth, StringComparison.OrdinalIgnoreCase);
        if (isCloud && !_lastCheckSuccess)
        {
            System.Windows.MessageBox.Show(
                _loc["Wizard.RequireSuccessfulCheck"],
                _loc["Message.Title"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        EnsureDefaults(_workingSettings);
        ResultSettings = CloneSettings(_workingSettings);
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateWizardUi()
    {
        Step1Panel.Visibility = _currentStep == StepMode ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == StepSetup ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == StepCheck ? Visibility.Visible : Visibility.Collapsed;

        StepCounterText.Text = string.Format(_loc["Wizard.StepCounter"], _currentStep, 3);

        BackButton.IsEnabled = _currentStep > StepMode;
        NextButton.IsEnabled = _currentStep < StepCheck;
        NextButton.Visibility = _currentStep < StepCheck ? Visibility.Visible : Visibility.Collapsed;

        var provider = _workingSettings.Translation.Provider;
        var isCloud = string.Equals(provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(provider, TranslationService.ProviderApiV3OAuth, StringComparison.OrdinalIgnoreCase);
        ApplyButton.IsEnabled = !isCloud || _lastCheckSuccess;
    }

    private void UpdateProviderDescription()
    {
        var key = _workingSettings.Translation.Provider switch
        {
            TranslationService.ProviderApi => "Settings.TranslationProviderHintApi",
            TranslationService.ProviderApiV3OAuth => "Settings.TranslationProviderHintApiV3OAuth",
            TranslationService.ProviderWeb => "Settings.TranslationProviderHintWeb",
            TranslationService.ProviderWebSiteExperimental => "Settings.TranslationProviderHintWebSiteExperimental",
            _ => "Settings.TranslationProviderHintWeb"
        };

        ProviderDescriptionText.Text = _loc[key];
    }

    private void UpdateSetupStepByProvider()
    {
        var provider = _workingSettings.Translation.Provider;
        var isV2 = string.Equals(provider, TranslationService.ProviderApi, StringComparison.OrdinalIgnoreCase);
        var isV3 = string.Equals(provider, TranslationService.ProviderApiV3OAuth, StringComparison.OrdinalIgnoreCase);

        V2FieldsPanel.Visibility = isV2 ? Visibility.Visible : Visibility.Collapsed;
        V3FieldsPanel.Visibility = isV3 ? Visibility.Visible : Visibility.Collapsed;

        OpenEnableApiButton.Visibility = (isV2 || isV3) ? Visibility.Visible : Visibility.Collapsed;
        OpenCredentialsButton.Visibility = isV2 ? Visibility.Visible : Visibility.Collapsed;
        OpenServiceAccountsButton.Visibility = isV3 ? Visibility.Visible : Visibility.Collapsed;
        Step2LinksPanel.Visibility = (isV2 || isV3) ? Visibility.Visible : Visibility.Collapsed;

        Step2InstructionText.Text = isV2
            ? _loc["Wizard.Step2InstructionV2"]
            : isV3
                ? _loc["Wizard.Step2InstructionV3"]
                : _loc["Wizard.Step2InstructionWeb"];
        OpenCredentialsHelpText.Visibility = isV2 ? Visibility.Visible : Visibility.Collapsed;
        if (isV2)
        {
            OpenCredentialsHelpText.Text = _loc["Wizard.OpenCredentialsHelp"];
        }

        if (!isV2)
        {
            ApiKeyTextBox.Text = _workingSettings.Translation.ApiKey;
        }
    }

    private void TryFillProjectIdFromServiceAccount(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("project_id", out var projectId) ||
                projectId.ValueKind != JsonValueKind.String)
            {
                return;
            }

            var value = projectId.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            ProjectIdTextBox.Text = value.Trim();
        }
        catch
        {
            // Diagnostics step will show parse issues explicitly.
        }
    }

    private void OpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                string.Format(_loc["Settings.RunAsAdminFailed"], ex.Message),
                _loc["Message.Title"],
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void InvalidateCheckState()
    {
        _lastCheckSuccess = false;
        UpdateWizardUi();
    }

    private static void EnsureDefaults(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Translation.TargetLanguage))
        {
            settings.Translation.TargetLanguage = TranslationLanguages.GetBestTargetLanguage(CultureInfo.CurrentUICulture);
        }

        if (string.IsNullOrWhiteSpace(settings.Translation.SourceLanguage))
        {
            settings.Translation.SourceLanguage = "auto";
        }

        if (string.IsNullOrWhiteSpace(settings.Translation.Provider))
        {
            settings.Translation.Provider = TranslationService.ProviderWeb;
        }
    }

    private static AppSettings CloneSettings(AppSettings source)
    {
        var json = JsonSerializer.Serialize(source);
        var clone = JsonSerializer.Deserialize<AppSettings>(json);
        return clone ?? AppSettings.Default;
    }

    private sealed class ProviderItem
    {
        public ProviderItem(string id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string DisplayName { get; }
    }
}
