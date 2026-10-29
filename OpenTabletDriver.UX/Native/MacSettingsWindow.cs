using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Eto.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Interop;
using OpenTabletDriver.Desktop.Interop.Display;
using OpenTabletDriver.Native.OSX.ApplicationServices;
using OpenTabletDriver.Native.OSX.IOkit;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.UX.Tools;

namespace OpenTabletDriver.UX.Native;

/// <summary>Hosts SwiftUI in this GUI process. C ABI strings are copied before callbacks return.</summary>
public static class MacSettingsWindow
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void CommandCallback(IntPtr json);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ShowFunction([MarshalAs(UnmanagedType.LPUTF8Str)] string json, CommandCallback callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ResultFunction([MarshalAs(UnmanagedType.LPUTF8Str)] string json);
    private static readonly CommandCallback Callback = Receive;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AttachFunction(CommandCallback callback);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SaveFunction();
    private static AttachFunction? attach;
    private static SaveFunction? save;
    private static SaveFunction? quit;
    private static ShowFunction? show;
    private static ResultFunction? result;
    private static MainForm? owner;
    private static bool busy;
    private static bool triedLoading;
    private static readonly JsonSerializerSettings JsonSettings = new()
    { ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys=false } } };

    public static bool Available
    {
        get
        {
            if (!OperatingSystem.IsMacOS()) return false;
            if (!triedLoading)
            {
                triedLoading = true;
                var path=Path.Combine(AppContext.BaseDirectory,"libBambooSettings.dylib");
                if (File.Exists(path))
                    try
                    {
                        var library=NativeLibrary.Load(path); // Retained for the GUI lifetime, including Swift window state.
                        show=Marshal.GetDelegateForFunctionPointer<ShowFunction>(NativeLibrary.GetExport(library,"BambooSettingsShow"));
                        result=Marshal.GetDelegateForFunctionPointer<ResultFunction>(NativeLibrary.GetExport(library,"BambooSettingsResult"));
                        attach=Marshal.GetDelegateForFunctionPointer<AttachFunction>(NativeLibrary.GetExport(library,"BambooSettingsAttach"));
                        save=Marshal.GetDelegateForFunctionPointer<SaveFunction>(NativeLibrary.GetExport(library,"BambooSettingsSave"));
                        quit=Marshal.GetDelegateForFunctionPointer<SaveFunction>(NativeLibrary.GetExport(library,"BambooSettingsQuit"));
                    }
                    catch (Exception ex) { Log.Exception(ex); }
            }
            return show!=null && result!=null;
        }
    }

    public static void Attach(MainForm window) { owner=window; attach?.Invoke(Callback); }
    public static bool TrySave() => Available && save?.Invoke()==1;

    public static bool Show(MainForm window)
    {
        if (!Available || !window.NativeSettingsReady) return false;
        owner=window;
        try { return show!(JsonConvert.SerializeObject(Capture(),JsonSettings),Callback)==1; }
        catch(Exception ex) { Log.Exception(ex); return false; }
    }

    private static NativeDisplay[] ReadDisplays()
    {
        if (DesktopInterop.VirtualScreen is MacOSDisplay mac) mac.Refresh();
        return DesktopInterop.VirtualScreen?.Displays.Where(d=>d is not IVirtualScreen)
            .Select(d=>new NativeDisplay(d.Identifier,d.Name ?? "Display",d.Width,d.Height,d.Position.X,d.Position.Y)).ToArray() ?? [];
    }

    private static BambooSettingsSnapshot Capture()
    {
        var profile=App.Current.Settings.Profiles.FirstOrDefault(p=>p.Tablet=="Wacom CTH-661");
        var displays=ReadDisplays();
        var snapshot=profile==null ? new BambooSettingsSnapshot() : BambooSettingsSnapshot.FromProfile(profile,displays);
        snapshot.SettingsToken=App.Current.SettingsToken;
        snapshot.Version=App.Version; snapshot.Language=Language.Selected;
        snapshot.Connected=owner?.BambooConnected==true && App.Driver.IsConnected;
        snapshot.Displays=displays;
        snapshot.InputGranted=IOKit.IOHIDCheckAccess(IOHIDRequestType.kIOHIDRequestTypeListenEvent)==IOHIDAccessType.kIOHIDAccessTypeGranted;
        snapshot.AccessibilityGranted=ApplicationServices.AXIsProcessTrusted();
        return snapshot;
    }

    public static void RequestQuit()
    {
        if (Available && quit?.Invoke()==1) return;
        Application.Instance.Quit();
    }

    private static void Receive(IntPtr json)
    {
        try
        {
            var copy=Marshal.PtrToStringUTF8(json) ?? throw new ArgumentException("Empty native command.");
            Application.Instance.AsyncInvoke(async void ()=>await Handle(copy));
        }
        catch(Exception ex) { Log.Exception(ex); }
    }

    private static async Task Handle(string json)
    {
        var command=JObject.Parse(json);
        if ((string?)command["action"]=="open")
        {
            if (owner?.NativeSettingsReady==true) Show(owner);
            else if(owner!=null) owner.OpenSettingsWhenReady=true;
            return;
        }
        if ((string?)command["action"]=="status")
        {
            if (!busy) result?.Invoke(JsonConvert.SerializeObject(new { status=new {
                connected=owner?.BambooConnected==true && App.Driver.IsConnected,
                inputGranted=IOKit.IOHIDCheckAccess(IOHIDRequestType.kIOHIDRequestTypeListenEvent)==IOHIDAccessType.kIOHIDAccessTypeGranted,
                accessibilityGranted=ApplicationServices.AXIsProcessTrusted()
            } },JsonSettings));
            return;
        }
        if (busy) { Reply(false,"An operation is already in progress."); return; }
        busy=true;
        try
        {
            switch((string?)command["action"])
            {
                case "save":
                {
                    if (!App.Driver.IsConnected) throw new InvalidOperationException(Language.T("The driver is disconnected. Reconnect and try again."));
                    var draft=command["settings"]?.ToObject<BambooSettingsSnapshot>(JsonSerializer.Create(JsonSettings)) ?? throw new ArgumentException("Missing settings.");
                    await SettingsOperations.RunAsync(async () =>
                    {
                        var tablet=(await App.Driver.Instance!.GetTablets().WaitAsync(TimeSpan.FromSeconds(5))).FirstOrDefault(t=>t.Properties.Name==draft.Profile)
                            ?? throw new InvalidOperationException(Language.T("Connect the tablet before saving settings."));
                        var live=App.Current.Settings.Profiles.Single(p=>p.Tablet==draft.Profile);
                        if (draft.ProfileToken!=BambooSettingsSnapshot.TokenFor(live) || draft.SettingsToken!=App.Current.SettingsToken)
                            throw new InvalidOperationException(Language.T("Settings changed in another window. Refresh before saving."));
                        var candidate=SettingsState.Copy(App.Current.Settings);
                        draft.Displays=ReadDisplays();
                        draft.ApplyTo(candidate.Profiles.Single(p=>p.Tablet==draft.Profile),tablet.Properties.Specifications.Digitizer);
                        owner!.Enabled=false;
                        try
                        {
                            // The daemon owns application, file commit, rollback and revision checking.
                            // Never abandon this RPC on a timeout while it might still commit.
                            App.Current.SetSettingsState(await App.Driver.Instance.SaveSettings(candidate,draft.SettingsToken));
                        }
                        catch
                        {
                            try { App.Current.SetSettingsState(await App.Driver.Instance.GetSettingsState().WaitAsync(TimeSpan.FromSeconds(3))); }
                            catch(Exception syncError) { Log.Exception(syncError); }
                            throw;
                        }
                        finally { owner.Enabled=true; }
                        Reply(true,null,Capture(),saved:true);
                    });
                    break;
                }
                case "language":
                    Language.SaveSelection((string?)command["language"] ?? "");
                    result?.Invoke(JsonConvert.SerializeObject(new { success=true,language=Language.Selected },JsonSettings));
                    break;
                case "quit": Application.Instance.Quit(); break;
                case "refresh": Reply(true,null,Capture()); break;
                case "advanced": owner!.ShowAdvancedSettings(); Reply(true); break;
                case "test": App.Current.InputTestWindow.Show(); Reply(true); break;
                case "logs": DesktopInterop.OpenFolder(OpenTabletDriver.Desktop.Diagnostics.StartupLog.DirectoryPath); Reply(true); break;
                case "diagnostics":
                {
                    var path=(string?)command["path"] ?? throw new ArgumentException("Missing export path.");
                    await File.WriteAllTextAsync(path,await SupportDiagnostics.CaptureAsync());
                    Reply(true); break;
                }
                default: throw new ArgumentException("Unknown native settings command.");
            }
        }
        catch(Exception ex) { Log.Exception(ex); Reply(false,ex.Message); }
        finally { busy=false; }
    }

    private static void Reply(bool success,string? error=null,BambooSettingsSnapshot? settings=null,bool saved=false) =>
        result?.Invoke(JsonConvert.SerializeObject(new { success,error,settings,saved },JsonSettings));
}
