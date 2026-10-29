using System.Linq;
using System.Numerics;
using OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Desktop.Touch;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class TouchSurfaceIndependenceTests
{
    private sealed class CaptureOutput : AbsoluteOutputMode
    {
        public override IAbsolutePointer? Pointer { get; set; }
        public IDeviceReport? Last { get; private set; }
        protected override void OnOutput(IDeviceReport report) => Last=report;
    }
    private static TabletReference Tablet() => new(new TabletConfiguration
    {
        Name="Wacom CTH-661", DigitizerIdentifiers=[],
        Specifications=new() { Digitizer=new() {Width=216.48f,Height=137,MaxX=21648,MaxY=13700}, Pen=new() }
    },[]);

    [Theory]
    [InlineData(0)] [InlineData(180)]
    public void PenAreaClippingDoesNotClipAnyTouchSurfaceCorner(int rotation)
    {
        foreach(var point in new[] { Vector2.Zero,new Vector2(2047,0),new Vector2(0,2047),new Vector2(2047,2047) })
        {
            var tablet=Tablet();
            using var touch=new BambooTouchNavigation { Tablet=tablet,TabletRotation=rotation,Rotate=false,TapToClick=false };
            using var output=new CaptureOutput { Tablet=tablet,AreaClipping=true,AreaLimiting=true };
            var preferences=new TabletMappingPreferences { Rotation=rotation };
            var display=new AreaSettings {Width=1920,Height=1080,X=960,Y=540};
            output.Input=preferences.TabletArea(tablet.Properties.Specifications.Digitizer,display).Area;
            output.Output=display.Area;
            output.Elements=[touch];
            var report=new BambooTouchReport { Raw=[],AuxButtons=[],Touches=[new TouchPoint {TouchID=0,Position=point},null] };
            output.Read(report); // Actual pre-transform touch filter and clipped absolute-output pipeline.
            var received=Assert.IsType<BambooTouchReport>(output.Last);
            Assert.Equal(point,received.Touches[0]!.Position);
            Assert.False((object)received is IAbsolutePositionReport);
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(180)]
    public void PointerMotionWorksInsideEveryStripExcludedFromPenArea(int rotation)
    {
        foreach(var path in new[] {
            (new Vector2(1,1),new Vector2(31,1)),(new Vector2(2046,1),new Vector2(2046,31)),
            (new Vector2(1,2046),new Vector2(31,2046)),(new Vector2(1,1),new Vector2(1,31)) })
        {
            var engine=new BambooGestureEngine { PointerAcceleration=false,PointerSmoothing=0,FineMovementSpeed=1,TapToClick=false };
            var transform=Matrix3x2.CreateRotation(-rotation*System.MathF.PI/180);
            var start=Vector2.Transform(path.Item1,transform);var end=Vector2.Transform(path.Item2,transform);
            engine.Update([new TouchPoint {TouchID=0,Position=start}],0);
            var move=Assert.Single(engine.Update([new TouchPoint {TouchID=0,Position=end}],20));
            Assert.Equal(TouchActionKind.Move,move.Kind);
            // Compare with identical motion at the center: preserve the normal smoothing
            // and noise dead zone, while proving the edge strip is not clipped or gated.
            var reference=new BambooGestureEngine { PointerAcceleration=false,PointerSmoothing=0,FineMovementSpeed=1,TapToClick=false };
            var center=Vector2.Transform(new Vector2(1024,1024),transform);
            reference.Update([new TouchPoint {TouchID=0,Position=center}],0);
            var expected=Assert.Single(reference.Update([new TouchPoint {TouchID=0,Position=center+(end-start)}],20));
            Assert.InRange(move.Delta.Length(),20,30);
            Assert.True(Vector2.Dot(move.Delta,end-start)>0);
            Assert.InRange(Vector2.Distance(expected.Delta,move.Delta),0,0.001f);
        }
    }
}
