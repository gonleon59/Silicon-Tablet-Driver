using System.Runtime.InteropServices;

namespace OpenTabletDriver.Native.OSX.IOkit
{
    public static class IOKit
    {
        private const string libiokiot = "/System/Library/Frameworks/IOKit.framework/IOKit";

        [DllImport(libiokiot)]
        public static extern IOHIDAccessType IOHIDCheckAccess(IOHIDRequestType requestType);

        [DllImport(libiokiot)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IOHIDRequestAccess(IOHIDRequestType requestType);
    }
}
