using System;
using System.Collections.Generic;
using Game.HotUpdate.Client;
using Game.HotUpdate.TowerDefense.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

namespace Game.HotUpdate.TowerDefense.Presentation
{
    /// <summary>Disposable prototype view. The fight owns all gameplay state; this component owns Unity objects only.</summary>
    public sealed partial class TowerDefensePrototypeView : MonoBehaviour, IDisposable
    {
        private sealed class EnemyView
        {
            internal GameObject Root;
            internal LineRenderer Health;
        }

        private sealed class TowerView
        {
            internal GameObject Root;
            internal LineRenderer Beam;
            internal Transform Turret;
            internal int ShotSequence;
            internal float BeamSeconds;
        }

        private readonly Dictionary<int, EnemyView> enemies = new Dictionary<int, EnemyView>();
        private readonly Dictionary<int, TowerView> towers = new Dictionary<int, TowerView>();
        private readonly HashSet<int> liveEnemies = new HashSet<int>();
        private readonly HashSet<int> liveTowers = new HashSet<int>();
        private readonly List<int> removals = new List<int>();
        private readonly List<UnityEngine.Object> materialAssets = new List<UnityEngine.Object>();
        private readonly List<Sprite> generatedIcons = new List<Sprite>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly List<Button> buildButtons = new List<Button>();
        private readonly Color ink = new Color(.94f, .975f, 1f);
        private FightConfiguration configuration;
        private FightSession session;
        private BattleBriefing briefing;
        private readonly BattlePauseState pauseState = new BattlePauseState();
        private uint seed;
        private Camera worldCamera;
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Image> atlasImages = new List<Image>();
        private readonly List<int> atlasIndices = new List<int>();
        private Camera previousCamera;
        private bool previousCameraEnabled;
        private GameObject eventSystemOwned;
        private RectTransform safeArea;
        private SpriteRenderer mapRenderer;
        private Transform field;
        private Material lineMaterial;
        private Material[] towerMaterials;
        private Material[] enemyMaterials;
        private TMP_FontAsset font;
        private TMP_Text objectiveText;
        private TMP_Text goldText;
        private TMP_Text buffsText;
        private Image objectiveFill;
        private Image draftProgress;
        private readonly TMP_Text[] cardRarities = new TMP_Text[3];
        private TMP_Text notice;
        private GameObject towerInspector;
        private Image towerPortrait;
        private TMP_Text towerName;
        private TMP_Text towerLevel;
        private TMP_Text pauseLabel;
        private Button startBattle;
        private Button repairBase;
        private Button upgrade;
        private TMP_Text upgradePrice;
        private TMP_Text upgradeStatus;
        private Button sell;
        private TMP_Text sellRefund;
        private GameObject cardModal;
        private GameObject resultModal;
        private TMP_Text resultText;
        private TMP_Text waveStatus;
        private TMP_Text draftScoreText;
        private TMP_Text reinforcementText;
        private TMP_Text killTallyText;
        private TMP_Text campaignCue;
        private RectTransform[] entranceMarkers;
        private RectTransform goalMarker;
        private RectTransform relayMarker;
        private TMP_Text relayCaption;
        private GameObject relayLamp;
        private TMP_Text[] buildPrices;
        private readonly TMP_Text[] cardNames = new TMP_Text[3];
        private readonly TMP_Text[] cardDescriptions = new TMP_Text[3];
        private readonly Image[] cardImages = new Image[3];
        private readonly Image[] cardFrames = new Image[3];
        private LineRenderer rangeRing;
        private int selectedDefinition;
        private bool buildMode;
        private bool completedReported;
        private GameObject exitConfirm;
        private GameObject systemMenu;
        private GameObject restartConfirm;
        private bool skipSimulationFrame;
        public string RunId { get; private set; }
        public event Action<FightSession> Finished;
        public event Action ExitRequested;
        private int selectedTower;
        private bool selectedBase;
        private float accumulator;
        private float uiTimer;
        private Rect lastSafeArea;
        private Rect battlefieldPixels;
        private int lastWidth;
        private int lastHeight;
        private BattlePhase shownPhase;
        private bool initialized;
        private bool disposed;
        private bool destroying;
        private const float OpeningCountdownSeconds = 10f;
        private float openingSecondsRemaining = OpeningCountdownSeconds;
        private const string FirstBattleHint = "推荐橡木箭塔。路口附近的草地能覆盖更多路段。";
        private bool firstBattleGuide;
        private bool PreparingBattle => session.Phase == BattlePhase.Planning && session.WaveIndex == 0;
        private bool GuidingOpening => firstBattleGuide && PreparingBattle;

        public FightSession Session => session;
        public Camera WorldCamera => worldCamera;

        internal void EnableFirstBattleGuide()
        {
            firstBattleGuide = true;
            SetNotice(FirstBattleHint);
            SynchronizeWorld(0);
            RefreshInterface();
        }

        // Lease ownership transfers before any native UI is created, including failed initialization.
        public static TowerDefensePrototypeView Create(
            FightConfiguration config,
            TMP_FontAsset uiFont,
            Sprite map = null,
            uint randomSeed = 1,
            List<IDisposable> assetLeases = null,
            ClientArt art = null,
            Texture2D hudAtlas = null)
        {
            if (uiFont == null)
            {
                throw new ArgumentNullException(nameof(uiFont), "Load the YooAsset TMP font before creating battle UI.");
            }

            if (config == null || config.Towers == null || config.Towers.Length == 0)
            {
                throw new ArgumentException("The prototype requires at least one tower definition.", nameof(config));
            }

            var root = new GameObject("TowerDefensePrototype");
            var view = root.AddComponent<TowerDefensePrototypeView>();
            try
            {
                if (assetLeases != null)
                {
                    root.AddComponent<PrototypeAssetLifetime>().Own(assetLeases);
                }

                view.Initialize(config, uiFont, map, randomSeed, art, hudAtlas);
            }
            catch
            {
                view.Dispose();
                throw;
            }

            return view;
        }

        public void Initialize(
            FightConfiguration config,
            TMP_FontAsset uiFont,
            Sprite map = null,
            uint randomSeed = 1,
            ClientArt art = null,
            Texture2D hudAtlas = null)
        {
            if (initialized || disposed)
            {
                throw new InvalidOperationException("Create one prototype view per fight.");
            }

            configuration = config ?? throw new ArgumentNullException(nameof(config));
            if (configuration.Towers == null || configuration.Towers.Length == 0)
            {
                throw new ArgumentException("The prototype requires at least one tower definition.", nameof(config));
            }

            seed = randomSeed;
            session = new FightSession(configuration, seed, AssaultRules.Default);
            briefing = new BattleBriefing(configuration);
            session.SetPaused(pauseState.IsPaused);
            skipSimulationFrame = true;
            RunId = Guid.NewGuid().ToString("N");
            completedReported = false;
            font = uiFont != null ? uiFont : throw new ArgumentNullException(nameof(uiFont), "Load the YooAsset TMP font before creating battle UI.");
            initialized = true;
            CreateWorld(map, art);
            CreateInterface(hudAtlas);
            selectedDefinition = configuration.Towers[0].Id;
            SetNotice("选择右侧塔卡，在地图上预览建塔。");
            SynchronizeWorld(0);
            RefreshInterface();
        }

        private void Update()
        {
            if (!initialized || disposed)
            {
                return;
            }

            UpdateSafeArea();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleEscape();
            }
            else
            {
                HandlePointer();
            }

            if (PreparingBattle && !mapDetails.activeSelf && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                StartBattle();
            }

            float frameTime = Time.unscaledDeltaTime;
            float elapsed = Mathf.Min(frameTime, .25f);
            bool advanceTime = !skipSimulationFrame && !session.IsPaused;
            skipSimulationFrame = false;
            if (advanceTime && PreparingBattle)
            {
                openingSecondsRemaining = Mathf.Max(0, openingSecondsRemaining - frameTime);
                if (openingSecondsRemaining == 0)
                {
                    StartBattle();
                }
            }
            else if (advanceTime && session.Phase == BattlePhase.Fighting)
            {
                accumulator += elapsed;
                const float tickSeconds = FightSession.TickMilliseconds / 1000f;
                int steps = 0;
                while (accumulator >= tickSeconds && steps < 5)
                {
                    session.Step();
                    accumulator -= tickSeconds;
                    steps++;
                    if (session.Phase != BattlePhase.Fighting)
                    {
                        accumulator = 0;
                        break;
                    }
                }
            }
            else
            {
                accumulator = 0;
            }

            UpdatePlacementPreview();
            if (buildGoldToast.activeSelf && Time.unscaledTime >= buildGoldToastUntil)
            {
                buildGoldToast.SetActive(false);
            }

            SynchronizeWorld(elapsed);
            uiTimer -= elapsed;
            if (uiTimer <= 0 || shownPhase != session.Phase)
            {
                RefreshInterface();
                uiTimer = .1f;
            }
        }

        private TowerDefinition FindTower(int id)
        {
            foreach (var definition in configuration.Towers)
            {
                if (definition.Id == id)
                {
                    return definition;
                }
            }

            throw new InvalidOperationException("Missing tower definition " + id);
        }

        private void SelectDefinition(int id)
        {
            if (buildMode && selectedDefinition == id)
            {
                CancelPlacement();
                return;
            }

            var definition = FindTower(id);
            if (session.IsPaused)
            {
                return;
            }

            if (session.Gold < session.BuildCost(definition))
            {
                ShowBuildGoldToast();
                return;
            }

            buildGoldToast.SetActive(false);
            buildMode = true;
            selectedDefinition = id;
            selectedTower = 0;
            selectedBase = false;
            useQuickActions = false;
            SetNotice(Mouse.current == null ? "拖动预览，模型为原色时松手建塔。" : "红色不可建造 · 右键取消");
            RefreshInterface();
        }

        private void CancelPlacement()
        {
            EndPlacement();
            SetNotice("已取消建造。");
            RefreshInterface();
        }

        private void EndPlacement()
        {
            buildMode = false;
            trackingPlacementTouch = false;
            placementGhost?.SetActive(false);
            buildGoldToast.SetActive(false);
        }

        private void BuildAt(Vector2 pointer)
        {
            var definition = FindTower(selectedDefinition);
            if (session.IsPaused || (session.Phase != BattlePhase.Planning && session.Phase != BattlePhase.Fighting))
            {
                return;
            }

            if (session.Gold < session.BuildCost(definition))
            {
                ShowBuildGoldToast();
                return;
            }

            if (!ResolvePlacement(pointer, out _, out int slot) || slot == 0 || !session.BuildTower(slot, selectedDefinition))
            {
                SetNotice("这里不能建塔。");
                return;
            }

            buildGoldToast.SetActive(false);
            SetNotice(session.Gold < session.BuildCost(definition) ? "已建造 · 金币不足" : "已建造 · 可继续放置");
            RefreshInterface();
        }

        private void HandleEscape()
        {
            if (exitConfirm.activeSelf)
            {
                CancelExit();
            }
            else if (restartConfirm.activeSelf)
            {
                restartConfirm.SetActive(false);
            }
            else if (systemMenu.activeSelf)
            {
                ToggleSystemMenu();
            }
            else if (mapDetails.activeSelf)
            {
                CloseMapDetails();
            }
            else if (buildMode)
            {
                CancelPlacement();
            }
            else if (useQuickActions || selectedBase)
            {
                CloseSelection();
            }
            else
            {
                ToggleSystemMenu();
            }
        }

        private void RequestExit()
        {
            if (pauseState.ReturnPending)
            {
                return;
            }

            if (session.Phase == BattlePhase.Victory || session.Phase == BattlePhase.Defeat
                || (session.Phase == BattlePhase.Planning && session.Towers.Count == 0))
            {
                ExitRequested?.Invoke();
                return;
            }

            pauseState.BeginReturn();
            ApplyPause();
            exitConfirm.SetActive(true);
        }

        private void CancelExit()
        {
            exitConfirm.SetActive(false);
            pauseState.CancelReturn();
            ApplyPause();
        }

        private void UpgradeSelected()
        {
            SetNotice(session.UpgradeTower(selectedTower) ? "防御塔已升级。" : "暂时无法升级。");
            RefreshInterface();
        }

        private void SellSelected()
        {
            int previousGold = session.Gold;
            if (session.SellTower(selectedTower))
            {
                selectedTower = 0;
                useQuickActions = false;
                SetNotice("已出售，返还 " + (session.Gold - previousGold) + " 金币。");
                SynchronizeWorld(0);
            }

            RefreshInterface();
        }

        private void SelectBase()
        {
            EndPlacement();
            selectedTower = 0;
            useQuickActions = false;
            selectedBase = true;
            SetNotice("");
            RefreshInterface();
        }

        private void CloseSelection()
        {
            selectedTower = 0;
            selectedBase = false;
            useQuickActions = false;
            RefreshInterface();
        }

        private void UpgradeBase()
        {
            if (session.UpgradeBase())
            {
                SetNotice("基地已加固至 Lv." + session.BaseLevel);
            }

            RefreshInterface();
        }

        private void StartBattle()
        {
            if (session.StartNextWave())
            {
                SetNotice("");
            }

            RefreshInterface();
        }

        private void RepairBase()
        {
            int previousHealth = session.BaseHp;
            if (session.RepairBase())
            {
                SetNotice("修复了 " + (session.BaseHp - previousHealth) + " 点耐久。");
                RefreshInterface();
            }
        }

        private void ChooseCard(int index)
        {
            if (session.ChooseCard(index))
            {
                interpolationHoldTick = session.Tick;
                SetNotice("强化已生效。");
                RefreshInterface();
            }
        }

        private void TogglePause()
        {
            if (!pauseState.ReturnPending)
            {
                pauseState.SetManualPaused(!pauseState.ManualPaused);
                ApplyPause();
            }
        }

        private void ToggleSystemMenu()
        {
            if (pauseState.ReturnPending || restartConfirm.activeSelf)
            {
                return;
            }

            if (audioSettingsPanel.activeSelf)
            {
                CloseAudioSettings();
                return;
            }

            systemMenu.SetActive(!systemMenu.activeSelf);
            ApplyPause();
        }

        private void ApplyPause()
        {
            accumulator = 0;
            if (initialized && !disposed)
            {
                interpolationHoldTick = session.Tick;
                bool paused = pauseState.IsPaused || systemMenu.activeSelf || mapDetails.activeSelf;
                skipSimulationFrame |= session.IsPaused != paused;
                session.SetPaused(paused);
                RefreshInterface();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            pauseState.SetApplicationPaused(paused);
            skipSimulationFrame = true;
            ApplyPause();
        }

        private void OnApplicationFocus(bool focused)
        {
            pauseState.SetApplicationFocused(focused);
            skipSimulationFrame = true;
            ApplyPause();
        }

        private void Restart()
        {
            foreach (var view in enemies.Values)
            {
                Destroy(view.Root);
            }

            foreach (var view in towers.Values)
            {
                Destroy(view.Root);
            }

            enemies.Clear();
            towers.Clear();
            session = new FightSession(configuration, seed, AssaultRules.Default);
            pauseState.ResetForNewRun();
            session.SetPaused(pauseState.IsPaused);
            systemMenu.SetActive(false);
            mapDetails.SetActive(false);
            restartConfirm.SetActive(false);
            exitConfirm.SetActive(false);
            RunId = Guid.NewGuid().ToString("N");
            completedReported = false;
            selectedTower = 0;
            selectedBase = false;
            useQuickActions = false;
            EndPlacement();
            openingSecondsRemaining = OpeningCountdownSeconds;
            skipSimulationFrame = true;
            accumulator = 0;
            rangeRing.enabled = false;
            SetNotice(GuidingOpening ? FirstBattleHint : string.Empty);
            RefreshInterface();
        }

        private void SetNotice(string message)
        {
            notice.text = message;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            SaveAudioSettings();
            session?.SetPaused(true);
            if (!destroying)
            {
                gameObject.SetActive(false);
            }

            if (previousCamera != null)
            {
                previousCamera.enabled = previousCameraEnabled;
            }

            if (eventSystemOwned != null)
            {
                Destroy(eventSystemOwned);
            }

            foreach (var mesh in generatedMeshes)
            {
                Destroy(mesh);
            }

            generatedMeshes.Clear();
            meshCache.Clear();
            foreach (var asset in materialAssets)
            {
                Destroy(asset);
            }

            materialAssets.Clear();
            foreach (var icon in generatedIcons)
            {
                Destroy(icon);
            }

            generatedIcons.Clear();
            if (!destroying)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            destroying = true;
            Dispose();
        }
    }

    public sealed class PrototypeWorldTarget : MonoBehaviour
    {
        public int TowerId;
    }
}
