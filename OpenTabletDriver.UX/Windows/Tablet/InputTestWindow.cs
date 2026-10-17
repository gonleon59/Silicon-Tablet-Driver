using System;
using System.Linq;
using Eto.Drawing;
using Eto.Forms;
using Newtonsoft.Json.Linq;
using OpenTabletDriver.Desktop.RPC;

namespace OpenTabletDriver.UX.Windows.Tablet
{
    public sealed class InputTestWindow : DesktopForm
    {
        private readonly ProgressBar pressureBar = new() { MinValue = 0, MaxValue = 1023 };
        private readonly Label pressure = new(), maximum = new(), tool = new(), penButtons = new(), padButtons = new(), fingers = new();
        private int maxPressure;
        private bool closed;
        public InputTestWindow()
        {
            Title = Language.T("Pen and touch test");
            ClientSize = new Size(640, 300);
            Padding = 18;
            pressure.Text = Language.T("Waiting for tablet data");
            var reset = new Button { Text = Language.T("Reset maximum") };
            reset.Click += (_, _) => { maxPressure = 0; maximum.Text = Language.T("Maximum observed") + ": 0 / 1023"; };
            Content = new StackLayout
            {
                Spacing = 12, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Items = {
                    new Label { Text = Language.T("Keep the pen away while testing fingers. Press the pen gently, then harder, to test pressure."), Wrap = WrapMode.Word },
                    pressure, pressureBar, maximum, tool, penButtons, padButtons, fingers, reset
                }
            };
            App.Driver.DeviceReport += Receive;
            Shown += async (_, _) => { if (App.Driver.IsConnected) await App.Driver.Instance.SetTabletDebug(true); };
            Closed += async (_, _) =>
            {
                closed = true;
                App.Driver.DeviceReport -= Receive;
                if (App.Driver.IsConnected) await App.Driver.Instance.SetTabletDebug(false);
            };
        }
        private void Receive(object? sender, DebugReportData report)
        {
            if (closed || report.Tablet.Properties.Name != "Wacom CTH-661") return;
            if (report.Data["Pressure"] is { } p)
            {
                int value = Math.Clamp(p.Value<int>(), 0, 1023);
                pressureBar.Value = value;
                maxPressure = Math.Max(maxPressure, value);
                pressure.Text = $"{Language.T("Pressure")}: {value} / 1023 ({value * 100.0 / 1023:F0}%)";
                maximum.Text = $"{Language.T("Maximum observed")}: {maxPressure} / 1023";
            }
            if (report.Data["Eraser"] is { } eraser) tool.Text = eraser.Value<bool>() ? Language.T("Eraser") : Language.T("Pen");
            if (report.Data["PenButtons"] is JArray pen) penButtons.Text = Language.T("Pen buttons") + ": " + FormatButtons(pen);
            if (report.Data["AuxButtons"] is JArray pad) padButtons.Text = Language.T("Tablet buttons") + ": " + FormatButtons(pad);
            if (report.Data["Touches"] is JArray touches) fingers.Text = Language.T("Fingers") + ": " + touches.Count(t => t.Type != JTokenType.Null);
        }
        private static string FormatButtons(JArray buttons) => string.Join("   ", buttons.Select((b, i) => $"{i + 1}: {(b.Value<bool>() ? "●" : "○")}"));
    }
}
