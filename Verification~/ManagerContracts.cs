// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

internal static class ManagerContracts
{
    internal static readonly List<string> Calls = new List<string>();
    internal static Exception UIFailure;
    internal static readonly List<string> Cleanup = new List<string>();
    internal static Task UIShutdown = Task.CompletedTask;
    internal static Exception CleanupFailure;
    internal static Exception ModuleFailure, ModuleTickFailure;
    internal static Action ModuleShutdown;
    internal static Task<bool> ResourceInitialization = Task.FromResult(true);
    internal static void Reset()
    {
        Calls.Clear();
        Cleanup.Clear();
        UIShutdown = Task.CompletedTask;
        CleanupFailure = null;
        ModuleFailure = ModuleTickFailure = null;
        ModuleShutdown = null;
        Game.Resource.ResourceManager.Instance.ResetForTest();
        Game.Framework.Storage.LocalStorageManager.Reset();
        UIFailure = null;
        ResourceInitialization = Task.FromResult(true);
        UnityEngine.Resources.Assets.Clear();
        Game.Framework.Network.NetworkManager.Reset();
        Game.Framework.Data.DataManager.Reset();
        Game.Framework.Scenes.SceneManager.Reset();
        Game.Framework.Config.ConfigManager.Reset();
    }
}

namespace Game.Resource
{
    public sealed class AssetLease<T> : IDisposable where T : UnityEngine.Object
    {
        public T Asset { get; }

        internal AssetLease(T value)
        {
            Asset = value;
        }

        public void Dispose()
        {
        }
    }

    public sealed class ResourceManager : Singleton<ResourceManager>
    {
        public Task<UnityEngine.TextAsset> LoadTextAssetAsync(string location) => Task.FromResult(new UnityEngine.TextAsset());
        public object CreateSceneLoader() => new object ();
        public bool Initialized { get; private set; }

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

        internal void ResetForTest()
        {
            Initialized = false;
            initializing = null;
        }

        public Task<AssetLease<T>> AcquireAsync<T>(string path, CancellationToken token = default)
            where T : UnityEngine.Object
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(new AssetLease<T>((T)Activator.CreateInstance(typeof(T))));
        }
    }
}

namespace UI
{
    public interface IUIAssetLoader
    {
        Task<UnityEngine.GameObject> LoadPrefabAsync(string path);
        void ReleasePrefab(UnityEngine.GameObject prefab);
    }

    public interface ICancellableUIAssetLoader : IUIAssetLoader
    {
        Task<UnityEngine.GameObject> LoadPrefabAsync(string path, CancellationToken token);
    }

    public sealed class UIManager : MonoSingleton<UIManager>
    {
        private bool initialized;
        public bool IsInitialized => initialized;

        public void SetAssetLoader(IUIAssetLoader loader)
        {
        }

        public async Task ShutdownAsync()
        {
            await ManagerContracts.UIShutdown;
            initialized = false;
            ManagerContracts.Cleanup.Add("ui");
        }

        protected override void OnStart() => Initialize();
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (ManagerContracts.UIFailure != null)
            {
                throw ManagerContracts.UIFailure;
            }

            initialized = true;
            ManagerContracts.Calls.Add("ui");
        }
    }
}

namespace ECS
{
    public sealed class EntityManager : MonoSingleton<EntityManager>
    {
        protected override void OnDestroy()
        {
            ManagerContracts.Cleanup.Add("entities");
            base.OnDestroy();
        }

        private bool initialized;
        protected override void OnStart()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("entities");
        }
    }

    public sealed class WorldManager : MonoSingleton<WorldManager>
    {
        protected override void OnDestroy()
        {
            ManagerContracts.Cleanup.Add("worlds");
            base.OnDestroy();
        }

        private bool initialized;
        protected override void OnStart()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("worlds");
        }

        public void Initialize()
        {
        }
    }
}

namespace Game.Framework.Network
{
    public sealed class NetworkManager : ConfigurableSingleton<NetworkManager>
    {
        public static NetworkManager Instance => GetOrCreateInstance(() => new NetworkManager());

        private bool initialized;
        public bool IsInitialized => initialized;

        public ValueTask DisposeAsync()
        {
            ManagerContracts.Cleanup.Add("network");
            ReleaseInstance(this);
            return default;
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("network");
        }

        internal static void Reset()
        {
            ResetInstance(null);
        }
    }
}

public sealed class GpuAnimatorManager : MonoSingleton<GpuAnimatorManager>
{
    protected override void OnDestroy()
    {
        ManagerContracts.Cleanup.Add("gpu");
        base.OnDestroy();
    }

    private bool initialized;
    protected override void OnStart()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        ManagerContracts.Calls.Add("gpu");
    }
}

namespace Game.Framework.Network.Unity
{
    public sealed class NetworkSettings : UnityEngine.ScriptableObject
    {
    }
}

namespace Game.Framework.Storage
{
    public sealed class LocalStorageManager : ConfigurableSingleton<LocalStorageManager>, IDisposable
    {
        public static LocalStorageManager Instance => GetOrCreateInstance(() => new LocalStorageManager());
        public string RootDirectory { get; private set; }

        public void Initialize()
        {
            RootDirectory = "test";
            ManagerContracts.Calls.Add("storage");
        }

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("storage");
            ReleaseInstance(this);
        }

        internal static void Reset() => ResetInstance(null);
    }
}

namespace Game.Framework.Scenes
{
    public sealed class SceneManager : ConfigurableSingleton<SceneManager>, IDisposable
    {
        public static SceneManager Instance => GetOrCreateInstance(() => new SceneManager());
        public bool Initialized { get; private set; }

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("scenes");
            ReleaseInstance(this);
        }

        public void Initialize(object loader = null)
        {
            Initialized = true;
            ManagerContracts.Calls.Add("scenes");
        }

        internal static void Reset() => ResetInstance(null);
    }
}

namespace Game.Framework.Audio
{
    public sealed class AudioManager : MonoSingleton<AudioManager>
    {
        protected override void OnDestroy()
        {
            ManagerContracts.Cleanup.Add("audio");
            base.OnDestroy();
        }

        private bool initialized;
        protected override void OnStart() => Initialize();
        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("audio");
        }
    }
}

namespace Game.Framework.Config
{
    public sealed class ConfigManager : ConfigurableSingleton<ConfigManager>, IDisposable
    {
        public static ConfigManager Instance => GetOrCreateInstance(() => new ConfigManager());
        public bool Initialized { get; private set; }

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("config");
            ReleaseInstance(this);
            if (ManagerContracts.CleanupFailure != null)
            {
                throw ManagerContracts.CleanupFailure;
            }
        }

        public void Initialize(Func<string, CancellationToken, Task<string>> loader = null)
        {
            Initialized = true;
            ManagerContracts.Calls.Add("config");
        }

        internal static void Reset() => ResetInstance(null);
    }
}
