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
    internal static Task<bool> ResourceInitialization = Task.FromResult(true);
    internal static void Reset()
    {
        Calls.Clear();
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
    public sealed class ResourceManager : Singleton<ResourceManager>
    {
        public Task<UnityEngine.TextAsset> LoadTextAssetAsync(string location) => Task.FromResult(new UnityEngine.TextAsset());
        public object CreateSceneLoader() => new object ();
        public Task<bool> InitializeAsync()
        {
            ManagerContracts.Calls.Add("resources");
            return ManagerContracts.ResourceInitialization;
        }
    }
}

namespace UI
{
    public sealed class UIManager : MonoSingleton<UIManager>
    {
        private bool initialized;
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
    public sealed class LocalStorageManager : Singleton<LocalStorageManager>
    {
        public void Initialize() => ManagerContracts.Calls.Add("storage");
    }
}

namespace Game.Framework.Scenes
{
    public sealed class SceneManager : ConfigurableSingleton<SceneManager>
    {
        public static SceneManager Instance => GetOrCreateInstance(() => new SceneManager());
        public bool Initialized { get; private set; }

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
    public sealed class ConfigManager : ConfigurableSingleton<ConfigManager>
    {
        public static ConfigManager Instance => GetOrCreateInstance(() => new ConfigManager());
        public bool Initialized { get; private set; }

        public void Initialize(Func<string, CancellationToken, Task<string>> loader = null)
        {
            Initialized = true;
            ManagerContracts.Calls.Add("config");
        }

        internal static void Reset() => ResetInstance(null);
    }
}
