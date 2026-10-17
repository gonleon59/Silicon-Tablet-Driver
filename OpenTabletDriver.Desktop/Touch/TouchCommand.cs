using System.ComponentModel;

namespace OpenTabletDriver.Desktop.Touch;

public enum TouchCommand
{
    [Description("No action")] None,
    [Description("Left click")] LeftClick,
    [Description("Right click")] RightClick,
    [Description("Double click")] DoubleClick,
    [Description("Back")] Back,
    [Description("Forward")] Forward,
    [Description("Mission Control")] MissionControl,
    [Description("Show desktop")] Desktop,
    [Description("Previous tab")] PreviousTab,
    [Description("Next tab")] NextTab
}
