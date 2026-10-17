using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.Generic;
using OpenTabletDriver.Plugin.Platform.Display;

namespace OpenTabletDriver.Desktop.Interop.Display
{
    using static OSX;

    public class MacOSDisplay : IVirtualScreen
    {
        public float Width
        {
            get
            {
                var left = Displays.Min(d => d.Position.X);
                var right = Displays.Max(d => d.Position.X + d.Width);
                return right - left;
            }
        }

        public float Height
        {
            get
            {
                var top = Displays.Min(d => d.Position.Y);
                var bottom = Displays.Max(d => d.Position.Y + d.Height);
                return bottom - top;
            }
        }

        public Vector2 Position => new Vector2(0, 0);

        public IEnumerable<IDisplay> Displays
        {
            get
            {
                lock (displayGate)
                {
                    long now = Environment.TickCount64;
                    if (cachedDisplays == null || now - lastRefresh >= 1000)
                    {
                        cachedDisplays = ReadDisplays();
                        lastRefresh = now;
                    }
                    return cachedDisplays;
                }
            }
        }
        private readonly object displayGate = new();
        private IDisplay[]? cachedDisplays;
        private long lastRefresh;
        public void Refresh() { lock (displayGate) cachedDisplays = null; }

        public int Index => 0;

        public override string ToString()
        {
            return $"VirtualDisplay {Index} ({Width}x{Height}@{Position})";
        }

        private static IDisplay[] ReadDisplays()
        {
            var ids = new uint[32];
            CGGetActiveDisplayList((uint)ids.Length, ids, out var count);
            if (count == 0) return [];
            var bounds = ids.Take((int)count).Select(CGDisplayBounds).ToArray();
            double offsetX = bounds.Min(d => d.origin.x), offsetY = bounds.Min(d => d.origin.y);
            var result = new IDisplay[count];
            for (int i = 0; i < count; i++)
                result[i] = new Display((float)bounds[i].size.width, (float)bounds[i].size.height,
                    new Vector2((float)(bounds[i].origin.x - offsetX), (float)(bounds[i].origin.y - offsetY)), i,
                    MacOSWorkspace.DisplayName(ids[i]), MacOSWorkspace.DisplayIdentifier(ids[i]));
            return result;
        }
    }
}
