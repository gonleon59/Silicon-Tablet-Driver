using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenTabletDriver.UX.Native;

/// <summary>Serializes native settings, advanced commands, presets and resynchronization.</summary>
public static class SettingsOperations
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task RunAsync(Func<Task> operation)
    {
        await Gate.WaitAsync();
        try { await operation(); }
        finally { Gate.Release(); }
    }
}
