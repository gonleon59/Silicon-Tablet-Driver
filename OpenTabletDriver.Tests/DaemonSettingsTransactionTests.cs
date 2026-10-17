using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletDriver.Daemon;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.Desktop.Reflection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.UX.Native;
using Xunit;

namespace OpenTabletDriver.Tests;

[CollectionDefinition("Daemon settings", DisableParallelization=true)]
public sealed class DaemonSettingsCollection { }

[Collection("Daemon settings")]
public sealed class DaemonSettingsTransactionTests
{
    // Construct no platform services, listeners, device discovery, logs or user configuration.
    private sealed class Fixture : IDisposable
    {
        private readonly DesktopPluginManager previousManager=AppInfo.PluginManager;
        private readonly AppInfo previousInfo=AppInfo.Current;
        public string DirectoryPath { get; }=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        public string SettingsPath=>Path.Combine(DirectoryPath,"settings.json");
        public DriverDaemon Daemon { get; }
        public TestPluginManager Plugins { get; }=new();
        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath);
            AppInfo.Current=new AppInfo { AppDataDirectory=DirectoryPath,SettingsFile=SettingsPath };
            AppInfo.PluginManager=Plugins;
            Daemon=(DriverDaemon)RuntimeHelpers.GetUninitializedObject(typeof(DriverDaemon));
            void Field(string name,object value)=>typeof(DriverDaemon).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Daemon,value);
            Field("<Driver>k__BackingField",new Driver(null!,null!,null!));
            Field("<Tools>k__BackingField",new Collection<ITool>());
            Field("settingsGate",new object());
            Field("settingsToken",Guid.NewGuid().ToString("N"));
            Daemon.SetSettings(new Settings { Revision="old" }).GetAwaiter().GetResult();
            File.WriteAllText(SettingsPath,JsonConvert.SerializeObject(new Settings { Revision="old" }));
        }
        public void Dispose()
        {
            AppInfo.PluginManager=previousManager;AppInfo.Current=previousInfo;
            Directory.Delete(DirectoryPath,true);
        }
    }

    private sealed class TestPluginManager : DesktopPluginManager
    {
        public Func<ITool> Tool { get; set; }=()=>new CallbackTool(()=>throw new InvalidOperationException("rejected"));
        public override T? ConstructObject<T>(string name,object[]? args=null) where T:class =>
            name=="review.tool" ? Tool() as T : base.ConstructObject<T>(name,args);
    }
    private sealed class CallbackTool(Func<bool> initialize,Action? cleanup=null) : ITool
    {
        public bool Initialize()=>initialize();
        public void Dispose() { cleanup?.Invoke(); }
    }
    private static Settings Candidate(string revision="candidate")
    {
        var candidate=new Settings { Revision=revision };
        candidate.Tools.Add(new PluginSettingStore("review.tool",[]) { Enable=true });
        return candidate;
    }

    [Fact] public async Task RealDaemonRejectionDoesNotCommitCandidate()
    {
        using var f=new Fixture();var old=await f.Daemon.GetSettingsState();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Daemon.SaveSettings(Candidate(),old.Token));
        Assert.Equal("old",(await f.Daemon.GetSettings()).Revision);
        Assert.Equal("old",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
        Assert.Single(Directory.GetFiles(f.DirectoryPath));
        Assert.Equal(old.Token,(await f.Daemon.GetSettingsState()).Token);
    }

    [Fact] public async Task LegacySetSettingsReportsRejectionAfterRollback()
    {
        using var f=new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Daemon.SetSettings(Candidate()));
        Assert.Equal("old",(await f.Daemon.GetSettings()).Revision);
    }

    [Fact] public async Task StaleRevisionCannotOverwriteAnotherClient()
    {
        using var f=new Fixture();var old=await f.Daemon.GetSettingsState();
        var newer=await f.Daemon.ApplySettings(new Settings { Revision="other client" },old.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Daemon.SaveSettings(new Settings { Revision="stale" },old.Token));
        Assert.Equal("other client",(await f.Daemon.GetSettings()).Revision);
        Assert.NotEqual(old.Token,newer.Token);
        Assert.Equal("old",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
    }

    [Fact] public async Task ApplyAndGetReturnDetachedSnapshots()
    {
        using var f=new Fixture();var old=await f.Daemon.GetSettingsState();
        var candidate=new Settings { Revision="applied" };
        var applied=await f.Daemon.ApplySettings(candidate,old.Token);
        candidate.Revision="caller mutation";applied.Settings.Revision="response mutation";
        Assert.Equal("applied",(await f.Daemon.GetSettings()).Revision);
    }

    [Fact] public async Task RollbackFailureIsBoundedAndLeavesPersistentFileIntact()
    {
        using var f=new Fixture();int initialized=0;
        f.Plugins.Tool=()=>new CallbackTool(()=>++initialized==1 ? true : throw new InvalidOperationException("tool unavailable"));
        await f.Daemon.SetSettings(Candidate("previous"));
        var state=await f.Daemon.GetSettingsState();
        var error=await Assert.ThrowsAnyAsync<AggregateException>(()=>f.Daemon.SaveSettings(Candidate(),state.Token));
        Assert.Equal(3,initialized); // initial success, candidate failure, one rollback failure
        Assert.Equal(2,error.InnerExceptions.Count);
        Assert.Equal("old",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
        Assert.Single(Directory.GetFiles(f.DirectoryPath));
    }

    [Fact] public async Task ConcurrentRpcWriterWaitsAndRejectsStaleRevisionAfterCommit()
    {
        using var f=new Fixture();
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        using var secondStarted=new ManualResetEventSlim();
        f.Plugins.Tool=()=>new CallbackTool(()=>{entered.Set();if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException();return true;});
        var state=await f.Daemon.GetSettingsState();
        var save=Task.Run(()=>f.Daemon.SaveSettings(Candidate("saved"),state.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var other=Task.Run(()=>{secondStarted.Set();return f.Daemon.ApplySettings(new Settings { Revision="stale" },state.Token);});
            Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.NotSame(other,await Task.WhenAny(other,Task.Delay(50)));
            release.Set();
            var saved=await save;
            await Assert.ThrowsAsync<InvalidOperationException>(()=>other);
            Assert.Equal(saved.Token,(await f.Daemon.GetSettingsState()).Token);
            Assert.Equal("saved",(await f.Daemon.GetSettings()).Revision);
            Assert.Equal("saved",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
        }
        finally { release.Set();await save; }
    }

    [Fact] public async Task NativeAndPresetOperationsCannotOverlap()
    {
        var order=new List<string>();var entered=new TaskCompletionSource();var release=new TaskCompletionSource();
        var native=SettingsOperations.RunAsync(async ()=>{order.Add("native start");entered.SetResult();await release.Task;order.Add("native end");});
        await entered.Task;
        var preset=SettingsOperations.RunAsync(()=>{order.Add("preset");return Task.CompletedTask;});
        Assert.False(preset.IsCompleted);release.SetResult();
        await Task.WhenAll(native,preset);
        Assert.Equal(new[]{"native start","native end","preset"},order);
    }
    [Fact] public async Task ToolReturningFalseIsARejectionNotASuccessfulSave()
    {
        using var f=new Fixture();f.Plugins.Tool=()=>new CallbackTool(()=>false);
        var old=await f.Daemon.GetSettingsState();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Daemon.SaveSettings(Candidate(),old.Token));
        Assert.Equal("old",(await f.Daemon.GetSettings()).Revision);
        Assert.Equal("old",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
    }

    [Fact] public async Task RevisionsDoNotRepeatBetweenDaemonSessions()
    {
        using var first=new Fixture();var token=(await first.Daemon.GetSettingsState()).Token;
        using var second=new Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>second.Daemon.ApplySettings(new Settings(),token));
    }

    [Fact] public async Task RpcRoundTripPreservesStateAndPropagatesRejection()
    {
        using var f=new Fixture();
        // An isolated pair of pipes: never connect to the installed driver's pipe name.
        string name="otdr"+Guid.NewGuid().ToString("N")[..12];
        using var server=new System.IO.Pipes.NamedPipeServerStream(name,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous);
        using var client=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous);
        await Task.WhenAll(server.WaitForConnectionAsync(),client.ConnectAsync()).WaitAsync(TimeSpan.FromSeconds(5));
        using var host=new StreamJsonRpc.JsonRpc(server,server,f.Daemon);
        using var rpc=new StreamJsonRpc.JsonRpc(client);
        host.ExceptionStrategy=rpc.ExceptionStrategy=StreamJsonRpc.ExceptionProcessing.ISerializable;
        var proxy=rpc.Attach<OpenTabletDriver.Desktop.Contracts.IDriverDaemon>();
        host.StartListening();rpc.StartListening();
        var state=await proxy.GetSettingsState().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("old",state.Settings.Revision);Assert.False(string.IsNullOrEmpty(state.Token));
        await Assert.ThrowsAnyAsync<StreamJsonRpc.RemoteInvocationException>(async ()=>await proxy.SaveSettings(Candidate(),state.Token).WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("old",(await proxy.GetSettings()).Revision);
        Assert.Equal("old",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
        state=await proxy.GetSettingsState();
        var saved=await proxy.SaveSettings(new Settings { Revision="rpc saved" },state.Token);
        Assert.Equal("rpc saved",saved.Settings.Revision);
        Assert.Equal(saved.Token,(await proxy.GetSettingsState()).Token);
        await Assert.ThrowsAnyAsync<StreamJsonRpc.RemoteInvocationException>(()=>proxy.ApplySettings(new Settings(),state.Token));
        int initialized=0;
        f.Plugins.Tool=()=>new CallbackTool(()=>++initialized==1 ? true : throw new InvalidOperationException("tool unavailable"));
        var previous=await proxy.ApplySettings(Candidate("previous"),saved.Token);
        var failure=await Assert.ThrowsAnyAsync<StreamJsonRpc.RemoteInvocationException>(
            ()=>proxy.SaveSettings(Candidate(),previous.Token));
        Assert.Contains("could not be restored",failure.Message);
        Assert.Equal(3,initialized);
        Assert.Equal("rpc saved",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(f.SettingsPath))!.Revision);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task RejectedToolIsDisposedAfterPartialInitialization(bool throws)
    {
        using var f=new Fixture();int disposed=0;
        f.Plugins.Tool=()=>new CallbackTool(()=>throws ? throw new InvalidOperationException("rejected") : false,()=>disposed++);
        var state=await f.Daemon.GetSettingsState();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Daemon.SaveSettings(Candidate(),state.Token));
        Assert.Equal(1,disposed);
    }
}
