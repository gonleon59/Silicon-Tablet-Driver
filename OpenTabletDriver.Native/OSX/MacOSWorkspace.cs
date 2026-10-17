using System;
using System.Runtime.InteropServices;

namespace OpenTabletDriver.Native.OSX;

/// <summary>Public AppKit metadata; no event injection or private selectors.</summary>
public static class MacOSWorkspace
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string ColorSync = "/System/Library/Frameworks/ColorSync.framework/ColorSync";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    static MacOSWorkspace() => LibSystem.dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", 0);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendIndex(IntPtr receiver, IntPtr selector, nuint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nuint SendCount(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern uint SendUInt(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr SendString(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
    [DllImport(ColorSync)] private static extern IntPtr CGDisplayCreateUUIDFromDisplayID(uint display);
    [DllImport(CoreFoundation)] private static extern IntPtr CFUUIDCreateString(IntPtr allocator, IntPtr uuid);
    private static IntPtr Selector(string name) => ObjectiveCRuntime.sel_registerName(name);
    private static string? StringValue(IntPtr value) => value == IntPtr.Zero ? null
        : Marshal.PtrToStringUTF8(Send(value, Selector("UTF8String")));

    public static string? FrontmostBundleIdentifier()
    {
        var pool = ObjectiveCRuntime.objc_autoreleasePoolPush();
        try
        {
            var workspace = Send(ObjectiveCRuntime.objc_getClass("NSWorkspace"), Selector("sharedWorkspace"));
            var application = Send(workspace, Selector("frontmostApplication"));
            return StringValue(Send(application, Selector("bundleIdentifier")));
        }
        finally { ObjectiveCRuntime.objc_autoreleasePoolPop(pool); }
    }

    public static string DisplayIdentifier(uint display)
    {
        var uuid = CGDisplayCreateUUIDFromDisplayID(display);
        if (uuid == IntPtr.Zero) return $"display-{display}";
        IntPtr value = IntPtr.Zero;
        var pool = ObjectiveCRuntime.objc_autoreleasePoolPush();
        try
        {
            value = CFUUIDCreateString(IntPtr.Zero, uuid);
            return StringValue(value) ?? $"display-{display}";
        }
        finally
        {
            if (value != IntPtr.Zero) OSX.CFRelease(value);
            OSX.CFRelease(uuid);
            ObjectiveCRuntime.objc_autoreleasePoolPop(pool);
        }
    }

    public static string? DisplayName(uint display)
    {
        var pool = ObjectiveCRuntime.objc_autoreleasePoolPush();
        try
        {
            var screens = Send(ObjectiveCRuntime.objc_getClass("NSScreen"), Selector("screens"));
            var key = SendString(ObjectiveCRuntime.objc_getClass("NSString"), Selector("stringWithUTF8String:"), "NSScreenNumber");
            nuint count = SendCount(screens, Selector("count"));
            if (count > 128) count = 128;
            for (nuint index = 0; index < count; index++)
            {
                var screen = SendIndex(screens, Selector("objectAtIndex:"), index);
                var properties = Send(screen, Selector("deviceDescription"));
                var number = SendPointer(properties, Selector("objectForKey:"), key);
                if (SendUInt(number, Selector("unsignedIntValue")) == display)
                    return StringValue(Send(screen, Selector("localizedName")));
            }
            return null;
        }
        finally { ObjectiveCRuntime.objc_autoreleasePoolPop(pool); }
    }
}
