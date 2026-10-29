using System.Numerics;
using OpenTabletDriver.Desktop.Profiles;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class FullTabletMappingTests
{
    private static readonly DigitizerSpecifications Digitizer=new() { Width=216.48f,Height=137,MaxX=21648,MaxY=13700 };
    private static readonly AreaSettings Display=new() { Width=4224,Height=1296,X=2112,Y=648 };
    private sealed class TransformProbe : AbsoluteOutputMode
    {
        public override IAbsolutePointer? Pointer { get; set; }
        public static Matrix3x2 Matrix(Area input,Area output)=>CalculateTransformation(input,output,Digitizer);
    }
    [Theory]
    [InlineData(0)] [InlineData(180)]
    public void HorizontalPresetsUseStandardMarginsRegardlessOfVerticalCropValues(int rotation)
    {
        var p=new TabletMappingPreferences { Rotation=rotation,AreaScale=0.3f,EdgeMargin=30,NearUser=true,PreserveProportions=true };
        var area=p.TabletArea(Digitizer,Display);
        Assert.Equal(Digitizer.Width-9,area.Width);Assert.Equal(Digitizer.Height-10,area.Height);
        Assert.Equal(4.5f,area.X-area.Width/2,3);Assert.Equal(5,area.Y-area.Height/2,3);
        Assert.Equal(Digitizer.Width/2,area.X);Assert.Equal(Digitizer.Height/2,area.Y);Assert.Equal(rotation,area.Rotation);
    }
    [Theory]
    [InlineData(0)] [InlineData(180)] [InlineData(270)]
    public void WorkingAreaMapsItsCornersAndCenterToTheSelectedDisplay(int rotation)
    {
        var p=new TabletMappingPreferences { Rotation=rotation,UseComfortableArea=false };
        foreach(var display in new[] { Display,new AreaSettings { Width=1920,Height=1080,X=-960,Y=540 } })
        {
            var matrix=TransformProbe.Matrix(p.TabletArea(Digitizer,display).Area,display.Area);
            float left=rotation==270 ? 0 : 4.5f*Digitizer.MaxX/Digitizer.Width;
            float top=rotation==270 ? 0 : 5*Digitizer.MaxY/Digitizer.Height;
            float right=Digitizer.MaxX-left,bottom=Digitizer.MaxY-top;
            var raw=new[] {new Vector2(left,top),new Vector2(right,top),new Vector2(left,bottom),new Vector2(right,bottom)};
            var normalized=rotation switch
            {
                180=>new[] {new Vector2(1,1),new Vector2(0,1),new Vector2(1,0),Vector2.Zero},
                270=>new[] {new Vector2(1,0),new Vector2(1,1),Vector2.Zero,new Vector2(0,1)},
                _=>new[] {Vector2.Zero,new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)}
            };
            var topLeft=new Vector2(display.X-display.Width/2,display.Y-display.Height/2);
            for(int i=0;i<raw.Length;i++)
                AssertNear(topLeft+normalized[i]*new Vector2(display.Width,display.Height),Vector2.Transform(raw[i],matrix));
            AssertNear(new(display.X,display.Y),Vector2.Transform(new(Digitizer.MaxX/2,Digitizer.MaxY/2),matrix));
        }
    }
    private static void AssertNear(Vector2 expected,Vector2 actual)
    {
        Assert.InRange(Vector2.Distance(expected,actual),0,0.003f);
    }
}
