using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Presentation;
using Game.Resource;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Game.HotUpdate.Client
{
    /// <summary>玩家页面和塔防之间的业务流程。资源、档案和视图均由本模块拥有。</summary>
    public sealed class ClientFlow : IDisposable
    {
        private CancellationTokenSource lifetime;
        private cfg.Tables tables;
        private GameObject root;
        private PlayerHubView hub;
        private TowerDefensePrototypeView battle;
        private bool artTransferred;
        private bool disposed;
        private bool entering;
        public ClientArt Art { get; private set; }
        public JourneyProgress Journey { get; private set; }
        public cfg.TdStage Stage => tables.TbTdStage.Get(1);
        public bool Entering => entering;

        public async Task StartAsync(cfg.Tables configuration, CancellationToken token)
        {
            tables = configuration ?? throw new ArgumentNullException(nameof(configuration));
            lifetime = new CancellationTokenSource();
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var openingToken = opening.Token;
            try
            {
                Journey = new JourneyProgress(tables, new LocalJourneyStore());
                Art = await ClientArt.LoadAsync(tables.TbClientTheme.Get(1), openingToken);
                openingToken.ThrowIfCancellationRequested();
                root = new GameObject("GameClient");
                root.AddComponent<PrototypeAssetLifetime>().Own(new List<IDisposable> { Art });
                artTransferred = true;
                var camera = new GameObject("PlayerPageCamera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.tag = "MainCamera";
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .14f, .11f);
                camera.depth = -20;
                if (EventSystem.current == null)
                {
                    var input = new GameObject("ClientEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                    input.transform.SetParent(root.transform, false);
                    input.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                }

                hub = PlayerHubView.Create(this, root.transform);
                Debug.Log("PLAYER_PAGE_READY");
            }
            catch
            {
                if (!artTransferred)
                {
                    Art?.Dispose();
                }

                Dispose();
                throw;
            }
        }

        public async Task EnterBattleAsync()
        {
            if (disposed || entering || battle != null)
            {
                return;
            }

            entering = true;
            hub.Refresh();
            var leases = new List<IDisposable>();
            try
            {
                var token = lifetime.Token;
                var map = await ResourceManager.Instance.AcquireAsync<Sprite>(Stage.MapAsset, token, true);
                leases.Add(map);
                token.ThrowIfCancellationRequested();
                battle = TowerDefensePrototypeView.Create(TowerDefenseTables.Create(tables, Stage.Id), Art.Font, map.Asset, 12345, leases);
                battle.transform.SetParent(root.transform, false);
                battle.SetArt(map.Asset, Art.Atlas, Art.Panel, Art.Font);
                battle.Finished += RecordBattle;
                battle.ExitRequested += ReturnToPlayerPage;
                hub.gameObject.SetActive(false);
                Debug.Log("TOWER_DEFENSE_READY " + Stage.ConfigVersion);
            }
            catch (OperationCanceledException) when (lifetime == null || lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (battle != null)
                {
                    battle.Dispose();
                    battle = null;
                }

                if (!disposed)
                {
                    hub.ShowError("出征准备失败，请重试。");
                }

                Debug.LogException(error);
            }
            finally
            {
                foreach (var lease in leases)
                {
                    lease.Dispose();
                }

                entering = false;
                if (!disposed)
                {
                    hub.Refresh();
                }
            }
        }

        private void RecordBattle(FightSession completed)
        {
            if (disposed || battle == null || !ReferenceEquals(battle.Session, completed))
            {
                return;
            }

            Journey.Record(new JourneyBattle
            {
                RunId = battle.RunId,
                Victory = completed.Phase == BattlePhase.Victory,
                Wave = completed.WaveIndex,
                Kills = completed.Kills,
                Towers = completed.Towers.Count,
                Cards = completed.ChosenCardStacks.Values.Sum()
            });
        }

        public void ReturnToPlayerPage()
        {
            if (disposed || battle == null)
            {
                return;
            }

            battle.Finished -= RecordBattle;
            battle.ExitRequested -= ReturnToPlayerPage;
            battle.Dispose();
            battle = null;
            hub.gameObject.SetActive(true);
            hub.Refresh();
            Debug.Log("PLAYER_PAGE_RETURNED");
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            lifetime?.Cancel();
            if (battle != null)
            {
                battle.Dispose();
                battle = null;
            }

            if (root != null)
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                root = null;
            }
            else if (!artTransferred)
            {
                Art?.Dispose();
            }

            lifetime?.Dispose();
            lifetime = null;
        }
    }
}
