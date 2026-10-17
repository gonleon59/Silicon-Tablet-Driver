using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Touch;
using OpenTabletDriver.Desktop.Binding;

namespace OpenTabletDriver.UX.Controls;

/// <summary>Edits the same profile stores as the advanced filter and binding editors.</summary>
public sealed class BambooSettingsPanel : Panel
{
    private readonly Func<Profile?> getProfile;
    private readonly StackLayout rows = new() { Spacing = 8, Padding = 12, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private bool refreshing;

    public BambooSettingsPanel(Func<Profile?> getProfile)
    {
        this.getProfile = getProfile;
        Content = new Scrollable { Content = rows, Border = BorderType.None };
    }

    public void Refresh()
    {
        refreshing = true;
        try
        {
            rows.Items.Clear();
            if (getProfile() is not { } profile) return;
            var store = profile.Filters.FirstOrDefault(f => f?.Path == typeof(BambooTouchNavigation).FullName);
            if (store == null)
            {
                var enable = new Button { Text = Language.T("Enable Bamboo navigation") };
                enable.Click += (_, _) => { profile.Filters.Add(new PluginSettingStore(new BambooTouchNavigation())); Refresh(); };
                rows.Items.Add(enable);
                return;
            }
            var penStore = profile.Filters.FirstOrDefault(f => f?.Path == typeof(BambooPenStabilizer).FullName);
            if (penStore == null)
            {
                penStore = new PluginSettingStore(new BambooPenStabilizer());
                profile.Filters.Add(penStore);
            }
            var penValues = new BambooPenStabilizer();
            penStore.ApplySettings(penValues);
            AddSlider(penStore, "Smoothing", "Pen hover smoothing (ms)", penValues.Smoothing, 0, 80);
            AddSlider(penStore, "ContactSmoothing", "Pen contact smoothing (ms)", penValues.ContactSmoothing, 0, 40);
            var values = new BambooTouchNavigation();
            store.ApplySettings(values);
            var enabled = new CheckBox { Text = Language.T("Touch navigation"), Checked = store.Enable };
            enabled.CheckedChanged += (_, _) => { if (!refreshing) store.Enable = enabled.Checked == true; };
            rows.Items.Add(enabled);
            rows.Items.Add(new Label { Text = Language.T("Change settings here, then press Save / Apply.") });
            AddSlider(store, "PointerSpeed", "Pointer speed", values.PointerSpeed, 0.1f, 4);
            AddSlider(store, "FineMovementSpeed", "Fine movement speed", values.FineMovementSpeed, 0.2f, 1);
            AddSlider(store, "AccelerationStrength", "Pointer acceleration strength", values.AccelerationStrength, 0, 1);
            AddSlider(store, "PointerSmoothing", "Pointer smoothing (ms)", values.PointerSmoothing, 0, 80);
            AddSlider(store, "ScrollSpeed", "Scroll speed", values.ScrollSpeed, 0.1f, 4);
            AddSlider(store, "ScrollAccelerationStrength", "Scroll acceleration strength", values.ScrollAccelerationStrength, 0, 1);
            AddCheck(store, "PointerAcceleration", "Pointer acceleration", values.PointerAcceleration);
            AddCheck(store, "ScrollAcceleration", "Scroll acceleration", values.ScrollAcceleration);
            AddCheck(store, "NaturalScrolling", "Natural scrolling", values.NaturalScrolling);
            AddCheck(store, "ScrollInertia", "Scroll inertia", values.ScrollInertia);
            AddCheck(store, "TapToClick", "Tap to click", values.TapToClick);
            AddCommand(store, "TapAction", "One-finger tap", values.TapAction);
            AddCommand(store, "TwoFingerTapAction", "Two-finger tap", values.TwoFingerTapAction);
            AddCheck(store, "TapToDrag", "Tap twice and hold to drag", values.TapToDrag);
            AddCheck(store, "PinchToZoom", "Pinch zoom via keyboard shortcuts", values.PinchToZoom);
            AddCheck(store, "Rotate", "Rotate in Preview", values.Rotate);
            AddCheck(store, "PageSwipes", "Two-finger page swipes", values.PageSwipes);
            AddCommand(store, "SwipeRightAction", "Swipe right", values.SwipeRightAction);
            AddCommand(store, "SwipeLeftAction", "Swipe left", values.SwipeLeftAction);
            rows.Items.Add(new Label { Text = Language.T("Button actions (advanced bindings remain available in the Pen and Buttons tabs)") });
            for (int i = 0; i < profile.BindingSettings.PenButtons.Count; i++)
                AddBinding(profile.BindingSettings.PenButtons, i, Language.T("Pen button") + " " + (i + 1));
            for (int i = 0; i < profile.BindingSettings.AuxButtons.Count; i++)
                AddBinding(profile.BindingSettings.AuxButtons, i, Language.T("Tablet button") + " " + (i + 1));
            var reset = new Button { Text = Language.T("Restore recommended touch settings") };
            reset.Click += (_, _) =>
            {
                // Reset only navigation settings, preserving button bindings and monitor mapping.
                var defaults = new PluginSettingStore(new BambooTouchNavigation());
                store.Settings = defaults.Settings;
                Refresh();
            };
            rows.Items.Add(reset);
        }
        finally { refreshing = false; }
    }

    private void AddSlider(PluginSettingStore store, string property, string label, float value, float min, float max)
    {
        var slider = new Slider { MinValue = 0, MaxValue = 1000, Value = (int)(Math.Clamp((value-min)/(max-min), 0, 1)*1000) };
        var number = new NumericStepper { MinValue = min, MaxValue = max, DecimalPlaces = 2, Increment = 0.05, Value = value, Width = 85 };
        bool syncing = false;
        slider.ValueChanged += (_, _) =>
        {
            if (syncing || refreshing) return;
            syncing = true;
            number.Value = min + slider.Value / 1000f * (max-min);
            store[property].SetValue((float)number.Value);
            syncing = false;
        };
        number.ValueChanged += (_, _) =>
        {
            if (syncing || refreshing) return;
            syncing = true;
            slider.Value = (int)((number.Value-min)/(max-min)*1000);
            store[property].SetValue((float)number.Value);
            syncing = false;
        };
        rows.Items.Add(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items =
            { new Label { Text = Language.T(label), Width = 230 }, new StackLayoutItem(slider, true), number } });
    }

    private void AddCheck(PluginSettingStore store, string property, string label, bool value)
    {
        var check = new CheckBox { Text = Language.T(label), Checked = value };
        check.CheckedChanged += (_, _) => { if (!refreshing) store[property].SetValue(check.Checked == true); };
        rows.Items.Add(check);
    }

    private static readonly (TouchCommand Command, string Label)[] Commands =
    [
        (TouchCommand.None, "No action"), (TouchCommand.LeftClick, "Left click"),
        (TouchCommand.RightClick, "Right click"), (TouchCommand.DoubleClick, "Double click"),
        (TouchCommand.Back, "Back"), (TouchCommand.Forward, "Forward"),
        (TouchCommand.MissionControl, "Mission Control"), (TouchCommand.Desktop, "Show desktop"),
        (TouchCommand.PreviousTab, "Previous tab"), (TouchCommand.NextTab, "Next tab")
    ];

    private void AddCommand(PluginSettingStore store, string property, string label, TouchCommand value)
    {
        var drop = new DropDown();
        foreach (var item in Commands) drop.Items.Add(Language.T(item.Label));
        drop.SelectedIndex = Math.Max(0, Array.FindIndex(Commands, c => c.Command == value));
        drop.SelectedIndexChanged += (_, _) => { if (!refreshing && drop.SelectedIndex >= 0) store[property].SetValue(Commands[drop.SelectedIndex].Command); };
        rows.Items.Add(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items =
            { new Label { Text = Language.T(label), Width = 230 }, drop } });
    }

    private void AddBinding(PluginSettingStoreCollection bindings, int index, string label)
    {
        var drop = new DropDown();
        drop.Items.Add(Language.T("Current / custom binding"));
        foreach (var item in Commands) drop.Items.Add(Language.T(item.Label));
        var current = bindings[index];
        TouchCommand? known = null;
        if (current?.Path == null) known = TouchCommand.None;
        else if (current.Path == typeof(DoubleClickBinding).FullName) known = TouchCommand.DoubleClick;
        else if (current.Path == typeof(MouseBinding).FullName)
        {
            var button = current["Button"].GetValue<string>();
            known = button == "Left" ? TouchCommand.LeftClick : button == "Right" ? TouchCommand.RightClick : null;
        }
        else if (current.Path == typeof(MultiKeyBinding).FullName)
            known = current["Keys"].GetValue<string>() switch {
                "Application+LeftBracket" => TouchCommand.Back, "Application+RightBracket" => TouchCommand.Forward,
                "Control+Up" => TouchCommand.MissionControl, "F11" => TouchCommand.Desktop,
                "Control+PageUp" => TouchCommand.PreviousTab, "Control+PageDown" => TouchCommand.NextTab, _ => null };
        drop.SelectedIndex = known.HasValue ? Array.FindIndex(Commands, c => c.Command == known.Value)+1 : 0;
        drop.SelectedIndexChanged += (_, _) =>
        {
            if (refreshing || drop.SelectedIndex <= 0) return;
            var command = Commands[drop.SelectedIndex-1].Command;
            bindings[index] = command switch
            {
                TouchCommand.LeftClick => new PluginSettingStore(new MouseBinding { Button = "Left" }),
                TouchCommand.RightClick => new PluginSettingStore(new MouseBinding { Button = "Right" }),
                TouchCommand.DoubleClick => new PluginSettingStore(new DoubleClickBinding()),
                TouchCommand.None => new PluginSettingStore((Type?)null),
                _ => new PluginSettingStore(new MultiKeyBinding { Keys = command switch {
                    TouchCommand.Back => "Application+LeftBracket", TouchCommand.Forward => "Application+RightBracket",
                    TouchCommand.MissionControl => "Control+Up", TouchCommand.Desktop => "F11",
                    TouchCommand.PreviousTab => "Control+PageUp", _ => "Control+PageDown" } })
            };
        };
        rows.Items.Add(new StackLayout { Orientation = Orientation.Horizontal, Spacing = 8, Items = { new Label { Text = label, Width = 230 }, drop } });
    }
}
