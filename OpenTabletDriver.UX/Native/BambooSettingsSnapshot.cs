using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Touch;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.UX.Native;

/// <summary>Small, typed boundary between the SwiftUI view and the existing driver profile.</summary>
public sealed class BambooSettingsSnapshot
{
    public string Profile { get; set; } = "Wacom CTH-661";
    public string Version { get; set; } = "";
    public string ProfileToken { get; set; } = "";
    public string SettingsToken { get; set; } = "";
    public static string TokenFor(Profile profile) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(profile))));
    public string Language { get; set; } = "en";
    public bool Connected { get; set; }
    public bool HasProfile { get; set; }
    public bool InputGranted { get; set; }
    public bool AccessibilityGranted { get; set; }
    public NativeDisplay[] Displays { get; set; } = [];
    public bool UseMapping { get; set; }
    public bool HasManualArea { get; set; }
    // Request-scoped edit intent; never persisted in the tablet profile.
    public bool MappingEdited { get; set; }
    public bool AllDisplays { get; set; }
    public string[] DisplayIds { get; set; } = [];
    public int Rotation { get; set; }
    public double AreaScale { get; set; } = 0.85;
    public double EdgeMargin { get; set; } = 8;
    public double HorizontalSideMargin { get; set; } = TabletMappingPreferences.DefaultHorizontalSideMargin;
    public double HorizontalTopBottomMargin { get; set; } = TabletMappingPreferences.DefaultHorizontalTopBottomMargin;
    public bool NearUser { get; set; } = true;
    public bool Proportional { get; set; } = true;
    public bool TouchEnabled { get; set; } = true;
    public Dictionary<string, double> Touch { get; set; } = [];
    public Dictionary<string, bool> Flags { get; set; } = [];
    public Dictionary<string, int> Commands { get; set; } = [];
    public double HoverSmoothing { get; set; } = 35;
    public double ContactSmoothing { get; set; } = 12;
    public double TipThreshold { get; set; } = 1;
    public double EraserThreshold { get; set; } = 1;
    public bool PressureEnabled { get; set; } = true;
    public int[] PenButtons { get; set; } = [];
    public int[] BodyButtons { get; set; } = [];

    private static readonly string[] NumberProperties = ["PointerSpeed", "FineMovementSpeed", "AccelerationStrength", "PointerSmoothing", "ScrollSpeed", "ScrollAccelerationStrength"];
    private static readonly string[] FlagProperties = ["PointerAcceleration", "ScrollAcceleration", "NaturalScrolling", "ScrollInertia", "TapToClick", "TapToDrag", "PinchToZoom", "Rotate", "PageSwipes"];
    private static readonly string[] CommandProperties = ["TapAction", "TwoFingerTapAction", "SwipeRightAction", "SwipeLeftAction"];

    public static BambooSettingsSnapshot FromProfile(Profile profile, NativeDisplay[]? displays = null)
    {
        var touchStore = profile.Filters.FirstOrDefault(f => f?.Path == typeof(BambooTouchNavigation).FullName);
        var touch = new BambooTouchNavigation();
        touchStore?.ApplySettings(touch);
        var pen = new BambooPenStabilizer();
        profile.Filters.FirstOrDefault(f => f?.Path == typeof(BambooPenStabilizer).FullName)?.ApplySettings(pen);
        var mapping = profile.AbsoluteModeSettings?.MappingPreferences;
        var selected = mapping?.DisplayIdentifiers ?? SelectedDisplays(profile, displays ?? []);
        return new()
        {
            Profile = profile.Tablet, ProfileToken=TokenFor(profile), HasProfile = true, TouchEnabled = touchStore?.Enable ?? false,
            UseMapping = mapping?.UsesComfortableArea == true, HasManualArea = mapping == null, AllDisplays = mapping != null ? selected.Length == 0 : displays is { Length: > 0 } && selected.Length == displays.Length,
            DisplayIds = selected, Displays = displays ?? [], Rotation = mapping?.Rotation ?? (int)(profile.AbsoluteModeSettings?.Tablet.Rotation ?? 0),
            AreaScale = mapping?.AreaScale ?? 0.85, EdgeMargin = mapping?.EdgeMargin ?? 8,
            HorizontalSideMargin = mapping?.HorizontalSideMargin ?? TabletMappingPreferences.DefaultHorizontalSideMargin,
            HorizontalTopBottomMargin = mapping?.HorizontalTopBottomMargin ?? TabletMappingPreferences.DefaultHorizontalTopBottomMargin,
            NearUser = mapping?.NearUser ?? true, Proportional = mapping?.PreserveProportions ?? true,
            Touch = NumberProperties.ToDictionary(p => p, p => Convert.ToDouble(typeof(BambooTouchNavigation).GetProperty(p)!.GetValue(touch))),
            Flags = FlagProperties.ToDictionary(p => p, p => (bool)typeof(BambooTouchNavigation).GetProperty(p)!.GetValue(touch)!),
            Commands = CommandProperties.ToDictionary(p => p, p => (int)(TouchCommand)typeof(BambooTouchNavigation).GetProperty(p)!.GetValue(touch)!),
            HoverSmoothing = pen.Smoothing, ContactSmoothing = pen.ContactSmoothing,
            TipThreshold = profile.BindingSettings.TipActivationThreshold, EraserThreshold = profile.BindingSettings.EraserActivationThreshold,
            PressureEnabled = !profile.BindingSettings.DisablePressure,
            PenButtons = profile.BindingSettings.PenButtons.Select(BindingCommand).ToArray(),
            BodyButtons = profile.BindingSettings.AuxButtons.Select(BindingCommand).ToArray()
        };
    }

    private static string[] SelectedDisplays(Profile profile, NativeDisplay[] displays)
    {
        if (profile.AbsoluteModeSettings?.Display is not { } area) return [];
        // Manual profiles have no stored monitor IDs. Recover the selection from their
        // existing output rectangle rather than silently selecting every screen.
        return displays.Where(d => d.X < area.X + area.Width / 2 && d.X + d.Width > area.X - area.Width / 2 &&
            d.Y < area.Y + area.Height / 2 && d.Y + d.Height > area.Y - area.Height / 2).Select(d => d.Id).ToArray();
    }

    public void ApplyTo(Profile profile, DigitizerSpecifications digitizer)
    {
        if (profile.Tablet != Profile || Profile != "Wacom CTH-661") throw new InvalidOperationException("The tablet profile has changed. Reopen settings.");
        // Validate before changing any part of the candidate profile.
        foreach (var p in NumberProperties)
        {
            if (!Touch.TryGetValue(p, out double v) || !double.IsFinite(v)) throw new ArgumentException("Invalid touch setting: " + p);
            var range = p switch { "PointerSmoothing" => (0d,80d), "FineMovementSpeed" => (0.2d,1d), "AccelerationStrength" or "ScrollAccelerationStrength" => (0d,2d), _ => (0.1d,10d) };
            if (v < range.Item1 || v > range.Item2) throw new ArgumentOutOfRangeException(p);
        }
        if (FlagProperties.Any(p => !Flags.ContainsKey(p)) || CommandProperties.Any(p => !Commands.TryGetValue(p, out int v) || !Enum.IsDefined((TouchCommand)v)))
            throw new ArgumentException("Incomplete gesture settings.");
        Validate(HoverSmoothing,0,80); Validate(ContactSmoothing,0,40); Validate(TipThreshold,0,100); Validate(EraserThreshold,0,100);
        bool applyMapping = MappingEdited || UseMapping || profile.AbsoluteModeSettings?.MappingPreferences != null;
        if (applyMapping)
        {
            Validate(AreaScale,0.3,1); Validate(EdgeMargin,0,30);
            Validate(HorizontalSideMargin,0,30); Validate(HorizontalTopBottomMargin,0,30);
            if (Rotation is not (0 or 180 or 270)) throw new ArgumentOutOfRangeException(nameof(Rotation));
        }
        if (applyMapping && !AllDisplays && DisplayIds.Length == 0) throw new ArgumentException("Select at least one display.");
        if (PenButtons.Length != profile.BindingSettings.PenButtons.Count || BodyButtons.Length != profile.BindingSettings.AuxButtons.Count ||
            PenButtons.Concat(BodyButtons).Any(v => v != -1 && !Enum.IsDefined((TouchCommand)v))) throw new ArgumentException("Invalid button assignment.");
        var touch = GetOrAdd<BambooTouchNavigation>(profile);
        touch.Enable = TouchEnabled;
        foreach (var p in NumberProperties) touch[p].SetValue((float)Touch[p]);
        foreach (var p in FlagProperties) touch[p].SetValue(Flags[p]);
        foreach (var p in CommandProperties) touch[p].SetValue((TouchCommand)Commands[p]);
        var pen = GetOrAdd<BambooPenStabilizer>(profile);
        pen["Smoothing"].SetValue((float)HoverSmoothing);
        pen["ContactSmoothing"].SetValue((float)ContactSmoothing);
        profile.BindingSettings.TipActivationThreshold = (float)TipThreshold;
        profile.BindingSettings.EraserActivationThreshold = (float)EraserThreshold;
        profile.BindingSettings.DisablePressure = !PressureEnabled;
        ApplyButtons(profile.BindingSettings.PenButtons, PenButtons);
        ApplyButtons(profile.BindingSettings.AuxButtons, BodyButtons);
        if (profile.AbsoluteModeSettings is { } absolute)
        {
            if (!applyMapping) return; // Unrelated edits preserve explicitly configured manual geometry.
            absolute.MappingPreferences = new TabletMappingPreferences
            {
                DisplayIdentifiers = AllDisplays ? [] : DisplayIds.Distinct().ToArray(), Rotation = Rotation,
                UseComfortableArea = Rotation == 270 && UseMapping,
                HorizontalSideMargin = (float)HorizontalSideMargin, HorizontalTopBottomMargin = (float)HorizontalTopBottomMargin,
                AreaScale = (float)AreaScale, EdgeMargin = (float)EdgeMargin, NearUser = NearUser, PreserveProportions = Proportional
            };
            if (absolute.MappingPreferences is { } mapping)
            {
                // Geometry is calculated by the shared profile model, not by the native view.
                absolute.Display = TabletMappingPreferences.DisplayArea(Displays.Select(d => d.AsDisplay()), mapping.DisplayIdentifiers);
                absolute.Tablet = mapping.TabletArea(digitizer, absolute.Display);
                absolute.LockAspectRatio = mapping.UsesComfortableArea && Proportional;
                profile.BindingSettings.DisableAuxiliaryButtons = Rotation == 270;
            }
        }
    }

    private static void Validate(double value,double min,double max)
    { if (!double.IsFinite(value) || value < min || value > max) throw new ArgumentOutOfRangeException(nameof(value)); }
    private static PluginSettingStore GetOrAdd<T>(Profile profile) where T : new()
    {
        var store = profile.Filters.FirstOrDefault(f => f?.Path == typeof(T).FullName);
        if (store != null) return store;
        store = new PluginSettingStore(new T()); profile.Filters.Add(store); return store;
    }
    private static void ApplyButtons(PluginSettingStoreCollection stores, int[] commands)
    {
        for (int i=0;i<commands.Length;i++)
        {
            if (commands[i] == -1 || BindingCommand(stores[i]) == commands[i]) continue; // Preserve custom/adaptive bindings.
            stores[i] = CreateBinding((TouchCommand)commands[i]);
        }
    }
    public static int BindingCommand(PluginSettingStore? store)
    {
        if (store?.Path == null) return (int)TouchCommand.None;
        if (store.Path == typeof(DoubleClickBinding).FullName) return (int)TouchCommand.DoubleClick;
        if (store.Path == typeof(AdaptiveBinding).FullName && store.Settings.FirstOrDefault(s => s.Property == "Binding")?.GetValue<string>() == "Button 1") return (int)TouchCommand.RightClick;
        if (store.Path == typeof(MouseBinding).FullName) return store.Settings.FirstOrDefault(s => s.Property == "Button")?.GetValue<string>() switch { "Left" => 1, "Right" => 2, _ => -1 };
        if (store.Path == typeof(MultiKeyBinding).FullName) return store.Settings.FirstOrDefault(s => s.Property == "Keys")?.GetValue<string>() switch
        { "Application+LeftBracket" => 4, "Application+RightBracket" => 5, "Control+Up" => 6, "F11" => 7, "Control+PageUp" => 8, "Control+PageDown" => 9, _ => -1 };
        return -1;
    }
    private static PluginSettingStore CreateBinding(TouchCommand command) => command switch
    {
        TouchCommand.None => new PluginSettingStore((Type?)null),
        TouchCommand.LeftClick => new PluginSettingStore(new MouseBinding { Button="Left" }),
        TouchCommand.RightClick => new PluginSettingStore(new MouseBinding { Button="Right" }),
        TouchCommand.DoubleClick => new PluginSettingStore(new DoubleClickBinding()),
        _ => new PluginSettingStore(new MultiKeyBinding { Keys=command switch
        { TouchCommand.Back=>"Application+LeftBracket",TouchCommand.Forward=>"Application+RightBracket",TouchCommand.MissionControl=>"Control+Up",TouchCommand.Desktop=>"F11",TouchCommand.PreviousTab=>"Control+PageUp",_=>"Control+PageDown" } })
    };
}

public sealed record NativeDisplay(string Id,string Name,float Width,float Height,float X,float Y)
{
    public OpenTabletDriver.Plugin.Platform.Display.IDisplay AsDisplay() => new DisplayAdapter(this);
    private sealed record DisplayAdapter(NativeDisplay Value) : OpenTabletDriver.Plugin.Platform.Display.IDisplay
    {
        public string Identifier=>Value.Id; public string Name=>Value.Name; public int Index=>0;
        public float Width=>Value.Width; public float Height=>Value.Height;
        public System.Numerics.Vector2 Position=>new(Value.X,Value.Y);
    }
}
