using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Eto.Forms;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop.Diagnostics;
using OpenTabletDriver.Native.OSX.ApplicationServices;
using OpenTabletDriver.Native.OSX.IOkit;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX.Tools;

public static class SupportDiagnostics
{
    public static async Task<string> CaptureAsync()
    {
        string[] tablets = [];
        string? tabletQueryError = null;
        try
        {
            if (!App.Driver.IsConnected) tabletQueryError = "Daemon disconnected";
            else tablets = (await App.Driver.Instance!.GetTablets().WaitAsync(TimeSpan.FromSeconds(3)))
                .Select(t => t.Properties.Name).ToArray();
        }
        catch (Exception ex) { tabletQueryError = ex.GetType().Name; }
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string Redact(string value) => string.IsNullOrEmpty(home) ? value : value.Replace(home, "~", StringComparison.Ordinal);
        var logs = Directory.Exists(StartupLog.DirectoryPath)
            ? new DirectoryInfo(StartupLog.DirectoryPath).GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(4).Select(f =>
                {
                    try
                    {
                        // Read only the tail; shared access permits capture while the driver is logging.
                        using var stream = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        stream.Seek(Math.Max(0, stream.Length - 65536), SeekOrigin.Begin);
                        using var reader = new StreamReader(stream);
                        return new { File = f.Name, Text = Redact(reader.ReadToEnd()) };
                    }
                    catch (IOException ex) { return new { File = f.Name, Text = ex.GetType().Name }; }
                    catch (UnauthorizedAccessException ex) { return new { File = f.Name, Text = ex.GetType().Name }; }
                }).ToArray() : [];
        object? permissions = OperatingSystem.IsMacOS() ? new
        {
            InputMonitoring = IOKit.IOHIDCheckAccess(IOHIDRequestType.kIOHIDRequestTypeListenEvent).ToString(),
            Accessibility = ApplicationServices.AXIsProcessTrusted(),
            CheckedFor = "GUI process"
        } : null;
        return JsonConvert.SerializeObject(new
        {
            Version = App.Version, CapturedUtc = DateTimeOffset.UtcNow,
            OS = RuntimeInformation.OSDescription, Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Permissions = permissions,
            Tablets = tablets, TabletQueryError = tabletQueryError,
            RecentLogs = logs
        }, Formatting.Indented);
    }

    public static async Task SaveAsync(Control parent)
    {
        var dialog = new SaveFileDialog { Title = Language.T("Save diagnostics"), FileName = $"OpenTabletDriver-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        dialog.Filters.Add(new FileFilter("JSON", ".json"));
        if (dialog.ShowDialog(parent) != DialogResult.Ok) return;
        try { await File.WriteAllTextAsync(dialog.FileName, await CaptureAsync()); }
        catch (Exception ex) { Log.Exception(ex); MessageBox.Show(parent, ex.Message, Language.T("Application Error")); }
    }
}
