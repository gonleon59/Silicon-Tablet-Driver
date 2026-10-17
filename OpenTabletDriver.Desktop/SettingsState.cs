using Newtonsoft.Json;

namespace OpenTabletDriver.Desktop;

/// <summary>A detached settings snapshot and an opaque daemon-session revision.</summary>
public sealed record SettingsState(Settings Settings, string Token)
{
    public static Settings Copy(Settings settings) =>
        JsonConvert.DeserializeObject<Settings>(JsonConvert.SerializeObject(settings))!;
}
