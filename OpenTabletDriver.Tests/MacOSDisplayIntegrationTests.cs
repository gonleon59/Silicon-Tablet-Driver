using System;
using System.Linq;
using OpenTabletDriver.Desktop.Interop.Display;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class MacOSDisplayIntegrationTests
{
    [SkippableFact]
    public void ConnectedDisplayMetadataLoadsFromPublicSystemLibraries()
    {
        Skip.IfNot(OperatingSystem.IsMacOS());
        var screen = new MacOSDisplay();
        var displays = screen.Displays.ToArray();
        Skip.If(displays.Length == 0, "No connected display on this runner.");
        Assert.All(displays, display =>
        {
            Assert.True(display.Width > 0 && display.Height > 0);
            Assert.False(string.IsNullOrWhiteSpace(display.Name));
            Assert.False(string.IsNullOrWhiteSpace(display.Identifier));
        });
        Assert.True(screen.Width > 0 && screen.Height > 0);
        screen.Refresh();
        Assert.Equal(displays.Select(d => d.Identifier), screen.Displays.Select(d => d.Identifier));
    }
}
