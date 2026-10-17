using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;

namespace OpenTabletDriver.Desktop.Touch;

public enum TouchActionKind
{
    Move, Scroll, LeftClick, RightClick, DragStart, DragEnd,
    ZoomIn, ZoomOut, RotateClockwise, RotateCounterclockwise, PageBack, PageForward
}

public enum TouchScrollPhase { None, Began, Changed, Ended, MomentumBegan, MomentumChanged, MomentumEnded }

public readonly record struct TouchAction(
    TouchActionKind Kind, Vector2 Delta = default, TouchScrollPhase Phase = TouchScrollPhase.None);

// A value buffer avoids allocating a list for each 50 Hz tablet report.
// Results own their values and remain valid after the next engine update.
public struct TouchActionBatch : IReadOnlyList<TouchAction>
{
    private TouchAction first, second, third, fourth;
    public int Count { get; private set; }
    public readonly TouchAction this[int index] => index >= 0 && index < Count
        ? index switch { 0 => first, 1 => second, 2 => third, _ => fourth }
        : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Add(TouchAction action)
    {
        switch (Count)
        {
            case 0: first = action; break;
            case 1: second = action; break;
            case 2: third = action; break;
            case 3: fourth = action; break;
            default: throw new InvalidOperationException("Touch action buffer capacity exceeded.");
        }
        Count++;
    }

    public readonly Enumerator GetEnumerator() => new(this);
    readonly IEnumerator<TouchAction> IEnumerable<TouchAction>.GetEnumerator() => GetEnumerator();
    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator(TouchActionBatch batch) : IEnumerator<TouchAction>
    {
        private int index = -1;
        public readonly TouchAction Current => batch[index];
        readonly object IEnumerator.Current => Current;
        public bool MoveNext() => ++index < batch.Count;
        public void Reset() => index = -1;
        public readonly void Dispose() { }
    }
}
