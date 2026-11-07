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
    public sealed class TowerDefensePrototypeView : MonoBehaviour, IDisposable
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
        private readonly Color ink = new Color(.10f, .19f, .23f);
        private readonly Color cream = new Color(.98f, .95f, .84f);
        private FightConfiguration configuration;
        private FightSession session;
        private uint seed;
        private Camera worldCamera;
        private Camera previousCamera;
        private bool previousCameraEnabled;
        private GameObject eventSystemOwned;
        private RectTransform safeArea;
        private SpriteRenderer mapRenderer;
        private Transform field;
        private Material routeMaterial;
        private Material lineMaterial;
        private Material[] towerMaterials;
        private Material[] enemyMaterials;
        private TMP_FontAsset font;
        private TMP_Text hud;
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
        private int selectedSlot;
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
            font = uiFont != null ? uiFont : throw new ArgumentNullException(nameof(uiFont), "Load the YooAsset TMP font before creating battle UI.");
            initialized = true;
            CreateWorld(map);
            CreateInterface();
            selectedDefinition = configuration.Towers[0].Id;
            SetNotice("点击发光建造点 → 选择下方塔卡建造 → 开始波次。波间选择一张强化卡。");
            SynchronizeWorld(0);
            RefreshInterface();
        }

        public void SetArt(Sprite map, Texture2D cardAtlas, Sprite cardFrame, TMP_FontAsset uiFont)
        {
            if (disposed || !initialized)
            {
                return;
            }

            ApplyMap(map);
            foreach (var icon in generatedIcons)
            {
                Destroy(icon);
            }

            generatedIcons.Clear();
            if (cardAtlas != null)
            {
                int width = cardAtlas.width / 3;
                int height = cardAtlas.height / 2;
                int padding = Math.Min(width, height) / 20;
                for (int index = 0; index < 6; index++)
                {
                    int x = index % 3 * width;
                    int y = index < 3 ? height : 0;
                    generatedIcons.Add(Sprite.Create(
                        cardAtlas,
                        new Rect(x + padding, y + padding, width - padding * 2, height - padding * 2),
                        new Vector2(.5f, .5f),
                        100));
                }
            }

            if (uiFont != null)
            {
                font = uiFont;
                foreach (var label in GetComponentsInChildren<TMP_Text>(true))
                {
                    label.font = font;
                }
            }

            foreach (var frame in cardFrames)
            {
                frame.sprite = cardFrame;
                frame.type = Image.Type.Simple;
                frame.color = cardFrame != null ? Color.white : cream;
            }

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
                TogglePause();
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

        private void CreateWorld(Sprite map)
        {
            field = new GameObject("Battlefield").transform;
            field.SetParent(transform, false);
            previousCamera = Camera.main;
            if (previousCamera != null)
            {
                previousCameraEnabled = previousCamera.enabled;
                previousCamera.enabled = false;
            }

            var cameraRoot = new GameObject("PrototypeCamera");
            cameraRoot.transform.SetParent(transform, false);
            cameraRoot.tag = "MainCamera";
            worldCamera = cameraRoot.AddComponent<Camera>();
            worldCamera.orthographic = true;
            worldCamera.transform.position = new Vector3(0, 22, -16);
            worldCamera.transform.LookAt(new Vector3(0, 0, .25f));
            worldCamera.orthographicSize = 9;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = new Color(.11f, .20f, .22f);
            worldCamera.depth = 20;
            var sun = new GameObject("PrototypeSun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55, -35, 0);
            towerMaterials = new[] { MakeMaterial(new Color(.24f, .59f, .35f)), MakeMaterial(new Color(.27f, .68f, .83f)), MakeMaterial(new Color(.94f, .53f, .20f)) };
            enemyMaterials = new[] { MakeMaterial(new Color(.87f, .35f, .32f)), MakeMaterial(new Color(.65f, .40f, .76f)), MakeMaterial(new Color(.95f, .72f, .26f)) };
            routeMaterial = MakeMaterial(new Color(.96f, .79f, .45f), true);
            lineMaterial = MakeMaterial(new Color(.85f, 1, .75f), true);
            var ground = Primitive(
                "MapFallback",
                PrimitiveType.Cube,
                field,
                new Vector3(0, -.14f, 0),
                new Vector3(22, .2f, 12),
                MakeMaterial(new Color(.36f, .57f, .31f)));
            Destroy(ground.GetComponent<Collider>());
            mapRenderer = new GameObject("MapArtwork").AddComponent<SpriteRenderer>();
            mapRenderer.transform.SetParent(field, false);
            mapRenderer.transform.localPosition = new Vector3(0, -.025f, 0);
            mapRenderer.transform.localRotation = Quaternion.Euler(90, 0, 0);
            ApplyMap(map);
            foreach (var route in configuration.Routes)
            {
                var line = new GameObject("Route " + route.Id).AddComponent<LineRenderer>();
                line.transform.SetParent(field, false);
                // TransformZ uses local +Z as the ribbon normal; +90 X maps it to world -Y (the XZ plane).
                line.transform.localRotation = Quaternion.Euler(90, 0, 0);
                line.alignment = LineAlignment.TransformZ;
                line.useWorldSpace = true;
                line.sharedMaterial = routeMaterial;
                line.startWidth = .48f;
                line.endWidth = .48f;
                line.numCapVertices = 4;
                line.numCornerVertices = 8;
                line.sortingOrder = 2;
                line.positionCount = route.Points.Length;
                for (int index = 0; index < route.Points.Length; index++)
                {
                    line.SetPosition(index, World(route.Points[index], .03f));
                }
            }

            var basePoint = configuration.Routes[0].Points[configuration.Routes[0].Points.Length - 1];
            var baseRoot = new GameObject("Base - 3D placeholder").transform;
            baseRoot.SetParent(field, false);
            baseRoot.localPosition = World(basePoint, 0);
            Primitive("BaseBody", PrimitiveType.Cube, baseRoot, new Vector3(0, .7f, 0), new Vector3(1.4f, 1.4f, 1.2f), towerMaterials[0]);
            Primitive("BaseRoof", PrimitiveType.Sphere, baseRoot, new Vector3(0, 1.5f, 0), new Vector3(1.6f, .65f, 1.4f), towerMaterials[2]);
            foreach (var slot in configuration.BuildSlots)
            {
                var pad = Primitive(
                    "Build pad " + slot.Id,
                    PrimitiveType.Cylinder,
                    field,
                    World(slot.Position, .06f),
                    new Vector3(.94f, .07f, .94f),
                    lineMaterial);
                pad.AddComponent<PrototypeWorldTarget>().SlotId = slot.Id;
                slots.Add(slot.Id, pad);
            }

            rangeRing = new GameObject("SelectedTowerRange").AddComponent<LineRenderer>();
            rangeRing.transform.SetParent(field, false);
            rangeRing.transform.localRotation = Quaternion.Euler(90, 0, 0);
            rangeRing.alignment = LineAlignment.TransformZ;
            rangeRing.useWorldSpace = true;
            rangeRing.sharedMaterial = lineMaterial;
            rangeRing.loop = true;
            rangeRing.positionCount = 48;
            rangeRing.startWidth = .06f;
            rangeRing.endWidth = .06f;
            rangeRing.sortingOrder = 4;
            rangeRing.enabled = false;
        }

        private void ApplyMap(Sprite map)
        {
            mapRenderer.sprite = map;
            mapRenderer.enabled = map != null;
            if (map != null)
            {
                mapRenderer.transform.localScale = new Vector3(22 / map.bounds.size.x, 12 / map.bounds.size.y, 1);
            }
        }

        private void CreateInterface()
        {
            if (EventSystem.current == null)
            {
                eventSystemOwned = new GameObject("PrototypeEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                eventSystemOwned.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }

            var canvasRoot = new GameObject("PrototypeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasRoot.transform.SetParent(transform, false);
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            safeArea = Area("SafeArea", canvasRoot.transform, Vector2.zero, Vector2.one);
            var header = Panel("HUD", safeArea, new Vector2(.02f, .87f), new Vector2(.98f, .98f), new Color(.98f, .95f, .84f, .97f));
            hud = Label("HUDText", header, new Vector2(.025f, .15f), new Vector2(.78f, .85f), 32, TextAnchor.MiddleLeft);
            var pause = MakeButton("Pause", header, new Vector2(.83f, .15f), new Vector2(.98f, .85f), TogglePause);
            pauseLabel = pause.GetComponentInChildren<TMP_Text>();
            notice = Label("Help", safeArea, new Vector2(.04f, .78f), new Vector2(.96f, .86f), 25, TextAnchor.MiddleLeft);
            notice.color = Color.white;
            var bottom = Panel("BuildBar", safeArea, new Vector2(.02f, .025f), new Vector2(.98f, .22f), new Color(.98f, .95f, .84f, .98f));
            int count = Math.Min(configuration.Towers.Length, 3);
            for (int index = 0; index < count; index++)
            {
                int definitionId = configuration.Towers[index].Id;
                float left = .015f + index * .17f;
                var button = MakeButton("Build", bottom, new Vector2(left, .12f), new Vector2(left + .155f, .88f), () => SelectDefinition(definitionId));
                buildButtons.Add(button);
            }

            towerDetails = Label("SelectedTower", bottom, new Vector2(.53f, .53f), new Vector2(.77f, .94f), 23, TextAnchor.MiddleLeft);
            upgrade = MakeButton("升级", bottom, new Vector2(.53f, .1f), new Vector2(.77f, .52f), UpgradeSelected);
            nextWave = MakeButton("Start wave", bottom, new Vector2(.80f, .12f), new Vector2(.985f, .88f), StartWave);
            nextWaveLabel = nextWave.GetComponentInChildren<TMP_Text>();
            cardModal = Panel("ChooseOneCard", safeArea, Vector2.zero, Vector2.one, new Color(.025f, .06f, .08f, .92f)).gameObject;
            var title = Label("CardTitle", cardModal.transform, new Vector2(.1f, .82f), new Vector2(.9f, .94f), 46, TextAnchor.MiddleCenter);
            title.text = "三选一 · 选择本局强化";
            title.color = cream;
            cardResume = MakeButton("继续选卡", cardModal.transform, new Vector2(.78f, .84f), new Vector2(.97f, .96f), () =>
            {
                session.SetPaused(false);
                RefreshInterface();
            }).gameObject;
            for (int index = 0; index < 3; index++)
            {
                int offer = index;
                float left = .12f + index * .26f;
                var card = MakeButton("Card " + index, cardModal.transform, new Vector2(left, .15f), new Vector2(left + .24f, .78f), () => ChooseCard(offer));
                Destroy(card.GetComponentInChildren<TMP_Text>().gameObject);
                cardFrames[index] = card.GetComponent<Image>();
                cardNames[index] = Label("Name", card.transform, new Vector2(.08f, .78f), new Vector2(.92f, .94f), 31, TextAnchor.MiddleCenter);
                var icon = Area("Icon", card.transform, new Vector2(.28f, .55f), new Vector2(.72f, .83f)).gameObject.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.enabled = false;
                cardImages[index] = icon;
                cardDescriptions[index] = Label("Effect", card.transform, new Vector2(.1f, .13f), new Vector2(.90f, .41f), 25, TextAnchor.MiddleCenter);
                Label("Choose", card.transform, new Vector2(.14f, .06f), new Vector2(.86f, .16f), 30, TextAnchor.MiddleCenter).text = "选择强化";
            }

            resultModal = Panel("Result", safeArea, Vector2.zero, Vector2.one, new Color(.025f, .06f, .08f, .90f)).gameObject;
            resultText = Label("ResultText", resultModal.transform, new Vector2(.15f, .46f), new Vector2(.85f, .76f), 54, TextAnchor.MiddleCenter);
            resultText.color = cream;
            MakeButton("再试一次", resultModal.transform, new Vector2(.34f, .23f), new Vector2(.66f, .38f), Restart);
            cardModal.SetActive(false);
            resultModal.SetActive(false);
            UpdateSafeArea();
        }

        private void SynchronizeWorld(float elapsed)
        {
            liveEnemies.Clear();
            foreach (var state in session.Enemies)
            {
                liveEnemies.Add(state.Id);
                if (!enemies.TryGetValue(state.Id, out var view))
                {
                    var body = Primitive(
                        "Enemy " + state.Id + " - 3D placeholder",
                        PrimitiveType.Capsule,
                        field,
                        World(state.Position, .45f),
                        new Vector3(.48f, .48f, .48f),
                        enemyMaterials[Math.Abs(state.DefinitionId) % enemyMaterials.Length]);
                    Destroy(body.GetComponent<Collider>());
                    var bar = new GameObject("Health").AddComponent<LineRenderer>();
                    bar.transform.SetParent(body.transform, false);
                    bar.useWorldSpace = false;
                    bar.sharedMaterial = lineMaterial;
                    bar.startWidth = .12f;
                    bar.endWidth = .12f;
                    bar.positionCount = 2;
                    bar.sortingOrder = 6;
                    view = new EnemyView { Root = body, Health = bar };
                    enemies.Add(state.Id, view);
                }

                view.Root.transform.localPosition = World(state.Position, .45f);
                float health = state.MaxHealth == 0 ? 0 : Mathf.Clamp01((float)state.Health / state.MaxHealth);
                view.Health.SetPosition(0, new Vector3(-.5f, 1.45f, 0));
                view.Health.SetPosition(1, new Vector3(-.5f + health, 1.45f, 0));
            }

            rangeRing.enabled = false;
            if (selectedSlot != 0)
            {
                foreach (var slot in configuration.BuildSlots)
                {
                    if (slot.Id == selectedSlot)
                    {
                        DrawRange(slot.Position, .65f);
                        break;
                    }
                }
            }

            foreach (var state in session.Towers)
            {
                if (!towers.TryGetValue(state.Id, out var view))
                {
                    var root = new GameObject("Tower " + state.Id + " - 3D placeholder");
                    root.transform.SetParent(field, false);
                    root.transform.localPosition = World(state.Position, 0);
                    int color = 0;
                    for (int index = 0; index < configuration.Towers.Length; index++)
                    {
                        if (configuration.Towers[index].Id == state.DefinitionId)
                        {
                            color = index % towerMaterials.Length;
                        }
                    }

                    var body = Primitive(
                        "Body",
                        PrimitiveType.Cylinder,
                        root.transform,
                        new Vector3(0, .50f, 0),
                        new Vector3(.65f, .5f, .65f),
                        towerMaterials[color]);
                    body.AddComponent<PrototypeWorldTarget>().TowerId = state.Id;
                    var cap = Primitive(
                        "Turret",
                        PrimitiveType.Sphere,
                        root.transform,
                        new Vector3(0, 1.05f, 0),
                        new Vector3(.7f, .45f, .7f),
                        towerMaterials[color]);
                    cap.AddComponent<PrototypeWorldTarget>().TowerId = state.Id;
                    var beam = new GameObject("Shot").AddComponent<LineRenderer>();
                    beam.transform.SetParent(root.transform, false);
                    beam.sharedMaterial = lineMaterial;
                    beam.startWidth = .08f;
                    beam.endWidth = .03f;
                    beam.positionCount = 2;
                    beam.sortingOrder = 5;
                    beam.enabled = false;
                    view = new TowerView { Root = root, Beam = beam };
                    towers.Add(state.Id, view);
                }

                if (state.ShotSequence != view.ShotSequence)
                {
                    view.ShotSequence = state.ShotSequence;
                    if (enemies.TryGetValue(state.LastTargetId, out var target))
                    {
                        view.Beam.SetPosition(0, view.Root.transform.position + Vector3.up);
                        view.Beam.SetPosition(1, target.Root.transform.position);
                        view.BeamSeconds = .10f;
                    }
                }

                view.BeamSeconds = Mathf.Max(0, view.BeamSeconds - elapsed);
                view.Beam.enabled = view.BeamSeconds > 0;
                if (state.Id == selectedTower)
                {
                    DrawRange(state.Position, state.Range / 1000f);
                }
            }

            removals.Clear();
            foreach (var pair in enemies)
            {
                if (!liveEnemies.Contains(pair.Key))
                {
                    Destroy(pair.Value.Root);
                    removals.Add(pair.Key);
                }
            }

            foreach (int id in removals)
            {
                enemies.Remove(id);
            }
        }

        private void DrawRange(Position2 position, float radius)
        {
            rangeRing.enabled = true;
            for (int index = 0; index < rangeRing.positionCount; index++)
            {
                float angle = index * Mathf.PI * 2 / rangeRing.positionCount;
                rangeRing.SetPosition(index, World(position, .10f) + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
        }

        private void HandlePointer()
        {
            Vector2 position;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
            }
            else
            {
                return;
            }

            if (EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
                if (uiHits.Count != 0)
                {
                    return;
                }
            }

            if (Physics.Raycast(worldCamera.ScreenPointToRay(position), out var hit, 100))
            {
                var target = hit.collider.GetComponent<PrototypeWorldTarget>();
                if (target == null)
                {
                    return;
                }

                if (target.TowerId != 0)
                {
                    selectedTower = target.TowerId;
                    selectedSlot = 0;
                    SetNotice("已选择防御塔，圆圈显示实际攻击范围。可在下方升级。");
                }
                else
                {
                    bool occupied = false;
                    foreach (var tower in session.Towers)
                    {
                        if (tower.SlotId == target.SlotId)
                        {
                            selectedTower = tower.Id;
                            occupied = true;
                            break;
                        }
                    }

                    if (!occupied)
                    {
                        selectedSlot = target.SlotId;
                        selectedTower = 0;
                        SetNotice("已选中建造点。点击下方一种塔卡，花费金币建造。");
                    }
                }

                RefreshInterface();
            }
        }

        private void RefreshInterface()
        {
            shownPhase = session.Phase;
            int cardCount = 0;
            foreach (int count in session.ChosenCardStacks.Values)
            {
                cardCount += count;
            }

            hud.text = "基地 " + session.BaseHp + " / " + configuration.BaseHealth + "     金币 " + session.Gold
                + "     波次 " + session.WaveIndex + " / " + configuration.Waves.Length + "     强化 " + cardCount;
            pauseLabel.text = session.IsPaused ? "继续" : "暂停";
            for (int index = 0; index < buildButtons.Count; index++)
            {
                var definition = configuration.Towers[index];
                buildButtons[index].GetComponentInChildren<TMP_Text>().text = definition.Name + "\n" + definition.BuildCost + " 金币";
                buildButtons[index].GetComponent<Image>().color = selectedDefinition == definition.Id ? new Color(.80f, .91f, .63f) : cream;
                buildButtons[index].interactable = !session.IsPaused && session.Gold >= definition.BuildCost
                    && (session.Phase == BattlePhase.Planning || session.Phase == BattlePhase.Fighting);
            }

            towerDetails.text = selectedSlot == 0 ? "点击已有防御塔查看和升级" : "建造点 " + selectedSlot + "：请选择塔卡";
            upgrade.interactable = false;
            foreach (var tower in session.Towers)
            {
                if (tower.Id != selectedTower)
                {
                    continue;
                }

                var definition = FindTower(tower.DefinitionId);
                int cost = definition.UpgradeCost * tower.Level;
                towerDetails.text = definition.Name + "  等级 " + tower.Level + "  伤害 " + tower.Damage;
                upgrade.GetComponentInChildren<TMP_Text>().text = tower.Level >= definition.MaxLevel ? "已满级" : "升级  " + cost + " 金币";
                upgrade.interactable = !session.IsPaused && tower.Level < definition.MaxLevel && session.Gold >= cost
                    && (session.Phase == BattlePhase.Planning || session.Phase == BattlePhase.Fighting);
                break;
            }

            nextWave.interactable = session.Phase == BattlePhase.Planning && !session.IsPaused;
            nextWaveLabel.text = session.Phase == BattlePhase.Fighting ? "战斗中" : "开始波次";
            cardModal.SetActive(session.Phase == BattlePhase.ChoosingCard);
            cardResume.SetActive(session.IsPaused);
            for (int index = 0; index < cardNames.Length; index++)
            {
                bool available = index < session.CurrentCardOffers.Count;
                cardFrames[index].gameObject.SetActive(available);
                if (!available)
                {
                    continue;
                }

                var card = session.CurrentCardOffers[index];
                cardNames[index].text = card.Name;
                cardDescriptions[index].text = card.Description;
                int icon = (int)card.Effect - 1;
                cardImages[index].enabled = icon >= 0 && icon < generatedIcons.Count;
                cardImages[index].sprite = cardImages[index].enabled ? generatedIcons[icon] : null;
            }

            bool finished = session.Phase == BattlePhase.Victory || session.Phase == BattlePhase.Defeat;
            resultModal.SetActive(finished);
            resultText.text = session.Phase == BattlePhase.Victory ? "守护成功！\n本次防守胜利" : "基地失守\n试试新的塔与卡牌组合";
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
            selectedDefinition = id;
            selectedTower = 0;
            if (selectedSlot != 0)
            {
                bool built = session.BuildTower(selectedSlot, selectedDefinition);
                if (built)
                {
                    foreach (var tower in session.Towers)
                    {
                        if (tower.SlotId == selectedSlot)
                        {
                            selectedTower = tower.Id;
                            break;
                        }
                    }

                    selectedSlot = 0;
                }

                SetNotice(built ? "防御塔已建造。" : "无法建造：请检查金币、暂停状态和建造点。");
            }
            else
            {
                SetNotice("已选择 " + FindTower(id).Name + "。先点击一个空建造点，再点击塔卡确认建造。");
            }

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
            selectedTower = 0;
            selectedSlot = 0;
            accumulator = 0;
            rangeRing.enabled = false;
            SetNotice("已使用相同种子重新开始，试试不同的建造和选卡。");
            RefreshInterface();
        }

        private void SetNotice(string message)
        {
            notice.text = message;
        }

        private void UpdateSafeArea()
        {
            if (lastWidth == Screen.width && lastHeight == Screen.height && lastSafeArea == Screen.safeArea)
            {
                return;
            }

            lastWidth = Screen.width;
            lastHeight = Screen.height;
            lastSafeArea = Screen.safeArea;
            if (Screen.width == 0 || Screen.height == 0)
            {
                return;
            }

            var usable = lastSafeArea.width > 0 && lastSafeArea.height > 0
                ? lastSafeArea : new Rect(0, 0, Screen.width, Screen.height);
            safeArea.anchorMin = new Vector2(Mathf.Clamp01(usable.xMin / Screen.width), Mathf.Clamp01(usable.yMin / Screen.height));
            safeArea.anchorMax = new Vector2(Mathf.Clamp01(usable.xMax / Screen.width), Mathf.Clamp01(usable.yMax / Screen.height));
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
            float usableAspect = Mathf.Max(.1f, usable.width / usable.height);
            worldCamera.orthographicSize = Mathf.Max(9, 12.5f / usableAspect);
        }

        private static Vector3 World(Position2 point, float height)
        {
            return new Vector3(point.X / 1000f, height, point.Y / 1000f);
        }

        private Material MakeMaterial(Color color, bool unlit = false)
        {
            var material = new Material(Shader.Find(unlit ? "Sprites/Default" : "Standard"));
            material.color = color;
            materials.Add(material);
            return material;
        }

        private static GameObject Primitive(string label, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var value = GameObject.CreatePrimitive(type);
            value.name = label;
            value.transform.SetParent(parent, false);
            value.transform.localPosition = position;
            value.transform.localScale = scale;
            value.GetComponent<Renderer>().sharedMaterial = material;
            return value;
        }

        private static RectTransform Area(string label, Transform parent, Vector2 minimum, Vector2 maximum)
        {
            var value = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            value.SetParent(parent, false);
            value.anchorMin = minimum;
            value.anchorMax = maximum;
            value.offsetMin = Vector2.zero;
            value.offsetMax = Vector2.zero;
            return value;
        }

        private RectTransform Panel(string label, Transform parent, Vector2 minimum, Vector2 maximum, Color color)
        {
            var value = Area(label, parent, minimum, maximum);
            value.gameObject.AddComponent<Image>().color = color;
            return value;
        }

        private TMP_Text Label(string label, Transform parent, Vector2 minimum, Vector2 maximum, int size, TextAnchor alignment)
        {
            var root = Area(label, parent, minimum, maximum).gameObject;
            root.SetActive(false);
            var text = root.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = Math.Min(18, size);
            text.fontSizeMax = size;
            text.alignment = alignment == TextAnchor.MiddleLeft ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            text.enableWordWrapping = true;
            text.color = ink;
            text.raycastTarget = false;
            root.SetActive(true);
            return text;
        }

        private Button MakeButton(string label, Transform parent, Vector2 minimum, Vector2 maximum, UnityEngine.Events.UnityAction callback)
        {
            var panel = Panel(label, parent, minimum, maximum, cream);
            var button = panel.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(.90f, 1, .79f);
            colors.pressedColor = new Color(.72f, .87f, .60f);
            colors.disabledColor = new Color(.62f, .65f, .59f);
            button.colors = colors;
            button.onClick.AddListener(callback);
            Label("Label", panel, new Vector2(.04f, .05f), new Vector2(.96f, .95f), 28, TextAnchor.MiddleCenter).text = label;
            return button;
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
