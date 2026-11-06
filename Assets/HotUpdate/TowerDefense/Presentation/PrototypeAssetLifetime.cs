using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.HotUpdate
{
    // 原生UI销毁完成后才归还字体/纹理租约；退出不会提前卸载正在绘制的资源。
    public sealed class PrototypeAssetLifetime : MonoBehaviour
    {
        private readonly List<IDisposable> leases = new List<IDisposable>();
        public void Own(List<IDisposable> owned)
        {
            leases.AddRange(owned);
            owned.Clear();
        }

        private void OnDestroy()
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }

            leases.Clear();
        }
    }
}
