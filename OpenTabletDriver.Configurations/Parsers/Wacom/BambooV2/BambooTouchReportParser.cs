using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.Plugin.Tablet.Touch;

namespace OpenTabletDriver.Configurations.Parsers.Wacom.BambooV2
{
    // CTH-661's 20-byte auxiliary packet contains two finger slots and four buttons.
    // Protocol reference: Linux drivers/hid/wacom_wac.c, wacom_bpt_touch.
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public class BambooTouchReportParser : IReportParser<IDeviceReport>
    {
        public IDeviceReport Parse(byte[] data) => data.Length == 20 && data[0] == 2
            ? new BambooTouchReport(data) : new DeviceReport(data);
    }

    public struct BambooTouchReport : ITouchReport, IAuxReport
    {
        public BambooTouchReport(byte[] data)
        {
            Raw = data;
            AuxButtons = [ (data[1] & 1) != 0, (data[1] & 2) != 0, (data[1] & 4) != 0, (data[1] & 8) != 0 ];
            Touches = new TouchPoint?[2];
            int stride = (data[1] & 0x80) != 0 ? 8 : 9;
            for (byte slot = 0; slot < 2; slot++)
            {
                int offset = 3 + slot * stride;
                if ((data[offset] & 0x80) == 0) continue;
                Touches[slot] = new TouchPoint
                {
                    TouchID = slot,
                    Position = new Vector2(((data[offset] << 8) | data[offset + 1]) & 0x7ff,
                                           ((data[offset + 2] << 8) | data[offset + 3]) & 0x7ff)
                };
            }
        }
        public byte[] Raw { get; set; }
        public bool[] AuxButtons { get; set; }
        public TouchPoint?[] Touches { get; set; }
    }
}
