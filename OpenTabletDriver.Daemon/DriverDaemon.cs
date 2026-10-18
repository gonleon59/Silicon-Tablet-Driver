using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Reflection.Metadata;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Desktop.Updater;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Logging;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.SystemDrivers;

namespace OpenTabletDriver.Daemon
{
    public class DriverDaemon : IDriverDaemon
    {
        private const string AVALONIA_REVISION = "0.7.0.0";

        public DriverDaemon(Driver driver)
        {
            Driver = driver;
            _logFile = new LogFile(AppInfo.Current.LogDirectory);

            Log.Output += (sender, message) =>
            {
                _logFile.Write(message);
                Message?.Invoke(sender, message);
            };

            InitializePlatform();
            Driver.TabletsChanged += (sender, e) => TabletsChanged?.Invoke(sender, e);
            Driver.CompositeDeviceHub.DevicesChanged += async (sender, args) =>
            {
                try
                {
                    if (!args.Additions.Any()) return;

                    // Include filtering in the async event's exception boundary.
                    if (args.Additions.Any(x => Driver.KnownVendorIDs.Contains(x.VendorID)))
                    {
                        await DetectTablets();
                        await SetSettings(Settings);
                    }
                    else
                    {
                        Log.Write(nameof(DriverDaemon), "No known tablets added, skipping detect", LogLevel.Debug);
                    }
                }
                catch (Exception error) { Log.Exception(error); }
            };

            foreach (var driverInfo in DriverInfo.GetDriverInfos())
            {
                var os = SystemInterop.CurrentPlatform switch
                {
                    PluginPlatform.Windows => "Windows",
                    PluginPlatform.Linux => "Linux",
                    PluginPlatform.MacOS => "MacOS",
                    _ => null
                };
                var wikiUrl = $"https://opentabletdriver.net/Wiki/FAQ/{os}";

                var message = new StringBuilder();
                message.Append($"'{driverInfo.Name}' driver is detected.");

                if (driverInfo.Status.HasFlag(DriverStatus.Blocking))
                    message.Append(" It will block detection of tablets.");
                if (driverInfo.Status.HasFlag(DriverStatus.Flaky))
                    message.Append(" It will cause flaky support to tablets.");
                if (driverInfo.Status.HasFlag(DriverStatus.Uncertain))
                    message.Append(" It may be a false positive.");

                var processStrings = safeGetProcessDetails(driverInfo.Processes);
                message.Append($" Processes found: [" + string.Join(", ", processStrings) + "].");

                if (os != null)
                    message.Append($" If any problems arise, visit '{wikiUrl}'.");

                Log.WriteNotify("Detect", message.ToString(), LogLevel.Warning);
            }

            LoadUserSettings().Wait();

            SleepDetector.Slept += async () =>
            {
                if (System.Diagnostics.Debugger.IsAttached)
                    return;

                Log.Write(nameof(DriverDaemon), "Sleep detected...");
                try
                {
                    await DetectTablets();
                    await SetSettings(Settings);
                }
                catch (Exception error) { Log.Exception(error); }
            };
        }

        private static IEnumerable<string> safeGetProcessDetails(Process[] processes)
        {
            foreach (var driverProcess in processes)
            {
                var details = "";
                try
                {
                    details += driverProcess.ProcessName;
                }
                catch
                {
                    details += "Failed to get ProcessName";
                }

                try
                {
                    details += ": " + driverProcess.MainModule?.FileName;
                }
                catch
                {
                    details += ": Failed to get FileName";
                }
                yield return details;
            }
        }

        public event EventHandler<LogMessage>? Message;
        public event EventHandler<DebugReportData>? DeviceReport;
        public event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;
        public event EventHandler? Resynchronize;

        public Driver Driver { get; }
        private Settings? Settings { set; get; }
        private Collection<ITool> Tools { set; get; } = new Collection<ITool>();
        private readonly IUpdater? Updater = DesktopInterop.Updater;
        private readonly ISleepDetector? SleepDetector = new SleepDetector();
        private Settings? lastValidSettings;

        private UpdateInfo? _updateInfo;
        private LogFile _logFile;

        private bool debugging;

        public Task WriteMessage(LogMessage message)
        {
            Log.Write(message);
            return Task.CompletedTask;
        }

        public Task LoadPlugins()
        {
            AppInfo.PluginManager.Load();

            // Add services to inject on plugin construction
            AppInfo.PluginManager.AddService<IDriver>(() => this.Driver);
            AppInfo.PluginManager.AddService<IDriverDaemon>(() => this);

            return Task.CompletedTask;
        }

        public Task<bool> InstallPlugin(string filePath)
        {
            return Task.FromResult(AppInfo.PluginManager.InstallPlugin(filePath));
        }

        // FIXME: needs API bump: IDriverDaemon expects friendlyName but this implementation takes a full path
        public Task<bool> UninstallPlugin(string directoryPath)
        {
            var plugins = AppInfo.PluginManager.GetLoadedPlugins();
            var context = plugins.First(ctx => ctx.Directory.FullName == directoryPath);
            return Task.FromResult(AppInfo.PluginManager.UninstallPlugin(context));
        }

        public Task<bool> DownloadPlugin(PluginMetadata metadata)
        {
            return AppInfo.PluginManager.DownloadPlugin(metadata);
        }

        public Task<IEnumerable<TabletReference>> GetTablets()
        {
            return Task.FromResult(Driver.Tablets);
        }

        public async Task<IEnumerable<TabletReference>> DetectTablets()
        {
            lock (settingsGate) Driver.Detect();
            await Task.Run(CheckForProblematicProcesses).ConfigureAwait(false);

            foreach (var tablet in Driver.InputDevices)
            {
                foreach (var dev in tablet.InputDevices)
                {
                    dev.RawReport += (_, report) => PostDebugReport(tablet, report);
                    dev.RawClone = debugging;
                }
            }

            return await GetTablets();
        }

        private readonly object settingsGate = new();
        private string settingsToken = Guid.NewGuid().ToString("N");

        private void CheckSettingsToken(string expectedToken)
        {
            if (string.IsNullOrEmpty(expectedToken) || expectedToken != settingsToken)
                throw new InvalidOperationException("Settings changed. Refresh before applying or saving.");
        }

        private SettingsState CaptureSettingsState() => new(SettingsState.Copy(Settings!), settingsToken);

        public Task<SettingsState> GetSettingsState()
        {
            lock (settingsGate) return Task.FromResult(CaptureSettingsState());
        }

        public Task<SettingsState> ApplySettings(Settings settings, string expectedToken)
        {
            lock (settingsGate)
            {
                CheckSettingsToken(expectedToken);
                SetSettings(settings);
                return Task.FromResult(CaptureSettingsState());
            }
        }

        public Task<SettingsState> SaveSettings(Settings settings, string expectedToken)
        {
            lock (settingsGate)
            {
                CheckSettingsToken(expectedToken);
                var previous = SettingsState.Copy(Settings!);
                string before = JsonConvert.SerializeObject(previous);
                var candidate = SettingsState.Copy(settings);
                try
                {
                    // The apply callback completes synchronously. Keep legacy RPC writers out
                    // until both runtime application and the file commit/rollback have finished.
                    SettingsFileTransaction.SaveAsync(previous, candidate, AppInfo.Current.SettingsFile, value =>
                    {
                        ApplySettingsCore(value);
                        return Task.CompletedTask;
                    }).GetAwaiter().GetResult();
                    settingsToken = Guid.NewGuid().ToString("N");
                }
                catch (SettingsRollbackException)
                {
                    settingsToken = Guid.NewGuid().ToString("N"); // Rollback failed; invalidate every prior snapshot.
                    throw;
                }
                catch
                {
                    if (JsonConvert.SerializeObject(Settings) != before) settingsToken = Guid.NewGuid().ToString("N");
                    throw; // A successful rollback retains the revision so the draft can be retried.
                }
                finally { NotifySettingsChanged(); }
                return Task.FromResult(CaptureSettingsState());
            }
        }

        public Task SetSettings(Settings? settings)
        {
            lock (settingsGate)
            {
                var previous = lastValidSettings == null ? null : SettingsState.Copy(lastValidSettings);
                string before = JsonConvert.SerializeObject(previous);
                try
                {
                    ApplySettingsCore(SettingsState.Copy(settings ?? Settings.GetDefaults()));
                    settingsToken = Guid.NewGuid().ToString("N");
                }
                catch (Exception error)
                {
                    // One rollback (or initial recovery), never recurse into SetSettings.
                    try
                    {
                        if (previous != null) ApplySettingsCore(previous);
                        else RecoverSettings(settings);
                    }
                    catch (Exception rollback)
                    {
                        settingsToken = Guid.NewGuid().ToString("N");
                        throw new AggregateException("Settings failed to apply and the previous driver state could not be restored.", error, rollback);
                    }
                    if (JsonConvert.SerializeObject(Settings) != before) settingsToken = Guid.NewGuid().ToString("N");
                    throw;
                }
                finally { NotifySettingsChanged(); }
                return Task.CompletedTask;
            }
        }

        private void NotifySettingsChanged()
        {
            try { Resynchronize?.Invoke(this, EventArgs.Empty); }
            catch (Exception error) { Log.Exception(error); }
        }

        private void ApplySettingsCore(Settings settings)
        {
            foreach (var dev in Driver.InputDevices)
                dev.OutputMode?.Dispose();

            Settings = settings;

            foreach (var dev in Driver.InputDevices)
            {
                var tabletReference = dev.CreateReference();
                string group = dev.Properties.Name;
                var profile = Settings.Profiles[dev];

                profile.BindingSettings.MatchSpecifications(dev.Properties.Specifications);

                dev.OutputMode = profile.OutputMode.Construct<IOutputMode>(tabletReference);

                if (dev.OutputMode != null)
                    Log.Write(group, $"Output mode: {profile.OutputMode.Name}");

                if (dev.OutputMode is AbsoluteOutputMode absoluteMode)
                {
                    if (profile.AbsoluteModeSettings == null)
                        throw new InvalidOperationException($"{nameof(AbsoluteModeSettings)} not found");

                    SetAbsoluteModeSettings(dev, absoluteMode, profile.AbsoluteModeSettings);
                    if (absoluteMode.Pointer is IPressureHandler)
                        LogPressureState(group, profile);
                    if (absoluteMode.Pointer is ITiltHandler)
                        LogTiltState(group, profile);
                    if (absoluteMode.Pointer is IRotationHandler)
                        LogRotationState(group, profile);
                }

                if (dev.OutputMode is RelativeOutputMode relativeMode)
                {
                    if (profile.RelativeModeSettings == null)
                        throw new InvalidOperationException($"{nameof(RelativeModeSettings)} not found");

                    SetRelativeModeSettings(dev, relativeMode, profile.RelativeModeSettings);
                    if (relativeMode.Pointer is IPressureHandler)
                        LogPressureState(group, profile);
                    if (relativeMode.Pointer is ITiltHandler)
                        LogTiltState(group, profile);
                    if (relativeMode.Pointer is IRotationHandler)
                        LogRotationState(group, profile);
                }

                if (dev.OutputMode is { } outputMode)
                {
                    outputMode.Tablet = tabletReference;
                    var bindingHandler = CreateBindingHandler(dev, outputMode, profile.BindingSettings);
                    SetOutputModeElements(dev, outputMode, profile, bindingHandler);

                    outputMode.DisablePressure = profile.BindingSettings.DisablePressure;
                    outputMode.DisableTilt = profile.BindingSettings.DisableTilt;
                    outputMode.DisableRotation = profile.BindingSettings.DisableRotation;
                }
            }

            if (Driver.InputDevices.Length > 0)
                Log.Write("Settings", "Driver is enabled.");

            SetToolSettings();

            lastValidSettings = SettingsState.Copy(settings);
        }

        private static void LogRotationState(string group, Profile profile)
        {
            Log.Write(group,
                $"Rotation: {(profile.BindingSettings.DisableRotation ? "Disabled" : "Enabled")}");
        }

        private static void LogTiltState(string group, Profile profile)
        {
            Log.Write(group, $"Tilt: {(profile.BindingSettings.DisableTilt ? "Disabled" : "Enabled")}");
        }

        private static void LogPressureState(string group, Profile profile)
        {
            Log.Write(group,
                $"Pressure: {(profile.BindingSettings.DisablePressure ? "Disabled" : "Enabled")}");
        }

        private void RecoverSettings(Settings? settings)
        {
            var recoveredSettings = Settings.GetDefaults();

            if (settings != null)
            {
                // Copy by value, not by reference
                foreach (var profile in settings.Profiles)
                {
                    var recoveredProfile = recoveredSettings.Profiles.GetProfile(profile.Tablet);
                    if (recoveredProfile != null)
                    {
                        if (profile.AbsoluteModeSettings != null)
                        {
                            recoveredProfile.AbsoluteModeSettings = new AbsoluteModeSettings
                            {
                                Display = new AreaSettings
                                {
                                    Area = profile.AbsoluteModeSettings.Display.Area
                                },
                                Tablet = new AreaSettings
                                {
                                    Area = profile.AbsoluteModeSettings.Tablet.Area
                                },
                                EnableClipping = profile.AbsoluteModeSettings.EnableClipping,
                                EnableAreaLimiting = profile.AbsoluteModeSettings.EnableAreaLimiting,
                                LockAspectRatio = profile.AbsoluteModeSettings.LockAspectRatio
                            };
                        }

                        if (profile.RelativeModeSettings != null)
                        {
                            recoveredProfile.RelativeModeSettings = new RelativeModeSettings
                            {
                                Sensitivity = profile.RelativeModeSettings.Sensitivity,
                                RelativeRotation = profile.RelativeModeSettings.RelativeRotation,
                                ResetTime = profile.RelativeModeSettings.ResetTime
                            };
                        }
                    }
                }
            }

            ApplySettingsCore(recoveredSettings);
        }

        public async Task ResetSettings()
        {
            await SetSettings(Settings.GetDefaults());
        }

        private async Task LoadUserSettings()
        {
            AppInfo.PluginManager.Clean();
            await LoadPlugins();
            await DetectTablets();

            var appdataDir = new DirectoryInfo(AppInfo.Current.AppDataDirectory);
            if (!appdataDir.Exists)
            {
                appdataDir.Create();
                Log.Write("Settings", $"Created OpenTabletDriver application data directory: {appdataDir.FullName}");
            }
            else
            {
                Log.Write("Settings", $"Using OpenTabletDriver application data directory: {appdataDir.FullName}", LogLevel.Debug);
            }

            var settingsFile = new FileInfo(AppInfo.Current.SettingsFile);

            if (settingsFile.Exists)
            {
                if (Settings.TryDeserialize(settingsFile, out var settings) &&
                    settings.Revision != AVALONIA_REVISION)
                {
                    settings.Revision = Settings.GetVersion(); // ensure Revision matches Daemon version
                    try { await SetSettings(settings); }
                    catch (Exception error) when (lastValidSettings != null)
                    {
                        // Preserve the existing file and the recovered state on startup.
                        Log.Exception(error);
                    }
                }
                else
                {
                    Log.Write("Settings", "Invalid settings found. Moving invalid config.");
                    MoveSettingsFile();
                    await ResetSettings();
                }
            }
            else
            {
                await ResetSettings();

                // only save fresh settings if a tablet was configured
                if (Settings!.Profiles.Any())
                    Settings.Serialize(settingsFile);
            }
        }

        private static void MoveSettingsFile()
        {
            var src = AppInfo.Current.SettingsFile;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var dstFileName = $"settings_bak-{now}.json";
            var dst = Path.Join(AppInfo.Current.AppDataDirectory, dstFileName);

            Log.Write("MoveSettingsFile", $"Moving settings file at '{src}' to '{dst}'", LogLevel.Debug);
            File.Move(src, dst);
        }

        private static void SetOutputModeElements(InputDeviceTree dev, IOutputMode outputMode, Profile profile, BindingHandler bindingHandler)
        {
            string group = dev.Properties.Name;

            var pressureRewriteFilter = new PressureRewriteFilter
            {
                TipPressureThreshold = profile.BindingSettings.TipActivationThreshold,
                EraserPressureThreshold = profile.BindingSettings.EraserActivationThreshold,
                MaxPenPressure = dev.Properties.Specifications.Pen.MaxPressure,
            };

            if (OperatingSystem.IsMacOS() && dev.Properties.Name == "Wacom CTH-661" &&
                !profile.Filters.Any(s => s?.Path == typeof(OpenTabletDriver.Desktop.Touch.BambooPenStabilizer).FullName))
                profile.Filters.Add(new PluginSettingStore(new OpenTabletDriver.Desktop.Touch.BambooPenStabilizer()));

            var elements = (from store in profile.Filters
                            where store is { Enable: true }
                            let filter = store!.Construct<IPositionedPipelineElement<IDeviceReport>>(outputMode.Tablet)
                            where filter != null
                            select filter!).ToArray();

            outputMode.Elements = elements.Prepend(pressureRewriteFilter).Append(bindingHandler).ToList();

            foreach (var filter in elements)
            {
                if (filter is OpenTabletDriver.Desktop.Touch.BambooTouchNavigation touch && profile.AbsoluteModeSettings is { } absolute)
                {
                    touch.TabletRotation = absolute.Tablet.Rotation;
                    touch.DisplayIdentifiers = absolute.MappingPreferences?.DisplayIdentifiers ?? [];
                }
                var pluginSettings = profile.Filters.First(x => x?.Path == filter.GetType().FullName);
                if (pluginSettings == null) continue;

                Log.Write(group, $"Filter Settings {pluginSettings.GetHumanReadableString()}");
            }
        }

        private static void SetAbsoluteModeSettings(InputDeviceTree dev, AbsoluteOutputMode absoluteMode, AbsoluteModeSettings settings)
        {
            if (settings.MappingPreferences is { } preferences && DesktopInterop.VirtualScreen is { } screen)
            {
                settings.Display = TabletMappingPreferences.DisplayArea(screen.Displays, preferences.DisplayIdentifiers);
                settings.Tablet = preferences.TabletArea(dev.Properties.Specifications.Digitizer, settings.Display);
                settings.LockAspectRatio = preferences.PreserveProportions;
            }
            string group = dev.Properties.Name;
            absoluteMode.Output = settings.Display.Area;

            Log.Write(group, $"Display area: {absoluteMode.Output}");

            absoluteMode.Input = settings.Tablet.Area;
            Log.Write(group, $"Tablet area: {absoluteMode.Input}");

            absoluteMode.AreaClipping = settings.EnableClipping;
            Log.Write(group, $"Clipping: {(absoluteMode.AreaClipping ? "Enabled" : "Disabled")}");

            absoluteMode.AreaLimiting = settings.EnableAreaLimiting;
            Log.Write(group, $"Ignoring reports outside area: {(absoluteMode.AreaLimiting ? "Enabled" : "Disabled")}");
        }

        private static void SetRelativeModeSettings(InputDeviceTree dev, RelativeOutputMode relativeMode, RelativeModeSettings settings)
        {
            string group = dev.Properties.Name;
            relativeMode.Sensitivity = settings.Sensitivity;

            Log.Write(group, $"Relative Mode Sensitivity (X, Y): {relativeMode.Sensitivity}");

            relativeMode.Rotation = settings.RelativeRotation;
            Log.Write(group, $"Relative Mode Rotation: {relativeMode.Rotation}");

            relativeMode.ResetTime = settings.ResetTime;
            Log.Write(group, $"Reset time: {relativeMode.ResetTime}");
        }

        /// <summary>
        /// Checks for any problematic processes running on the user's computer that may
        /// impair function or detection of tablets, such as video game anti-cheat software.
        /// </summary>
        private void CheckForProblematicProcesses()
        {
            if (SystemInterop.CurrentPlatform == PluginPlatform.Windows)
            {
                if (Process.GetProcessesByName("vgc").Any())
                    Log.Write("Detect", "Valorant's anti-cheat program Vanguard is detected. Tablet function may be impaired.", LogLevel.Warning);
                if (Process.GetProcessesByName("VALORANT-Win64-Shipping").Any())
                    Log.Write("Detect", "Valorant is detected. Tablet function may be impaired.", LogLevel.Warning);
            }
        }

        private static BindingHandler CreateBindingHandler(InputDeviceTree dev, IOutputMode outputMode, BindingSettings settings)
        {
            string group = dev.Properties.Name;
            var tabletReference = outputMode.Tablet;

            Debug.Assert(tabletReference != null,
                "tabletReference was null. This was expected to be checked by the sender");

            var bindingHandler = new BindingHandler(tabletReference);

            var bindingServiceProvider = new ServiceManager();
            object? pointer = outputMode switch
            {
                AbsoluteOutputMode absoluteOutputMode => absoluteOutputMode.Pointer,
                RelativeOutputMode relativeOutputMode => relativeOutputMode.Pointer,
                _ => null
            };

            if (pointer is IMouseButtonHandler mouseButtonHandler)
                bindingServiceProvider.AddService(() => mouseButtonHandler);

            if (pointer is IMouseScrollHandler mouseScrollHandler)
                bindingServiceProvider.AddService(() => mouseScrollHandler);

            if (pointer is IPenActionHandler penActionHandler)
                bindingServiceProvider.AddService(() => penActionHandler);

            var tip = bindingHandler.Tip = new ThresholdBindingState
            {
                Binding = settings.TipButton?.Construct<IBinding>(bindingServiceProvider, tabletReference),
            };

            if (tip.Binding != null)
            {
                Log.Write(group, $"Tip Binding: [{tip.Binding}]@{settings.TipActivationThreshold}%");
            }

            var eraser = bindingHandler.Eraser = new ThresholdBindingState
            {
                Binding = settings.EraserButton?.Construct<IBinding>(bindingServiceProvider, tabletReference),
            };

            if (eraser.Binding != null)
            {
                Log.Write(group, $"Eraser Binding: [{eraser.Binding}]@{settings.EraserActivationThreshold}%");
            }

            if (settings.PenButtons.Any(b => b?.Path != null))
            {
                SetBindingHandlerCollectionSettings(bindingServiceProvider, settings.PenButtons, bindingHandler.PenButtons, tabletReference, settings.EnableDragBindings);
                Log.Write(group, $"Pen Bindings: " + string.Join(", ", bindingHandler.PenButtons.Select(b => b.Value?.Binding)));

                if (settings.EnableDragBindings)
                    Log.Write(group, "Pen Bindings are configured as drag-only (requires pen pressure to activate)");
            }

            if (!settings.DisableAuxiliaryButtons && settings.AuxButtons.Any(b => b?.Path != null))
            {
                SetBindingHandlerCollectionSettings(bindingServiceProvider, settings.AuxButtons, bindingHandler.AuxButtons, tabletReference);
                Log.Write(group, $"Express Key Bindings: " + string.Join(", ", bindingHandler.AuxButtons.Select(b => b.Value?.Binding)));
            }

            for (int wheelIndex = 0; wheelIndex < settings.WheelBindings.Count; wheelIndex++)
            {
                var wheelBindingSetting = settings.WheelBindings[wheelIndex];
                var wheelBindingHandler = bindingHandler.Wheels[wheelIndex];

                if (wheelBindingSetting.WheelButtons.Any(b => b?.Path != null))
                {
                    SetBindingHandlerCollectionSettings(bindingServiceProvider, wheelBindingSetting.WheelButtons,
                        wheelBindingHandler.WheelButtons, tabletReference);

                    Log.Write(group,
                        $"Wheel {wheelIndex + 1} Button Bindings: [" + string.Join("], [",
                            wheelBindingHandler.WheelButtons.Select(b => b.Value?.Binding)) + "]");
                }

                var clockwiseRotation = wheelBindingHandler.ClockwiseRotation = new DeltaThresholdBindingState
                {
                    Binding = wheelBindingSetting.ClockwiseRotation?.Construct<IBinding>(bindingServiceProvider,
                        tabletReference),
                    ActivationThreshold = wheelBindingSetting.ClockwiseActivationThreshold,
                    IsNegativeThreshold = false
                };

                var counterClockwiseRotation = wheelBindingHandler.CounterClockwiseRotation =
                    new DeltaThresholdBindingState
                    {
                        Binding = wheelBindingSetting.CounterClockwiseRotation?.Construct<IBinding>(
                            bindingServiceProvider, tabletReference),
                        ActivationThreshold = wheelBindingSetting.CounterClockwiseActivationThreshold,
                        IsNegativeThreshold = true
                    };

                if (clockwiseRotation.Binding != null)
                    Log.Write(group, $"Wheel {wheelIndex + 1} Clockwise Rotation: [{clockwiseRotation.Binding}]@{clockwiseRotation.ActivationThreshold}°");

                if (counterClockwiseRotation.Binding != null)
                    Log.Write(group, $"Wheel {wheelIndex + 1} Counter-Clockwise Rotation: [{counterClockwiseRotation.Binding}]@{counterClockwiseRotation.ActivationThreshold}°");
            }

            if (settings.MouseButtons.Any(b => b?.Path != null))
            {
                SetBindingHandlerCollectionSettings(bindingServiceProvider, settings.MouseButtons, bindingHandler.MouseButtons, tabletReference);
                Log.Write(group, $"Mouse Button Bindings: [" + string.Join("], [", bindingHandler.MouseButtons.Select(b => b.Value?.Binding)) + "]");
            }

            var scrollUp = bindingHandler.MouseScrollUp = new BindingState
            {
                Binding = settings.MouseScrollUp?.Construct<IBinding>(bindingServiceProvider, tabletReference)
            };

            var scrollDown = bindingHandler.MouseScrollDown = new BindingState
            {
                Binding = settings.MouseScrollDown?.Construct<IBinding>(bindingServiceProvider, tabletReference)
            };

            if (scrollUp.Binding != null || scrollDown.Binding != null)
            {
                Log.Write(group, $"Mouse Scroll: Up: [{scrollUp.Binding}] Down: [{scrollDown.Binding}]");
            }

            return bindingHandler;
        }

        private static void SetBindingHandlerCollectionSettings(IServiceManager serviceManager, PluginSettingStoreCollection collection, Dictionary<int, BindingState?> targetDict, TabletReference tabletReference, bool bindingRequiresPressure = false)
        {
            for (int index = 0; index < collection.Count; index++)
            {
                var binding = collection[index]?.Construct<IBinding>(serviceManager, tabletReference);
                var state = binding == null ? null : new BindingState
                {
                    Binding = binding,
                    RequiresPenPressure = bindingRequiresPressure,
                };

                if (!targetDict.TryAdd(index, state))
                    targetDict[index] = state;
            }
        }

        private void SetToolSettings()
        {
            foreach (var runningTool in Tools)
                runningTool.Dispose();
            Tools.Clear();

            if (Settings != null)
            {
                foreach (var store in Settings.Tools)
                {
                    if (store is not { Enable: true })
                        continue;

                    var tool = store.Construct<ITool>()
                        ?? throw new InvalidOperationException($"Could not construct {store.Name ?? store.Path} tool.");
                    try
                    {
                        if (!tool.Initialize())
                            throw new InvalidOperationException($"Failed to initialize {store.Name ?? store.Path} tool.");
                        Tools.Add(tool);
                    }
                    catch (Exception error)
                    {
                        try { tool.Dispose(); }
                        catch (Exception cleanup) { throw new AggregateException("Tool initialization and cleanup failed.",error,cleanup); }
                        throw;
                    }
                }
            }
        }

        public Task<Settings> GetSettings()
        {
            lock (settingsGate) return Task.FromResult(SettingsState.Copy(Settings!));
        }

        public Task<IEnumerable<SerializedDeviceEndpoint>> GetDevices()
        {
            return Task.FromResult(Driver.CompositeDeviceHub.GetDevices().Select(d => new SerializedDeviceEndpoint(d)));
        }

        public Task<AppInfo> GetApplicationInfo()
        {
            return Task.FromResult(AppInfo.Current);
        }

        public Task SetTabletDebug(bool enabled)
        {
            debugging = enabled;
            foreach (var dev in Driver.InputDevices.SelectMany(d => d.InputDevices))
                dev.RawClone = debugging;

            Log.Debug("Tablet", $"Tablet debugging is {(debugging ? "enabled" : "disabled")}");

            return Task.CompletedTask;
        }

        public Task<string> RequestDeviceString(int vid, int pid, int index)
        {
            var tablet = Driver.CompositeDeviceHub.GetDevices().Where(d => d.VendorID == vid && d.ProductID == pid).FirstOrDefault();
            if (tablet == null)
                throw new IOException("Device not found");

            return Task.FromResult(tablet.GetDeviceString((byte)index) ?? throw new InvalidOperationException($"Unable to look up device string on index {index}"));
        }

        public Task SendFeatureInit(int vid, int pid, string strInterface, byte[] initData)
        {
            var tablet = Driver.CompositeDeviceHub.GetDevices().FirstOrDefault(d => d.VendorID == vid && d.ProductID == pid && d.DeviceAttributes.TryGetValue("USB_INTERFACE_NUMBER", out var identifierInterface) && identifierInterface == strInterface);
            if (tablet == null)
                throw new IOException("Device not found");

            tablet.OpenAndSendFeatureInit(initData);

            return Task.CompletedTask;
        }

        public Task SendOutputInit(int vid, int pid, string strInterface, byte[] initData)
        {
            var tablet = Driver.CompositeDeviceHub.GetDevices().FirstOrDefault(d => d.VendorID == vid && d.ProductID == pid && d.DeviceAttributes.TryGetValue("USB_INTERFACE_NUMBER", out var identifierInterface) && identifierInterface == strInterface);
            if (tablet == null)
                throw new IOException("Device not found");

            tablet.OpenAndSendOutputInit(initData);

            return Task.CompletedTask;
        }

        public Task<IEnumerable<LogMessage>> GetCurrentLog()
        {
            return Task.FromResult(_logFile.Read());
        }

        public async Task<DiagnosticInfo> GetDiagnosticInfo()
        {
            var log = await GetCurrentLog();
            return new DiagnosticInfo(log, await GetDevices());
        }

        private void PostDebugReport(TabletReference tablet, IDeviceReport report)
        {
            DeviceReport?.Invoke(this, new DebugReportData(tablet, report));
        }

        public async Task<SerializedUpdateInfo?> CheckForUpdates()
        {
            if (Updater == null)
                return null;

            _updateInfo = await Updater.CheckForUpdates();
            return _updateInfo?.ToSerializedUpdateInfo();
        }

        public async Task InstallUpdate()
        {
            if (_updateInfo == null)
                throw new InvalidOperationException("No update available"); // Misbehaving client

            try
            {
                var update = await _updateInfo.GetUpdate();
                Updater?.Install(update);
            }
            finally
            {
                _updateInfo = null;
            }
        }

        public Task ForceResynchronize()
        {
            Resynchronize?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        private static void InitializePlatform()
        {
            switch (SystemInterop.CurrentPlatform)
            {
                case PluginPlatform.Windows:
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

                    if (Environment.OSVersion.Version.Build >= 22000) // Windows 11
                    {
                        unsafe
                        {
                            var state = Native.Windows.Windows.PowerThrottlingState.Create();
                            state.ControlMask = (int)Native.Windows.Windows.PowerThrottlingStateMask.IgnoreTimerResolution;

                            if (!Native.Windows.Windows.SetProcessInformation(
                                Process.GetCurrentProcess().Handle,
                                Native.Windows.Windows.ProcessInformationClass.ProcessPowerThrottling,
                                (IntPtr)Unsafe.AsPointer(ref state),
                                Unsafe.SizeOf<Native.Windows.Windows.PowerThrottlingState>()))
                            {
                                Log.Write("Platform", "Failed to allow management of timer resolution, asynchronous filters may have lower timing resolution when OTD is minimized.", LogLevel.Error);
                            }
                        }
                    }
                    break;
            }
        }
    }
}
