using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Newtonsoft.Json;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver.Desktop.Profiles;

public sealed class TabletMappingPreferences
{
    public const float DefaultHorizontalSideMargin = 4.5f;
    public const float DefaultHorizontalTopBottomMargin = 5;
    public string[] DisplayIdentifiers { get; set; } = [];
    public int Rotation { get; set; }
    public bool UseComfortableArea { get; set; } = true;
    [JsonIgnore]
    public bool UsesComfortableArea => Rotation is 90 or 270 && UseComfortableArea;
    public float HorizontalSideMargin { get; set; } = DefaultHorizontalSideMargin;
    public float HorizontalTopBottomMargin { get; set; } = DefaultHorizontalTopBottomMargin;
    public bool NearUser { get; set; } = true;
    public float EdgeMargin { get; set; } = 8;
    public float AreaScale { get; set; } = 0.85f;
    public bool PreserveProportions { get; set; } = true;

    public static AreaSettings DisplayArea(IEnumerable<IDisplay> available, string[] identifiers)
    {
        var all = available.Where(d => d is not IVirtualScreen).ToArray();
        var selected = identifiers.Length == 0 ? all
            : all.Where(d => identifiers.Contains(d.Identifier)).ToArray();
        if (selected.Length == 0) selected = all;
        if (selected.Length == 0) throw new InvalidOperationException("No displays are available.");
        float left = selected.Min(d => d.Position.X), top = selected.Min(d => d.Position.Y);
        float right = selected.Max(d => d.Position.X + d.Width), bottom = selected.Max(d => d.Position.Y + d.Height);
        return new() { Width = right - left, Height = bottom - top, X = (left + right) / 2, Y = (top + bottom) / 2 };
    }

    public AreaSettings TabletArea(DigitizerSpecifications tablet, AreaSettings display)
    {
        if (tablet.Width <= 0 || tablet.Height <= 0 || display.Width <= 0 || display.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(tablet), "Mapping dimensions must be positive.");
        int rotation = Rotation is 0 or 90 or 180 or 270 ? Rotation : 0;
        bool vertical = rotation is 90 or 270;
        float maxWidth = vertical ? tablet.Height : tablet.Width;
        float maxHeight = vertical ? tablet.Width : tablet.Height;
        if (!vertical)
        {
            float side = Math.Clamp(float.IsFinite(HorizontalSideMargin) ? HorizontalSideMargin : DefaultHorizontalSideMargin, 0, maxWidth / 3);
            float topBottom = Math.Clamp(float.IsFinite(HorizontalTopBottomMargin) ? HorizontalTopBottomMargin : DefaultHorizontalTopBottomMargin, 0, maxHeight / 3);
            maxWidth -= side * 2;
            maxHeight -= topBottom * 2;
        }
        if (!UsesComfortableArea)
            return new() { Width = maxWidth, Height = maxHeight, X = tablet.Width / 2, Y = tablet.Height / 2, Rotation = rotation };
        float margin = float.IsFinite(EdgeMargin) ? Math.Clamp(EdgeMargin, 0, Math.Min(maxWidth, maxHeight) / 3) : 8;
        maxWidth -= margin * 2;
        maxHeight -= margin * 2;
        float width = maxWidth, height = maxHeight;
        if (PreserveProportions)
        {
            float ratio = display.Width / display.Height;
            width = Math.Min(width, height * ratio);
            height = width / ratio;
        }
        float scale = float.IsFinite(AreaScale) ? Math.Clamp(AreaScale, 0.3f, 1) : 0.85f;
        width *= scale;
        height *= scale;
        var center = new Vector2(tablet.Width / 2, tablet.Height / 2);
        if (NearUser)
            center += Vector2.Transform(new Vector2(0, (maxHeight - height) / 2),
                Matrix3x2.CreateRotation(rotation * MathF.PI / 180));
        return new() { Width = width, Height = height, X = center.X, Y = center.Y, Rotation = rotation };
    }
}
