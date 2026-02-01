using System.Globalization;
using System.Windows;

namespace UltScan;

public partial class OnboardingDemoWindow : Window
{
    public OnboardingDemoWindow()
    {
        InitializeComponent();

        var app = (App)System.Windows.Application.Current;
        Title = app.Localization["Demo.Title"];
        DemoTitle.Text = app.Localization["Demo.Header"];
        DemoSubtitle.Text = app.Localization["Demo.Subtitle"];

        var hotkeyLabel = app.HotKeyPresetList.Count > 0
            ? app.Localization[HotKeyPresets.FindById(app.Settings.HotKey.Id)?.LabelKey ?? "HotKey.Custom"]
            : app.Localization["HotKey.Custom"];

        DemoHint.Text = string.Format(app.Localization["Demo.Hint"], hotkeyLabel);
        DemoSampleText.Text = GetSampleText(app.Localization.CurrentLocaleId);
        DemoNextButton.Content = app.Localization["Demo.Next"];
    }

    private static string GetSampleText(string localeId)
    {
        if (!string.IsNullOrWhiteSpace(localeId) &&
            localeId.StartsWith("en", true, CultureInfo.InvariantCulture))
        {
            return "よくできました。あなたはこのテキストの翻訳に成功しました。いまは翻訳ウィンドウの「×」ボタンで閉じてから、「次へ」を押してください。";
        }

        return "You did great — you translated this text successfully. Now close the translation window using the “×” button, then click Next.";
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        Close();
        var app = (App)System.Windows.Application.Current;
        app.ShowSettingsWindowFromWelcome();
    }
}
