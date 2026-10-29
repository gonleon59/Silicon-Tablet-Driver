using System.Numerics;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class TabletMappingTests
{
    private record Screen(string Identifier, float Width, float Height, Vector2 Position) : IDisplay
    { public int Index => 0; }

    [Fact]
    public void SelectedDisplaysUseTheirActualPositions()
    {
        IDisplay[] displays = [new Screen("a", 1000, 800, Vector2.Zero), new Screen("b", 2000, 1200, new(1000, -100))];
        var area = TabletMappingPreferences.DisplayArea(displays, ["b"]);
        Assert.Equal(2000, area.Width); Assert.Equal(2000, area.X);
        Assert.Equal(1200, area.Height); Assert.Equal(500, area.Y);
        var all = TabletMappingPreferences.DisplayArea(displays, []);
        Assert.Equal(3000, all.Width); Assert.Equal(1200, all.Height);
    }

    [Fact]
    public void DisconnectedSelectionFallsBackToAvailableDisplays()
    {
        var area = TabletMappingPreferences.DisplayArea([new Screen("a", 1000, 800, Vector2.Zero)], ["missing"]);
        Assert.Equal(1000, area.Width);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(270)]
    public void ComfortableAreaKeepsProportionsAndMargins(int rotation)
    {
        var tablet = new DigitizerSpecifications { Width = 216, Height = 137 };
        var preferences = new TabletMappingPreferences { Rotation = rotation };
        var area = preferences.TabletArea(tablet, new AreaSettings { Width = 5120, Height = 2880 });
        Assert.InRange(area.Width / area.Height, 1.7777f, 1.7779f);
        float halfWidth = rotation is 90 or 270 ? area.Height / 2 : area.Width / 2;
        float halfHeight = rotation is 90 or 270 ? area.Width / 2 : area.Height / 2;
        Assert.InRange(area.X - halfWidth, 7.999f, 216);
        Assert.InRange(area.X + halfWidth, 0, 208.001f);
        Assert.InRange(area.Y - halfHeight, 7.999f, 137);
        Assert.InRange(area.Y + halfHeight, 0, 129.001f);
        Assert.Equal(rotation, area.Rotation);
        if (rotation == 0) Assert.True(area.Y > tablet.Height / 2);
        if (rotation == 180) Assert.True(area.Y < tablet.Height / 2);
        if (rotation == 270) Assert.True(area.X > tablet.Width / 2);
    }
}
