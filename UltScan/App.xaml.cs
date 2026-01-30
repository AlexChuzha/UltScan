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
        private Window? _messageWindow;
        private HotKeyManager? _hotKey;
        private MainWindow? _mainWindow;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            CreateTrayIcon();

            CreateMessageWindowForHotKeys();
            RegisterGlobalHotKey();
        }

        private void CreateMessageWindowForHotKeys()
        {
            _messageWindow = new Window
            {
                Width = 1,
                Height = 1,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true,
                Opacity = 0
            };

            // Важно: нужно создать handle => Show/Hide
            _messageWindow.Show();
            _messageWindow.Hide();
        }

        private void RegisterGlobalHotKey()
        {
            const uint VK_Z = 0x5A; // virtual-key code для 'Z'

            _hotKey = new HotKeyManager(
                _messageWindow!,
                ModifierKeys.Shift | ModifierKeys.Win | ModifierKeys.NoRepeat,
                VK_Z);

            _hotKey.HotKeyPressed += (_, __) =>
            {
                Dispatcher.Invoke(ShowCaptureWindow);
            };
        }

        private void ShowCaptureWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (_, __) => _mainWindow = null;
            }

            _mainWindow.StartCaptureMode();
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

            _hotKey?.Dispose();
            _hotKey = null;

            base.OnExit(e);
        }
    }
}
