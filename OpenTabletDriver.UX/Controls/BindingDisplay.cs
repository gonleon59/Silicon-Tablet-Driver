using System;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Windows.Bindings;

namespace OpenTabletDriver.UX.Controls
{
    public class BindingDisplay : Panel
    {
        public BindingDisplay()
        {
            this.Content = new StackLayout
            {
                Spacing = 5,
                MinimumSize = new Size(300, 0),
                Orientation = Orientation.Horizontal,
                Items =
                {
                    new StackLayoutItem
                    {
                        Expand = true,
                        Control = mainButton = new Button()
                    },
                    new StackLayoutItem
                    {
                        Control = advancedButton = new Button
                        {
                            Text = "...",
                            Width = 25
                        }
                    }
                }
            };

            mainButton.TextBinding.Bind(this.StoreBinding.Convert<string?>(DescribeBinding));

            mainButton.Click += async (sender, e) =>
            {
                var dialog = new BindingEditorDialog(Store);
                this.Store = await dialog.ShowModalAsync(this);
            };

            advancedButton.Click += async (sender, e) =>
            {
                var dialog = new AdvancedBindingEditorDialog(Store);
                var result = await dialog.ShowModalAsync(this);
                if (result.AdvancedBindingEditorDialogAction != AdvancedBindingEditorDialogAction.Close)
                    this.Store = result.PluginSettingStore;
            };
        }

        private Button mainButton, advancedButton;

        private static string? DescribeBinding(PluginSettingStore? binding)
        {
            if (binding?.Path == "OpenTabletDriver.Desktop.Binding.MultiKeyBinding")
            {
                var keys = binding.Settings.FirstOrDefault(s => s.Property == "Keys")?.Value?.ToString();
                var action = keys switch
                {
                    "Application+LeftBracket" => "Back",
                    "Application+RightBracket" => "Forward",
                    "Control+Up" => "Mission Control (all windows)",
                    "F11" => "Show desktop",
                    _ => null
                };
                if (action != null) return $"{Language.T(action)} ({keys})";
            }
            return binding?.GetElidedHumanReadableString(100);
        }

        public event EventHandler<EventArgs>? StoreChanged;

        private PluginSettingStore? store;
        public PluginSettingStore? Store
        {
            set
            {
                this.store = value;
                StoreChanged?.Invoke(this, new EventArgs());
            }
            get => this.store;
        }

        public BindableBinding<BindingDisplay, PluginSettingStore?> StoreBinding
        {
            get
            {
                return new BindableBinding<BindingDisplay, PluginSettingStore?>(
                    this,
                    c => c.Store,
                    (c, v) => c.Store = v,
                    (c, h) => c.StoreChanged += h,
                    (c, h) => c.StoreChanged -= h
                );
            }
        }
    }
}
