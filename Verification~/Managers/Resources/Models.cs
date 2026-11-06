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

        public string PackageVersion { get; private set; }
        public ResourceInitializationOptions LastOptions;
        public async Task<bool> InitializeAsync(ResourceInitializationOptions options, CancellationToken token = default)
        {
            LastOptions = options;
            token.ThrowIfCancellationRequested();
            bool ready = await Game.Framework.TaskUtil.WaitAsync(InitializeAsync(), token);
            PackageVersion = options.PackageVersion;
            return ready;
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

        public async Task<AssetLease<T>> AcquireAsync<T>(string path, CancellationToken token = default, bool keepAliveUntilDisposed = false)
            where T : UnityEngine.Object
        {
            token.ThrowIfCancellationRequested();
            if (keepAliveUntilDisposed)
            {
                ManagerContracts.ProtectedAcquisitions++;
            }

            if (!Initialized)
            {
                throw new InvalidOperationException("Initialize YooAsset first.");
            }

            UnityEngine.Object asset;
            if (typeof(T) == typeof(UnityEngine.TextAsset))
            {
                ManagerContracts.DataAcquisitions.Add(path);
                if (path == "Data/Luban/manifest")
                {
                    ManagerContracts.Calls.Add("tables");
                }

                byte[] bytes = await ManagerContracts.LoadDataBytes(path, token);
                token.ThrowIfCancellationRequested();
                asset = new UnityEngine.TextAsset
                {
                    bytes = bytes
                };
            }
            else if (typeof(T) == typeof(UnityEngine.GameObject))
            {
                asset = new UnityEngine.GameObject();
            }
            else
            {
                asset = (UnityEngine.Object)Activator.CreateInstance(typeof(T));
            }

            return new AssetLease<T>((T)asset);
        }
    }
}

namespace Game.Resource
{
    public static class YooAssetsSettings
    {
        public static YooAsset.InitializeParameters CreateEditorSimulateModeParameters() => new YooAsset.InitializeParameters();
        public static YooAsset.InitializeParameters CreateOfflinePlayModeParameters() => new YooAsset.OfflinePlayModeParameters();
        public static YooAsset.InitializeParameters CreateHostPlayModeParameters(string main, string fallback = null) => new YooAsset.HostPlayModeParameters();
    }
}
