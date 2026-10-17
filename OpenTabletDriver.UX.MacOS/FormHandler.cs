using System;
using Eto.Forms;
using MonoMac.AppKit;
using MonoMac.ObjCRuntime;

namespace OpenTabletDriver.UX.MacOS;

internal class FormHandler : Eto.Mac.Forms.FormHandler
{
    protected override void Initialize()
    {
        base.Initialize();
        Widget.WindowStateChanged += UpdateActivationPolicy;
        Widget.LostFocus += UpdateActivationPolicy;
        Widget.GotFocus += UpdateActivationPolicy;
    }

    public override void Show()
    {
        Activate();
        base.Show();
    }

    // Form.Show() only calls the handler's Show() the first time the form is shown.
    // Any later call (e.g. the tray icon's "Show OpenTabletDriver" menu item) just sets
    // Visible = true, so the app has to be activated from here to be brought to the front.
    public override bool Visible
    {
        get => base.Visible;
        set
        {
            if (value)
                Activate();

            base.Visible = value;
        }
    }

    private static void Activate()
    {
        if (Environment.OSVersion.Version < new Version(10, 14))
            NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
        else
            NSApplication.SharedApplication.PerformSelector(new Selector("activate"), NSApplication.SharedApplication, 0);
    }

    private static void UpdateActivationPolicy(object sender, EventArgs e)
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Accessory;
    }
}
