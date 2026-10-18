using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using OpenTabletDriver.Devices;
using OpenTabletDriver.Plugin.Components;
using OpenTabletDriver.Plugin.Devices;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class DeviceHotplugTests
{
    private static IDeviceEndpoint Endpoint(string path)
    {
        var endpoint = Substitute.For<IDeviceEndpoint>();
        endpoint.DevicePath.Returns(path);
        endpoint.VendorID.Returns(0x056a);
        return endpoint;
    }

    private sealed class Hub(params IDeviceEndpoint[] initial) : IDeviceHub
    {
        private IDeviceEndpoint[] devices = initial;
        public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;
        public IEnumerable<IDeviceEndpoint> GetDevices() => devices;
        public void Set(params IDeviceEndpoint[] next)
        {
            var previous = Interlocked.Exchange(ref devices, next);
            DevicesChanged?.Invoke(this, new DevicesChangedEventArgs(previous, next));
        }
    }

    private static RootHub Root(params IDeviceHub[] hubs)
    {
        var provider = Substitute.For<IDeviceHubsProvider>();
        provider.DeviceHubs.Returns(hubs);
        return new RootHub(provider);
    }

    [Fact]
    public void EventArgumentsRetainBeforeAndAfterEvenWhenProducerClearsItsLists()
    {
        var oldDevice = Endpoint("old"); var newDevice = Endpoint("new");
        var previous = new List<IDeviceEndpoint> { oldDevice };
        var current = new List<IDeviceEndpoint> { newDevice };
        var changes = new DevicesChangedEventArgs(previous, current);
        previous.Clear(); current.Clear();
        Assert.Equal(new[] { oldDevice }, changes.Removals);
        Assert.Equal(new[] { newDevice }, changes.Additions);
    }

    [Fact]
    public void EventArgumentsEnumerateLazyProducersOnlyOnce()
    {
        int enumerations = 0;
        var endpoint = Endpoint("device");
        IEnumerable<IDeviceEndpoint> Devices() { enumerations++; yield return endpoint; }
        var changes = new DevicesChangedEventArgs([], Devices());
        Assert.Single(changes.Additions); Assert.Single(changes.Changes);
        Assert.Single(changes.Current);
        Assert.Equal(1, enumerations);
    }

    [Fact]
    public void GetDevicesReturnsAStableSnapshotDuringTheNextNotification()
    {
        var first = Endpoint("first"); var second = Endpoint("second");
        var hub = new Hub(first); var root = Root(hub);
        var snapshot = root.GetDevices();
        hub.Set(second);
        Assert.Equal(new[] { first }, snapshot);
        Assert.Equal(new[] { second }, root.GetDevices());
    }

    [Fact]
    public async Task InFlightVendorEnumerationSurvivesAnotherDeviceChange()
    {
        var first = Endpoint("first"); var second = Endpoint("second");
        var hub = new Hub(); var root = Root(hub);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        int notifications = 0;
        root.DevicesChanged += (_, changes) =>
        {
            if (Interlocked.Increment(ref notifications) != 1) return;
            try
            {
                // Pause exactly where the real daemon enumerates Additions on hotplug.
                bool found = changes.Additions.Any(endpoint =>
                {
                    entered.TrySetResult();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
                    return endpoint.VendorID == -1;
                });
                Assert.False(found);
                Assert.Equal(new[] { first }, changes.Current);
                finished.TrySetResult();
            }
            catch (Exception error) { finished.TrySetException(error); }
        };
        hub.Set(first);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            hub.Set(second);
        }
        finally { release.Set(); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ConnectingAndDisconnectingHubsReportsCorrectDirectionAndStableHistory()
    {
        var first = Endpoint("first"); var second = Endpoint("second");
        var root = Root(new Hub(first)); var extra = new Hub(second);
        var events = new List<DevicesChangedEventArgs>();
        root.DevicesChanged += (_, changes) => events.Add(changes);
        var originalHubs = root.DeviceHubs;
        root.ConnectDeviceHub(extra);
        root.DisconnectDeviceHub(extra);
        Assert.Single(originalHubs);
        Assert.Equal(2, events.Count);
        Assert.Equal(new[] { second }, events[0].Additions);
        Assert.Empty(events[0].Removals);
        Assert.Equal(new[] { second }, events[1].Removals);
        Assert.Empty(events[1].Additions);
        Assert.Equal(new[] { first, second }, events[0].Current);
        Assert.Equal(new[] { first }, root.GetDevices());
    }

    [Fact]
    public async Task BurstKeepsTheLastNotifiedBaselineAndTheFinalDeviceSet()
    {
        var first = Endpoint("first"); var middle = Endpoint("middle"); var last = Endpoint("last");
        var hub = new Hub(first); var root = Root(hub);
        var done = new TaskCompletionSource<DevicesChangedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        root.DevicesChanged += (_, changes) =>
        {
            if (changes.Current.Contains(last)) done.TrySetResult(changes);
        };
        hub.Set(middle); hub.Set(last);
        var result = await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(middle, result.Current);
        Assert.Equal(new[] { last }, result.Current);
    }

    [Fact]
    public async Task ConcurrentIndependentHubsKeepEnumerationStable()
    {
        var hubs = Enumerable.Range(0, 4).Select(_ => new Hub()).ToArray();
        var root = Root(hubs);
        var final = hubs.Select((_, i) => Endpoint("final" + i)).ToArray();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        root.DevicesChanged += (_, changes) =>
        {
            if (changes.Current.Count() == 4 && changes.Current.All(final.Contains)) done.TrySetResult();
        };
        await Task.WhenAll(hubs.Select((hub, index) => Task.Run(() =>
        {
            for (int i = 0; i < 80; i++)
            {
                hub.Set(Endpoint(index + "/" + i));
                var snapshot = root.GetDevices();
                foreach (var endpoint in snapshot) Assert.NotNull(endpoint.DevicePath);
                hub.Set();
            }
            hub.Set(final[index]);
        })));
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(final.OrderBy(e => e.DevicePath), root.GetDevices().OrderBy(e => e.DevicePath));
    }
}
