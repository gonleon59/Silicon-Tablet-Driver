using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Generic;
using OpenTabletDriver.Native.OSX.Input;
using static OpenTabletDriver.Native.OSX.OSX;

namespace OpenTabletDriver.Desktop.Touch;

/// <summary>Ordinary pointer events: touch must not impersonate the pressure-sensitive pen.</summary>
internal sealed class MacOSTouchOutput : IDisposable
{
    private readonly IntPtr source = CGEventSourceCreate(CGEventSourceStatePrivate);
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private bool dragging;
    public string[] DisplayIdentifiers { get; set; } = [];
    private int clickCount;
    private long lastClick = -1;
    private CGPoint lastClickPosition;

    private static CGPoint Cursor()
    {
        var e = CGEventCreate(IntPtr.Zero);
        try { return CGEventGetLocation(e); }
        finally { if (e != IntPtr.Zero) CFRelease(e); }
    }

    private void Mouse(CGEventType type, CGPoint position, CGMouseButton button, Vector2 delta = default)
    {
        var e = CGEventCreateMouseEvent(source, type, position, button);
        if (e == IntPtr.Zero) return;
        try
        {
            CGEventSetIntegerValueField(e, CGEventField.mouseEventClickState, clickCount);
            CGEventSetDoubleValueField(e, CGEventField.mouseEventDeltaX, delta.X);
            CGEventSetDoubleValueField(e, CGEventField.mouseEventDeltaY, delta.Y);
            CGEventSetFlags(e, CGEventSourceFlagsState(CGEventSourceStateHIDSystemState));
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, e);
        }
        finally { CFRelease(e); }
    }

    private CGPoint ClampToScreen(CGPoint target)
    {
        if (screenBounds == null || clock.ElapsedMilliseconds - screenRefresh >= 1000)
        {
            var ids = new uint[32];
            CGGetActiveDisplayList((uint)ids.Length, ids, out uint count);
            var active = ids.Take((int)Math.Min(count, (uint)ids.Length)).ToArray();
            var selected = DisplayIdentifiers.Length == 0 ? active
                : active.Where(id => DisplayIdentifiers.Contains(MacOSWorkspace.DisplayIdentifier(id))).ToArray();
            if (selected.Length == 0) selected = active;
            screenBounds = selected.Select(CGDisplayBounds).ToArray();
            screenRefresh = clock.ElapsedMilliseconds;
        }
        CGPoint nearest = target;
        double closest = double.PositiveInfinity;
        foreach (var bounds in screenBounds)
        {
            double x = Math.Clamp(target.x, bounds.origin.x, bounds.origin.x + Math.Max(0, bounds.size.width - 1));
            double y = Math.Clamp(target.y, bounds.origin.y, bounds.origin.y + Math.Max(0, bounds.size.height - 1));
            double distance = (target.x - x) * (target.x - x) + (target.y - y) * (target.y - y);
            if (distance < closest) { closest = distance; nearest = new CGPoint(x, y); }
        }
        return nearest;
    }
    private CGRect[]? screenBounds;
    private long screenRefresh;

    public void Move(Vector2 delta)
    {
        var current = Cursor();
        var next = ClampToScreen(current + new CGPoint(delta.X, delta.Y));
        var applied = new Vector2((float)(next.x - current.x), (float)(next.y - current.y));
        Mouse(dragging ? CGEventType.kCGEventLeftMouseDragged : CGEventType.kCGEventMouseMoved,
            next, CGMouseButton.kCGMouseButtonLeft, applied);
    }

    public void Down(bool right = false, bool drag = false)
    {
        var position = Cursor();
        long now = clock.ElapsedMilliseconds;
        double dx = position.x - lastClickPosition.x, dy = position.y - lastClickPosition.y;
        clickCount = !drag && !right && lastClick >= 0 && now - lastClick <= GetDoubleClickInterval() * 1000 && dx * dx + dy * dy < 25
            ? Math.Min(2, clickCount + 1) : 1;
        lastClick = right || drag ? -1 : now;
        lastClickPosition = position;
        dragging = !right;
        Mouse(right ? CGEventType.kCGEventRightMouseDown : CGEventType.kCGEventLeftMouseDown, position,
            right ? CGMouseButton.kCGMouseButtonRight : CGMouseButton.kCGMouseButtonLeft);
    }

    public void Up(bool right = false)
    {
        Mouse(right ? CGEventType.kCGEventRightMouseUp : CGEventType.kCGEventLeftMouseUp, Cursor(),
            right ? CGMouseButton.kCGMouseButtonRight : CGMouseButton.kCGMouseButtonLeft);
        if (!right) dragging = false;
    }

    private Vector2 scrollRemainder;
    public void Scroll(Vector2 delta, TouchScrollPhase phase)
    {
        scrollRemainder += delta;
        int x = (int)scrollRemainder.X, y = (int)scrollRemainder.Y;
        scrollRemainder -= new Vector2(x, y);
        if (x == 0 && y == 0 && phase is TouchScrollPhase.Changed or TouchScrollPhase.MomentumChanged) return;
        if (phase is TouchScrollPhase.Ended or TouchScrollPhase.MomentumEnded) scrollRemainder = Vector2.Zero;
        var e = CGEventCreateScrollWheelEvent2(source, CGScrollEventUnit.kCGScrollEventUnitPixel, 2, y, x, 0);
        if (e == IntPtr.Zero) return;
        try
        {
            CGEventSetIntegerValueField(e, CGEventField.scrollWheelEventIsContinuous, 1); // Continuous pixel scrolling.
            long scrollPhase = phase switch { TouchScrollPhase.Began => 1, TouchScrollPhase.Changed => 2, TouchScrollPhase.Ended => 4, _ => 0 };
            long momentumPhase = phase switch { TouchScrollPhase.MomentumBegan => 1, TouchScrollPhase.MomentumChanged => 2, TouchScrollPhase.MomentumEnded => 3, _ => 0 };
            CGEventSetIntegerValueField(e, CGEventField.scrollWheelEventScrollPhase, scrollPhase);
            CGEventSetIntegerValueField(e, CGEventField.scrollWheelEventMomentumPhase, momentumPhase);
            CGEventPost(CGEventTapLocation.kCGHIDEventTap, e);
        }
        finally { CFRelease(e); }
    }

    public void Dispose()
    {
        if (dragging) Up();
        if (source != IntPtr.Zero) CFRelease(source);
    }
}
