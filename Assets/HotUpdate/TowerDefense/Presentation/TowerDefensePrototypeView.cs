using System;
using System.Collections.Generic;
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
        private readonly Dictionary<int, GameObject> slots = new Dictionary<int, GameObject>();
        private readonly HashSet<int> liveEnemies = new HashSet<int>();
        private readonly List<int> removals = new List<int>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Sprite> generatedIcons = new List<Sprite>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private readonly List<Button> buildButtons = new List<Button>();
        private readonly Color ink = new Color(.95f, .93f, .82f);
        private readonly Color cream = new Color(.98f, .95f, .84f);
        private FightConfiguration configuration;
        private FightSession session;
        private uint seed;
        private Camera worldCamera;
        private readonly List<Mesh> generatedMeshes = new List<Mesh>();
        private readonly List<Image> skinnedPanels = new List<Image>();
        private readonly List<Image> atlasImages = new List<Image>();
        private readonly List<int> atlasIndices = new List<int>();
        private Sprite panelSprite;
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
        private readonly TMP_Text[] stats = new TMP_Text[4];
        private readonly TMP_Text[] buildTitles = new TMP_Text[3];
        private readonly TMP_Text[] buildCosts = new TMP_Text[3];
        private readonly TMP_Text[] cardRarities = new TMP_Text[3];
        private TMP_Text notice;
        private TMP_Text towerDetails;
        private TMP_Text nextWaveLabel;
        private TMP_Text pauseLabel;
        private Button nextWave;
        private Button upgrade;
        private GameObject cardModal;
        private GameObject cardResume;
        private GameObject resultModal;
        private TMP_Text resultText;
        private readonly TMP_Text[] cardNames = new TMP_Text[3];
        private readonly TMP_Text[] cardDescriptions = new TMP_Text[3];
        private readonly Image[] cardImages = new Image[3];
        private readonly Image[] cardFrames = new Image[3];
        private LineRenderer rangeRing;
        private int selectedDefinition;
        private bool buildMode;
        private bool completedReported;
        private bool pausedBeforeExit;
        private GameObject exitConfirm;
        public string RunId { get; private set; }
        public event Action<FightSession> Finished;
        public event Action ExitRequested;
        private int selectedTower;
        private float accumulator;
        private float uiTimer;
        private Rect lastSafeArea;
        private int lastWidth;
        private int lastHeight;
        private BattlePhase shownPhase;
        private bool initialized;
        private bool disposed;
        private bool destroying;

        public FightSession Session => session;
        public Camera WorldCamera => worldCamera;

        // Lease ownership transfers before any native UI is created, including failed initialization.
        public static TowerDefensePrototypeView Create(
            FightConfiguration config,
            TMP_FontAsset uiFont,
            Sprite map = null,
            uint randomSeed = 1,
            List<IDisposable> assetLeases = null)
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

                view.Initialize(config, uiFont, map, randomSeed);
            }
            catch
            {
                view.Dispose();
                throw;
            }

            return view;
        }

        public void Initialize(FightConfiguration config, TMP_FontAsset uiFont, Sprite map = null, uint randomSeed = 1)
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
            session = new FightSession(configuration, seed);
            RunId = Guid.NewGuid().ToString("N");
            completedReported = false;
            font = uiFont != null ? uiFont : throw new ArgumentNullException(nameof(uiFont), "Load the YooAsset TMP font before creating battle UI.");
            initialized = true;
            CreateWorld(map);
            CreateInterface();
            selectedDefinition = configuration.Towers[0].Id;
            SetNotice("选择下方塔卡，再点击绿色区域部署。点击防御塔查看射程。");
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
            HandlePointer();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                HandleEscape();
            }

            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                StartWave();
            }

            float elapsed = Mathf.Min(Time.unscaledDeltaTime, .25f);
            if (session.Phase == BattlePhase.Fighting && !session.IsPaused)
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
            var definition = FindTower(id);
            if (session.IsPaused || session.Gold < definition.BuildCost)
            {
                SetNotice("金币不足或战斗已暂停，暂时无法建造。");
                return;
            }

            buildMode = !buildMode || selectedDefinition != id;
            selectedDefinition = id;
            selectedTower = 0;
            SetNotice(buildMode ? "选择绿色建造区域部署" + definition.Name + "，按返回键可取消。" : "已取消建造。");
            RefreshInterface();
        }

        private void BuildAt(int slot)
        {
            if (!session.BuildTower(slot, selectedDefinition))
            {
                SetNotice("无法建造，请检查金币与位置。");
                return;
            }

            foreach (var tower in session.Towers)
            {
                if (tower.SlotId == slot)
                {
                    selectedTower = tower.Id;
                    break;
                }
            }

            buildMode = false;
            SetNotice("防御塔已建造，圆圈显示实际攻击范围。");
        }

        private void HandleEscape()
        {
            if (exitConfirm.activeSelf)
            {
                CancelExit();
            }
            else if (buildMode)
            {
                buildMode = false;
                SetNotice("已取消建造。");
                RefreshInterface();
            }
            else
            {
                TogglePause();
            }
        }

        private void RequestExit()
        {
            if (session.Phase == BattlePhase.Victory || session.Phase == BattlePhase.Defeat
                || (session.Phase == BattlePhase.Planning && session.Towers.Count == 0))
            {
                ExitRequested?.Invoke();
                return;
            }

            pausedBeforeExit = session.IsPaused;
            session.SetPaused(true);
            exitConfirm.SetActive(true);
        }

        private void CancelExit()
        {
            exitConfirm.SetActive(false);
            session.SetPaused(pausedBeforeExit);
            RefreshInterface();
        }

        private void UpgradeSelected()
        {
            SetNotice(session.UpgradeTower(selectedTower) ? "防御塔已升级。" : "暂时无法升级。");
            RefreshInterface();
        }

        private void StartWave()
        {
            SetNotice(session.StartNextWave() ? "敌人来袭，守住基地！" : "请先完成当前波次、选卡或解除暂停。");
            RefreshInterface();
        }

        private void ChooseCard(int index)
        {
            if (session.ChooseCard(index))
            {
                SetNotice("强化已生效。准备下一波吧。");
                RefreshInterface();
            }
        }

        private void TogglePause()
        {
            session.SetPaused(!session.IsPaused);
            RefreshInterface();
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
            session = new FightSession(configuration, seed);
            RunId = Guid.NewGuid().ToString("N");
            completedReported = false;
            selectedTower = 0;
            buildMode = false;
            accumulator = 0;
            rangeRing.enabled = false;
            SetNotice("已使用相同种子重新开始，试试不同的建造和选卡。");
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
            foreach (var material in materials)
            {
                Destroy(material);
            }

            materials.Clear();
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
        public int SlotId;
        public int TowerId;
    }
}
