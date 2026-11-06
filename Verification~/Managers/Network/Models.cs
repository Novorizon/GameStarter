// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Network
{
    public sealed class NetworkManager : ConfigurableSingleton<NetworkManager>, IDisposable
    {
        public static NetworkManager Instance => GetOrCreateInstance(() => new NetworkManager());
        private bool initialized;
        public bool IsInitialized => initialized;
        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("network");
            ReleaseInstance(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }

        public override void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("network");
        }

        public static void Reset()
        {
            ResetInstance(null);
        }
    }
}
