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
        private sealed class SceneLoader : Game.Framework.Scenes.ISceneLoader
        {
        }

        public Game.Framework.Scenes.ISceneLoader CreateSceneLoader()
        {
            if (!Initialized)
            {
                throw new InvalidOperationException("Initialize YooAsset before creating the scene loader.");
            }

            return new SceneLoader();
        }
        public bool Initialized
        {
            get;
            private set;
        }

        public string PackageVersion { get; private set; }
        public bool PackageInitialized { get; private set; }
        public ResourceInitializationOptions LastOptions;
        public YooAsset.InitializeParameters LastPackageParameters;
        private Task<bool> packageInitializing;
        private Task<bool> initializing;
        private int generation;

        public async Task<bool> InitializePackageAsync(YooAsset.InitializeParameters parameters, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (PackageInitialized)
            {
                return true;
            }

            ManagerContracts.StartupSteps.Add("resources-initialize");
            LastPackageParameters = parameters;
            packageInitializing = ManagerContracts.ResourcePackageInitialization;
            int started = generation;
            bool ready = await Game.Framework.TaskUtil.WaitAsync(packageInitializing, token);
            token.ThrowIfCancellationRequested();
            if (started != generation)
            {
                return false;
            }

            return PackageInitialized = ready;
        }

        public Task<bool> InitializeAsync(ResourceInitializationOptions options, CancellationToken token = default)
        {
            return PrepareContentAsync(options, token);
        }

        public async Task<bool> PrepareContentAsync(ResourceInitializationOptions options, CancellationToken token = default)
        {
            LastOptions = options;
            token.ThrowIfCancellationRequested();
            if (!await InitializePackageAsync(options.Parameters ?? new YooAsset.InitializeParameters(), token))
            {
                return false;
            }

            bool ready = await Game.Framework.TaskUtil.WaitAsync(InitializeAsync(), token);
            PackageVersion = options.PackageVersion;
            return ready;
        }

        public async Task<bool> InitializeAsync()
        {
            if (Initialized)
            {
                return true;
            }

            if (!PackageInitialized && !await InitializePackageAsync(new YooAsset.InitializeParameters()))
            {
                return false;
            }

            ManagerContracts.Calls.Add("resources");
            ManagerContracts.StartupSteps.Add("content-prepare");
            initializing = ManagerContracts.ResourceInitialization;
            int started = generation;
            bool ready = await initializing;
            if (started != generation)
            {
                return false;
            }

            return Initialized = ready;
        }

        public async Task ShutdownAsync()
        {
            generation++;
            Initialized = false;
            PackageInitialized = false;
            foreach (var pending in new[] { packageInitializing, initializing })
            {
                if (pending != null)
                {
                    try
                    {
                        await pending;
                    }
                    catch
                    {
                    }
                }
            }

            ManagerContracts.Cleanup.Add("resources");
        }

        public new static void Release()
        {
            if (TryGetInstance(out var current))
            {
                _ = current.ShutdownAsync();
            }
        }

        public void ResetForTest()
        {
            generation++;
            Initialized = false;
            PackageInitialized = false;
            initializing = null;
            packageInitializing = null;
            LastOptions = null;
            LastPackageParameters = null;
            PackageVersion = null;
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
