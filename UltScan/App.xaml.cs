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
        private Forms.ToolStripMenuItem? _captureItem;
        private Forms.ToolStripMenuItem? _repeatItem;
        private Forms.ToolStripMenuItem? _settingsItem;
        private Forms.ToolStripMenuItem? _exitItem;
        private SettingsWindow? _settingsWindow;
        private Window? _messageWindow;
        private HotKeyManager? _hotKey;
        private MainWindow? _mainWindow;
        private CaptureOverlayWindow? _captureOverlayWindow;
        private TextOverlayWindow? _overlayWindow;
        private PinButtonWindow? _pinWindow;
        private Rect? _lastCaptureRect;

        public AppSettings Settings { get; private set; } = AppSettings.Default;
        public IReadOnlyList<HotKeyPreset> HotKeyPresetList => HotKeyPresets.All;
        public LocalizationService Localization { get; private set; } = new();

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var isFirstRun = !File.Exists(AppSettings.SettingsPath);
            Settings = AppSettings.Load();
            Localization = LocalizationService.LoadFromDisk();
            InitializeLocale();
            InitializeTranslationSettings();

            CreateTrayIcon();

            CreateMessageWindowForHotKeys();
            RegisterGlobalHotKey();

            if (isFirstRun)
            {
                ShowWelcomeWindow();
            }
        }

        public void ShowSettingsWindowFromWelcome()
        {
            ShowSettingsWindow();
        }

        private void ShowWelcomeWindow()
        {
            var welcome = new WelcomeWindow();
            welcome.ShowDialog();
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

        private void InitializeTranslationSettings()
        {
            var changed = false;
            if (string.IsNullOrWhiteSpace(Settings.Translation.TargetLanguage))
            {
                Settings.Translation.TargetLanguage = TranslationLanguages.GetBestTargetLanguage(CultureInfo.CurrentUICulture);
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(Settings.Translation.SourceLanguage))
            {
                Settings.Translation.SourceLanguage = "auto";
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(Settings.Translation.Provider))
            {
                Settings.Translation.Provider = TranslationService.ProviderWeb;
                changed = true;
            }

            if (changed)
            {
                Settings.Save();
            }
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

            _lastCaptureRect = rect;
            UpdateTrayMenuText();
            _captureOverlayWindow = new CaptureOverlayWindow(rect);
            _captureOverlayWindow.Closed += (_, __) => _captureOverlayWindow = null;
            _captureOverlayWindow.Show();

            _overlayWindow = new TextOverlayWindow(rect);
            _overlayWindow.Closed += (_, __) => _overlayWindow = null;
            _overlayWindow.Show();

            var outputRect = _overlayWindow != null
                ? new Rect(_overlayWindow.Left, _overlayWindow.Top, _overlayWindow.Width, _overlayWindow.Height)
                : (Rect?)null;
            _pinWindow = new PinButtonWindow(rect, CloseOverlayWindow, SetOverlayHighlight, outputRect);
            _pinWindow.Closed += (_, __) => _pinWindow = null;
            _pinWindow.Show();
            UpdateTrayMenuText();
        }

        private void SetOverlayHighlight(bool isHighlighted)
        {
            _overlayWindow?.SetHighlight(isHighlighted);
        }

        private void CloseOverlayWindow()
        {
            if (_pinWindow != null)
            {
                _pinWindow.Close();
                _pinWindow = null;
            }

            if (_captureOverlayWindow != null)
            {
                _captureOverlayWindow.Close();
                _captureOverlayWindow = null;
            }

            if (_overlayWindow != null)
            {
                _overlayWindow.Close();
                _overlayWindow = null;
            }

            UpdateTrayMenuText();
        }

        private void CreateTrayIcon()
        {
            _notifyIcon = new Forms.NotifyIcon
            {
                Text = "UltScan",
                Visible = true,
                Icon = LoadTrayIcon()
            };
            _notifyIcon.DoubleClick += (_, __) => ShowSettingsWindow();

            var menu = new Forms.ContextMenuStrip();

            _captureItem = new Forms.ToolStripMenuItem();
            _captureItem.Click += (_, __) => ShowCaptureWindow();

            _repeatItem = new Forms.ToolStripMenuItem();
            _repeatItem.Click += (_, __) =>
            {
                if (_overlayWindow != null)
                {
                    CloseOverlayWindow();
                }
                else if (_lastCaptureRect.HasValue)
                {
                    ShowOverlayWindow(_lastCaptureRect.Value);
                }
            };

            _settingsItem = new Forms.ToolStripMenuItem(Localization["App.Tray.Settings"]);
            _settingsItem.Font = new System.Drawing.Font(_settingsItem.Font, System.Drawing.FontStyle.Bold);
            _settingsItem.Click += (_, __) => ShowSettingsWindow();

            _exitItem = new Forms.ToolStripMenuItem(Localization["App.Tray.Exit"]);
            _exitItem.Click += (_, __) => ExitApplication();

            menu.Items.Add(_captureItem);
            menu.Items.Add(_repeatItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_settingsItem);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add(_exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            UpdateTrayMenuText();
        }

        private void UpdateLocalizedUi()
        {
            if (_notifyIcon?.ContextMenuStrip != null)
            {
                UpdateTrayMenuText();
            }

            _settingsWindow?.RefreshLocalization();
            _mainWindow?.RefreshLocalization();
        }

        public void UpdateTrayMenuText()
        {
            if (_captureItem == null || _repeatItem == null || _settingsItem == null || _exitItem == null)
            {
                return;
            }

            var hotkeyLabel = FormatHotKeyLabelForMenu(GetCurrentHotKeyLabel());
            _captureItem.Text = string.Format(Localization["App.Tray.CaptureWithHotKey"], hotkeyLabel);
            if (_overlayWindow != null || _pinWindow != null || _captureOverlayWindow != null)
            {
                _repeatItem.Text = Localization["App.Tray.CloseOverlay"];
                _repeatItem.Enabled = true;
            }
            else
            {
                _repeatItem.Text = Localization["App.Tray.RepeatLast"];
                _repeatItem.Enabled = _lastCaptureRect.HasValue;
            }
            _settingsItem.Text = Localization["App.Tray.Settings"];
            _exitItem.Text = Localization["App.Tray.Exit"];
        }

        private string GetCurrentHotKeyLabel()
        {
            var preset = HotKeyPresets.FindById(Settings.HotKey.Id);
            if (preset != null)
            {
                return Localization[preset.LabelKey];
            }

            return Localization["HotKey.Custom"];
        }

        private static string FormatHotKeyLabelForMenu(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return label;
            }

            return label.Replace("Win", "Win (⊞)");
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
