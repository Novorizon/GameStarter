using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Presentation;
using Game.Resource;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace Game.HotUpdate.Client
{
    /// <summary>玩家页面和塔防之间的业务流程。资源、档案和视图均由本模块拥有。</summary>
    public sealed partial class ClientFlow : IDisposable
    {
        private CancellationTokenSource lifetime;
        private cfg.Tables tables;
        private Dictionary<int, TowerDefenseMapObjectives.Settings> mapSettings;
        private GameObject root;
        private PlayerHubView hub;
        private GameServerConnection server;
        private TowerDefensePrototypeView battle;
        private bool artTransferred;
        private bool disposed;
        private bool entering;
        private Task campStoryTask;
        private ExpeditionPlan preparedExpedition;
        private int battleCampLevel;
        private int battleStageId;
        private readonly Queue<JourneyBattle> pendingResults = new Queue<JourneyBattle>();
        public ClientArt Art { get; private set; }
        public JourneyProgress Journey { get; private set; }
        public cfg.TdStage Stage => tables.TbTdStage.Get(Journey.NextStageId > 0 ? Journey.NextStageId : Math.Max(1, Journey.ClearedStages));
        internal IReadOnlyList<cfg.TdStage> Stages => tables.TbTdStage.DataList;
        public bool Entering => entering;
        public string ExpeditionError { get; private set; }
        internal bool CampStoryInProgress => campStoryTask != null && !campStoryTask.IsCompleted;
        internal bool CanSelectStage(int stageId)
        {
            return Journey.Camp.ExpeditionUnlocked && !CampStoryInProgress && stageId > 0
                && (Journey.NextStageId > 0 || Journey.StoryStep == CampaignStep.Complete)
                && (stageId <= Journey.ClearedStages || stageId == Journey.NextStageId
                    || (Journey.ClearedStages > 0 && Art.Campaign.IsSideBattle(stageId)));
        }

        public async Task StartAsync(cfg.Tables configuration, CancellationToken token)
        {
            tables = configuration ?? throw new ArgumentNullException(nameof(configuration));
            lifetime = new CancellationTokenSource();
            using var opening = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var openingToken = opening.Token;
            try
            {
                Journey = new JourneyProgress(tables, new LocalJourneyStore());
                mapSettings = await TowerDefenseMapObjectives.LoadAsync(Stages, openingToken);
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
#if NOVORIZON_CURRENT_NAVIGATION_CHECK
                if (StartCurrentNavigationPlayerCheck())
                {
                    Debug.Log("PLAYER_PAGE_READY");
                    return;
                }
#endif
                server = GameServerConnection.FromEnvironment(hub.SetServerStatus);
                RefreshServer();
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

        public void RefreshServer()
        {
            _ = server?.RefreshAsync();
        }

        public ExpeditionPlan PrepareExpedition(int stageId = 0)
        {
            if (disposed || entering || battle != null || CampStoryInProgress)
            {
                return null;
            }

            if (!SaveBattleProgress())
            {
                hub.ShowError("上一场补给尚未保存，请稍后重试出征。");
                return null;
            }

            if (stageId == 0)
            {
                stageId = preparedExpedition?.Stage.Id ?? Stage.Id;
            }

            if (!CanSelectStage(stageId))
            {
                hub.Refresh();
                return null;
            }

            if (preparedExpedition?.Stage.Id != stageId)
            {
                uint seed = BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(), 0);
                preparedExpedition = new ExpeditionPlan(
                    tables,
                    stageId,
                    seed,
                    Journey.Camp.Level.Id,
                    Art.Campaign,
                    Journey.ExpeditionChoices(),
                    mapSettings[stageId]);
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
            if (disposed || entering || battle != null || !Journey.Camp.ExpeditionUnlocked || CampStoryInProgress)
            {
                return false;
            }

            var plan = preparedExpedition ?? PrepareExpedition();
            if (plan == null)
            {
                return false;
            }

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
                var assets = await LoadBattleAssetsAsync(plan, leases, lifetime.Token);
                battle = TowerDefensePrototypeView.Create(plan.Configuration, assets.Font, assets.Map, plan.Seed, leases, Art, assets.HudAtlas);
                battle.transform.SetParent(root.transform, false);
                battle.SetArt(assets.Map, Art.Atlas, assets.Font);
                if (plan.Stage.Id == 1 && Journey.HighestWave == 0)
                {
                    battle.EnableFirstBattleGuide();
                }

                battleCampLevel = plan.CampLevel.Id;
                battleStageId = plan.Stage.Id;
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

        private async Task<(Sprite Map, TMP_FontAsset Font, Texture2D HudAtlas)> LoadBattleAssetsAsync(
            ExpeditionPlan plan,
            List<IDisposable> leases,
            CancellationToken token)
        {
            var resources = ResourceManager.Instance;
            var map = await resources.AcquireAsync<Sprite>(plan.Stage.MapAsset, token, true);
            leases.Add(map);
            var battleFont = Art.Font;
            if (resources.IsValid(TowerDefensePrototypeView.HudFontAddress))
            {
                var font = await resources.AcquireAsync<TMP_FontAsset>(TowerDefensePrototypeView.HudFontAddress, token, true);
                leases.Add(font);
                battleFont = font.Asset;
            }

            Texture2D hudAtlas = null;
            if (resources.IsValid(TowerDefensePrototypeView.HudAssetAddress))
            {
                var hud = await resources.AcquireAsync<Texture2D>(TowerDefensePrototypeView.HudAssetAddress, token, true);
                leases.Add(hud);
                hudAtlas = hud.Asset;
            }

            token.ThrowIfCancellationRequested();
            return (map.Asset, battleFont, hudAtlas);
        }

        public void RetryArrival()
        {
            if (disposed || hub == null || Journey.Camp.ArrivalSeen || CampStoryInProgress)
            {
                return;
            }

            campStoryTask = ShowCampStoryAsync(false);
        }

        public void PrepareCamp()
        {
            if (disposed || hub == null || !Journey.Camp.ArrivalSeen || Journey.Camp.ExpeditionUnlocked || CampStoryInProgress)
            {
                return;
            }

            campStoryTask = ShowCampStoryAsync(true);
        }

        public void ContinueCampaign()
        {
            if (disposed || hub == null || entering || battle != null || !Journey.Camp.ExpeditionUnlocked || CampStoryInProgress)
            {
                return;
            }

            var scene = Art.Campaign.Scene(Journey);
            if (scene != null)
            {
                campStoryTask = ShowCampaignStoryAsync(scene, Journey.StoryStep);
            }
        }

        private async Task ShowCampaignStoryAsync(CampaignScene scene, CampaignStep step)
        {
            try
            {
                while (true)
                {
                    int choice = await hub.PlayCampaignStoryAsync(scene, lifetime.Token);
                    if (choice < 0)
                    {
                        break;
                    }

                    Journey.AdvanceCampaign(step, choice);
                    if (step != CampaignStep.ReturnBell)
                    {
                        break;
                    }

                    step = Journey.StoryStep;
                    scene = Art.Campaign.Scene(Journey);
                }
            }
            catch (OperationCanceledException) when (disposed)
            {
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                hub.ShowError("剧情进度未能保存，请重新确认。");
                Debug.LogWarning("Campaign save failed: " + error.Message);
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    hub.ShowError("剧情暂时无法继续，请重试。");
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

        private async Task ShowCampStoryAsync(bool outpost)
        {
            try
            {
                if (outpost && Journey.Camp.Level.Id == 1)
                {
                    Journey.Camp.Upgrade();
                }

                var playback = hub.PlayCampStoryAsync(outpost, lifetime.Token);
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
                    hub.ShowError(outpost ? "整备或哨站进度未能保存，请重试。" : "抵达进度未能保存，请点击“重试抵达序章”。");
                    Debug.LogWarning("Camp story save failed: " + error.Message);
                }
            }
            catch (Exception error)
            {
                if (!disposed)
                {
                    hub.ShowError(outpost ? "哨站消息未能完成，请重新查看。" : "序章未能完成，请点击“重试抵达序章”。");
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
                StageId = battleStageId,
                RelayHeld = completed.RelayHeld,
                Wave = completed.WaveIndex,
                Kills = completed.Kills,
                Towers = completed.TowersBuilt,
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
            else if (Journey.Camp.Level.Id > battleCampLevel)
            {
                hub.ShowError(Journey.Camp.Level.Id >= 4 ? "营地的小屋已经建好了。" : "种子和取水工具已带回，农田和取水点布置好了。");
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
            server?.Dispose();
            server = null;
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
