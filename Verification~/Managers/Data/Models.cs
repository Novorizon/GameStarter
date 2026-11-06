// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Config
{
    public sealed class ConfigManager : ConfigurableSingleton<ConfigManager>, IDisposable
    {
        public static ConfigManager Instance => GetOrCreateInstance(() => new ConfigManager());
        public bool Initialized
        {
            get;
            private set;
        }

        public Func<string, CancellationToken, Task<string>> TextLoader;
        public Func<string, CancellationToken, Task<byte[]>> BytesLoader;
        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("config");
            ReleaseInstance(this);
            if (ManagerContracts.CleanupFailure != null)
            {
                throw ManagerContracts.CleanupFailure;
            }
        }

        public override void Initialize()
        {
            Initialize(null, null);
        }

        public void Initialize(Func<string, CancellationToken, Task<string>> loader = null, Func<string, CancellationToken, Task<byte[]>> bytesLoader = null)
        {
            TextLoader = loader;
            BytesLoader = bytesLoader;
            Initialized = true;
            ManagerContracts.Calls.Add("config");
        }

        public static void Reset() => ResetInstance(null);
    }
}
