using System;
using System.Collections.Generic;
using Eto.Forms;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Plugin;

namespace OpenTabletDriver.UX
{
    public sealed class TrayIcon : IDisposable
    {
        public TrayIcon(MainForm window)
        {
            this.window = window;

            Indicator = new TrayIndicator
            {
                Title = "OpenTabletDriver",
                Image = DesktopInterop.CurrentPlatform == PluginPlatform.MacOS
                    ? Eto.Drawing.Bitmap.FromResource("OpenTabletDriver.UX.Assets.TabletMenu.png") : App.Logo
            };

            RefreshMenuItems();

            Indicator.Activated += (_, _) =>
            {
                window.ShowSettingsWindow();
            };
        }

        public TrayIndicator Indicator { get; }
        private MainForm window;

        public void Dispose()
        {
            Indicator.Hide();
            Indicator.Dispose();
        }

        // macOS doesn't render a menu bar for windows of background apps
        // This is used to clone the items of the menu bar to the context menu
        private static MenuItem CloneMenuItem(MenuItem original)
        {
            if (original is SeparatorMenuItem)
            {
                return new SeparatorMenuItem();
            }

            if (original is ButtonMenuItem buttonItem)
            {
                var cloned = new ButtonMenuItem
                {
                    Text = buttonItem.Text,
                    Enabled = buttonItem.Enabled,
                    Shortcut = buttonItem.Shortcut
                };

                // Clone sub-items recursively
                foreach (var subItem in buttonItem.Items)
                {
                    cloned.Items.Add(CloneMenuItem(subItem));
                }

                // Re-create the click handler by triggering the original item's click
                cloned.Click += (sender, e) => buttonItem.PerformClick();

                return cloned;
            }

            return original;
        }

        public void RefreshMenuItems()
        {
            var showWindow = new ButtonMenuItem
            {
                Text = Language.T("Show Window")
            };
            showWindow.Click += (sender, e) =>
            {
                window.ShowSettingsWindow();
            };

            var close = new ButtonMenuItem
            {
                Text = Language.T("Close")
            };

            if (DesktopInterop.CurrentPlatform == PluginPlatform.MacOS)
            {
                // It's more idiomatic for macOS to include the name here
                showWindow.Text = Language.T("Show OpenTabletDriver");

                // Applications on macOS will keep running even after closing all their windows
                // Offering a way to quit the app here is more idiomatic
                close.Text = Language.T("Quit");
                close.Click += (sender, e) => Native.MacSettingsWindow.RequestQuit();
            }

            else
            {
                close.Click += (sender, e) => window.Close();
            }

            var items = new List<MenuItem>();
            var presets = AppInfo.PresetManager.GetPresets();

            if (presets.Count != 0)
            {
                foreach (var preset in presets)
                {
                    var presetItem = new ButtonMenuItem
                    {
                        Text = preset.Name
                    };
                    presetItem.Click += MainForm.PresetButtonHandler;

                    items.Add(presetItem);
                }

                items.Add(new SeparatorMenuItem());
            }

            items.Add(showWindow);

            // macOS doesn't present a menu bar for agent apps
            if (DesktopInterop.CurrentPlatform == PluginPlatform.MacOS && window.Menu != null)
            {
                items.Add(new SeparatorMenuItem());

                var fileMenu = window.Menu.Items.GetSubmenu(Language.T("&File")) as ButtonMenuItem;
                if (fileMenu != null)
                {
                    foreach (var item in fileMenu.Items)
                    {
                        if (item.Text == Language.T("Close") || item.Text == Language.T("Presets"))
                            continue;

                        items.Add(CloneMenuItem(item));
                    }
                    items.Add(new SeparatorMenuItem());
                }

                var tabletsMenu = window.Menu.Items.GetSubmenu(Language.T("Tablets"));
                if (tabletsMenu != null)
                    items.Add(CloneMenuItem(tabletsMenu));

                // Keep language selection available when macOS hides the window menu bar.
                items.Add(Language.CreateMenu());

                var pluginsMenu = window.Menu.Items.GetSubmenu(Language.T("Plugins"));
                if (pluginsMenu != null)
                    items.Add(CloneMenuItem(pluginsMenu));

                var helpMenu = window.Menu.Items.GetSubmenu(Language.T("&Help"));
                if (helpMenu != null)
                    items.Add(CloneMenuItem(helpMenu));

                items.Add(new SeparatorMenuItem());
            }

            items.Add(close);

            Indicator.Menu = new ContextMenu(items);
        }
    }
}
