using System;
using System.Numerics;

namespace OpenTabletDriver.Desktop.Touch;

/// <summary>Resample hardware scroll steps on a timer, without crossing native phase boundaries.</summary>
public sealed class SmoothScrollBuffer
{
    private Vector2 pending;
    private TouchScrollPhase continuation;
    private long lastTick;
    public bool HasPending => pending.LengthSquared() > 0;

    public TouchActionBatch Submit(Vector2 delta, TouchScrollPhase phase, long now)
    {
        TouchActionBatch actions = default;
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y)) return actions;
        if (phase is TouchScrollPhase.Began or TouchScrollPhase.MomentumBegan)
        {
            Flush(ref actions);
            continuation = phase == TouchScrollPhase.Began ? TouchScrollPhase.Changed : TouchScrollPhase.MomentumChanged;
            lastTick = now;
            actions.Add(new(TouchActionKind.Scroll, default, phase));
        }
        if (!HasPending && phase is TouchScrollPhase.Changed or TouchScrollPhase.MomentumChanged) lastTick = now;
        pending += delta;
        if (phase is TouchScrollPhase.Ended or TouchScrollPhase.MomentumEnded)
        {
            Flush(ref actions);
            actions.Add(new(TouchActionKind.Scroll, default, phase));
        }
        return actions;
    }

    public TouchActionBatch Tick(long now)
    {
        TouchActionBatch actions = default;
        if (!HasPending) return actions;
        long elapsed = Math.Clamp(now - lastTick, 0, 50);
        if (elapsed == 0) return actions;
        lastTick = now;
        var step = pending * (1 - MathF.Exp(-elapsed / 12f));
        pending -= step;
        if (pending.LengthSquared() < 0.0001f) { step += pending; pending = Vector2.Zero; }
        actions.Add(new(TouchActionKind.Scroll, step, continuation));
        return actions;
    }

    private void Flush(ref TouchActionBatch actions)
    {
        if (HasPending) actions.Add(new(TouchActionKind.Scroll, pending, continuation));
        pending = Vector2.Zero;
    }
}
