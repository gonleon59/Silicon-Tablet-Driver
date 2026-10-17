using System;
using System.Diagnostics;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Touch;

/// <summary>Filters coordinates in millimeters. Pressure, buttons and eraser identity pass unchanged.</summary>
[PluginName("Bamboo pen hover stabilization")]
[SupportedPlatform(PluginPlatform.MacOS)]
public sealed class BambooPenStabilizer : IPositionedPipelineElement<IDeviceReport>
{
    [TabletReference] public TabletReference? Tablet { get; set; }
    [Property("Pen hover smoothing (ms)"), DefaultPropertyValue(35f)] public float Smoothing { get; set; } = 35;
    [Property("Pen contact smoothing (ms)"), DefaultPropertyValue(12f)] public float ContactSmoothing { get; set; } = 12;
    public PipelinePosition Position => PipelinePosition.PreTransform;
    public event Action<IDeviceReport?>? Emit;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly HoverPositionFilter filter = new();

    public void Consume(IDeviceReport? report)
    {
        if (Tablet?.Properties.Name == "Wacom CTH-661")
        {
            if (report is ITabletReport pen)
            {
                var d = Tablet.Properties.Specifications.Digitizer;
                var units = new Vector2(d.Width / d.MaxX, d.Height / d.MaxY);
                bool contact = pen.Pressure > 0;
                if (d.MaxX > 0 && d.MaxY > 0 && d.Width > 0 && d.Height > 0)
                    pen.Position = filter.Update(pen.Position * units, clock.Elapsed.TotalMilliseconds, contact, contact ? ContactSmoothing : Smoothing) / units;
            }
            else if (report is OutOfRangeReport) filter.Reset();
        }
        Emit?.Invoke(report);
    }
}

public sealed class HoverPositionFilter
{
    private Vector2 filtered, anchor, previous;
    private double lastTime = -1;
    private bool wasContact;

    public void Reset() => lastTime = -1;

    public Vector2 Update(Vector2 position, double now, bool contact, float smoothing)
    {
        float duration = float.IsFinite(smoothing) ? Math.Clamp(smoothing, 0, 80) : 35;
        if (contact != wasContact || duration <= 0 || lastTime < 0 || now <= lastTime || now-lastTime > 100)
        {
            filtered = anchor = previous = position;
            lastTime = now;
            wasContact = contact;
            return position;
        }
        float elapsed = (float)(now-lastTime);
        float speed = Vector2.Distance(position, previous) * 1000 / elapsed;
        float tau = duration / (1 + speed / 30);
        filtered = Vector2.Lerp(filtered, position, 1-MathF.Exp(-elapsed / Math.Max(2, tau)));
        var delta = filtered-anchor;
        float distance = delta.Length();
        // A small soft dead zone suppresses stationary hover noise without snapping position.
        float radius = contact ? 0.02f * duration / 12 : 0.12f * duration / 35;
        if (distance > radius) anchor += delta * ((distance-radius)/distance);
        previous = position;
        lastTime = now;
        return anchor;
    }
}
