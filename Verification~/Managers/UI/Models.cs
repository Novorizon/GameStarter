// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

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

        protected override void OnExit()
        {
            ManagerContracts.UIRelease = ShutdownAsync();
        }

        public async Task ShutdownAsync()
        {
            await ManagerContracts.UIShutdown;
            initialized = false;
            ManagerContracts.Cleanup.Add("ui");
        }

        protected override void OnStart() => Initialize();
        public override void Initialize()
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
