using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OpenTabletDriver.Desktop;

/// <summary>Called under the daemon's settings gate; detects other file writers before commit.</summary>
public static class SettingsFileTransaction
{
    public static async Task SaveAsync(Settings previous, Settings candidate, string path, Func<Settings, Task> apply)
    {
        string? Fingerprint() => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;
        string? original = Fingerprint();
        string staged = path + ".native-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using var file = new FileStream(staged, options);
            void Stage()
            {
                byte[] data = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(candidate, Formatting.Indented));
                file.Position = 0;
                file.SetLength(0);
                file.Write(data);
                file.Flush(true);
            }
            Stage(); // Fail an unwritable location before changing the running driver.
            if (Fingerprint() != original) throw new IOException("Settings file changed. Refresh before saving.");
            try
            {
                await apply(candidate);
                Stage(); // Persist any geometry/defaults normalized while applying the candidate.
                file.Close();
                if (Fingerprint() != original) throw new IOException("Settings file changed. Refresh before saving.");
                File.Move(staged, path, true);
            }
            catch (Exception error)
            {
                try { await apply(previous); }
                catch (Exception rollback)
                {
                    throw new SettingsRollbackException(error, rollback);
                }
                throw;
            }
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Both application/commit and the single rollback attempt failed.</summary>
public sealed class SettingsRollbackException(Exception error, Exception rollback) : AggregateException(
    "Settings could not be saved and the running driver could not be restored. Refresh settings before continuing.", error, rollback);
