using System.Numerics;
using OpenTabletDriver.Desktop.Touch;
using Xunit;

namespace OpenTabletDriver.Tests;

public class PenStabilizationTests
{
    [Fact] public void StationaryHoverNoiseIsSuppressed()
    {
        var filter = new HoverPositionFilter();
        var center = new Vector2(50, 50);
        filter.Update(center, 0, false, 35);
        for (int i=1; i<100; i++)
            Assert.True(Vector2.Distance(center, filter.Update(center + new Vector2(i%2==0 ? 0.1f : -0.1f, 0), i*8, false, 35)) < 0.01f);
    }

    [Fact] public void ContactStartsAtActualPositionAndReentryDoesNotInterpolateAcrossTablet()
    {
        var filter = new HoverPositionFilter();
        filter.Update(new(10,10), 0, false, 35);
        Assert.Equal(new Vector2(11,11), filter.Update(new(11,11), 8, true, 12));
        filter.Reset();
        Assert.Equal(new Vector2(100,80), filter.Update(new(100,80), 16, false, 35));
    }

    [Fact] public void FineContactMotionIsNotFrozenAndZeroDisablesFiltering()
    {
        var filter = new HoverPositionFilter();
        filter.Update(new(50,50), 0, true, 12);
        Vector2 result=default;
        for(int i=1;i<=100;i++) result=filter.Update(new(50+i*0.01f,50),i*8,true,12);
        Assert.InRange(result.X,50.9f,51);
        Assert.Equal(new Vector2(60,60),filter.Update(new(60,60),808,true,0));
    }
}
