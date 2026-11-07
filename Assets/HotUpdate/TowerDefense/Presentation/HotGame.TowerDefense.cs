using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Presentation;
using Game.Resource;
using TMPro;
using UnityEngine;

namespace Game.HotUpdate
{
    public sealed partial class HotGame
    {
        private TowerDefensePrototypeView towerDefenseView;

        private async Task StartTowerDefenseAsync(cfg.Tables tables, CancellationToken token)
        {
            var leases = new List<IDisposable>();
            var assets = tables.TbTdViewAsset.DataList.ToDictionary(row => row.Key, row => row.Asset);
            try
            {
                var map = await ResourceManager.Instance.AcquireAsync<Sprite>(tables.TbTdStage.Get(1).MapAsset, token, true);
                leases.Add(map);
                var atlas = await ResourceManager.Instance.AcquireAsync<Texture2D>(assets["cardIcons"], token, true);
                leases.Add(atlas);
                var frame = await ResourceManager.Instance.AcquireAsync<Sprite>(assets["cardFrame"], token, true);
                leases.Add(frame);
                var font = await ResourceManager.Instance.AcquireAsync<TMP_FontAsset>(assets["font"], token, true);
                leases.Add(font);
                token.ThrowIfCancellationRequested();
                towerDefenseView = TowerDefensePrototypeView.Create(TowerDefenseTables.Create(tables, 1), font.Asset, map.Asset, 12345, leases);
                towerDefenseView.SetArt(map.Asset, atlas.Asset, frame.Asset, font.Asset);
                host.Log("TOWER_DEFENSE_READY " + tables.TbTdStage.Get(1).ConfigVersion);
            }
            finally
            {
                foreach (var lease in leases)
                {
                    lease.Dispose();
                }
            }
        }

        private void StopTowerDefense()
        {
            if (towerDefenseView != null)
            {
                towerDefenseView.Dispose();
                towerDefenseView = null;
            }
        }
    }

}
