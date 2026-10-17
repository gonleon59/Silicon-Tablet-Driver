using System;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop;

namespace OpenTabletDriver.UX.Native;

/// <summary>Compatibility entry point; production saves are owned by the daemon.</summary>
public static class NativeSettingsTransaction
{
    public static Task SaveAsync(Settings previous, Settings candidate, string path, Func<Settings, Task> apply) =>
        SettingsFileTransaction.SaveAsync(previous, candidate, path, apply);
}
