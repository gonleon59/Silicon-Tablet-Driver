using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Binding;

[PluginName("Double click")]
public sealed class DoubleClickBinding : IStateBinding
{
    [Resolved] public IMouseButtonHandler? Pointer { get; set; }
    public void Press(TabletReference tablet, IDeviceReport report)
    {
        if (Pointer is not ISynchronousPointer synchronous) return;
        for (int i = 0; i < 2; i++)
        {
            Pointer.MouseDown(MouseButton.Left);
            synchronous.Flush();
            Pointer.MouseUp(MouseButton.Left);
            synchronous.Flush();
        }
    }
    public void Release(TabletReference tablet, IDeviceReport report) { }
    public override string ToString() => "Double click";
}
