using System;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Desktop.Touch;
using OpenTabletDriver.Plugin.Tablet.Touch;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class BambooGestureLifecycleTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static TouchPoint F(byte id, float x, float y) => new() { TouchID = id, Position = new(x, y) };

    [Fact]
    public void DoubleTapHoldReleasesOnCancellation()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100)], 0);
        Assert.Equal(TouchActionKind.LeftClick, Assert.Single(engine.Update([], 100)).Kind);
        Assert.Equal(TouchActionKind.DragStart, Assert.Single(engine.Update([F(0, 100, 100)], 200)).Kind);
        Assert.Equal(TouchActionKind.DragEnd, Assert.Single(engine.Reset()).Kind);
        Assert.Empty(engine.Reset());
    }

    [Fact]
    public void ScrollMomentumEndsWhenNewContactArrives()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100), F(1, 200, 100)], 0);
        engine.Update([F(0, 100, 130), F(1, 200, 130)], 20);
        engine.Update([F(0, 100, 160), F(1, 200, 160)], 40);
        Assert.Equal(TouchScrollPhase.Ended, Assert.Single(engine.Update([], 50)).Phase);
        Assert.True(engine.HasMomentum);
        Assert.Equal(TouchScrollPhase.MomentumBegan, Assert.Single(engine.Tick(66)).Phase);
        Assert.Equal(TouchScrollPhase.MomentumEnded, Assert.Single(engine.Update([F(0, 100, 100)], 70)).Phase);
        Assert.False(engine.HasMomentum);
    }

    [Fact]
    public void PageSwipeSurvivesAsynchronousFingerLift()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100), F(1, 200, 100)], 0);
        engine.Update([F(0, 140, 100), F(1, 240, 100)], 20);
        engine.Update([F(0, 200, 100), F(1, 300, 100)], 40);
        engine.Update([F(0, 200, 100)], 50);
        Assert.Equal(TouchActionKind.PageBack, Assert.Single(engine.Update([], 60)).Kind);
    }

    [Fact]
    public void RotationDoesNotScrollOrZoom()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100), F(1, 200, 100)], 0);
        var actions = engine.Update([F(0, 110, 70), F(1, 190, 130)], 100);
        Assert.Equal(TouchActionKind.RotateClockwise, Assert.Single(actions).Kind);
        Assert.Empty(engine.Update([], 120));
    }

    [Fact]
    public void InvalidContactDoesNotMovePointer()
    {
        var engine = new BambooGestureEngine();
        Assert.Empty(engine.Update([F(0, float.NaN, 100), F(255, 100, 100)], 0));
    }
    [Fact] public void BriefLostContactResumesScrollWithoutJump()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100), F(1, 200, 100)], 0);
        engine.Update([F(0, 100, 130), F(1, 200, 130)], 20);
        engine.Update([F(0, 100, 130)], 30);
        Assert.Empty(engine.Update([F(0, 100, 135), F(1, 200, 135)], 40));
        var action = Assert.Single(engine.Update([F(0, 100, 140), F(1, 200, 140)], 50));
        Assert.Equal(TouchActionKind.Scroll, action.Kind);
        Assert.InRange(action.Delta.Length(), 0, 5);
        Assert.Equal(TouchScrollPhase.Began, action.Phase);
    }

    [Fact] public void SecondTapCanLandSlightlyAwayAndStillStartDrag()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100)], 0);
        engine.Update([], 100);
        Assert.Equal(TouchActionKind.DragStart, Assert.Single(engine.Update([F(0, 140, 100)], 500)).Kind);
        Assert.Equal(TouchActionKind.DragEnd, Assert.Single(engine.Update([], 600)).Kind);
    }

    [Fact] public void UnevenParallelFingerMotionScrollsInsteadOfZooming()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 519, 266), F(1, 619, 266)], 0);
        var action = Assert.Single(engine.Update([F(0, 518, 282), F(1, 640, 282)], 25));
        Assert.Equal(TouchActionKind.Scroll, action.Kind);
    }

    [Fact] public void HoldingTwoFingersAfterScrollDoesNotOscillate()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100), F(1, 200, 100)], 0);
        for (int i = 1; i <= 10; i++) engine.Update([F(0, 100, 100+i*10), F(1, 200, 100+i*10)], i*20);
        // Allow the smoothing tail to settle, then inject small stationary sensor noise.
        for (int i = 11; i <= 30; i++) engine.Update([F(0, 100, 200), F(1, 200, 200)], i*20);
        for (int i = 31; i <= 80; i++)
            Assert.Empty(engine.Update([F(0, 100, 200 + (i%2==0 ? 2 : -2)), F(1, 200, 200)], i*20));
        Assert.False(engine.HasMomentum);
    }

    [Fact] public void QuickTapToleratesFingerLandingDrift()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 611, 392)], 0);
        engine.Update([F(0, 614, 372)], 40);
        Assert.Equal(TouchActionKind.LeftClick, Assert.Single(engine.Update([], 63)).Kind);
    }

    [Fact] public void FastFlickDoesNotKeepMovingAfterFingerStops()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100)], 0);
        Assert.Equal(TouchActionKind.Move, Assert.Single(engine.Update([F(0, 140, 100)], 20)).Kind);
        Assert.Empty(engine.Update([F(0, 140, 100)], 40));
        for (int i = 3; i <= 20; i++)
            Assert.Empty(engine.Update([F(0, 140 + (i%2==0 ? 1 : -1), 100)], i*20));
    }

    [Fact] public void SmallPointerCorrectionStartsWithinFiveSensorSteps()
    {
        var engine = new BambooGestureEngine();
        engine.Update([F(0, 100, 100)], 0);
        float movement = 0;
        for (int i = 1; i <= 5; i++)
            foreach (var action in engine.Update([F(0, 100+i, 100)], i*10)) movement += action.Delta.X;
        Assert.InRange(movement, 0.1f, 5);
    }

    [Fact] public void ReplacingSingleContactCancelsTapAndDoesNotArmDrag()
    {
        var e=new BambooGestureEngine();
        e.Update([F(0,100,100)],0);
        Assert.Empty(e.Update([F(1,800,800)],20));
        Assert.Empty(e.Update([],40));
        Assert.Empty(e.Update([F(1,800,800)],100));
        Assert.Equal(TouchActionKind.LeftClick,Assert.Single(e.Update([],140)).Kind);
    }

    [Theory]
    [InlineData(10)] [InlineData(20)] [InlineData(40)]
    public void SlowScrollAtFiftyHzRetainsDistanceAndHasNoPeriodicStalls(float speed)
    {
        var result=MeasureScroll(20,speed,4000);
        output.WriteLine($"50 Hz: speed={speed}, input={speed*4}, output={result.Distance:F3}, maxGap={result.MaxGap} ms");
        Assert.InRange(result.Distance,speed*4-6,speed*4);
        Assert.InRange(result.MaxGap,0,20);
    }

    [Theory]
    [InlineData(5)] [InlineData(10)] [InlineData(16)] [InlineData(25)]
    public void ScrollDistanceDoesNotDependOnReportFrequency(int interval)
    {
        var baseline=MeasureScroll(20,40,2000);
        var actual=MeasureScroll(interval,40,2000);
        output.WriteLine($"interval={interval} ms, input=80, output={actual.Distance:F3}, baseline20ms={baseline.Distance:F3}, maxGap={actual.MaxGap} ms");
        Assert.InRange(actual.Distance/baseline.Distance,0.98f,1.02f);
        Assert.InRange(actual.MaxGap,0,interval);
    }

    private static (float Distance,long MaxGap) MeasureScroll(int interval,float speed,int duration)
    {
        var e=new BambooGestureEngine { PageSwipes=false,ScrollInertia=false };
        e.Update([F(0,100,100),F(1,200,100)],0);
        float distance=0;long previous=-1,maxGap=0;
        for (int t=interval;t<=duration;t+=interval)
        {
            float y=100+t*speed/1000;
            foreach (var a in e.Update([F(0,100,y),F(1,200,y)],t))
            {
                if (a.Kind!=TouchActionKind.Scroll || a.Delta.Y<=0) continue;
                distance+=a.Delta.Y;
                if (previous>=0) maxGap=Math.Max(maxGap,t-previous);
                previous=t;
            }
        }
        return (distance,maxGap);
    }
    [Fact] public void QuantizedSlowMovementRetainsDistanceAfterARealStop()
    {
        var e=new BambooGestureEngine { PageSwipes=false,ScrollInertia=false };
        e.Update([F(0,100,100),F(1,200,100)],0);
        for (int t=20;t<=400;t+=20) e.Update([F(0,100,100+t*.1f),F(1,200,100+t*.1f)],t);
        for (int t=420;t<=1000;t+=20) e.Update([F(0,100,140),F(1,200,140)],t);
        float distance=0;
        for (int t=1020;t<=5000;t+=20)
        {
            float y=140+MathF.Floor((t-1000)/100f);
            foreach(var a in e.Update([F(0,100,y),F(1,200,y)],t))
                if(a.Kind==TouchActionKind.Scroll)distance+=a.Delta.Y;
        }
        Assert.InRange(distance,36,42);
    }
}
