using System.Windows;

namespace UltScan;

public partial class WelcomeWindow : Window
{
    public WelcomeWindow()
    {
        InitializeComponent();

        var app = (App)System.Windows.Application.Current;
        Title = app.Localization["Welcome.Title"];
        WelcomeTitle.Text = app.Localization["Welcome.Header"];
        WelcomeBody.Text = app.Localization["Welcome.Body"];
        WelcomeAction.Text = app.Localization["Welcome.Action"];
        NextButton.Content = app.Localization["Welcome.Next"];
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        Close();
        var app = (App)System.Windows.Application.Current;
        app.ShowDemoWindowFromWelcome();
    }
}
