using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenTabletDriver.Desktop.Contracts;
using OpenTabletDriver.Desktop.RPC;
using OpenTabletDriver.Plugin.Tablet;
using StreamJsonRpc;

namespace OpenTabletDriver.UX.RPC
{
    public static class TabletQuery
    {
        // A lost connection is handled by MainForm's existing reconnect path. A null result
        // means this refresh belongs to an unavailable or superseded daemon connection.
        public static async Task<TabletReference[]?> GetConnectedTablets(RpcClient<IDriverDaemon> driver)
        {
            var instance = driver.Instance;
            if (instance == null || !driver.IsConnected) return null;

            try
            {
                var tablets = (await instance.GetTablets()).ToArray();
                return driver.IsConnected && ReferenceEquals(instance, driver.Instance) ? tablets : null;
            }
            catch (ConnectionLostException) { return null; }
            catch (ObjectDisposedException) { return null; }
            catch (IOException) { return null; }
        }
    }
}
