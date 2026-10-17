using System.Linq;
using System.Numerics;
using OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2;
using OpenTabletDriver.Desktop.Touch;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;
using OpenTabletDriver.Plugin.Tablet.Wheel;
using Xunit;

namespace OpenTabletDriver.Tests
{
    public class BambooTouchTests
    {
        private static TouchPoint Finger(byte id, float x, float y) => new() { TouchID = id, Position = new Vector2(x, y) };
        [Theory]
        [InlineData(false, 12)] [InlineData(true, 11)]
        public void ParserDecodesBothPacketLayoutsAndButtons(bool alternate, int offset)
        {
            byte[] packet = new byte[20]; packet[0] = 2; packet[1] = (byte)(0x0a | (alternate ? 0x80 : 0));
            packet[3] = 0x83; packet[4] = 0x21; packet[5] = 0x04; packet[6] = 0x56;
            packet[offset] = 0x85; packet[offset + 1] = 0x67; packet[offset + 2] = 0x02; packet[offset + 3] = 0x34;
            var result = Assert.IsType<BambooTouchReport>(new BambooTouchReportParser().Parse(packet));
            Assert.Equal(new Vector2(0x321, 0x456), result.Touches[0]!.Position);
            Assert.Equal(new Vector2(0x567, 0x234), result.Touches[1]!.Position);
            Assert.Equal(new[] { false, true, false, true }, result.AuxButtons);
            Assert.False((object)result is IAbsoluteWheelReport);
        }
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(9)] [InlineData(19)] [InlineData(21)]
        public void TruncatedOrUnexpectedReportsAreNotDecoded(int length)
        {
            var packet = new byte[length]; if(length > 0) packet[0] = 2;
            Assert.IsType<DeviceReport>(new BambooTouchReportParser().Parse(packet));
        }
        [Fact] public void InactiveContactsAreNull()
        {
            var packet = new byte[20]; packet[0] = 2; packet[4] = 0x45;
            Assert.All(Assert.IsType<BambooTouchReport>(new BambooTouchReportParser().Parse(packet)).Touches, Assert.Null);
        }
        [Fact] public void ShortTapClicksButLongHoldDoesNot()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100)], 0);
            Assert.Equal(TouchActionKind.LeftClick, Assert.Single(engine.Update([], 100)).Kind);
            engine.Update([Finger(0,100,100)], 1000);
            Assert.Empty(engine.Update([], 1400));
        }
        [Fact] public void TwoFingerTapIsSecondaryClick()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100), Finger(1,200,100)], 0);
            engine.Update([Finger(0,100,100)], 80);
            Assert.Equal(TouchActionKind.RightClick, Assert.Single(engine.Update([], 100)).Kind);
        }
        [Fact] public void ScrollDoesNotJumpOrClickWhenOneFingerLifts()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100), Finger(1,200,100)], 0);
            Assert.Equal(TouchActionKind.Scroll, Assert.Single(engine.Update([Finger(0,100,120), Finger(1,200,120)], 20)).Kind);
            Assert.Equal(TouchScrollPhase.Ended, Assert.Single(engine.Update([Finger(1,200,120)], 30)).Phase);
            Assert.Empty(engine.Update([Finger(1,230,150)], 40));
            Assert.Empty(engine.Update([], 50));
        }
        [Fact] public void PinchProducesZoomNotScroll()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100), Finger(1,200,100)], 0);
            var action = Assert.Single(engine.Update([Finger(0,70,100), Finger(1,230,100)], 30));
            Assert.Equal(TouchActionKind.ZoomIn, action.Kind);
        }
        [Fact] public void DisabledTapAndStreamGapsDoNotClick()
        {
            var engine = new BambooGestureEngine { TapToClick = false };
            engine.Update([Finger(0,100,100)], 0); Assert.Empty(engine.Update([], 100));
            engine.TapToClick = true;
            engine.Update([Finger(0,100,100)], 1000); Assert.Empty(engine.Update([], 2000));
        }
        [Fact] public void MovementCancelsTapAndResetClearsHistory()
        {
            var engine = new BambooGestureEngine { PointerAcceleration = false };
            engine.Update([Finger(0,100,100)], 0);
            var movement = Assert.Single(engine.Update([Finger(0,140,110)], 20)).Delta;
            Assert.InRange(movement.X, 0.1f, 40);
            Assert.InRange(movement.Y, 0.1f, 10);
            Assert.Empty(engine.Update([], 30));
            engine.Update([Finger(0,100,100)], 100); engine.Reset();
            Assert.Empty(engine.Update([], 120));
        }
        [Fact] public void StationarySensorNoiseDoesNotMoveCursor()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100)], 0);
            for (int i = 1; i <= 100; i++)
                Assert.Empty(engine.Update([Finger(0,100 + (i % 2 == 0 ? 3 : -3),100)], i * 10));
        }
        [Fact] public void SmallDeliberateStepsAccumulateIntoSmoothMovement()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100)], 0);
            float travel = 0;
            for (int i = 1; i <= 60; i++)
                foreach (var action in engine.Update([Finger(0,100+i,100)], i * 10))
                {
                    Assert.Equal(TouchActionKind.Move, action.Kind);
                    Assert.InRange(action.Delta.X, 0, 1.1f);
                    Assert.Equal(0, action.Delta.Y);
                    travel += action.Delta.X;
                }
            Assert.InRange(travel, 50, 60);
        }
        [Fact] public void ReplacingContactNeverReusesPointerSmoothingHistory()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100)], 0);
            engine.Update([Finger(0,160,100)], 20);
            engine.Update([], 40);
            Assert.Empty(engine.Update([Finger(1,800,800)], 100));
            Assert.Empty(engine.Update([Finger(1,801,799)], 120));
        }
        [Fact] public void AccelerationPreservesSlowMotionAndBoundsFastMotion()
        {
            Assert.Equal(1, BambooGestureEngine.AccelerationGain(100, 1));
            float previous = 1;
            for (int speed = 150; speed <= 2000; speed += 10)
            {
                float gain = BambooGestureEngine.AccelerationGain(speed, 1);
                Assert.InRange(gain, previous, 3.5f);
                previous = gain;
            }
            Assert.Equal(1, BambooGestureEngine.AccelerationGain(1000, 0));
            Assert.Equal(1, BambooGestureEngine.AccelerationGain(float.NaN, 1));
        }
        [Fact] public void AccelerationIncreasesOnlyFastPointerMovement()
        {
            var accelerated = new BambooGestureEngine { AccelerationStrength = 1 };
            var linear = new BambooGestureEngine { PointerAcceleration = false };
            accelerated.Update([Finger(0,100,100)], 0);
            linear.Update([Finger(0,100,100)], 0);
            float a = 0, b = 0;
            for (int i = 1; i <= 30; i++)
            {
                a += accelerated.Update([Finger(0,100 + i * 20,100)], i * 20).Sum(x => x.Delta.X);
                b += linear.Update([Finger(0,100 + i * 20,100)], i * 20).Sum(x => x.Delta.X);
            }
            Assert.InRange(a / b, 2.5f, 3.5f);
            Assert.Empty(accelerated.Update([], 620));
        }
        [Fact] public void PinchWithOneStationaryFingerIsNotScrolling()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100), Finger(1,200,100)], 0);
            for (int i = 1; i <= 3; i++)
                Assert.Empty(engine.Update([Finger(0,100,100), Finger(1,200 + i * 5,100)], i * 20));
            Assert.Equal(TouchActionKind.ZoomIn,
                Assert.Single(engine.Update([Finger(0,100,100), Finger(1,225,100)], 80)).Kind);
        }
        [Fact] public void RapidPinchNeverBurstsZoomKeyboardSteps()
        {
            var engine = new BambooGestureEngine();
            engine.Update([Finger(0,100,100), Finger(1,200,100)], 0);
            Assert.Equal(TouchActionKind.ZoomIn,
                Assert.Single(engine.Update([Finger(0,80,100), Finger(1,220,100)], 20)).Kind);
            Assert.Empty(engine.Update([Finger(0,50,100), Finger(1,250,100)], 40));
            Assert.Empty(engine.Update([Finger(0,20,100), Finger(1,280,100)], 60));
            Assert.Equal(TouchActionKind.ZoomIn,
                Assert.Single(engine.Update([Finger(0,20,100), Finger(1,280,100)], 280)).Kind);
        }
    }
}
