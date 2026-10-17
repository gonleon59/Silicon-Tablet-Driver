using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin.Platform.Display;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests;

[Collection("Daemon settings")]
public sealed class BambooProfileDefaultsTests
{
    [SkippableFact]
    public void FreshBambooProfileIncludesTouchAndButtonAssignments()
    {
        Skip.IfNot(OperatingSystem.IsMacOS());
        var previous=AppInfo.PluginManager;
        var manager=new DesktopPluginManager();
        manager.AddService<IVirtualScreen>(()=>new TestScreen());
        AppInfo.PluginManager=manager;
        try
        {
            var configuration = JsonConvert.DeserializeObject<TabletConfiguration>(File.ReadAllText(ConfigPath()))!;
            var profile = Profile.GetDefaults(new TabletReference(configuration, []));
            Assert.Contains(profile.Filters, filter => filter?.Path == "OpenTabletDriver.Desktop.Touch.BambooTouchNavigation");
            Assert.Equal("OpenTabletDriver.Desktop.Binding.DoubleClickBinding", profile.BindingSettings.PenButtons[1]?.Path);
            Assert.All(profile.BindingSettings.AuxButtons, button => Assert.Equal("OpenTabletDriver.Desktop.Binding.MultiKeyBinding", button?.Path));
            Assert.NotNull(profile.AbsoluteModeSettings?.MappingPreferences);
            Assert.True(profile.AbsoluteModeSettings!.MappingPreferences!.NearUser);
        }
        finally { AppInfo.PluginManager=previous; }
    }

    private sealed class TestScreen : IVirtualScreen
    {
        public float Width=>1920;
        public float Height=>1080;
        public Vector2 Position=>Vector2.Zero;
        public int Index=>0;
        public IEnumerable<IDisplay> Displays=>[];
    }

    private static string ConfigPath([CallerFilePath] string file = "") => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(file)!, "..", "OpenTabletDriver.Configurations", "Configurations", "Wacom", "CTH-661.json"));
}
