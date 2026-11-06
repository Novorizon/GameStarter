// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Storage
{
    public sealed class LocalStorageManager : ConfigurableSingleton<LocalStorageManager>, IDisposable
    {
        public static LocalStorageManager Instance => GetOrCreateInstance(() => new LocalStorageManager());
        public string RootDirectory
        {
            get;
            private set;
        }

        public override void Initialize()
        {
            RootDirectory = "test";
            ManagerContracts.Calls.Add("storage");
        }

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("storage");
            ReleaseInstance(this);
            if (ManagerContracts.CleanupFailure != null)
            {
                throw ManagerContracts.CleanupFailure;
            }
        }

        public static void Reset() => ResetInstance(null);
    }
}
