using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private Task arrivalTask;
        private ExpeditionPlan preparedExpedition;
        private int battleCampLevel;
        private readonly Queue<JourneyBattle> pendingResults = new Queue<JourneyBattle>();
        public ClientArt Art { get; private set; }
        public JourneyProgress Journey { get; private set; }
        public cfg.TdStage Stage => tables.TbTdStage.Get(1);
        public bool Entering => entering;
        public string ExpeditionError { get; private set; }

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
                if (EventSystem.current == null)
                {
                    var input = new GameObject("ClientEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                    input.transform.SetParent(root.transform, false);
                    input.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
                }

                hub = PlayerHubView.Create(this, root.transform);
                RetryArrival();
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

        public ExpeditionPlan PrepareExpedition()
        {
            if (disposed || entering || battle != null || !Journey.Camp.ArrivalSeen || hub.ArrivalPlaying)
            {
                return null;
            }

            if (preparedExpedition == null)
            {
                uint seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                preparedExpedition = new ExpeditionPlan(tables, Stage.Id, seed, Journey.Camp.Level.Id);
                ExpeditionError = null;
            }

            return preparedExpedition;
        }

        public void CancelExpedition()
        {
            if (!entering)
            {
                preparedExpedition = null;
                ExpeditionError = null;
            }
        }

        public async Task<bool> EnterBattleAsync()
        {
            if (disposed || entering || battle != null || !Journey.Camp.ArrivalSeen || hub.ArrivalPlaying)
            {
                return false;
            }

            var plan = preparedExpedition ?? PrepareExpedition();
            entering = true;
            ExpeditionError = null;
            if (!SaveBattleProgress())
            {
                entering = false;
                ExpeditionError = "上一场补给尚未保存，请稍后再次确认出征重试。";
                hub.ShowError(ExpeditionError);
                return false;
            }

            hub.Refresh();
            var leases = new List<IDisposable>();
            try
            {
                var token = lifetime.Token;
                var map = await ResourceManager.Instance.AcquireAsync<Sprite>(plan.Stage.MapAsset, token, true);
                leases.Add(map);
                token.ThrowIfCancellationRequested();
                battle = TowerDefensePrototypeView.Create(plan.Configuration, Art.Font, map.Asset, plan.Seed, leases);
                battle.transform.SetParent(root.transform, false);
                battle.SetArt(map.Asset, Art.Atlas, Art.Panel, Art.Font);
                battleCampLevel = plan.CampLevel.Id;
                battle.Finished += RecordBattle;
                battle.ExitRequested += ReturnToPlayerPage;
                preparedExpedition = null;
                hub.gameObject.SetActive(false);
                Debug.Log("TOWER_DEFENSE_READY " + plan.Stage.ConfigVersion + " seed=" + plan.Seed);
                return true;
            }
            catch (OperationCanceledException) when (lifetime == null || lifetime.IsCancellationRequested)
            {
                return false;
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
                    ExpeditionError = "战场资源准备失败，请再次确认出征重试。";
                    hub.ShowError(ExpeditionError);
                }

                Debug.LogException(error);
                return false;
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

        public void RetryArrival()
        {
            if (disposed || hub == null || Journey.Camp.ArrivalSeen || (arrivalTask != null && !arrivalTask.IsCompleted))
            {
                return;
            }

            arrivalTask = ShowArrivalAsync();
        }

        private async Task ShowArrivalAsync()
        {
            try
            {
                var playback = hub.PlayArrivalAsync(lifetime.Token);
                hub.Refresh();
                await playback;
            }
            catch (OperationCanceledException) when (disposed)
            {
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                if (!disposed)
                {
                    hub.ShowError("抵达进度未能保存，请点击“重试抵达序章”。");
                    Debug.LogWarning("Arrival save failed: " + error.Message);
                }
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    hub.ShowError("序章未能完成，请点击“重试抵达序章”。");
                }

                Debug.LogException(error);
            }
            finally
            {
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

            pendingResults.Enqueue(new JourneyBattle
            {
                RunId = battle.RunId,
                Victory = completed.Phase == BattlePhase.Victory,
                CampLevelId = battleCampLevel,
                Wave = completed.WaveIndex,
                Kills = completed.Kills,
                Towers = completed.Towers.Count,
                Cards = completed.ChosenCardStacks.Values.Sum()
            });
            SaveBattleProgress();
        }

        private bool SaveBattleProgress()
        {
            if (pendingResults.Count == 0)
            {
                return true;
            }

            try
            {
                while (pendingResults.Count > 0)
                {
                    Journey.Record(pendingResults.Peek());
                    pendingResults.Dequeue();
                }
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("Local battle progress could not be saved: " + error.Message);
                return false;
            }
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
            bool saved = SaveBattleProgress();
            hub.Refresh();
            if (!saved)
            {
                hub.ShowError("本次战绩与补给尚未保存，下次出征时会重试。");
            }
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
            hub?.Stop();
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
