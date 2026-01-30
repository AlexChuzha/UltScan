using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace UltScan
{
    public partial class App : System.Windows.Application
    {
        private Forms.NotifyIcon? _notifyIcon;
        private SettingsWindow? _settingsWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            CreateTrayIcon();
            // Никаких окон не создаём и не показываем
        }

        private void CreateTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Text = "UltScan",
                Visible = true,
                Icon = LoadTrayIcon()
            };

            var menu = new Forms.ContextMenuStrip();

            var settingsItem = new Forms.ToolStripMenuItem("Настройки");
            settingsItem.Click += (_, __) => ShowSettingsWindow();

            var exitItem = new Forms.ToolStripMenuItem("Выход");
            exitItem.Click += (_, __) => ExitApplication();

            menu.Items.Add(settingsItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        private Icon LoadTrayIcon()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            return new Icon(path);
        }

        private void ShowSettingsWindow()
        {
            // Важно: UI действия делаем в WPF-диспетчере
            Dispatcher.Invoke(() =>
            {
                if (_settingsWindow == null)
                {
                    _settingsWindow = new SettingsWindow();
                    _settingsWindow.Closed += (_, __) => _settingsWindow = null;
                }

                _settingsWindow.Show();
                if (_settingsWindow.WindowState == WindowState.Minimized)
                    _settingsWindow.WindowState = WindowState.Normal;

                _settingsWindow.Activate();
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;
                _settingsWindow.Focus();
            });
        }

        private void ExitApplication()
        {
            Dispatcher.Invoke(() =>
            {
                if (_settingsWindow != null)
                {
                    _settingsWindow.Close();
                    _settingsWindow = null;
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                Shutdown();
            });
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }

            base.OnExit(e);
        }
    }
}
