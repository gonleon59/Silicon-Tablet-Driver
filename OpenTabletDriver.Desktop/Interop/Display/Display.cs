using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using OpenTabletDriver.Plugin.Platform.Display;

namespace OpenTabletDriver.Desktop.Interop.Display
{
    internal class Display : IDisplay
    {
        [SetsRequiredMembers]
        internal Display(float width, float height, Vector2 position, int index = 0, string? name = null, string? identifier = null)
        {
            Width = width;
            Height = height;
            Position = position;
            Index = index;
            Name = name ?? $"Display {index + 1}";
            Identifier = identifier ?? $"screen-{index}";
        }

        public required int Index { internal init; get; }
        public string Name { get; internal init; }
        public string Identifier { get; internal init; }
        public required float Width { internal init; get; }
        public required float Height { internal init; get; }
        public required Vector2 Position { internal init; get; }

        public override string ToString()
        {
            return $"Display {Index} ({Width}x{Height}@{Position})";
        }
    }
}
