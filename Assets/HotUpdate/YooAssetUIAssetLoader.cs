#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Resource;
using UI;
using UnityEngine;

namespace Game
{
    /// <summary>热更侧 UI 接线：每次成功加载持有一份独立租约，在实例销毁后释放。</summary>
    internal sealed class YooAssetUIAssetLoader : ICancellableUIAssetLoader
    {
        private readonly ResourceManager resources;
        private readonly Dictionary<GameObject, Stack<AssetLease<GameObject>>> leases = new Dictionary<GameObject, Stack<AssetLease<GameObject>>>();
        internal YooAssetUIAssetLoader(ResourceManager resources)
        {
            this.resources = resources;
        }

        public Task<GameObject?> LoadPrefabAsync(string path) => LoadPrefabAsync(path, CancellationToken.None);
        public async Task<GameObject?> LoadPrefabAsync(string path, CancellationToken cancellationToken)
        {
            var lease = await resources.AcquireAsync<GameObject>(path, cancellationToken, keepAliveUntilDisposed: true);
            var prefab = lease.Asset;
            if (!leases.TryGetValue(prefab, out var owned))
            {
                owned = new Stack<AssetLease<GameObject>>();
                leases.Add(prefab, owned);
            }

            owned.Push(lease);
            return prefab;
        }

        public void ReleasePrefab(GameObject prefab)
        {
            if (!leases.TryGetValue(prefab, out var owned) || owned.Count == 0)
            {
                throw new InvalidOperationException("The UI loader does not own a lease for this prefab.");
            }

            var lease = owned.Pop();
            if (owned.Count == 0)
            {
                leases.Remove(prefab);
            }

            lease.Dispose();
        }
    }
}
