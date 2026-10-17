using System.Numerics;
using OpenTabletDriver.Desktop.Touch;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class SmoothScrollBufferTests
{
    [Fact]
    public void HardwareStepIsSpreadAcrossFramesAndDistanceIsPreserved()
    {
        var buffer = new SmoothScrollBuffer();
        Assert.Equal(TouchScrollPhase.Began, Assert.Single(buffer.Submit(new(0, 30), TouchScrollPhase.Began, 0)).Phase);
        Vector2 total = default;
        for (int t = 8; t <= 200; t += 8)
        {
            foreach (var action in buffer.Tick(t))
            {
                Assert.InRange(action.Delta.Y, 0.001f, 16);
                Assert.Equal(TouchScrollPhase.Changed, action.Phase);
                total += action.Delta;
            }
        }
        Assert.InRange(total.Y, 29.999f, 30.001f);
        Assert.False(buffer.HasPending);
    }

    [Fact]
    public void EndFlushesRemainderBeforeSendingEndPhase()
    {
        var buffer = new SmoothScrollBuffer();
        buffer.Submit(new(0, 20), TouchScrollPhase.Began, 0);
        var first = Assert.Single(buffer.Tick(8));
        var end = buffer.Submit(default, TouchScrollPhase.Ended, 10);
        Assert.Equal(2, end.Count);
        Assert.Equal(TouchScrollPhase.Changed, end[0].Phase);
        Assert.Equal(TouchScrollPhase.Ended, end[1].Phase);
        Assert.InRange(first.Delta.Y + end[0].Delta.Y, 19.999f, 20.001f);
        Assert.Empty(buffer.Tick(20));
    }

    [Fact]
    public void MomentumUsesItsOwnContinuationPhase()
    {
        var buffer = new SmoothScrollBuffer();
        buffer.Submit(new(10, 0), TouchScrollPhase.MomentumBegan, 0);
        Assert.Equal(TouchScrollPhase.MomentumChanged, Assert.Single(buffer.Tick(8)).Phase);
        var end = buffer.Submit(default, TouchScrollPhase.MomentumEnded, 10);
        Assert.Equal(TouchScrollPhase.MomentumChanged, end[0].Phase);
        Assert.Equal(TouchScrollPhase.MomentumEnded, end[1].Phase);
    }
    [Fact]
    public void ResumingAfterPauseDoesNotEmitOneLargeStep()
    {
        var buffer = new SmoothScrollBuffer();
        buffer.Submit(default, TouchScrollPhase.Began, 0);
        buffer.Submit(new(0, 30), TouchScrollPhase.Changed, 1000);
        Assert.InRange(Assert.Single(buffer.Tick(1008)).Delta.Y, 0.001f, 16);
    }

}
