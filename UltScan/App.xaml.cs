using System;
using System.Collections.Generic;
using System.Globalization;
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
        private TextOverlayWindow? _overlayWindow;

        public AppSettings Settings { get; private set; } = AppSettings.Default;
        public IReadOnlyList<HotKeyPreset> HotKeyPresetList => HotKeyPresets.All;
        public LocalizationService Localization { get; private set; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Settings = AppSettings.Load();
            Localization = LocalizationService.LoadFromDisk();
            InitializeLocale();

            CreateTrayIcon();

            CreateMessageWindowForHotKeys();
            RegisterGlobalHotKey();
        }

        private void InitializeLocale()
        {
            var localeId = Settings.LocaleId;
            if (string.IsNullOrWhiteSpace(localeId))
            {
                var osLocale = CultureInfo.CurrentUICulture.Name;
                localeId = Localization.GetBestLocaleId(osLocale);
                Settings.LocaleId = localeId;
                Settings.Save();
            }

            Localization.SetLocale(localeId);
            Localization.LocaleChanged += (_, __) => UpdateLocalizedUi();
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
            if (_messageWindow == null)
            {
                return;
            }

            if (!TryApplyHotKey(Settings.HotKey, showError: false))
            {
                Settings.HotKey = HotKeyPresets.Default.ToConfig();
                TryApplyHotKey(Settings.HotKey, showError: true);
            }
        }

        public bool TryApplyHotKey(HotKeyConfig config, bool showError)
        {
            if (_messageWindow == null)
            {
                return false;
            }

            _hotKey?.Dispose();
            _hotKey = null;

            try
            {
                _hotKey = new HotKeyManager(_messageWindow, config.Modifiers, config.VirtualKey);
                _hotKey.HotKeyPressed += (_, __) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        CloseOverlayWindow();
                        ShowCaptureWindow();
                    });
                };

                return true;
            }
            catch (Exception ex)
            {
                if (showError)
                {
                    var message = string.Format(
                        Localization["Message.HotKeyRegisterFail"],
                        ex.Message);

                    System.Windows.MessageBox.Show(
                        message,
                        Localization["Message.Title"],
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return false;
            }
        }

        private void ShowCaptureWindow()
        {
            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.Closed += (_, __) => _mainWindow = null;
                _mainWindow.SelectionCompleted += (_, rect) => ShowOverlayWindow(rect);
            }

            _mainWindow.StartCaptureMode();
        }

        private void ShowOverlayWindow(Rect rect)
        {
            CloseOverlayWindow();

            _overlayWindow = new TextOverlayWindow(rect);
            _overlayWindow.Closed += (_, __) => _overlayWindow = null;
            _overlayWindow.Show();
        }

        private void CloseOverlayWindow()
        {
            if (_overlayWindow == null)
            {
                return;
            }

            _overlayWindow.Close();
            _overlayWindow = null;
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

            var settingsItem = new Forms.ToolStripMenuItem(Localization["App.Tray.Settings"]);
            settingsItem.Click += (_, __) => ShowSettingsWindow();

            var exitItem = new Forms.ToolStripMenuItem(Localization["App.Tray.Exit"]);
            exitItem.Click += (_, __) => ExitApplication();

            menu.Items.Add(settingsItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
        }

        private void UpdateLocalizedUi()
        {
            if (_notifyIcon?.ContextMenuStrip != null)
            {
                var items = _notifyIcon.ContextMenuStrip.Items;
                if (items.Count >= 3)
                {
                    items[0].Text = Localization["App.Tray.Settings"];
                    items[2].Text = Localization["App.Tray.Exit"];
                }
            }

            _settingsWindow?.RefreshLocalization();
            _mainWindow?.RefreshLocalization();
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

                CloseOverlayWindow();

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

            CloseOverlayWindow();

            _hotKey?.Dispose();
            _hotKey = null;

            CloseOverlayWindow();

            base.OnExit(e);
        }
    }
}
