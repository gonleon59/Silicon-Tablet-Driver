using System;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenTabletDriver.Desktop.Diagnostics;

/// <summary>Flush startup output immediately, including failures before the settings window exists.</summary>
public static class StartupLog
{
    public static string DirectoryPath => Path.Combine(
        OperatingSystem.IsMacOS()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
            : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenTabletDriver", "Logs", "Startup");
    private static bool started;

    public static void Start(string component)
    {
        if (started) return;
        started = true;
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            foreach (var old in new DirectoryInfo(DirectoryPath).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Skip(9))
                try { old.Delete(); } catch (IOException) { /* Another process may still have this log open. */ }
            var path = Path.Combine(DirectoryPath, $"{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}-{component}-{Environment.ProcessId}.log");
            var file = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), Encoding.UTF8) { AutoFlush = true };
            var gate = new object();
            Console.SetError(new TeeWriter(Console.Error, file, gate));
            Console.SetOut(new TeeWriter(Console.Out, file, gate));
            Console.Error.WriteLine($"Started {component}: pid={Environment.ProcessId}, runtime={Environment.Version}, OS={Environment.OSVersion}, architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            AppDomain.CurrentDomain.UnhandledException += (_, e) => WriteException(e.ExceptionObject);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => WriteException(e.Exception);
        }
        catch (IOException e) { Console.Error.WriteLine($"Startup log unavailable: {e.Message}"); }
        catch (UnauthorizedAccessException e) { Console.Error.WriteLine($"Startup log unavailable: {e.Message}"); }
    }

    public static void WriteException(object? error) => Console.Error.WriteLine($"{DateTime.UtcNow:O} ERROR {error}");

    private sealed class TeeWriter(TextWriter console, StreamWriter file, object gate) : TextWriter
    {
        // The file is intentionally retained until process exit; every write is flushed.
        private const long MaximumBytes = 5 * 1024 * 1024;
        private bool writable = true;
        private void Save(string? value, bool line = false)
        {
            if (!writable) return;
            try
            {
                if (file.BaseStream.Position >= MaximumBytes) return;
                if (line) file.WriteLine(value); else file.Write(value);
            }
            catch (IOException) { writable = false; }
            catch (ObjectDisposedException) { writable = false; }
        }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value)
        {
            lock (gate)
            {
                console.Write(value);
                Save(value.ToString());
            }
        }
        public override void Write(string? value)
        {
            lock (gate)
            {
                console.Write(value);
                Save(value);
            }
        }
        public override void WriteLine(string? value)
        {
            lock (gate)
            {
                console.WriteLine(value);
                Save(value, true);
            }
        }
        public override void Flush() { lock (gate) { console.Flush(); if (writable) { try { file.Flush(); } catch (IOException) { writable = false; } } } }
    }
}
