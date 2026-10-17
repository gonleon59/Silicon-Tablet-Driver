using System;
using System.Linq;
using OpenTabletDriver.Desktop.Diagnostics;

namespace OpenTabletDriver.UX.MacOS
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            StartupLog.Start("GUI");
            bool relaunch = args.Contains("--permissions-relaunch");
            args = args.Where(a => a != "--permissions-relaunch").ToArray();
            if (PermissionHelper.HasPermissions(args, relaunch))
            {
                App.Run("OpenTabletDriver.UX.MacOS.Platform, OpenTabletDriver.UX.MacOS", args);
            }
        }
    }
}
