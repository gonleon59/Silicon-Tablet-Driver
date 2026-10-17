using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Desktop.Touch;
using Xunit;

namespace OpenTabletDriver.Tests;

public class BambooSettingsRoundTripTests
{
    [Fact] public void GestureChoicesAndPreciseMotionSurviveSaveAndReload()
    {
        var original = new BambooTouchNavigation { PointerSpeed=1.5f, ScrollSpeed=1.7f,
            ScrollAccelerationStrength=0.9f, PointerSmoothing=25, FineMovementSpeed=0.5f,
            TapAction=TouchCommand.DoubleClick, SwipeLeftAction=TouchCommand.NextTab };
        var store = JsonConvert.DeserializeObject<PluginSettingStore>(JsonConvert.SerializeObject(new PluginSettingStore(original)))!;
        var loaded = new BambooTouchNavigation();
        store.ApplySettings(loaded);
        Assert.Equal(1.7f,loaded.ScrollSpeed);
        Assert.Equal(0.9f,loaded.ScrollAccelerationStrength);
        Assert.Equal(25,loaded.PointerSmoothing);
        Assert.Equal(0.5f,loaded.FineMovementSpeed);
        Assert.Equal(TouchCommand.DoubleClick,loaded.TapAction);
        Assert.Equal(TouchCommand.NextTab,loaded.SwipeLeftAction);
    }
}
