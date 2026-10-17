using System;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Touch;
using OpenTabletDriver.Desktop.Binding;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.UX.Native;
using Xunit;

namespace OpenTabletDriver.Tests;

public class NativeSettingsSnapshotTests
{
    private static readonly DigitizerSpecifications Digitizer = new() { Width=216.48f,Height=137,MaxX=21648,MaxY=13700 };
    private static Profile Fixture()
    {
        var p=new Profile { Tablet="Wacom CTH-661",OutputMode=new PluginSettingStore("custom.output",[]),
            AbsoluteModeSettings=new() { Display=new() { Width=2304,Height=1296,X=1152,Y=648 },Tablet=new() { Width=180,Height=100,X=110,Y=70 } } };
        p.Filters.Add(new PluginSettingStore(new BambooTouchNavigation { ScrollSpeed=1.7f,ScrollAccelerationStrength=0.9f }));
        p.Filters.Add(new PluginSettingStore("custom.filter",[]) { Enable=false });
        p.BindingSettings.PenButtons.Add(new PluginSettingStore(new AdaptiveBinding(OpenTabletDriver.Plugin.Platform.Pointer.PenAction.BarrelButton1)));
        p.BindingSettings.PenButtons.Add(new PluginSettingStore(new DoubleClickBinding()));
        p.BindingSettings.AuxButtons.Add(new PluginSettingStore("custom.binding",[]));
        return p;
    }
    [Fact] public void TouchEditPreservesManualGeometryCustomPluginsAndAdaptiveBindings()
    {
        var p=Fixture();var adaptive=p.BindingSettings.PenButtons[0];var custom=p.BindingSettings.AuxButtons[0];
        var s=BambooSettingsSnapshot.FromProfile(p);s.Touch["PointerSpeed"]=1.2;s.ApplyTo(p,Digitizer);
        Assert.Equal(180,p.AbsoluteModeSettings!.Tablet.Width);Assert.Null(p.AbsoluteModeSettings.MappingPreferences);
        Assert.Same(adaptive,p.BindingSettings.PenButtons[0]);Assert.Same(custom,p.BindingSettings.AuxButtons[0]);
        Assert.Contains(p.Filters,f=>f?.Path=="custom.filter" && !f.Enable);
        var loaded=new BambooTouchNavigation();p.Filters[0]!.ApplySettings(loaded);
        Assert.Equal(1.7f,loaded.ScrollSpeed);Assert.Equal(0.9f,loaded.ScrollAccelerationStrength);Assert.Equal(1.2f,loaded.PointerSpeed);
    }
    [Fact] public void ManualRotationAndAdvancedAccelerationRemainUsable()
    {
        var p=Fixture();p.AbsoluteModeSettings!.Tablet.Rotation=45;
        var s=BambooSettingsSnapshot.FromProfile(p);s.Touch["AccelerationStrength"]=1.5;
        s.ApplyTo(p,Digitizer);Assert.Equal(45,p.AbsoluteModeSettings.Tablet.Rotation);
        var loaded=new BambooTouchNavigation();p.Filters[0]!.ApplySettings(loaded);Assert.Equal(1.5f,loaded.AccelerationStrength);
    }
    [Fact] public void InvalidInputDoesNotMutateProfile()
    {
        var p=Fixture();var before=JsonConvert.SerializeObject(p);var s=BambooSettingsSnapshot.FromProfile(p);
        s.Touch["PointerSpeed"]=double.NaN;
        Assert.Throws<ArgumentException>(()=>s.ApplyTo(p,Digitizer));Assert.Equal(before,JsonConvert.SerializeObject(p));
    }
    [Fact] public void EmptyDisplaySelectionIsRejectedBeforeChanges()
    {
        var p=Fixture();var before=JsonConvert.SerializeObject(p);var s=BambooSettingsSnapshot.FromProfile(p);s.UseMapping=true;s.AllDisplays=false;s.DisplayIds=[];
        Assert.Throws<ArgumentException>(()=>s.ApplyTo(p,Digitizer));Assert.Equal(before,JsonConvert.SerializeObject(p));
    }
    [Fact] public void VerticalPresetUsesSelectedDisplayAndDisablesBodyButtons()
    {
        var p=Fixture();var s=BambooSettingsSnapshot.FromProfile(p);s.UseMapping=true;s.Rotation=270;s.DisplayIds=["second"];
        s.Displays=[new("first","First",2304,1296,0,0),new("second","Second",1920,1080,2304,0)];
        s.ApplyTo(p,Digitizer);
        Assert.Equal(270,p.AbsoluteModeSettings!.Tablet.Rotation);Assert.Equal(1920,p.AbsoluteModeSettings.Display.Width);
        Assert.True(p.BindingSettings.DisableAuxiliaryButtons);Assert.True(p.AbsoluteModeSettings.Tablet.X>Digitizer.Width/2);
        s.Rotation=0;s.ApplyTo(p,Digitizer);Assert.False(p.BindingSettings.DisableAuxiliaryButtons);
    }
    [Fact] public void NativeSchemaRoundTripRetainsEnumActionsAndUnknownBindings()
    {
        var p=Fixture();var s=BambooSettingsSnapshot.FromProfile(p);s.Commands["TapAction"]=(int)TouchCommand.DoubleClick;
        var copy=JsonConvert.DeserializeObject<BambooSettingsSnapshot>(JsonConvert.SerializeObject(s))!;copy.ApplyTo(p,Digitizer);
        var touch=new BambooTouchNavigation();p.Filters[0]!.ApplySettings(touch);Assert.Equal(TouchCommand.DoubleClick,touch.TapAction);
        Assert.Equal("custom.binding",p.BindingSettings.AuxButtons[0]!.Path);
    }
    [Theory]
    [InlineData("OpenTabletDriver.Desktop.Binding.MouseBinding")]
    [InlineData("OpenTabletDriver.Desktop.Binding.AdaptiveBinding")]
    [InlineData("OpenTabletDriver.Desktop.Binding.MultiKeyBinding")]
    public void ReadingSparseBindingsDoesNotMutateProfileOrInvalidateToken(string path)
    {
        var p=Fixture();
        p.BindingSettings.PenButtons[0]=new PluginSettingStore(path,[]) { Enable=false };
        p.BindingSettings.AuxButtons[0]=new PluginSettingStore(path,[]);
        string before=JsonConvert.SerializeObject(p);
        var snapshot=BambooSettingsSnapshot.FromProfile(p);
        Assert.Equal(before,JsonConvert.SerializeObject(p));
        Assert.Equal(BambooSettingsSnapshot.TokenFor(p),snapshot.ProfileToken);
        Assert.Equal(-1,snapshot.PenButtons[0]);
    }
}
