using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using OpenTabletDriver.Desktop;
using OpenTabletDriver.UX.Native;
using Xunit;

namespace OpenTabletDriver.Tests;

public class NativeSettingsTransactionTests
{
    [Fact] public async Task SuccessfulSaveReplacesCompleteFileAndAppliesCandidate()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var old=new Settings { Revision="old" };var updated=new Settings { Revision="new" };Settings? applied=null;
            string path=Path.Combine(dir,"settings.json");File.WriteAllText(path,"old file");
            await NativeSettingsTransaction.SaveAsync(old,updated,path,s=>{applied=s;return Task.CompletedTask;});
            Assert.Same(updated,applied);Assert.Equal("new",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path))!.Revision);
            Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact] public async Task DaemonFailureRollsBackWithoutDestroyingPreviousFile()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var old=new Settings { Revision="old" };var updated=new Settings { Revision="new" };Settings? applied=null;
            string path=Path.Combine(dir,"settings.json");File.WriteAllText(path,"keep this");
            await Assert.ThrowsAsync<InvalidOperationException>(()=>NativeSettingsTransaction.SaveAsync(old,updated,path,s=>{
                if (ReferenceEquals(s,updated)) throw new InvalidOperationException("daemon failed");applied=s;return Task.CompletedTask;
            }));
            Assert.Same(old,applied);Assert.Equal("keep this",File.ReadAllText(path));Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir,true); }
    }
    [Fact] public async Task UnwritableLocationFailsBeforeChangingDriver()
    {
        bool called=false;string path=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"),"missing","settings.json");
        await Assert.ThrowsAsync<DirectoryNotFoundException>(()=>NativeSettingsTransaction.SaveAsync(new(),new(),path,_=>{called=true;return Task.CompletedTask;}));
        Assert.False(called);
    }
    [Fact] public async Task ConcurrentFileChangeIsPreservedAndRunningSettingsAreRolledBack()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            var old=new Settings { Revision="old" };var candidate=new Settings { Revision="candidate" };Settings? applied=null;
            string path=Path.Combine(dir,"settings.json");File.WriteAllText(path,"old file");
            await Assert.ThrowsAsync<IOException>(()=>NativeSettingsTransaction.SaveAsync(old,candidate,path,s=>
            {
                applied=s;
                if (ReferenceEquals(s,candidate)) File.WriteAllText(path,"external edit");
                return Task.CompletedTask;
            }));
            Assert.Same(old,applied);Assert.Equal("external edit",File.ReadAllText(path));Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public async Task SavedFileIncludesNormalizationDuringApply()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string path=Path.Combine(dir,"settings.json");
            await NativeSettingsTransaction.SaveAsync(new(),new(),path,s=>{s.Revision="normalized";return Task.CompletedTask;});
            Assert.Equal("normalized",JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path))!.Revision);
        }
        finally { Directory.Delete(dir,true); }
    }

    [Fact] public async Task RollbackFailureReportsBothErrorsAndCleansStagedFile()
    {
        string dir=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            string path=Path.Combine(dir,"settings.json");File.WriteAllText(path,"keep");int attempts=0;
            var error=await Assert.ThrowsAnyAsync<AggregateException>(()=>NativeSettingsTransaction.SaveAsync(new(),new(),path,_=>
            { attempts++;throw new InvalidOperationException("attempt "+attempts); }));
            Assert.Equal(2,attempts);Assert.Equal(2,error.InnerExceptions.Count);
            Assert.Equal("keep",File.ReadAllText(path));Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir,true); }
    }
}
