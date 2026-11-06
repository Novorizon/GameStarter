// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Resource
{
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        public T Asset
        {
            get;
        }

        public AssetLease(T value)
        {
            Asset = value;
        }

        public void Dispose()
        {
            ManagerContracts.LeasesReleased++;
        }
    }

    public sealed class ResourceManager : Singleton<ResourceManager>
    {
        public Task<UnityEngine.TextAsset> LoadTextAssetAsync(string location) => Task.FromResult(new UnityEngine.TextAsset());
        public object CreateSceneLoader() => new object ();
        public bool Initialized
        {
            get;
            private set;
        }

        private Task<bool> initializing;
        public async Task<bool> InitializeAsync()
        {
            ManagerContracts.Calls.Add("resources");
            initializing = ManagerContracts.ResourceInitialization;
            return Initialized = await initializing;
        }

        public async Task ShutdownAsync()
        {
            if (initializing != null)
            {
                try
                {
                    await initializing;
                }
                catch
                {
                }
            }

            Initialized = false;
            ManagerContracts.Cleanup.Add("resources");
        }

        public static void Release()
        {
            if (IsCreated)
            {
                _ = Instance.ShutdownAsync();
            }
        }

        public void ResetForTest()
        {
            Initialized = false;
            initializing = null;
        }

        public Task<AssetLease<T>> AcquireAsync<T>(string path, CancellationToken token = default, bool keepAliveUntilDisposed = false)
            where T : UnityEngine.Object
        {
            token.ThrowIfCancellationRequested();
            if (keepAliveUntilDisposed)
            {
                ManagerContracts.ProtectedAcquisitions++;
            }

            var asset = typeof(T) == typeof(UnityEngine.GameObject) ? (UnityEngine.Object)new UnityEngine.GameObject() : (UnityEngine.Object)Activator.CreateInstance(typeof(T));
            return Task.FromResult(new AssetLease<T>((T)asset));
        }
    }
}
