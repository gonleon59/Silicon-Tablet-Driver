using System.Collections.Generic;
using Eto.Forms;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.UX.Controls.Generic;

namespace OpenTabletDriver.UX.Controls.Bindings
{
    public sealed class PenBindingEditor : BindingEditor
    {
        public PenBindingEditor()
        {
            this.Content = new Scrollable
            {
                Border = BorderType.None,
                Content = new StackLayout
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Items =
                    {
                        new TableLayout
                        {
                            Rows =
                            {
                                new TableRow
                                {
                                    Cells =
                                    {
                                        new Group
                                        {
                                            Text = Language.T("Tip Settings"),
                                            Content = new StackLayout
                                            {
                                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                                                Spacing = 5,
                                                Items =
                                                {
                                                    new Group
                                                    {
                                                        Text = Language.T("Tip Binding"),
                                                        Orientation = Orientation.Horizontal,
                                                        ExpandContent = false,
                                                        Content = tipButton = new BindingDisplay()
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = Language.T("Tip Threshold"),
                                                        ToolTip = Language.T("The minimum threshold in order for the assigned binding to activate."),
                                                        Orientation = Orientation.Horizontal,
                                                        Content = tipThreshold = new FloatSlider(),
                                                        Unit = "%"
                                                    }
                                                }
                                            }
                                        },
                                        new Group
                                        {
                                            Text = Language.T("Eraser Settings"),
                                            Content = new StackLayout
                                            {
                                                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                                                Spacing = 5,
                                                Items =
                                                {
                                                    new Group
                                                    {
                                                        Text = Language.T("Eraser Binding"),
                                                        ExpandContent = false,
                                                        Orientation = Orientation.Horizontal,
                                                        Content = eraserButton = new BindingDisplay()
                                                    },
                                                    new UnitGroup
                                                    {
                                                        Text = Language.T("Eraser Threshold"),
                                                        ToolTip = Language.T("The minimum threshold in order for the assigned binding to activate."),
                                                        Orientation = Orientation.Horizontal,
                                                        Content = eraserThreshold = new FloatSlider(),
                                                        Unit = "%"
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        },
                        new Group
                        {
                            Text = Language.T("Pen Buttons"),
                            Content = penButtons = new BindingDisplayList
                            {
                                Prefix = Language.T("Pen Binding")
                            }
                        },
                        new Group {
                            Text = Language.T("Miscellaneous"),
                            Content = new StackLayout {
                                Orientation = Orientation.Horizontal,
                                Items = {
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = Language.T("Disable pressure if it is available"),
                                        Content = disablePressure = new CheckBox {
                                            Text = Language.T("Disable Pressure"),
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = Language.T("Disable tilt if it is available"),
                                        Content = disableTilt = new CheckBox {
                                            Text = Language.T("Disable Tilt"),
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = Language.T("Disable rotation if it is available"),
                                        Content = disableRotation = new CheckBox {
                                            Text = Language.T("Disable Rotation"),
                                        }
                                    },
                                    new Group {
                                        Orientation = Orientation.Horizontal,
                                        ToolTip = Language.T("Pen Bindings require pressure to activate"),
                                        Content = enableDragBindings = new CheckBox {
                                            Text = Language.T("Drag Bindings"),
                                        }
                                    },
                                }
                            }
                        }
                    }
                }
            };

            tipButton.StoreBinding.Bind(SettingsBinding.Child(c => c.TipButton));
            eraserButton.StoreBinding.Bind(SettingsBinding.Child(c => c.EraserButton));
            tipThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.TipActivationThreshold));
            eraserThreshold.ValueBinding.Bind(SettingsBinding.Child(c => c.EraserActivationThreshold));
            penButtons.ItemSourceBinding.Bind(SettingsBinding.Child(c => (IList<PluginSettingStore>)c.PenButtons)!);
            disablePressure.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisablePressure));
            disableTilt.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisableTilt));
            disableRotation.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.DisableRotation));
            enableDragBindings.CheckedBinding.Cast<bool>().Bind(SettingsBinding.Child(c => c.EnableDragBindings));
        }

        private BindingDisplay tipButton, eraserButton;
        private FloatSlider tipThreshold, eraserThreshold;
        private CheckBox disablePressure, disableTilt, disableRotation, enableDragBindings;
        private BindingDisplayList penButtons;
    }
}
