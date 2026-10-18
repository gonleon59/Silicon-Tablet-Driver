using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Plugin.Tablet;
using OpenTabletDriver.UX.RPC;
using StreamJsonRpc;
using Xunit;

namespace OpenTabletDriver.Tests;

public sealed class TabletQueryTests
{
    // A private test pipe and minimal RPC target: never start a daemon or touch user settings.
    public sealed class Target(Func<Task<IEnumerable<TabletReference>>> query)
    {
        public Task<IEnumerable<TabletReference>> GetTablets() => query();
    }

    private sealed class Connection : IAsyncDisposable
    {
        public readonly string Name = "otdh" + Guid.NewGuid().ToString("N")[..12];
        public NamedPipeServerStream Server { get; }
        public RpcClient<IDriverDaemon> Client { get; }
        public JsonRpc? Host { get; private set; }
        private readonly TaskCompletionSource disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Connection()
        {
            Server = new NamedPipeServerStream(Name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            Client = new RpcClient<IDriverDaemon>(Name);
            Client.Disconnected += (_, _) => disconnected.TrySetResult();
        }
        public async Task Start(Target target)
        {
            await Task.WhenAll(Server.WaitForConnectionAsync(), Client.Connect()).WaitAsync(TimeSpan.FromSeconds(5));
            Host = new JsonRpc(Server, Server, target);
            Host.StartListening();
        }
        public async ValueTask DisposeAsync()
        {
            Host?.Dispose(); Server.Dispose();
            if (Host != null) await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task UnconnectedDriverSkipsTheRefresh()
    {
        var client = new RpcClient<IDriverDaemon>("otdh" + Guid.NewGuid().ToString("N")[..12]);
        Assert.Null(await TabletQuery.GetConnectedTablets(client));
    }

    [Fact]
    public async Task ConnectedDriverWithoutTabletReturnsAnEmptyList()
    {
        await using var connection = new Connection();
        await connection.Start(new Target(() => Task.FromResult<IEnumerable<TabletReference>>([])));
        var tablets = await TabletQuery.GetConnectedTablets(connection.Client).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(tablets); Assert.Empty(tablets);
    }

    [Fact]
    public async Task DaemonLossDuringRefreshDoesNotEscapeToTheUi()
    {
        await using var connection = new Connection();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reply = new TaskCompletionSource<IEnumerable<TabletReference>>(TaskCreationOptions.RunContinuationsAsynchronously);
        await connection.Start(new Target(() => { entered.TrySetResult(); return reply.Task; }));
        var refresh = TabletQuery.GetConnectedTablets(connection.Client);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        connection.Host!.Dispose();
        try { Assert.Null(await refresh.WaitAsync(TimeSpan.FromSeconds(5))); }
        finally { reply.TrySetResult([]); }
    }

    [Fact]
    public async Task RealServerErrorsRemainVisibleToTheCaller()
    {
        await using var connection = new Connection();
        await connection.Start(new Target(() => throw new InvalidOperationException("test server failure")));
        await Assert.ThrowsAsync<RemoteInvocationException>(() => TabletQuery.GetConnectedTablets(connection.Client));
    }
}
