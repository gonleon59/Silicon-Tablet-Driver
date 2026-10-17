using System;
using System.Collections.Generic;
using System.Linq;
using Eto.Forms;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Interop.Display;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.UX.Controls.Output;

/// <summary>Convenient presets; the detailed area editors remain available below.</summary>
public sealed class TabletMappingPanel : Panel
{
    private readonly Func<Profile?> getProfile;
    private readonly StackLayout monitors = new() { Spacing = 3 };
    private readonly DropDown orientation = new();
    private readonly DropDown size = new();
    private readonly CheckBox nearUser = new() { Text = Language.T("Move working area towards me") };
    private readonly CheckBox proportional = new() { Text = Language.T("Preserve proportions") };
    private readonly NumericStepper margin = new() { MinValue = 0, MaxValue = 30, Increment = 1, Value = 8, Width = 70 };
    private readonly List<(IDisplay Display, CheckBox Checkbox)> choices = [];
    private readonly CheckBox all = new() { Text = Language.T("All monitors") };
    private DigitizerSpecifications? tablet;
    private bool updating;

    public TabletMappingPanel(Func<Profile?> getProfile)
    {
        this.getProfile = getProfile;
        orientation.Items.Add(Language.T("Right-handed — buttons on the left"));
        orientation.Items.Add(Language.T("Left-handed — buttons on the right"));
        orientation.Items.Add(Language.T("Vertical — buttons at the top"));
        size.Items.Add(Language.T("Compact"));
        size.Items.Add(Language.T("Comfortable"));
        size.Items.Add(Language.T("Large"));
        var custom = new Button { Text = Language.T("Edit area manually") };
        custom.Click += (_, _) => { if (getProfile()?.AbsoluteModeSettings is { } settings) settings.MappingPreferences = null; };
        var refresh = new Button { Text = Language.T("Refresh monitors") };
        refresh.Click += (_, _) => Refresh();
        Content = new StackLayout
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Items =
            {
                new StackLayout { Spacing = 3, Items = { new Label { Text = Language.T("Use these monitors") }, all, monitors, refresh } },
                new StackLayoutItem(new StackLayout
                {
                    Spacing = 4,
                    Items =
                    {
                        orientation,
                        new Label { Text = Language.T("Change settings here, then press Save / Apply.") },
                        new StackLayout { Orientation = Orientation.Horizontal, Spacing = 5, Items =
                            { new Label { Text = Language.T("Working area") }, size, new Label { Text = Language.T("Edge margin (mm)") }, margin } },
                        new StackLayout { Orientation = Orientation.Horizontal, Spacing = 12, Items = { nearUser, proportional } },
                        custom
                    }
                }, true)
            }
        };
        all.CheckedChanged += (_, _) =>
        {
            if (updating) return;
            updating = true;
            if (all.Checked == true) foreach (var entry in choices) entry.Checkbox.Checked = true;
            updating = false;
            Apply();
        };
        orientation.SelectedIndexChanged += (_, _) => Apply();
        size.SelectedIndexChanged += (_, _) => Apply();
        nearUser.CheckedChanged += (_, _) => Apply();
        proportional.CheckedChanged += (_, _) => Apply();
        margin.ValueChanged += (_, _) => Apply();
    }

    public void SetTablet(DigitizerSpecifications value)
    {
        tablet = value;
        Refresh();
    }

    public void Refresh()
    {
        var settings = getProfile()?.AbsoluteModeSettings;
        Enabled = tablet != null && settings != null;
        if (settings == null) return;
        updating = true;
        try
        {
            if (DesktopInterop.VirtualScreen is MacOSDisplay mac) mac.Refresh();
            var displays = DesktopInterop.VirtualScreen?.Displays.Where(d => d is not IVirtualScreen).ToArray() ?? [];
            choices.Clear();
            monitors.Items.Clear();
            var preferences = settings.MappingPreferences;
            all.Checked = preferences?.DisplayIdentifiers.Length == 0 ||
                preferences == null && displays.All(d => Contains(settings.Display, d));
            foreach (var display in displays)
            {
                var checkbox = new CheckBox
                {
                    Text = $"{display.Name} ({display.Width:0} × {display.Height:0})",
                    Checked = all.Checked == true || (preferences != null
                        ? preferences.DisplayIdentifiers.Contains(display.Identifier) : Contains(settings.Display, display))
                };
                checkbox.CheckedChanged += (_, _) =>
                {
                    if (updating) return;
                    if (!choices.Any(c => c.Checkbox.Checked == true))
                    {
                        updating = true;
                        checkbox.Checked = true;
                        updating = false;
                        return;
                    }
                    updating = true;
                    all.Checked = choices.All(c => c.Checkbox.Checked == true);
                    updating = false;
                    Apply();
                };
                choices.Add((display, checkbox));
                monitors.Items.Add(checkbox);
            }
            orientation.SelectedIndex = (int)settings.Tablet.Rotation switch { 180 => 1, 270 => 2, _ => 0 };
            size.SelectedIndex = preferences?.AreaScale < 0.7f ? 0 : preferences?.AreaScale >= 0.95f ? 2 : 1;
            nearUser.Checked = preferences?.NearUser ?? true;
            proportional.Checked = preferences?.PreserveProportions ?? true;
            margin.Value = preferences?.EdgeMargin ?? 8;
        }
        finally { updating = false; }
    }

    private void Apply()
    {
        var profile = getProfile();
        if (updating || tablet == null || profile?.AbsoluteModeSettings is not { } settings) return;
        var selected = choices.Where(c => c.Checkbox.Checked == true).Select(c => c.Display).ToArray();
        if (selected.Length == 0) return;
        var preferences = new TabletMappingPreferences
        {
            DisplayIdentifiers = all.Checked == true ? [] : selected.Select(d => d.Identifier).ToArray(),
            Rotation = orientation.SelectedIndex switch { 1 => 180, 2 => 270, _ => 0 },
            AreaScale = size.SelectedIndex switch { 0 => 0.6f, 2 => 1, _ => 0.85f },
            NearUser = nearUser.Checked == true,
            PreserveProportions = proportional.Checked == true,
            EdgeMargin = (float)margin.Value
        };
        settings.MappingPreferences = preferences;
        var displayArea = TabletMappingPreferences.DisplayArea(choices.Select(c => c.Display), preferences.DisplayIdentifiers);
        var tabletArea = preferences.TabletArea(tablet, displayArea);
        // Change complete areas before re-enabling the existing aspect-ratio hooks.
        settings.LockAspectRatio = false;
        settings.Display.Area = displayArea.Area;
        // The detailed editor clamps each change using the current rotation.
        // Set rotation and a safe center before resizing, then apply the final center.
        settings.Tablet.Rotation = tabletArea.Rotation;
        settings.Tablet.X = tablet.Width / 2;
        settings.Tablet.Y = tablet.Height / 2;
        settings.Tablet.Area = tabletArea.Area;
        settings.LockAspectRatio = preferences.PreserveProportions;
        profile.BindingSettings.DisableAuxiliaryButtons = preferences.Rotation == 270;
    }

    private static bool Contains(AreaSettings area, IDisplay display) =>
        display.Position.X >= area.X - area.Width / 2 - 1 &&
        display.Position.Y >= area.Y - area.Height / 2 - 1 &&
        display.Position.X + display.Width <= area.X + area.Width / 2 + 1 &&
        display.Position.Y + display.Height <= area.Y + area.Height / 2 + 1;
}
