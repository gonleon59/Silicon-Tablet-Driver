using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Plugin.Tablet.Touch;
using OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Touch
{
    [PluginName("Bamboo touch navigation")]
    [SupportedPlatform(PluginPlatform.MacOS)]
    public sealed class BambooTouchNavigation : IPositionedPipelineElement<IDeviceReport>, IDisposable
    {
        [TabletReference] public TabletReference? Tablet { get; set; }
        [Property("Pointer speed"), DefaultPropertyValue(1.5f)] public float PointerSpeed { get; set; } = 1.5f;
        [BooleanProperty("Pointer acceleration", "Enabled"), DefaultPropertyValue(true)] public bool PointerAcceleration { get; set; } = true;
        [Property("Acceleration strength"), DefaultPropertyValue(0.4f)] public float AccelerationStrength { get; set; } = 0.4f;
        [Property("Scroll speed"), DefaultPropertyValue(0.25f)] public float ScrollSpeed { get; set; } = 0.25f;
        [BooleanProperty("Natural scrolling", "Enabled"), DefaultPropertyValue(true)] public bool NaturalScrolling { get; set; } = true;
        [BooleanProperty("Tap to click", "Enabled"), DefaultPropertyValue(true)] public bool TapToClick { get; set; } = true;
        [BooleanProperty("Pinch zoom via keyboard shortcuts", "Enabled"), DefaultPropertyValue(true)] public bool PinchToZoom { get; set; } = true;
        [Property("Pointer smoothing (ms)"), DefaultPropertyValue(40f)] public float PointerSmoothing { get; set; } = 40;
        [Property("Fine movement speed"), DefaultPropertyValue(0.65f)] public float FineMovementSpeed { get; set; } = 0.65f;
        [Property("Tap action"), DefaultPropertyValue(TouchCommand.LeftClick)] public TouchCommand TapAction { get; set; } = TouchCommand.LeftClick;
        [Property("Two-finger tap action"), DefaultPropertyValue(TouchCommand.RightClick)] public TouchCommand TwoFingerTapAction { get; set; } = TouchCommand.RightClick;
        [Property("Swipe right action"), DefaultPropertyValue(TouchCommand.Back)] public TouchCommand SwipeRightAction { get; set; } = TouchCommand.Back;
        [Property("Swipe left action"), DefaultPropertyValue(TouchCommand.Forward)] public TouchCommand SwipeLeftAction { get; set; } = TouchCommand.Forward;
        public PipelinePosition Position => PipelinePosition.PreTransform;
        public event Action<IDeviceReport?>? Emit;
        private readonly object gate = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly BambooGestureEngine gestures = new();
        private readonly SmoothScrollBuffer scrolling = new();
        private bool timerRunning;
        private long lastScrollInput = -1;
        private float lastScrollGain = 1;
        private MacOSTouchOutput? pointer;
        private Timer? momentum;
        private readonly TouchPoint?[] rotated = [new TouchPoint { TouchID = 0 }, new TouchPoint { TouchID = 1 }];
        public float TabletRotation { get; set; }
        public string[] DisplayIdentifiers { get; set; } = [];
        [BooleanProperty("Tap twice and hold to drag", "Enabled"), DefaultPropertyValue(true)] public bool TapToDrag { get; set; } = true;
        [BooleanProperty("Scroll inertia", "Enabled"), DefaultPropertyValue(true)] public bool ScrollInertia { get; set; } = true;
        [BooleanProperty("Two-finger page swipes", "Enabled"), DefaultPropertyValue(true)] public bool PageSwipes { get; set; } = true;
        [BooleanProperty("Rotate in Preview", "Enabled"), DefaultPropertyValue(true)] public bool Rotate { get; set; } = true;
        private long suppressUntil, nextWorkspaceCheck;
        private bool canRotateInPreview;
        private bool disposed;
        [BooleanProperty("Scroll acceleration", "Enabled"), DefaultPropertyValue(true)] public bool ScrollAcceleration { get; set; } = true;
        [Property("Scroll acceleration strength"), DefaultPropertyValue(0.4f)] public float ScrollAccelerationStrength { get; set; } = 0.4f;

        public void Consume(IDeviceReport? report)
        {
            lock (gate)
            {
                if (!disposed && Tablet?.Properties.Name == "Wacom CTH-661")
                {
                    long now = clock.ElapsedMilliseconds;
                    if (report is ITabletReport)
                    {
                        suppressUntil = now + 700;
                        Apply(gestures.Reset());
                    }
                    else if (report is OutOfRangeReport)
                        suppressUntil = now + 300;
                    else if (report is BambooTouchReport touch)
                    {
                        if (now < suppressUntil) Apply(gestures.Reset());
                        else
                        {
                            gestures.TapToClick = TapToClick;
                            gestures.TapToDrag = TapToDrag && TapAction == TouchCommand.LeftClick;
                            gestures.ScrollInertia = ScrollInertia;
                            gestures.PageSwipes = PageSwipes;
                            if (Rotate && now >= nextWorkspaceCheck)
                            {
                                canRotateInPreview = MacOSWorkspace.FrontmostBundleIdentifier() == "com.apple.Preview";
                                nextWorkspaceCheck = now + 500;
                            }
                            gestures.Rotate = Rotate && canRotateInPreview;
                            gestures.PinchToZoom = PinchToZoom;
                            gestures.PointerAcceleration = PointerAcceleration;
                            gestures.AccelerationStrength = AccelerationStrength;
                            gestures.PointerSmoothing = PointerSmoothing;
                            gestures.FineMovementSpeed = FineMovementSpeed;
                            var transform = Matrix3x2.CreateRotation(-TabletRotation * MathF.PI / 180);
                            for (int i = 0; i < rotated.Length; i++)
                            {
                                if (i >= touch.Touches.Length || touch.Touches[i] is not { } point) rotated[i] = null;
                                else
                                {
                                    rotated[i] ??= new TouchPoint();
                                    rotated[i]!.TouchID = point.TouchID;
                                    rotated[i]!.Position = Vector2.Transform(point.Position, transform);
                                }
                            }
                            Apply(gestures.Update(rotated, now));
                            UpdateTimer();
                        }
                    }
                }
            }
            Emit?.Invoke(report); // Preserve auxiliary buttons and pen processing.
        }

        private void Apply(TouchActionBatch actions)
        {
            foreach (var action in actions) Apply(action);
        }

        private void TickMomentum(object? state)
        {
            lock (gate)
            {
                if (disposed) return;
                try
                {
                    long now = clock.ElapsedMilliseconds;
                    Apply(gestures.Tick(now));
                    SendScroll(scrolling.Tick(now));
                }
                catch (Exception error)
                {
                    var release = gestures.Reset();
                    try { Apply(release); }
                    catch (Exception releaseError) { Log.Exception(releaseError); }
                    Log.Exception(error);
                }
                UpdateTimer();
            }
        }

        private void UpdateTimer()
        {
            bool active = gestures.HasMomentum || scrolling.HasPending;
            if (active == timerRunning) return;
            timerRunning = active;
            momentum ??= new Timer(TickMomentum, null, Timeout.Infinite, Timeout.Infinite);
            // Only runs during scroll output. Do not re-arm it on every hardware packet.
            momentum.Change(active ? 8 : Timeout.Infinite, active ? 8 : Timeout.Infinite);
        }

        private void SendScroll(TouchActionBatch actions)
        {
            foreach (var action in actions) pointer?.Scroll(action.Delta, action.Phase);
        }

        private static float Speed(float value) => float.IsFinite(value) ? Math.Clamp(value, 0.1f, 10) : 1;

        private void Apply(TouchAction action)
        {
            pointer ??= new MacOSTouchOutput { DisplayIdentifiers = DisplayIdentifiers };
            switch (action.Kind)
            {
                case TouchActionKind.Move:
                    pointer.Move(action.Delta * Speed(PointerSpeed));
                    break;
                case TouchActionKind.Scroll:
                    long now = clock.ElapsedMilliseconds;
                    if (action.Phase is TouchScrollPhase.Began or TouchScrollPhase.Changed)
                    {
                        long elapsed = lastScrollInput < 0 ? 20 : Math.Clamp(now - lastScrollInput, 8, 50);
                        lastScrollGain = ScrollAcceleration
                            ? BambooGestureEngine.AccelerationGain(action.Delta.Length() * 1000 / elapsed, ScrollAccelerationStrength) : 1;
                        lastScrollInput = now;
                    }
                    var delta = action.Delta * Speed(ScrollSpeed) * lastScrollGain * (NaturalScrolling ? -1 : 1);
                    SendScroll(scrolling.Submit(delta, action.Phase, now));
                    if (action.Phase is TouchScrollPhase.Ended or TouchScrollPhase.MomentumEnded) lastScrollInput = -1;
                    break;
                case TouchActionKind.LeftClick:
                case TouchActionKind.RightClick:
                    Execute(action.Kind == TouchActionKind.RightClick ? TwoFingerTapAction : TapAction);
                    break;
                case TouchActionKind.DragStart: pointer.Down(drag: true); break;
                case TouchActionKind.DragEnd: pointer.Up(); break;
                case TouchActionKind.ZoomIn: Shortcut("Equal"); break;
                case TouchActionKind.ZoomOut: Shortcut("Minus"); break;
                case TouchActionKind.PageBack: Execute(SwipeRightAction); break;
                case TouchActionKind.PageForward: Execute(SwipeLeftAction); break;
                case TouchActionKind.RotateClockwise:
                case TouchActionKind.RotateCounterclockwise:
                    // These commands rotate documents in Preview; Cmd-R would reload a browser.
                    if (MacOSWorkspace.FrontmostBundleIdentifier() == "com.apple.Preview")
                        Shortcut(action.Kind == TouchActionKind.RotateClockwise ? "R" : "L");
                    break;
            }
        }

        private void Execute(TouchCommand command)
        {
            switch (command)
            {
                case TouchCommand.LeftClick: pointer!.Down(); pointer.Up(); break;
                case TouchCommand.RightClick: pointer!.Down(true); pointer.Up(true); break;
                case TouchCommand.DoubleClick:
                    pointer!.Down(); pointer.Up(); pointer.Down(); pointer.Up(); break;
                case TouchCommand.Back: Shortcut("LeftBracket"); break;
                case TouchCommand.Forward: Shortcut("RightBracket"); break;
                case TouchCommand.MissionControl: Shortcut("Up", "Control"); break;
                case TouchCommand.Desktop: Shortcut("F11", null); break;
                case TouchCommand.PreviousTab: Shortcut("PageUp", "Control"); break;
                case TouchCommand.NextTab: Shortcut("PageDown", "Control"); break;
            }
        }

        private static void Shortcut(string key, string? modifier = "Application")
        {
            var keyboard = DesktopInterop.VirtualKeyboard;
            if (keyboard == null) return;
            if (modifier != null) keyboard.Press(modifier);
            try { keyboard.Press(key); keyboard.Release(key); }
            finally { if (modifier != null) keyboard.Release(modifier); }
        }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                Apply(gestures.Reset());
                disposed = true;
                momentum?.Dispose();
                pointer?.Dispose();
            }
        }
    }
}
