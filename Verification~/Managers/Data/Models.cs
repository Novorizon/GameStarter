// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Data
{
    public sealed class DataManager : ConfigurableSingleton<DataManager>, IDisposable
    {
        public static DataManager Instance => GetOrCreateInstance(() => new DataManager());
        public bool Initialized
        {
            get;
            private set;
        }

        public Func<string, CancellationToken, Task<byte[]>> Loader;
        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("data");
            ReleaseInstance(this);
            if (ManagerContracts.CleanupFailure != null)
            {
                throw ManagerContracts.CleanupFailure;
            }
        }

        public override void Initialize()
        {
            Initialize(null);
        }

        public void Initialize(Func<string, CancellationToken, Task<byte[]>> loader)
        {
            Loader = loader;
            Initialized = true;
            ManagerContracts.Calls.Add("data");
        }

        public static void Reset() => ResetInstance(null);
    }
}
