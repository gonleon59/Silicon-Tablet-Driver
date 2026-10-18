using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Components;
using OpenTabletDriver.Plugin.Devices;

namespace OpenTabletDriver.Devices
{
    public class RootHub : ICompositeDeviceHub, IDeviceHub
    {
        public RootHub(IDeviceHubsProvider hubsProvider)
        {
            internalHubs = hubsProvider.DeviceHubs.ToHashSet();
            hubs = new HashSet<IDeviceHub>(internalHubs);
            ForceEnumeration();
            oldEndpoints = endpoints.ToArray();

            foreach (var hub in hubs)
            {
                HookDeviceNotification(hub);
            }

            Log.Write(nameof(RootHub), $"Initialized internal child hubs: {string.Join(", ", hubs.Select(h => h.GetType().Name))}", LogLevel.Debug);
        }

        private readonly object syncObject = new();
        private readonly HashSet<IDeviceHub> internalHubs;
        private readonly HashSet<IDeviceHub> hubs;
        private IEnumerable<IDeviceEndpoint> oldEndpoints = [];
        private readonly List<IDeviceEndpoint> endpoints = new();
        private long version;
        private IServiceProvider? serviceProvider;

        public event EventHandler<DevicesChangedEventArgs>? DevicesChanged;

        public IEnumerable<IDeviceHub> DeviceHubs
        {
            get { lock (syncObject) return hubs.ToArray(); }
        }

        public static RootHub WithProvider(IServiceProvider provider)
        {
            return new RootHub(provider.GetRequiredService<IDeviceHubsProvider>())
                .RegisterServiceProvider(provider);
        }

        public IEnumerable<IDeviceEndpoint> GetDevices()
        {
            lock (syncObject) return endpoints.ToArray();
        }

        public void ConnectDeviceHub<T>() where T : IDeviceHub
        {
            if (serviceProvider == null)
                throw new InvalidOperationException("Cannot instantiate device hubs without a service provider");

            ConnectDeviceHub(ActivatorUtilities.CreateInstance<T>(serviceProvider!));
        }

        public void ConnectDeviceHub(IDeviceHub rootHub)
        {
            Log.Write(nameof(RootHub), $"Connecting hub: {rootHub.GetType().Name}", LogLevel.Debug);
            DevicesChangedEventArgs? changes = null;
            lock (syncObject)
            {
                if (hubs.Add(rootHub))
                {
                    HookDeviceNotification(rootHub);
                    changes = CommitHubChange();
                }
            }
            if (changes != null)
                DevicesChanged?.Invoke(this, changes);
            else
                Log.Write(nameof(RootHub), $"Connection failed, {rootHub.GetType().Name} is already connected", LogLevel.Debug);
        }

        public void DisconnectDeviceHub<T>() where T : IDeviceHub
        {
            foreach (var hub in DeviceHubs.Where(static t => t.GetType().IsAssignableTo(typeof(T))))
            {
                DisconnectDeviceHub(hub);
            }
        }

        public void DisconnectDeviceHub(IDeviceHub rootHub)
        {
            Log.Write(nameof(RootHub), $"Disconnecting hub: {rootHub.GetType().Name}", LogLevel.Debug);
            DevicesChangedEventArgs? changes = null;
            lock (syncObject)
            {
                if (hubs.Remove(rootHub))
                {
                    UnhookDeviceNotification(rootHub);
                    changes = CommitHubChange();
                }
            }
            if (changes != null)
                DevicesChanged?.Invoke(this, changes);
            else
                Log.Write(nameof(RootHub), $"Disconnection failed, {rootHub.GetType().Name} is not a connected hub", LogLevel.Debug);
        }

        private async void OnDevicesChanged(object? sender, DevicesChangedEventArgs eventArgs)
        {
            try
            {
                long lastVersion;
                lock (syncObject)
                {
                    endpoints.RemoveAll(e => eventArgs.Removals.Contains(e, DevicesChangedEventArgs.Comparer));
                    endpoints.AddRange(eventArgs.Additions.Where(e => !endpoints.Contains(e, DevicesChangedEventArgs.Comparer)));
                    lastVersion = ++version;
                }

                await Task.Delay(20);

                DevicesChangedEventArgs? changes = null;
                lock (syncObject)
                {
                    if (version == lastVersion)
                        changes = CaptureChanges();
                }

                if (changes != null)
                {
                    Log.Write(nameof(RootHub), "Invoking DevicesChanged", LogLevel.Debug);
                    DevicesChanged?.Invoke(this, changes);
                }
                else
                {
                    Log.Write(nameof(RootHub), $"Debounced {sender?.GetType().Name}'s DevicesChanged event");
                }
            }
            catch (Exception error)
            {
                // This is an async event boundary; never let failures escape to the thread pool.
                Log.Exception(error);
            }
        }

        private void HookDeviceNotification(IDeviceHub rootHub)
        {
            rootHub.DevicesChanged += HandleHubDeviceNotification;
        }

        private void UnhookDeviceNotification(IDeviceHub rootHub)
        {
            rootHub.DevicesChanged -= HandleHubDeviceNotification;
        }

        private void HandleHubDeviceNotification(object? sender, DevicesChangedEventArgs eventArgs)
        {
            if (eventArgs.Changes.Any())
            {
                Log.Debug(sender?.GetType().Name ?? "RootHub/DeviceNotification", $"Changes: {eventArgs.Changes.Count()}, Add: {eventArgs.Additions.Count()}, Remove: {eventArgs.Removals.Count()}");
                OnDevicesChanged(sender, eventArgs);
            }
        }

        // Called with syncObject held. Pending debounce callbacks must not republish stale changes.
        private DevicesChangedEventArgs CommitHubChange()
        {
            ++version;
            ForceEnumeration();
            return CaptureChanges();
        }

        private DevicesChangedEventArgs CaptureChanges()
        {
            var changes = new DevicesChangedEventArgs(oldEndpoints, endpoints);
            oldEndpoints = changes.Current;
            return changes;
        }

        private void ForceEnumeration()
        {
            endpoints.Clear();
            endpoints.AddRange(hubs.SelectMany(h => h.GetDevices()));
        }

        private RootHub RegisterServiceProvider(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
            return this;
        }
    }
}
