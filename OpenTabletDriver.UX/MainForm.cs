using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.UX.Controls;
using StreamJsonRpc;

namespace OpenTabletDriver.UX
{
    public class MainForm : DesktopForm
    {
        public MainForm()
        {
            this.DataContext = App.Current;

            // Call InitializeForm on ctor since DesktopForm.Show() won't be called on binary launch
            InitializeForm();
            InitializePlatform();

            SetTitle();
            base.Menu = ConstructLimitedMenu();
            fullMenu = ConstructMenu();

            base.Content = placeholder;

            trayIcon?.Indicator.Show();

            saveButton = new Button(async (s, e) => await SaveSettings())
            {
                Text = Language.T("Save")
            };

            applyButton = new Button(async (s, e) => await ApplySettings())
            {
                Text = Language.T("Apply")
            };

            App.Driver.Connected += HandleDaemonConnected;
            App.Driver.Disconnected += HandleDaemonDisconnected;
            App.Driver.Resynchronize += HandleDaemonResynchronize;
            App.Driver.TabletsChanged += (sender, tablets) =>
            {
                if (!App.Driver.IsConnected) return;
                var list = tablets.ToArray();
                BambooConnected = list.Any(t => t.Properties.Name == "Wacom CTH-661");
                SetTitle(list);
            };

            Application.Instance.InvokeAsync(ConnectToDaemon);
        }

        private async Task ConnectToDaemon()
        {
            try
            {
                while (true)
                {
                    var timeout = Task.Delay(TimeSpan.FromSeconds(15));
                    var result = await Task.WhenAny(App.Driver.Connect(), timeout);

                    if (result != timeout || App.Driver.IsConnected)
                        break; // daemon connected

                    var message = SystemInterop.CurrentPlatform switch
                    {
                        PluginPlatform.Windows =>
                            "Connecting to daemon has timed out.\nVerify that OpenTabletDriver.Daemon is running or is in the same folder as OpenTabletDriver.UX\nPress OK to retry",
                        PluginPlatform.Linux =>
                            """
                            Connecting to daemon has timed out.
                            Verify that OpenTabletDriver.Daemon is running, e.g. by starting the systemd user service (usually 'systemctl --user start opentabletdriver'), or by starting 'otd-daemon'.
                            Press OK to retry
                            """,
                        _ =>
                            "Connecting to daemon has timed out. Verify that OpenTabletDriver.Daemon is running.\nPress OK to retry"
                    };

                    var dialogResult = MessageBox.Show(this, message, "Daemon Connection Error",
                        MessageBoxButtons.OKCancel, MessageBoxType.Error);

                    if (App.Driver.IsConnected) break;

                    if (dialogResult == DialogResult.Cancel)
                        Environment.Exit(1);

                    if (App.EnableDaemonWatchdog)
                        StartDaemonWatchdog();
                }

                if (!this.SkipUpdate)
                    CheckForUpdates();
            }
            catch (Exception ex)
            {
                ex.ShowMessageBox();
                Environment.Exit(2);
            }
        }

        public bool NativeSettingsReady { get; private set; }
        public bool OpenSettingsWhenReady { get; set; }
        public bool BambooConnected { get; private set; }
        public void ShowSettingsWindow()
        {
            if (Native.MacSettingsWindow.Available && !NativeSettingsReady)
            {
                // A second launch can arrive before profile synchronization finishes.
                // Keep the request pending instead of exposing the legacy editor.
                OpenSettingsWhenReady = true;
                return;
            }
            if (Native.MacSettingsWindow.Show(this)) { Visible=false; return; }
            ShowAdvancedSettings();
        }
        public void ShowAdvancedSettings() { Show(); BringToFront(); }

        private const int DEFAULT_CLIENT_WIDTH = 960;
        private const int DEFAULT_CLIENT_HEIGHT = 760;

        private readonly MenuBar fullMenu;
        private readonly Placeholder placeholder = new()
        {
            Text = Language.T("Connecting to OpenTabletDriver Daemon..."),
        };

        private TrayIcon? trayIcon;

        public bool SilenceDaemonShutdown { get; set; }
        public bool SkipUpdate { get; set; }

        protected override void InitializeForm()
        {
            var bounds = Screen.FromPoint(Mouse.Position).Bounds;

            if (this.WindowState != WindowState.Maximized)
            {
                var minWidth = Math.Min(DEFAULT_CLIENT_WIDTH, bounds.Width * 0.95);
                var minHeight = Math.Min(DEFAULT_CLIENT_HEIGHT, bounds.Height * 0.95);

                this.Size = new Size((int)minWidth, (int)minHeight);

                if (SystemInterop.CurrentPlatform == PluginPlatform.Windows)
                {
                    var x = Screen.WorkingArea.Center.X - (minWidth / 2);
                    var y = Screen.WorkingArea.Center.Y - (minHeight / 2);
                    this.Location = new Point((int)x, (int)y);
                }
            }
        }

        protected void InitializePlatform()
        {
            switch (SystemInterop.CurrentPlatform)
            {
                case PluginPlatform.Windows:
                    var programPath = AppInfo.ProgramDirectory;
                    var tempPath = Regex.Escape(Path.GetTempPath());
                    var regex = new Regex(@$"^{tempPath}Temp\d+.*?\.zip");

                    if (regex.IsMatch(programPath))
                    {
                        MessageBox.Show(this, $"You are running OpenTabletDriver.UX from a zip file.\n\nPlease extract the zip file to a folder then run OpenTabletDriver.UX from there.", "Error", MessageBoxType.Error);
                        Environment.Exit(1);
                    }
                    break;
                case PluginPlatform.MacOS:
                    this.Padding = 10;
                    break;
            }

            if (App.EnableTrayIcon)
            {
                trayIcon = new TrayIcon(this);
                if (WindowState == WindowState.Minimized)
                {
                    this.Visible = false;
                    this.ShowInTaskbar = false;
                }
                this.WindowStateChanged += (sender, e) =>
                {
                    switch (this.WindowState)
                    {
                        case WindowState.Normal:
                        case WindowState.Maximized:
                            this.Visible = true;
                            this.ShowInTaskbar = true;
                            break;
                        case WindowState.Minimized:
                            this.Visible = false;
                            this.ShowInTaskbar = false;
                            break;
                    }
                };
                Application.Instance.Terminating += (sender, e) => trayIcon.Dispose();
            }

            if (App.EnableDaemonWatchdog)
            {
                // Check if daemon is already active, if not then start it as a subprocess if it exists in the local path.
                StartDaemonWatchdog();

                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    App.DaemonWatchdog?.Dispose();
                    App.DaemonWatchdog = null;
                };

                Application.Instance.Terminating += (sender,e) =>
                {
                    App.DaemonWatchdog?.Dispose(); App.DaemonWatchdog=null;
                };
                this.Closing += (sender,e) =>
                {
                    if (!Native.MacSettingsWindow.Available) { App.DaemonWatchdog?.Dispose(); App.DaemonWatchdog=null; }
                };
            }
        }

        private static void StartDaemonWatchdog()
        {
            if (Instance.Exists("OpenTabletDriver.Daemon") || !DaemonWatchdog.CanExecute) return;

            var watchdog = new DaemonWatchdog();
            watchdog.Start();
            App.DaemonWatchdog = watchdog;
        }

        private static MenuBar ConstructLimitedMenu()
        {
            var quitCommand = new Command { MenuText = Language.T("Quit"), Shortcut = Application.Instance.CommonModifier | Keys.Q };
            quitCommand.Executed += (sender, e) => Native.MacSettingsWindow.RequestQuit();

            var aboutCommand = new Command { MenuText = Language.T("About..."), Shortcut = Keys.F1 };
            aboutCommand.Executed += (sender, e) => App.Current.AboutWindow.Show();

            var wikiUrl = new Command { MenuText = Language.T("Open Wiki...") };
            wikiUrl.Executed += (sender, e) => DesktopInterop.Open(App.WikiUrl);

            var menuBar = new MenuBar
            {
                Items =
                {
                    new ButtonMenuItem
                    {
                        Text = Language.T("&Help"),
                        Items =
                        {
                            wikiUrl,
                        }
                    }
                },
                QuitItem = quitCommand,
                AboutItem = aboutCommand
            };

            return menuBar;
        }

        private MenuBar ConstructMenu()
        {
            var quitCommand = new Command { MenuText = Language.T("Quit"), Shortcut = Application.Instance.CommonModifier | Keys.Q };
            quitCommand.Executed += (sender, e) => Native.MacSettingsWindow.RequestQuit();

            var aboutCommand = new Command { MenuText = Language.T("About..."), Shortcut = Keys.F1 };
            aboutCommand.Executed += (sender, e) => App.Current.AboutWindow.Show();

            var resetSettings = new Command { MenuText = Language.T("Reset to defaults") };
            resetSettings.Executed += async (sender, e) => await ResetSettingsDialog();

            var loadSettings = new Command { MenuText = Language.T("Load settings..."), Shortcut = Application.Instance.CommonModifier | Keys.O };
            loadSettings.Executed += async (sender, e) => await LoadSettingsDialog();

            var saveSettingsAs = new Command { MenuText = Language.T("Save settings as..."), Shortcut = Application.Instance.CommonModifier | Keys.Shift | Keys.S };
            saveSettingsAs.Executed += async (sender, e) => await SaveSettingsDialog();

            var saveSettings = new Command { MenuText = Language.T("Save settings"), Shortcut = Application.Instance.CommonModifier | Keys.S };
            saveSettings.Executed += async (sender, e) => await SaveSettings();

            var applySettings = new Command { MenuText = Language.T("Apply settings"), Shortcut = Application.Instance.CommonModifier | Keys.Enter };
            applySettings.Executed += async (sender, e) => await ApplySettings();

            var refreshPresets = new Command { MenuText = Language.T("Refresh presets") };
            refreshPresets.Executed += async (sender, e) => await RefreshPresets();

            var savePreset = new Command { MenuText = Language.T("Save as preset...") };
            savePreset.Executed += async (sender, e) => await SavePresetDialog();

            var openPresetsDirectory = new Command { MenuText = Language.T("Open presets directory...") };
            openPresetsDirectory.Executed += async (sender, e) => DesktopInterop.OpenFolder(AppInfo.Current.PresetDirectory);

            var detectTablet = new Command { MenuText = Language.T("Detect tablet"), Shortcut = Application.Instance.CommonModifier | Keys.D };
            detectTablet.Executed += async (sender, e) =>
            {
                AppInfo.Current.ConfigurationDirectory = null; // force recheck on next access
                await DetectTablet();
            };

            var showTabletDebugger = new Command { MenuText = Language.T("Tablet debugger...") };
            showTabletDebugger.Executed += (sender, e) => App.Current.DebuggerWindow.Show();

            var inputTest = new Command { MenuText = Language.T("Pen and touch test...") };
            inputTest.Executed += (_, _) => App.Current.InputTestWindow.Show();

            var deviceStringReader = new Command { MenuText = Language.T("Device string reader...") };
            deviceStringReader.Executed += (sender, e) => App.Current.StringReaderWindow.Show();

            var deviceInitSender = new Command { MenuText = Language.T("Device init sender...") };
            deviceInitSender.Executed += (sender, e) => App.Current.InitSenderWindow.Show();

            var pluginManager = new Command { MenuText = Language.T("Open Plugin Manager...") };
            pluginManager.Executed += (sender, e) => App.Current.PluginManagerWindow.Show();

            var wikiUrl = new Command { MenuText = Language.T("Open Wiki...") };
            wikiUrl.Executed += (sender, e) => DesktopInterop.Open(App.WikiUrl);

            var showGuide = new Command { MenuText = Language.T("Show guide...") };
            showGuide.Executed += (sender, e) => App.Current.StartupGreeterWindow.Show();

            var exportDiagnostics = new Command { MenuText = Language.T("Export diagnostics...") };
            exportDiagnostics.Executed += async (sender, e) => await ExportDiagnostics();

            var exportDiagnosticsToClipboard = new Command { MenuText = Language.T("Export diagnostics to Clipboard...") };
            exportDiagnosticsToClipboard.Executed += async (sender, e) => await ExportDiagnosticsToClipboard();

            var updater = new Command { MenuText = Language.T("Check for updates...") };
            updater.Executed += (sender, e) =>
            {
                if (OperatingSystem.IsMacOS())
                    MessageBox.Show(Language.T("This Bamboo preview is updated manually from its release package."), Language.T("Check for updates..."));
                else App.Current.UpdaterWindow.Show();
            };

            var menuBar = new MenuBar
            {
                Items =
                {
                    Language.CreateMenu(),
                    // File submenu
                    new ButtonMenuItem
                    {
                        Text = Language.T("&File"),
                        Items =
                        {
                            loadSettings,
                            saveSettings,
                            saveSettingsAs,
                            resetSettings,
                            applySettings,
                            new SeparatorMenuItem(),
                            savePreset,
                            refreshPresets,
                            openPresetsDirectory,
                            new ButtonMenuItem
                            {
                                Text = Language.T("Presets"),
                                Items =
                                {
                                    new ButtonMenuItem
                                    {
                                        Text = Language.T("No presets loaded"),
                                        Enabled = false
                                    }
                                }
                            }
                        }
                    },
                    // Tablets submenu
                    new ButtonMenuItem
                    {
                        Text = Language.T("Tablets"),
                        Items =
                        {
                            detectTablet,
                            showTabletDebugger,
                            inputTest,
                            deviceStringReader,
                            deviceInitSender,
                        }
                    },
                    // Plugins submenu
                    new ButtonMenuItem
                    {
                        Text = Language.T("Plugins"),
                        Items =
                        {
                            pluginManager
                        }
                    },
                    new ButtonMenuItem
                    {
                        Text = Language.T("&Help"),
                        Items =
                        {
                            wikiUrl,
                            exportDiagnostics,
                            exportDiagnosticsToClipboard,
                            showGuide
                        }
                    }
                },
                ApplicationItems =
                {
                    // application (OS X) or file menu (others)
                },
                QuitItem = quitCommand,
                AboutItem = aboutCommand
            };

            switch (SystemInterop.CurrentPlatform)
            {
                case PluginPlatform.Windows:
                case PluginPlatform.MacOS:
                {
                    menuBar.Items.GetSubmenu(Language.T("&Help")).Items.Add(updater);
                    break;
                }
            }

            return menuBar;
        }

        private void SetTitle(IEnumerable<TabletReference>? tablets = null)
        {
            string prefix = $"OpenTabletDriver v{App.Version}";
            string affix = string.Empty;

            if (tablets?.Any() ?? false)
            {
                // Limit to 3 tablets in the title
                int numTablets = Math.Min(tablets.Count(), 3);
                affix = string.Join(", ", tablets.Take(numTablets).Select(t => t.Properties.Name));
            }

            this.Title = !string.IsNullOrEmpty(affix)
                ? $"{prefix} - {affix}"
                : prefix;
        }

        // ReSharper disable once AsyncVoidMethod
        private void HandleDaemonConnected(object? sender, EventArgs e) => Application.Instance.AsyncInvoke(async void () =>
        {
            try
            {
                Debug.Assert(App.Driver.IsConnected);
                Log.Output += LogToDriver;

                // Load full menu
                this.Menu = fullMenu;

                // Load the application information from the daemon
                AppInfo.Current = await App.Driver.Instance.GetApplicationInfo();

                AppInfo.PluginManager = new DesktopPluginManager();
                AppInfo.PresetManager = new PresetManager();

                // Load any new plugins
                AppInfo.PluginManager.Load();

                // Show the startup greeter
                if (!File.Exists(AppInfo.Current.SettingsFile) && this.WindowState != WindowState.Minimized)
                    App.Current.StartupGreeterWindow.Show();

                // Synchronize settings
                await SyncSettings();

                // Set window content
                base.Content = new TabletSwitcherPanel
                {
                    CommandsControl = new StackLayout
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalContentAlignment = HorizontalAlignment.Right,
                        Spacing = 5,
                        Items =
                        {
                            Language.CreateButton(),
                            new Button((_, _) => App.Current.InputTestWindow.Show()) { Text = Language.T("Pen and touch test") },
                            saveButton,
                            applyButton,
                        }
                    }
                };

                // Update preset options in File menu and tray icon
                await RefreshPresets();

                // Update title to new instance
                var tablets=(await App.Driver.Instance.GetTablets()).ToArray();
                SetTitle(tablets);

                // Having a mismatch between the version of the daemon and the application will cause issues
                MatchDaemonVersion();
                BambooConnected=tablets?.Any(t=>t.Properties.Name=="Wacom CTH-661") ?? false;
                NativeSettingsReady=true;
                if (OpenSettingsWhenReady) { OpenSettingsWhenReady=false; ShowSettingsWindow(); }
            }
            // The disconnected handler owns recovery; transport loss is not an application error.
            catch (ConnectionLostException) { }
            catch (ObjectDisposedException) when (!App.Driver.IsConnected) { }
            catch (IOException) when (!App.Driver.IsConnected) { }
        });

        private Button saveButton;
        private Button applyButton;

        private static async void LogToDriver(object? sender, LogMessage message)
        {
            try
            {
                var daemon = App.Driver.Instance;
                if (daemon != null && App.Driver.IsConnected)
                    await daemon.WriteMessage(message);
            }
            catch (ConnectionLostException) { }
            catch (ObjectDisposedException) { }
            catch (IOException) { }
        }

        private static async void HandleDaemonResynchronize(object? sender, EventArgs e)
        {
            try { if (App.Driver.IsConnected) await SyncSettings(); }
            catch (ConnectionLostException) { }
            catch (ObjectDisposedException) when (!App.Driver.IsConnected) { }
            catch (IOException) when (!App.Driver.IsConnected) { }
        }

        private void HandleDaemonDisconnected(object? sender, EventArgs e)
        {
            NativeSettingsReady=false;
            BambooConnected=false;
            Log.Output -= LogToDriver;
            if (SilenceDaemonShutdown)
                return;

            // Hide all controls until reconnected
            Application.Instance.Invoke(() =>
            {
                var previousContent = base.Content;
                base.Content = placeholder;
                // Save/Apply buttons are reused on reconnect; detach updates without disposing them.
                if (previousContent is TabletSwitcherPanel panel) panel.StopUpdates();
                base.Menu = null;
                SetTitle(); // reset title to avoid last connected tablets from showing up

                Application.Instance.InvokeAsync(ConnectToDaemon).ConfigureAwait(false);
            });
        }

        private static async Task ResetSettings()
        {
            Debug.Assert(App.Driver.IsConnected);
            await Native.SettingsOperations.RunAsync(async () =>
            {
                await App.Driver.Instance.ResetSettings();
                await SyncSettingsCore();
            });
        }

        private static async Task ResetSettingsDialog()
        {
            if (MessageBox.Show("Reset settings to default?", "Reset to defaults", MessageBoxButtons.OKCancel, MessageBoxType.Question) == DialogResult.Ok)
                await ResetSettings();
        }

        private static Task SyncSettings() => Native.SettingsOperations.RunAsync(SyncSettingsCore);

        private static async Task SyncSettingsCore()
        {
            Debug.Assert(App.Driver.IsConnected);
            App.Current.SetSettingsState(await App.Driver.Instance.GetSettingsState());
        }

        private static async Task ChangeSettings(Func<Task> operation)
        {
            await Native.SettingsOperations.RunAsync(async () =>
            {
                try { await operation(); }
                catch (Exception error)
                {
                    Log.Exception(error);
                    if (App.Driver.IsConnected)
                        try { await SyncSettingsCore(); } catch (Exception syncError) { Log.Exception(syncError); }
                    error.ShowMessageBox();
                }
            });
        }

        private async Task LoadSettingsDialog()
        {
            var fileDialog = Extensions.OpenFileDialog(
                "Load OpenTabletDriver settings...",
                Eto.EtoEnvironment.GetFolderPath(Eto.EtoSpecialFolder.Documents),
                [new FileFilter("OpenTabletDriver Settings (*.json)", ".json")]
            );

            switch (fileDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var file = new FileInfo(fileDialog.FileName);
                    if (file.Exists)
                    {
                        if (Settings.TryDeserialize(file, out var settings))
                        {
                            await ChangeSettings(async () => App.Current.SetSettingsState(
                                await App.Driver.Instance!.ApplySettings(settings, App.Current.SettingsToken)));
                        }
                        else
                        {
                            MessageBox.Show(
                                "Invalid settings file.",
                                MessageBoxType.Error
                            );
                        }
                    }
                    break;
            }
        }

        private async Task SaveSettingsDialog()
        {
            var fileDialog = Extensions.SaveFileDialog(
                "Save OpenTabletDriver settings...",
                Eto.EtoEnvironment.GetFolderPath(Eto.EtoSpecialFolder.Documents),
                [new FileFilter("OpenTabletDriver Settings (*.json)", ".json")],
                "opentabletdriver-settings.json"
            );

            switch (fileDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var file = new FileInfo(fileDialog.FileName);
                    if (App.Current.Settings is Settings settings)
                    {
                        await ChangeSettings(async () =>
                        {
                            var state=await App.Driver.Instance!.ApplySettings(SettingsState.Copy(App.Current.Settings),App.Current.SettingsToken);
                            state.Settings.Serialize(file);
                            App.Current.SetSettingsState(state);
                        });
                    }
                    break;
            }
        }

        private async Task SaveSettings()
        {
            if (Native.MacSettingsWindow.TrySave()) return;
            DisableApplySaveButtons();

            Debug.Assert(App.Driver.IsConnected, "Save should be disabled when no driver is connected");

            if (App.Current.Settings is Settings settings)
            {
                if (settings.Profiles.Any(p => p.AbsoluteModeSettings?.Tablet.Width + p.AbsoluteModeSettings?.Tablet.Height == 0))
                {
                    var result = MessageBox.Show(
                        "Warning: Your tablet area is invalid. Saving this configuration may cause problems." + Environment.NewLine +
                        "Are you sure you want to save your configuration?",
                        MessageBoxButtons.YesNo,
                        MessageBoxType.Warning
                    );
                    if (result != DialogResult.Yes)
                        return;
                }

                await ChangeSettings(async () => App.Current.SetSettingsState(
                    await App.Driver.Instance.SaveSettings(SettingsState.Copy(App.Current.Settings),App.Current.SettingsToken)));
            }
        }

        private CancellationTokenSource _disableApplySaveButtons = new();

        private void DisableApplySaveButtons(bool disableSave = true)
        {
            Application.Instance.InvokeAsync(async () =>
            {
                await _disableApplySaveButtons.CancelAsync();
                _disableApplySaveButtons = new CancellationTokenSource();
                if (disableSave)
                    saveButton.Enabled = false;

                applyButton.Enabled = false;

                // debounce re-enabling task
                await Task.Delay(1000, _disableApplySaveButtons.Token).ContinueWith(task =>
                {
                    if (task.IsCanceled) return;

                    saveButton.Enabled = true;
                    applyButton.Enabled = true;
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }).ConfigureAwait(false);
        }

        private async Task ApplySettings()
        {
            if (Native.MacSettingsWindow.TrySave()) return;
            DisableApplySaveButtons(false);

            Debug.Assert(App.Driver.IsConnected, "Apply should be disabled when no driver is connected");

            try
            {
                if (App.Current.Settings is Settings settings)
                    await ChangeSettings(async () => App.Current.SetSettingsState(
                        await App.Driver.Instance.ApplySettings(SettingsState.Copy(App.Current.Settings),App.Current.SettingsToken)));
            }
            catch (StreamJsonRpc.RemoteInvocationException riex) when (riex.ErrorData is JObject err)
            {
                var type = (string)err["type"]!;
                var message = (string)err["message"]!;
                var stack = (string)err["stack"]!;
                var logMessage = new LogMessage
                {
                    Group = type,
                    Message = message,
                    StackTrace = stack
                };
                Log.Write(logMessage);
            }
        }

        private static void LoadPresets() => AppInfo.PresetManager.Refresh();

        private Task RefreshPresets()
        {
            LoadPresets();

            if (trayIcon != null) // Check non-Linux
                trayIcon.RefreshMenuItems();

            // Update File submenu
            var presets = AppInfo.PresetManager.GetPresets();
            var presetsMenu = fullMenu.Items.GetSubmenu(Language.T("&File")).Items.GetSubmenu(Language.T("Presets")) as ButtonMenuItem;
            presetsMenu.Items.Clear();

            if (presets.Count != 0)
            {
                foreach (var preset in presets)
                {
                    var presetItem = new ButtonMenuItem
                    {
                        Text = preset.Name
                    };
                    presetItem.Click += PresetButtonHandler;

                    presetsMenu.Items.Add(presetItem);
                }
            }
            else
            {
                var emptyPresetsItem = new ButtonMenuItem
                {
                    Text = Language.T("No presets loaded"),
                    Enabled = false
                };

                presetsMenu.Items.Add(emptyPresetsItem);
            }

            return Task.CompletedTask;
        }

        private async Task SavePresetDialog()
        {
            // TODO: this should probably use a modal dialog instead, as presets are only readable from the Preset directory
            var fileDialog = Extensions.SaveFileDialog(
                "Save OpenTabletDriver settings as preset...",
                AppInfo.Current.PresetDirectory,
                [new FileFilter("OpenTabletDriver Settings (*.json)", ".json")],
                "mypreset.json"
            );

            switch (fileDialog.ShowDialog(this))
            {
                case DialogResult.Ok:
                case DialogResult.Yes:
                    var file = new FileInfo(fileDialog.FileName + (fileDialog.FileName.EndsWith(".json") ? "" : ".json"));
                    if (App.Current.Settings is Settings settings)
                        settings.Serialize(file);
                    await RefreshPresets();
                    break;
            }
        }

        public static async void PresetButtonHandler(object? sender, EventArgs e)
        {
            var buttonMenuItem = sender as ButtonMenuItem;
            Debug.Assert(buttonMenuItem != null, "Invalid sender");
            Debug.Assert(App.Driver.IsConnected, "Preset buttons should not be available when daemon isn't connected");

            var presetName = buttonMenuItem.Text;
            var preset = AppInfo.PresetManager.FindPreset(presetName);
            Debug.Assert(preset != null, "It should be impossible to select a preset that doesn't exist");

            await ChangeSettings(async () =>
            {
                App.Current.SetSettingsState(await App.Driver.Instance.ApplySettings(SettingsState.Copy(preset.Settings),App.Current.SettingsToken));
                Log.Write("Settings", $"Applied preset '{preset.Name}'");
            });
        }

        private static async Task DetectTablet()
        {
            Debug.Assert(App.Driver.IsConnected, "It should not be possible to request tablet detection when daemon isn't connected");
            await ChangeSettings(async () =>
            {
                await App.Driver.Instance.DetectTablets();
                var state=await App.Driver.Instance.GetSettingsState();
                App.Current.SetSettingsState(await App.Driver.Instance.ApplySettings(state.Settings,state.Token));
            });
        }

        private readonly string _diagnosticsPrefix = $"Diagnostics-{App.Version.Replace(".", "")}";

        private async Task ExportDiagnostics()
        {
            Debug.Assert(App.Driver.IsConnected, "It should not be possible to export diagnostics without a connected daemon");

            try
            {
                var diagnosticDump = await App.Driver.Instance.GetDiagnosticInfo();

                var tablets = await App.Driver.Instance.GetTablets();
                var tabletReferences = tablets as TabletReference[] ?? tablets.ToArray();
                string tabletNames = tabletReferences.Length != 0
                    ? " " + string.Join(", ", tabletReferences.Select(x => x.Properties.Name))
                    : string.Empty;

                var fileDialog = Extensions.SaveFileDialog(
                    "Save diagnostic information to...",
                    Eto.EtoEnvironment.GetFolderPath(Eto.EtoSpecialFolder.Documents),
                    [new FileFilter("Diagnostic information", ".json")],
                    $"{_diagnosticsPrefix}{tabletNames}.json"
                );

                switch (fileDialog.ShowDialog(this))
                {
                    case DialogResult.Ok:
                    case DialogResult.Yes:
                        string[] options = [".json", ".txt", ".log"];
                        var file = new FileInfo(fileDialog.FileName + (options.Any(fileDialog.FileName.EndsWith) ? "" : ".json"));
                        if (file.Exists)
                            file.Delete();

                        await using (var fs = file.OpenWrite())
                        await using (var sw = new StreamWriter(fs))
                            await sw.WriteLineAsync(diagnosticDump.ToString());
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                ex.ShowMessageBox();
            }
        }
        private static async Task ExportDiagnosticsToClipboard()
        {
            Debug.Assert(App.Driver.IsConnected, "It should not be possible to export diagnostics without a connected daemon");

            try
            {
                var diagnosticDump = await App.Driver.Instance.GetDiagnosticInfo();

                Clipboard.Instance.Clear();
                Clipboard.Instance.Text = diagnosticDump.ToString();
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
                ex.ShowMessageBox();
            }
        }

        private static void MatchDaemonVersion()
        {
            Debug.Assert(App.Driver.IsConnected, "It should not be possible to fetch the daemon version without a connected daemon");

            var daemonVersionString = AppInfo.Current.Version?.ToString() ?? "Outdated";
            var versionMatch = AppInfo.Current.Version != null && AppInfo.Current.Version == App.AssemblyVersion;

            if (versionMatch == false)
            {
                var specificsTip = "You may need to restart the daemon.";

                if (App.DaemonWatchdog != null || App.AssemblyVersion < AppInfo.Current.Version)
                    specificsTip = "You may need to restart the UX.";
                else if (OperatingSystem.IsLinux())
                    specificsTip = "You may need to restart the user service or edit symlinks.";

                Log.WriteNotify("UX", $"Daemon version ({daemonVersionString}) does not match the UX version ({App.AssemblyVersion})." +
                                        Environment.NewLine + specificsTip, LogLevel.Warning);
            }
        }

        private static void CheckForUpdates()
        {
            // ReSharper disable once AsyncVoidMethod
            Application.Instance.AsyncInvoke(async void () =>
            {
                if (await App.Current.UpdaterWindow.GetWindow().HasUpdates())
                {
                    App.Current.UpdaterWindow.Show();
                }
            });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (Native.MacSettingsWindow.Available)
            {
                // Advanced settings is another window of the menu-bar app, not its lifetime owner.
                e.Cancel=true;
                Visible=false;
                return;
            }
            App.Driver.Disconnected -= HandleDaemonDisconnected;
            base.OnClosing(e);
        }
    }
}
