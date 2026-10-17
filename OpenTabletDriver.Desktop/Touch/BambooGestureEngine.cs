using System;
using System.Numerics;
using OpenTabletDriver.Plugin.Tablet.Touch;

namespace OpenTabletDriver.Desktop.Touch;

/// <summary>Deterministic two-contact recognizer. Timestamps are monotonic milliseconds.</summary>
public sealed class BambooGestureEngine
{
    private enum Gesture { Undecided, Scroll, Pinch, Rotate, Page }
    private Gesture gesture;
    private long started, lastTime = -1, lastZoomAt = -1, lastRotationAt = -1, lastScrollAt;
    private int count, maxCount, previousMask;
    private Vector2 previous, origin, filteredPointer, pointerAnchor, scrollVelocity, pageDisplacement, scrollAnchor, filteredScroll, originSeparation;
    private float initialSpan, zoomSpan, initialAngle, rotationAnchor;
    private Vector2 previousPointerDelta;
    private bool moved, remainingFinger, dragging, scrollStarted, scrollResting;
    private long scrollUnchangedSince = -1;
    private Vector2 scrollRestOrigin;
    private long lastTapAt = -1;
    private Vector2 lastTapPosition, momentumVelocity;
    private long momentumStarted, momentumLastTime;
    private bool momentumActive, momentumBegan;

    public bool TapToClick { get; set; } = true;
    public bool TapToDrag { get; set; } = true;
    public bool PinchToZoom { get; set; } = true;
    public bool Rotate { get; set; } = true;
    public bool PageSwipes { get; set; } = true;
    public bool ScrollInertia { get; set; } = true;
    public bool PointerAcceleration { get; set; } = true;
    public float AccelerationStrength { get; set; } = 0.4f;
    public float PointerSmoothing { get; set; } = 40;
    public float FineMovementSpeed { get; set; } = 1;
    public bool HasMomentum => momentumActive;

    public static float AccelerationGain(float unitsPerSecond, float strength)
    {
        if (!float.IsFinite(unitsPerSecond) || !float.IsFinite(strength)) return 1;
        float t = Math.Clamp((unitsPerSecond - 150) / 850, 0, 1);
        return 1 + 2.5f * Math.Clamp(strength, 0, 2) * t * t * (3 - 2 * t);
    }

    /// <summary>Cancel a gesture, releasing held buttons and ending scroll phases.</summary>
    public TouchActionBatch Reset()
    {
        TouchActionBatch actions = default;
        if (dragging) actions.Add(new(TouchActionKind.DragEnd));
        EndScroll(ref actions);
        EndMomentum(ref actions);
        ClearContact();
        lastTapAt = -1;
        lastTime = -1;
        return actions;
    }

    public TouchActionBatch Update(TouchPoint?[] points, long now)
    {
        TouchActionBatch actions = default;
        TouchPoint? first = null, second = null;
        foreach (var point in points)
        {
            if (point == null || point.TouchID > 1 || !IsFinite(point.Position)) continue;
            if (first == null) first = point;
            else if (second == null && point.TouchID != first.TouchID) second = point;
        }
        int n = first == null ? 0 : second == null ? 1 : 2;
        int mask = (first == null ? 0 : 1 << first.TouchID) | (second == null ? 0 : 1 << second.TouchID);
        long elapsed = lastTime < 0 ? 1 : Math.Max(1, now - lastTime);
        if (lastTime >= 0 && (now < lastTime || now - lastTime > 500))
            actions = Reset();
        lastTime = now;
        if (n == 0)
        {
            Lift(now, ref actions);
            return actions;
        }
        var center = second == null ? first!.Position : (first!.Position + second.Position) / 2;
        var separation = second == null ? Vector2.Zero : second.Position - first!.Position;
        float span = separation.Length();
        float angle = MathF.Atan2(separation.Y, separation.X) * (180 / MathF.PI);
        if (count == 0)
        {
            EndMomentum(ref actions);
            started = now;
            maxCount = n;
            InitializePositions(center, span, angle, separation);
            if (n == 1 && TapToClick && TapToDrag && lastTapAt >= 0 &&
                now >= lastTapAt && now - lastTapAt <= 500 && Vector2.Distance(center, lastTapPosition) <= 60)
            {
                dragging = true;
                actions.Add(new(TouchActionKind.DragStart));
            }
            lastTapAt = -1;
        }
        else if (n != count || mask != previousMask)
        {
            if (dragging)
            {
                actions.Add(new(TouchActionKind.DragEnd));
                dragging = false;
                moved = true;
            }
            EndScroll(ref actions);
            if (n == count && mask != previousMask)
            {
                // A replacement is a new contact, never the end of a tap on the old one.
                moved = true;
                lastTapAt = -1;
            }
            if (n < count || n == count && mask != previousMask) remainingFinger = true;
            InitializePositions(center, span, angle, separation);
            // Capacitive contacts can briefly disappear; resume two-finger gestures
            // after re-basing, while keeping a remaining single finger suppressed.
            if (n == 2 && maxCount == 2) remainingFinger = false;
            maxCount = Math.Max(maxCount, n);
        }
        else if (!remainingFinger)
        {
            var delta = center - previous;
            float travel = Vector2.Distance(center, origin);
            if (travel > 24) moved = true;
            if (delta.LengthSquared() >= 40000)
            {
                filteredPointer = pointerAnchor = filteredScroll = scrollAnchor = center;
                scrollVelocity = Vector2.Zero;
                scrollResting = false;
                scrollUnchangedSince = -1;
            }
            else if (n == 1) MovePointer(center, elapsed, ref actions);
            else TwoFinger(center, span, angle, separation, delta, elapsed, now, ref actions);
        }
        count = n;
        previousMask = mask;
        previous = center;
        return actions;
    }

    /// <summary>Advance scroll momentum without manufacturing tablet reports.</summary>
    public TouchActionBatch Tick(long now)
    {
        TouchActionBatch actions = default;
        if (!momentumActive) return actions;
        if (!ScrollInertia || now < momentumLastTime || now - momentumStarted >= 900 ||
            momentumVelocity.LengthSquared() < 64)
        {
            EndMomentum(ref actions);
            return actions;
        }
        long elapsed = Math.Clamp(now - momentumLastTime, 0, 50);
        if (elapsed == 0) return actions;
        float decay = MathF.Exp(-elapsed / 180f);
        var delta = momentumVelocity * (0.18f * (1 - decay));
        momentumVelocity *= decay;
        momentumLastTime = now;
        actions.Add(new(TouchActionKind.Scroll, delta,
            momentumBegan ? TouchScrollPhase.MomentumChanged : TouchScrollPhase.MomentumBegan));
        momentumBegan = true;
        return actions;
    }

    private void MovePointer(Vector2 center, long elapsed, ref TouchActionBatch actions)
    {
        var rawDelta = center - previous;
        // Stop instead of emitting the remaining smoothing tail after a fast flick.
        // Rebase only the filter reference; the system cursor never jumps to this point.
        if (previousPointerDelta.LengthSquared() > 100 && rawDelta.LengthSquared() <= 36)
        {
            filteredPointer = pointerAnchor = center;
            previousPointerDelta = rawDelta;
            return;
        }
        previousPointerDelta = rawDelta;
        float rawVelocity = rawDelta.Length() * 1000 / Math.Max(8, elapsed);
        // Keep strong filtering for small sensor noise, shorten lag for fast movement.
        float smoothing = float.IsFinite(PointerSmoothing) ? Math.Clamp(PointerSmoothing, 0, 80) : 40;
        float timeConstant = rawDelta.LengthSquared() > 100
            ? Math.Max(4, smoothing / (1 + rawVelocity / 400)) : smoothing;
        float alpha = timeConstant <= 0 ? 1 : Math.Clamp(1 - MathF.Exp(-elapsed / timeConstant), 0.1f, 1);
        var oldFiltered = filteredPointer;
        filteredPointer = Vector2.Lerp(filteredPointer, center, alpha);
        var displacement = filteredPointer - pointerAnchor;
        float distance = displacement.Length();
        const float stationaryRadius = 2;
        if (distance <= stationaryRadius) return;
        var step = displacement * ((distance - stationaryRadius) / distance);
        pointerAnchor += step;
        float velocity = Vector2.Distance(filteredPointer, oldFiltered) * 1000 / Math.Max(8, elapsed);
        float gain = PointerAcceleration ? AccelerationGain(velocity, AccelerationStrength) : 1;
        // Blend fine-positioning gain continuously into normal speed; no speed threshold jump.
        float fine = float.IsFinite(FineMovementSpeed) ? Math.Clamp(FineMovementSpeed, 0.2f, 1) : 1;
        gain *= fine + (1 - fine) * Math.Clamp((velocity - 30) / 170, 0, 1);
        actions.Add(new(TouchActionKind.Move, step * gain));
    }

    private void TwoFinger(Vector2 center, float span, float angle, Vector2 separation, Vector2 delta,
        long elapsed, long now, ref TouchActionBatch actions)
    {
        var travelVector = center - origin;
        float travel = travelVector.Length();
        float spanChange = Math.Abs(span - initialSpan);
        float angleChange = AngleDifference(angle, initialAngle);
        if (gesture == Gesture.Undecided)
        {
            float arc = initialSpan * Math.Abs(angleChange) * (MathF.PI / 180);
            var differential = (separation - originSeparation) / 2;
            var firstTravel = travelVector - differential;
            var secondTravel = travelVector + differential;
            bool parallel = Vector2.Dot(firstTravel, secondTravel) > 0 &&
                firstTravel.LengthSquared() >= 4 && secondTravel.LengthSquared() >= 4;
            bool fastHorizontal = PageSwipes && Math.Abs(travelVector.X) > Math.Abs(travelVector.Y) * 2 &&
                Math.Abs(delta.X) * 1000 / Math.Max(8, elapsed) >= 250;
            if (parallel && travel > 6 && !fastHorizontal)
                gesture = Gesture.Scroll;
            else if (PinchToZoom && initialSpan > 40 && spanChange > 24 && spanChange > travel * 1.8f)
                gesture = Gesture.Pinch;
            else if (Rotate && initialSpan > 40 && Math.Abs(angleChange) >= 10 &&
                spanChange < initialSpan * 0.15f && arc > travel * 1.25f)
                gesture = Gesture.Rotate;
            else if (PageSwipes && Math.Abs(travelVector.X) >= 28 &&
                Math.Abs(travelVector.X) > Math.Abs(travelVector.Y) * 2 &&
                Math.Abs(delta.X) * 1000 / Math.Max(8, elapsed) >= 250)
                gesture = Gesture.Page;
            else if (travel > 10)
            {
                bool possiblePage = PageSwipes && Math.Abs(travelVector.X) > Math.Abs(travelVector.Y) * 2 &&
                    Math.Abs(delta.X) * 1000 / Math.Max(8, elapsed) >= 250 && travel < 28;
                if (!possiblePage) gesture = Gesture.Scroll;
            }
            if (gesture != Gesture.Undecided) moved = true;
        }
        switch (gesture)
        {
            case Gesture.Page:
                pageDisplacement = travelVector;
                break;
            case Gesture.Pinch:
                if (zoomSpan <= 0) break;
                float ratio = span / zoomSpan;
                if ((ratio > 1.18f || ratio < 1 / 1.18f) && (lastZoomAt < 0 || now - lastZoomAt >= 250))
                {
                    actions.Add(new(ratio > 1 ? TouchActionKind.ZoomIn : TouchActionKind.ZoomOut));
                    zoomSpan = span;
                    lastZoomAt = now;
                }
                break;
            case Gesture.Rotate:
                float rotation = AngleDifference(angle, rotationAnchor);
                if (Math.Abs(rotation) >= 30 && (lastRotationAt < 0 || now - lastRotationAt >= 300))
                {
                    actions.Add(new(rotation > 0 ? TouchActionKind.RotateClockwise : TouchActionKind.RotateCounterclockwise));
                    rotationAnchor = angle;
                    lastRotationAt = now;
                }
                break;
            case Gesture.Scroll:
                if (scrollResting)
                {
                    if (Vector2.Distance(center, scrollRestOrigin) <= 3) break;
                    scrollResting = false;
                    scrollUnchangedSince = -1;
                    filteredScroll = scrollRestOrigin; // Retain scrollAnchor and its pending displacement.
                }
                if (delta == Vector2.Zero)
                {
                    if (scrollUnchangedSince < 0) scrollUnchangedSince = now - elapsed;
                    if (now - scrollUnchangedSince >= 120)
                    {
                        scrollResting = true;
                        scrollRestOrigin = filteredScroll = center;
                        scrollVelocity = Vector2.Zero;
                        break;
                    }
                }
                else scrollUnchangedSince = -1;
                // Filter by elapsed time, then retain displacement behind a soft dead zone.
                // Counting small packets as a stop discards deliberate slow movement.
                float alpha = 1 - MathF.Exp(-elapsed / 40f);
                filteredScroll = Vector2.Lerp(filteredScroll, center, alpha);
                var displacement = filteredScroll - scrollAnchor;
                float distance = displacement.Length();
                const float scrollStationaryRadius = 3;
                var step = distance > scrollStationaryRadius
                    ? displacement * ((distance - scrollStationaryRadius) / distance) : Vector2.Zero;
                scrollAnchor += step;
                var velocity = step * (1000f / elapsed);
                scrollVelocity = Vector2.Lerp(scrollVelocity, velocity, alpha);
                if (step != Vector2.Zero) lastScrollAt = now;
                if (step != Vector2.Zero || !scrollStarted)
                    actions.Add(new(TouchActionKind.Scroll, step,
                        scrollStarted ? TouchScrollPhase.Changed : TouchScrollPhase.Began));
                scrollStarted = true;
                break;
        }
    }

    private void Lift(long now, ref TouchActionBatch actions)
    {
        if (count == 0) return;
        if (dragging) actions.Add(new(TouchActionKind.DragEnd));
        else if (gesture == Gesture.Page && now - started <= 700 && Math.Abs(pageDisplacement.X) >= 80)
            actions.Add(new(pageDisplacement.X > 0 ? TouchActionKind.PageBack : TouchActionKind.PageForward));
        else if (TapToClick && !moved && now - started <= 280)
        {
            actions.Add(new(maxCount == 1 ? TouchActionKind.LeftClick : TouchActionKind.RightClick));
            if (maxCount == 1) { lastTapAt = now; lastTapPosition = previous; }
        }
        EndScroll(ref actions);
        if (gesture == Gesture.Scroll && ScrollInertia && now - lastScrollAt <= 80 &&
            scrollVelocity.LengthSquared() > 6400)
        {
            momentumVelocity = scrollVelocity;
            momentumStarted = momentumLastTime = now;
            momentumActive = true;
            momentumBegan = false;
        }
        ClearContact();
    }

    private void InitializePositions(Vector2 center, float span, float angle, Vector2 separation)
    {
        origin = previous = filteredPointer = pointerAnchor = filteredScroll = scrollAnchor = center;
        scrollResting = false;
        scrollUnchangedSince = -1;
        previousPointerDelta = Vector2.Zero;
        originSeparation = separation;
        initialSpan = zoomSpan = span;
        initialAngle = rotationAnchor = angle;
    }
    private void EndScroll(ref TouchActionBatch actions)
    {
        if (scrollStarted) actions.Add(new(TouchActionKind.Scroll, default, TouchScrollPhase.Ended));
        scrollStarted = false;
    }
    private void EndMomentum(ref TouchActionBatch actions)
    {
        if (momentumBegan) actions.Add(new(TouchActionKind.Scroll, default, TouchScrollPhase.MomentumEnded));
        momentumActive = momentumBegan = false;
        momentumVelocity = Vector2.Zero;
    }
    private void ClearContact()
    {
        count = maxCount = previousMask = 0;
        pageDisplacement = Vector2.Zero;
        moved = remainingFinger = dragging = scrollStarted = false;
        gesture = Gesture.Undecided;
        lastZoomAt = lastRotationAt = -1;
        scrollVelocity = Vector2.Zero;
    }
    private static bool IsFinite(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y);
    private static float AngleDifference(float a, float b) => MathF.IEEERemainder(a - b, 360);
}
