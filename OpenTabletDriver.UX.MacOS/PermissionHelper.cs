using System;
using System.Diagnostics;
using System.IO;
using MonoMac.AppKit;
using MonoMac.Foundation;
using OpenTabletDriver.Native.OSX;
using OpenTabletDriver.Native.OSX.ApplicationServices;
using OpenTabletDriver.Native.OSX.IOkit;

namespace OpenTabletDriver.UX.MacOS
{
    public static class PermissionHelper
    {
        public static bool HasPermissions(string[] arguments, bool alreadyRelaunched)
        {
            NSApplication.Init();
            var responsiblePid = LibQuarantine.responsibility_get_pid_responsible_for_pid(Environment.ProcessId);
            var responsibleAppName = NSRunningApplication.GetRunningApplication(responsiblePid)?.LocalizedName ?? "OpenTabletDriver";

            if (responsiblePid > 0 && responsiblePid != Environment.ProcessId)
            {
                // A direct terminal launch checks the host application's TCC identity.
                // Do not tell the user to remove correctly granted driver permissions.
                bool russian = Language.Current == "ru";
                using var contextAlert = new NSAlert
                {
                    MessageText = russian ? "Способ запуска OpenTabletDriver" : "OpenTabletDriver launch context",
                    InformativeText = russian
                        ? $"Этот экземпляр запущен через {responsibleAppName}, поэтому macOS проверяет разрешения этого приложения. Переключатели OpenTabletDriver менять не нужно. Откройте драйвер обычным способом из папки «Программы»."
                        : $"This instance was launched through {responsibleAppName}, so macOS checks that application's permissions. Do not remove the driver's grants. Open the driver normally from Applications.",
                    AlertStyle = NSAlertStyle.Warning
                };
                contextAlert.AddButton(russian ? "Выйти" : "Quit");
                if (!alreadyRelaunched) contextAlert.AddButton(russian ? "Открыть обычным способом" : "Open normally");
                if (contextAlert.RunModal() == 1001 && !alreadyRelaunched)
                {
                    var bundle = Directory.GetParent(Environment.ProcessPath!)?.Parent?.Parent?.FullName;
                    if (bundle != null && bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    {
                        var launch = new ProcessStartInfo("/usr/bin/open") { UseShellExecute = false };
                        launch.ArgumentList.Add("-n");
                        launch.ArgumentList.Add(bundle);
                        launch.ArgumentList.Add("--args");
                        launch.ArgumentList.Add("--permissions-relaunch");
                        foreach (var argument in arguments) launch.ArgumentList.Add(argument);
                        Process.Start(launch);
                    }
                }
                return false;
            }

            // Always re-read the OS result. A checked Settings row can belong to an older
            // ad-hoc signature, and neither a prompt nor opening Settings grants access.
            while (true)
            {
                var input = Environment.OSVersion.Version < new Version(10, 15)
                    ? IOHIDAccessType.kIOHIDAccessTypeGranted
                    : IOKit.IOHIDCheckAccess(IOHIDRequestType.kIOHIDRequestTypeListenEvent);
                var accessibility = ApplicationServices.AXIsProcessTrusted();
                Console.Error.WriteLine($"Permissions: pid={Environment.ProcessId}, responsiblePid={responsiblePid}, input={input}, accessibility={accessibility}");
                if (input == IOHIDAccessType.kIOHIDAccessTypeGranted && accessibility)
                    return true;

                bool russian = Language.Current == "ru";
                string yes = russian ? "разрешено" : "allowed";
                string no = russian ? "не разрешено для этой сборки" : "not allowed for this build";
                using var alert = new NSAlert
                {
                    MessageText = russian ? "Доступ OpenTabletDriver" : "OpenTabletDriver permissions",
                    InformativeText = russian
                        ? $"Мониторинг ввода: {(input == IOHIDAccessType.kIOHIDAccessTypeGranted ? yes : no)}\nУниверсальный доступ: {(accessibility ? yes : no)}\n\nПриложение: {responsibleAppName}\n\nЕсли переключатель уже включён, разрешение могло остаться от предыдущей подписи приложения. Удалите старую запись и добавьте текущий OpenTabletDriver из папки «Программы». Затем закройте и снова откройте драйвер.\n\n«Проверить снова» повторно читает разрешения macOS. Драйвер не перезапускается автоматически."
                        : $"Input Monitoring: {(input == IOHIDAccessType.kIOHIDAccessTypeGranted ? yes : no)}\nAccessibility: {(accessibility ? yes : no)}\n\nApplication: {responsibleAppName}\n\nAn enabled switch may belong to the previous application signature. Remove that entry and add the current OpenTabletDriver from Applications, then quit and reopen the driver.\n\nRecheck reads permissions from macOS again. The driver does not restart automatically.",
                    AlertStyle = NSAlertStyle.Warning
                };
                alert.AddButton(russian ? "Проверить снова" : "Recheck");
                alert.AddButton(russian ? "Мониторинг ввода…" : "Input Monitoring…");
                alert.AddButton(russian ? "Универсальный доступ…" : "Accessibility…");
                alert.AddButton(russian ? "Выйти" : "Quit");
                var result = alert.RunModal();
                switch (result)
                {
                    case 1000: // Explicit user retry, never an automatic relaunch.
                        break;
                    case 1001:
                        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_ListenEvent"));
                        break;
                    case 1002:
                        NSWorkspace.SharedWorkspace.OpenUrl(new NSUrl("x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility"));
                        break;
                    default:
                        return false;
                }
            }
        }
    }
}
