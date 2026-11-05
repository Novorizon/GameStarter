// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    }
}

namespace Game.Resource
{
    public sealed class ResourceManager : Singleton<ResourceManager>
    {
        public Task<bool> InitializeAsync()
        {
            ManagerContracts.Calls.Add("resources");
            return ManagerContracts.ResourceInitialization;
        }
    }
}

namespace UI
{
    public sealed class UIRoot : MonoSingleton<UIRoot>
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
    public sealed class NetworkManager : Singleton<NetworkManager>
    {
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
            Instance.initialized = false;
        }
    }
}

namespace Game.Framework.Network.Unity
{
    public sealed class NetworkSettings : UnityEngine.ScriptableObject
    {
    }
}
