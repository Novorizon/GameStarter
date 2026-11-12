using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Framework.Audio;
using Game.HotUpdate.Camp;
using Game.HotUpdate.Client;
using Game.HotUpdate.TowerDefense.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Game.HotUpdate.TowerDefense.Presentation
{
    public sealed partial class TowerDefensePrototypeView
    {
        private const float BattlefieldLeft = .012f;
        private const float BattlefieldRight = .771f;
        private const float BattlefieldBottom = .052f;
        private const float BattlefieldTop = .874f;
        private static readonly Vector2 MapDetailsSize = new Vector2(960, 506);
        private static readonly Vector2 SystemMenuSize = new Vector2(610, 658);
        private static readonly Vector2 AudioSettingsSize = new Vector2(610, 444);

        private static readonly string[] RarityNames = { "普通  ·  战地祝福", "优秀  ·  战地祝福", "稀有  ·  战地祝福", "史诗  ·  战地祝福", "传说  ·  战地祝福" };
        private static readonly Color[] RarityColors =
        {
            new Color(.91f, .89f, .85f), new Color(.36f, .88f, .32f), new Color(.28f, .65f, 1),
            new Color(.75f, .38f, .98f), new Color(1, .59f, .16f)
        };
        private readonly Color gold = new Color(1f, .82f, .43f);
        private readonly Color muted = new Color(.72f, .84f, .89f);
        private readonly Color hud = new Color(.075f, .20f, .27f, .97f);
        private readonly Color surface = new Color(.15f, .31f, .38f);
        private readonly Color action = new Color(.17f, .47f, .35f);
        private readonly Color divider = new Color(.43f, .66f, .74f, .53f);
        private readonly Color danger = new Color(1f, .65f, .65f);
        private readonly Color healthColor = new Color(.52f, .94f, .72f);
        private readonly List<(AudioGroup Group, Slider Control, TMP_Text Percent)> volumeSliders
            = new List<(AudioGroup Group, Slider Control, TMP_Text Percent)>();
        private GameObject mapDetails;
        private RectTransform mapDetailsPanel;
        private Button mapDetailsClose;
        private GameObject systemMenuHome;
        private GameObject audioSettingsPanel;
        private GameObject buildGoldToast;
        private float buildGoldToastUntil;
        private bool volumeChanged;
        private Sprite hudShape;

        public void SetArt(Sprite map, Texture2D cardAtlas, TMP_FontAsset uiFont)
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
                int height = cardAtlas.height / 3;
                for (int index = 0; index < 9; index++)
                {
                    int x = index % 3 * width;
                    int y = (2 - index / 3) * height;
                    generatedIcons.Add(Sprite.Create(cardAtlas, new Rect(x, y, width, height), new Vector2(.5f, .5f), 100));
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

            for (int index = 0; index < atlasImages.Count; index++)
            {
                int cell = atlasIndices[index];
                atlasImages[index].sprite = cell < generatedIcons.Count ? generatedIcons[cell] : null;
                atlasImages[index].enabled = atlasImages[index].sprite != null;
            }

            RefreshInterface();
        }

        private void CreateInterface(Texture2D hudAtlas)
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
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 200;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = HudReferenceResolution;
            scaler.matchWidthOrHeight = .5f;
            safeArea = Area("SafeArea", canvasRoot.transform, Vector2.zero, Vector2.one);
            PrepareHudSprites(hudAtlas);
            if (hudSprites[(int)HudPiece.Panel] == null)
            {
                hudShape = CreateHudShape();
            }

            CreateBattleStatus();
            CreateSystemControls();
            CreateBuildSidebar();
            CreateTowerInspector();
            entranceMarkers = configuration.Routes.Select(route => CreateRouteMarker(route.EntranceName, new Color(.44f, .24f, .17f, .95f))).ToArray();
            CreateObjectiveControls();
            if (configuration.RelayRouteId != 0)
            {
                relayMarker = CreateRouteMarker("副站 · 点亮", new Color(.20f, .39f, .34f, .96f));
                relayMarker.sizeDelta = new Vector2(138, 33);
                relayCaption = relayMarker.GetComponentInChildren<TMP_Text>();
            }

            var dispatch = HudPlate("CampaignDispatch", safeArea, .529f, .883f, .769f, .986f, HudPiece.Inset);
            var dispatchTitle = Label("DispatchCaption", dispatch, new Vector2(.052f, .65f), new Vector2(.95f, .92f), 17, TextAnchor.MiddleLeft);
            dispatchTitle.text = "战地消息";
            dispatchTitle.color = gold;
            campaignCue = Label("CampaignCue", dispatch, new Vector2(.052f, .08f), new Vector2(.95f, .65f), 21, TextAnchor.MiddleLeft);
            CreateTowerQuickActions();
            CreateBuildGoldToast();
            CreateMapDetailsInterface();
            CreateDraftInterface();
            CreateResultInterface();
            CreateSystemInterface();
            CreateExitInterface();
            UpdateSafeArea();
        }

        private void CreateMapDetailsInterface()
        {
            mapDetails = Panel("MapDetails", safeArea, Vector2.zero, Vector2.one, new Color(.025f, .047f, .053f, .65f)).gameObject;
            var panel = HudPlate("MapDetailsPanel", mapDetails.transform, .5f, .5f, .5f, .5f, HudPiece.Panel);
            mapDetailsPanel = panel;
            panel.sizeDelta = MapDetailsSize;
            Glyph(panel, HudPiece.Wave, .057f, .815f, .125f, .952f, cyan);
            var title = Label("MapName", panel, new Vector2(.146f, .805f), new Vector2(.940f, .960f), 40, TextAnchor.MiddleLeft);
            title.text = configuration.StageName;
            title.fontStyle = FontStyles.Bold;
            Separator(panel, .057f, .781f, .943f, .786f);

            bool hasPreview = mapRenderer.sprite != null;
            float left = hasPreview ? .531f : .057f;
            if (hasPreview)
            {
                var frame = HudPlate("MapPreviewFrame", panel, .057f, .159f, .485f, .747f, HudPiece.Inset);
                var preview = Area("MapPreview", frame, new Vector2(.045f, .050f), new Vector2(.955f, .950f)).gameObject.AddComponent<Image>();
                preview.sprite = mapRenderer.sprite;
                preview.preserveAspect = true;
                preview.raycastTarget = false;
            }

            var rule = configuration.Objective;
            int routeCount = briefing.Waves.SelectMany(wave => wave.RouteIds).Distinct().Count();
            string routes = configuration.HidePlannedRoutes ? "路线随战地情报揭示" : $"{routeCount} 条进攻路线";
            var overview = Label("Overview", panel, new Vector2(left, .657f), new Vector2(.943f, .759f), 22, TextAnchor.MiddleLeft);
            overview.text = $"敌军 {briefing.EnemyCount} 名 · {routes}";
            overview.color = muted;

            var victory = Label("Victory", panel, new Vector2(left, .537f), new Vector2(.943f, .632f), 24, TextAnchor.MiddleLeft);
            victory.text = "胜利条件";
            victory.color = healthColor;
            victory.fontStyle = FontStyles.Bold;
            Label("VictoryCondition", panel, new Vector2(left, .410f), new Vector2(.943f, .533f), 22, TextAnchor.MiddleLeft).text = rule.Type switch
            {
                BattleObjectiveType.DefendBase => $"守住{rule.TargetName}，击杀全部 {briefing.EnemyCount} 名敌军。",
                BattleObjectiveType.InterceptEscapes => $"进攻结束时，逃入{rule.TargetName}的敌军少于 {rule.EscapesToDefeat} 名。",
                _ => $"挺过全部进攻，守住{rule.TargetName}。"
            };

            var defeat = Label("Defeat", panel, new Vector2(left, .291f), new Vector2(.943f, .386f), 24, TextAnchor.MiddleLeft);
            defeat.text = "失败条件";
            defeat.color = danger;
            defeat.fontStyle = FontStyles.Bold;
            Label("DefeatCondition", panel, new Vector2(left, .164f), new Vector2(.943f, .287f), 22, TextAnchor.MiddleLeft).text
                = rule.HasBase ? $"{rule.TargetName}耐久归零。" : $"累计逃脱 {rule.EscapesToDefeat} 名敌军。";

            mapDetailsClose = MakeButton("返回战场", panel, new Vector2(.689f, .051f), new Vector2(.943f, .145f), CloseMapDetails);
            var navigation = mapDetailsClose.navigation;
            navigation.mode = Navigation.Mode.None;
            mapDetailsClose.navigation = navigation;
            mapDetails.SetActive(false);
        }

        private void OpenMapDetails()
        {
            if (mapDetails.activeSelf || systemMenu.activeSelf || pauseState.ReturnPending
                || (session.Phase != BattlePhase.Planning && session.Phase != BattlePhase.Fighting))
            {
                return;
            }

            mapDetails.SetActive(true);
            ApplyPause();
            EventSystem.current?.SetSelectedGameObject(mapDetailsClose.gameObject);
        }

        private void CloseMapDetails()
        {
            var events = EventSystem.current;
            if (events != null && events.currentSelectedGameObject == mapDetailsClose.gameObject)
            {
                events.SetSelectedGameObject(null);
            }

            mapDetails.SetActive(false);
            ApplyPause();
        }

        private void CreateDraftInterface()
        {
            cardModal = Panel("ChooseOneCard", safeArea, Vector2.zero, Vector2.one, new Color(.035f, .065f, .055f, .91f)).gameObject;
            Label("CardTitle", cardModal.transform, new Vector2(.15f, .765f), new Vector2(.85f, .85f), 44, TextAnchor.MiddleCenter).text = "选择一张强化卡";
            MakeButton("系统菜单", cardModal.transform, new Vector2(.83f, .86f), new Vector2(.97f, .95f), ToggleSystemMenu);
            for (int index = 0; index < 3; index++)
            {
                int offer = index;
                float left = .175f + index * .225f;
                var card = MakeButton(
                    "Card " + index,
                    cardModal.transform,
                    new Vector2(left, .18f),
                    new Vector2(left + .20f, .73f),
                    () => ChooseCard(offer),
                    false,
                    HudPiece.Card);
                cardFrames[index] = card.GetComponent<Image>();
                var tag = Label("Rarity", card.transform, new Vector2(.12f, .85f), new Vector2(.88f, .93f), 19, TextAnchor.MiddleCenter);
                tag.color = gold;
                cardRarities[index] = tag;
                cardImages[index] = Icon("Icon", card.transform, new Vector2(.20f, .46f), new Vector2(.80f, .82f), 3);
                cardNames[index] = Label("Name", card.transform, new Vector2(.10f, .36f), new Vector2(.90f, .46f), 30, TextAnchor.MiddleCenter);
                cardDescriptions[index] = Label("Effect", card.transform, new Vector2(.12f, .22f), new Vector2(.88f, .35f), 25, TextAnchor.MiddleCenter);
                cardDescriptions[index].color = muted;
                var choose = Label("Choose", card.transform, new Vector2(.12f, .08f), new Vector2(.88f, .17f), 26, TextAnchor.MiddleCenter);
                choose.text = "选择此强化";
                choose.color = gold;
            }

            cardModal.SetActive(false);
        }

        private void CreateResultInterface()
        {
            resultModal = Panel("Result", safeArea, Vector2.zero, Vector2.one, new Color(.035f, .065f, .055f, .91f)).gameObject;
            var panel = HudPlate("ResultPanel", resultModal.transform, .29f, .22f, .71f, .80f, HudPiece.Panel);
            Icon("ResultEmblem", panel, new Vector2(.37f, .64f), new Vector2(.63f, .91f), 8);
            resultText = Label("ResultText", panel, new Vector2(.10f, .40f), new Vector2(.90f, .65f), 39, TextAnchor.MiddleCenter);
            var again = MakeButton("再试一次", panel, new Vector2(.20f, .23f), new Vector2(.80f, .37f), Restart);
            again.GetComponentInChildren<TMP_Text>().color = gold;
            MakeButton("返回营地", panel, new Vector2(.20f, .07f), new Vector2(.80f, .20f), RequestExit, style: HudPiece.Upgrade);
            resultModal.SetActive(false);
        }

        private void CreateSystemInterface()
        {
            var overlay = new Color(.025f, .047f, .053f, .65f);
            systemMenu = Panel("SystemMenu", safeArea, Vector2.zero, Vector2.one, overlay).gameObject;
            var menu = HudPlate("MenuPanel", systemMenu.transform, .5f, .5f, .5f, .5f, HudPiece.Panel);
            menu.sizeDelta = SystemMenuSize;
            systemMenuHome = menu.gameObject;
            var title = Label("Title", menu, new Vector2(.12f, .80f), new Vector2(.88f, .95f), 40, TextAnchor.MiddleCenter);
            title.text = "系统菜单";
            title.fontStyle = FontStyles.Bold;
            MakeButton("返回战场", menu, new Vector2(.14f, .614f), new Vector2(.86f, .757f), ToggleSystemMenu, style: HudPiece.Upgrade);
            MakeButton("重新开始", menu, new Vector2(.14f, .449f), new Vector2(.86f, .592f), () => restartConfirm.SetActive(true));
            MakeButton("回到营地", menu, new Vector2(.14f, .284f), new Vector2(.86f, .427f), RequestExit);
            MakeButton("音量设置", menu, new Vector2(.14f, .119f), new Vector2(.86f, .262f), OpenAudioSettings);
            CreateAudioSettings();
            systemMenu.SetActive(false);

            restartConfirm = Panel("ConfirmRestart", safeArea, Vector2.zero, Vector2.one, new Color(.035f, .065f, .055f, .92f)).gameObject;
            var confirm = HudPlate("RestartPanel", restartConfirm.transform, .5f, .5f, .5f, .5f, HudPiece.Panel);
            confirm.sizeDelta = new Vector2(460, 238);
            Label("Title", confirm, new Vector2(.10f, .65f), new Vector2(.90f, .86f), 36, TextAnchor.MiddleCenter).text = "重新开始？";
            Label("Hint", confirm, new Vector2(.10f, .42f), new Vector2(.90f, .63f), 24, TextAnchor.MiddleCenter).text = "当前布防和战斗进度将清空。";
            MakeButton("取消", confirm, new Vector2(.07f, .13f), new Vector2(.47f, .35f), () => restartConfirm.SetActive(false));
            MakeButton("确认重开", confirm, new Vector2(.53f, .13f), new Vector2(.93f, .35f), Restart);
            restartConfirm.SetActive(false);
        }

        private void CreateAudioSettings()
        {
            var panel = HudPlate("AudioSettings", systemMenu.transform, .5f, .5f, .5f, .5f, HudPiece.Panel);
            panel.sizeDelta = AudioSettingsSize;
            audioSettingsPanel = panel.gameObject;
            MakeButton("返回", panel, new Vector2(.765f, .810f), new Vector2(.925f, .930f), CloseAudioSettings);
            var title = Label("Title", panel, new Vector2(.29f, .80f), new Vector2(.71f, .935f), 37, TextAnchor.MiddleCenter);
            title.text = "音量设置";
            title.fontStyle = FontStyles.Bold;
            CreateVolumeSlider(panel, "背景音乐", AudioGroup.Music, .475f, .719f);
            CreateVolumeSlider(panel, "游戏音效", AudioGroup.Sound, .160f, .404f);
            audioSettingsPanel.SetActive(false);
        }

        private void CreateBuildGoldToast()
        {
            float center = (BattlefieldLeft + BattlefieldRight) * .5f;
            var toast = HudPlate("BuildGoldToast", safeArea, center, .774f, center, .774f, HudPiece.Inset);
            toast.sizeDelta = new Vector2(214, 55);
            toast.GetComponent<Image>().raycastTarget = false;
            var message = Label("Message", toast, new Vector2(.08f, .10f), new Vector2(.92f, .90f), 26, TextAnchor.MiddleCenter);
            message.text = "金币不足";
            message.color = gold;
            message.fontStyle = FontStyles.Bold;
            buildGoldToast = toast.gameObject;
            buildGoldToast.SetActive(false);
        }

        private void ShowBuildGoldToast()
        {
            buildGoldToastUntil = Time.unscaledTime + 1.6f;
            buildGoldToast.SetActive(true);
        }

        private void CreateVolumeSlider(Transform parent, string label, AudioGroup group, float bottom, float top)
        {
            var row = Area(label, parent, new Vector2(.073f, bottom), new Vector2(.927f, top));
            Label("Caption", row, new Vector2(.02f, .53f), new Vector2(.73f, .95f), 24, TextAnchor.MiddleLeft).text = label;
            var percent = Label("Percent", row, new Vector2(.74f, .53f), new Vector2(.98f, .95f), 23, TextAnchor.MiddleLeft);
            percent.color = cyan;
            percent.alignment = TextAlignmentOptions.MidlineRight;
            var touch = Panel("Slider", row, Vector2.zero, new Vector2(1, .53f), Color.clear);
            var rail = Area("Rail", touch, new Vector2(0, .33f), new Vector2(1, .67f));
            rail.offsetMin = new Vector2(11, 0);
            rail.offsetMax = new Vector2(-11, 0);
            var track = HudPlate("Track", rail, 0, 0, 1, 1, HudPiece.Track);
            var fill = HudPlate("Fill", rail, 0, 0, 1, 1, HudPiece.Fill);
            var handle = HudPlate("Handle", rail, 0, 0, 0, 1, HudPiece.ActiveBadge);
            handle.sizeDelta = new Vector2(22, 12);
            var trackImage = track.GetComponent<Image>();
            var fillImage = fill.GetComponent<Image>();
            var handleImage = handle.GetComponent<Image>();
            trackImage.raycastTarget = fillImage.raycastTarget = handleImage.raycastTarget = false;
            trackImage.pixelsPerUnitMultiplier = fillImage.pixelsPerUnitMultiplier = trackImage.sprite.rect.height / 12f;

            var slider = touch.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImage;
            slider.maxValue = 100;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(value =>
            {
                percent.text = Mathf.RoundToInt(value) + "%";
                ChangeVolume(group, value / 100f);
            });
            volumeSliders.Add((group, slider, percent));
        }

        private void OpenAudioSettings()
        {
            bool available = GameSettings.TryGetInstance(out var settings);
            available &= AudioManager.TryGetInstance(out var audio);
            if (available && audio.MasterVolume < 1f)
            {
                // Preserve the effective volume of profiles saved with the old master slider.
                float master = audio.MasterVolume;
                foreach (var group in new[] { AudioGroup.Music, AudioGroup.Sound, AudioGroup.Voice, AudioGroup.UI })
                {
                    settings.SetGroupVolume(group, audio.GetGroupVolume(group) * master, saveImmediately: false);
                }

                settings.SetMasterVolume(1f, saveImmediately: false);
                volumeChanged = true;
            }

            foreach (var (group, control, percent) in volumeSliders)
            {
                float volume = available ? audio.GetGroupVolume(group) : 1f;
                int rounded = Mathf.RoundToInt(volume * 100);
                control.SetValueWithoutNotify(rounded);
                control.interactable = available;
                percent.text = rounded + "%";
            }

            systemMenuHome.SetActive(false);
            audioSettingsPanel.SetActive(true);
        }

        private void CloseAudioSettings()
        {
            SaveAudioSettings();
            audioSettingsPanel.SetActive(false);
            systemMenuHome.SetActive(true);
        }

        private void ChangeVolume(AudioGroup group, float value)
        {
            if (!GameSettings.TryGetInstance(out var settings) || !AudioManager.TryGetInstance(out _))
            {
                return;
            }

            settings.SetGroupVolume(group, value, saveImmediately: false);
            volumeChanged = true;
        }

        private void SaveAudioSettings()
        {
            if (!volumeChanged || !GameSettings.TryGetInstance(out var settings))
            {
                return;
            }

            try
            {
                settings.Save();
                volumeChanged = false;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not save game volume: " + error.Message);
            }
        }

        private void CreateExitInterface()
        {
            exitConfirm = Panel("ConfirmReturn", safeArea, Vector2.zero, Vector2.one, new Color(.035f, .065f, .055f, .91f)).gameObject;
            var panel = HudPlate("ReturnPanel", exitConfirm.transform, .5f, .5f, .5f, .5f, HudPiece.Panel);
            panel.sizeDelta = new Vector2(460, 238);
            Label("Title", panel, new Vector2(.10f, .65f), new Vector2(.90f, .86f), 36, TextAnchor.MiddleCenter).text = "返回营地？";
            Label("Hint", panel, new Vector2(.10f, .42f), new Vector2(.90f, .63f), 24, TextAnchor.MiddleCenter).text = "当前防守将结束，未完成的战斗不会记入战绩。";
            MakeButton("继续防守", panel, new Vector2(.07f, .13f), new Vector2(.47f, .35f), CancelExit);
            MakeButton("确认返回", panel, new Vector2(.53f, .13f), new Vector2(.93f, .35f), () => ExitRequested?.Invoke());
            exitConfirm.SetActive(false);
        }

        private void RefreshInterface()
        {
            shownPhase = session.Phase;
            int cardCount = 0;
            foreach (int count in session.ChosenCardStacks.Values)
            {
                cardCount += count;
            }

            bool hasBase = configuration.Objective.HasBase;
            int current = hasBase ? session.BaseHp : session.Leaks;
            int limit = hasBase ? session.BaseMaxHp : configuration.Objective.EscapesToDefeat;
            objectiveText.text = hasBase ? $"{current} / {limit}" : $"{current} <size=70%>/ {limit}</size>";
            float progress = current / (float)limit;
            ApplyHudPiece(objectiveFill, !hasBase || progress <= .25f ? HudPiece.LowHpFill : HudPiece.Fill);
            SetProgress(objectiveFill, progress);
            goldText.text = session.Gold.ToString();
            buffsText.text = "强化 " + cardCount;
            buffsText.transform.parent.gameObject.SetActive(cardCount > 0);
            bool manuallyPaused = pauseState.ManualPaused;
            pauseLabel.text = manuallyPaused ? "继续" : "暂停";
            ApplyHudPiece(pauseFrame, manuallyPaused ? HudPiece.Upgrade : HudPiece.Control);
            pauseGlyph.sprite = hudSprites[(int)(manuallyPaused ? HudPiece.Play : HudPiece.Pause)];
            bool planning = session.Phase == BattlePhase.Planning;
            bool fighting = session.Phase == BattlePhase.Fighting;
            bool canBuild = !session.IsPaused && (planning || fighting);
            for (int index = 0; index < buildButtons.Count; index++)
            {
                var definition = configuration.Towers[index];
                int price = session.BuildCost(definition);
                bool affordable = session.Gold >= price;
                bool chosen = buildMode && selectedDefinition == definition.Id;
                ApplyHudPiece(buildButtons[index].image, chosen ? HudPiece.Selected : affordable ? HudPiece.Card : HudPiece.Unavailable);
                buildTags[index].text = affordable ? towerRoles[index] : "金币不足";
                buildTags[index].color = affordable ? cyan : danger;
                buildButtons[index].interactable = canBuild;
                buildPrices[index].text = price.ToString();
            }

            RefreshSelectedTower(canBuild);
            RefreshBaseInspector(planning || fighting);
            bool opening = PreparingBattle;
            openingCountdown.gameObject.SetActive(opening);
            if (opening)
            {
                openingCountdownText.SetText("{0} 秒", Mathf.CeilToInt(openingSecondsRemaining));
            }

            startBattle.gameObject.SetActive(opening);
            startBattle.interactable = opening && canBuild;
            if (repairBase != null)
            {
                repairBase.gameObject.SetActive(fighting && !session.RepairUsed);
                repairBase.interactable = session.CanRepair;
            }

            killTallyText.gameObject.SetActive(!opening);

            cardModal.SetActive(session.Phase == BattlePhase.ChoosingCard);
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
                int rarity = Mathf.Clamp(card.Rarity, 1, 5) - 1;
                cardRarities[index].text = RarityNames[rarity];
                cardRarities[index].color = RarityColors[rarity];
                Color frameTint = hudSprites[(int)HudPiece.Card] != null ? Color.white : surface;
                cardFrames[index].color = Color.Lerp(frameTint, RarityColors[rarity], .24f);
                int icon = card.IconIndex >= 0 ? card.IconIndex : (int)card.Effect + 2;
                cardImages[index].enabled = icon >= 0 && icon < generatedIcons.Count;
                cardImages[index].sprite = cardImages[index].enabled ? generatedIcons[icon] : null;
            }

            bool finished = session.Phase == BattlePhase.Victory || session.Phase == BattlePhase.Defeat;
            resultModal.SetActive(finished);
            if (finished)
            {
                RefreshBattleResult();
            }
            if (relayCaption != null)
            {
                relayCaption.text = session.RelayHeld ? "副站 · 点亮" : "副站 · 失守";
                relayCaption.color = session.RelayHeld ? healthColor : danger;
                relayLamp.SetActive(session.RelayHeld);
                if (session.Phase == BattlePhase.Victory)
                {
                    resultText.text += session.RelayHeld ? "\n<size=60%>副站守住 · 获得白崖情报</size>" : "\n<size=60%>副站失守</size>";
                }
            }
            if (finished && !completedReported)
            {
                completedReported = true;
                Finished?.Invoke(session);
            }

            RefreshAssaultHud();
        }

        private void RefreshAssaultHud()
        {
            bool fighting = session.Phase == BattlePhase.Fighting;
            bool planning = session.Phase == BattlePhase.Planning;
            bool active = planning || fighting || session.Phase == BattlePhase.ChoosingCard;
            int gap = session.ReinforcementTicksRemaining;
            bool hiddenRoutes = configuration.HidePlannedRoutes && (planning || gap > 0);
            int index = gap > 0 && !hiddenRoutes ? session.WaveIndex : Math.Max(0, session.WaveIndex - 1);
            var batch = briefing.Waves[index];
            string cue = configuration.Waves[index].PlanningCue;
            campaignCue.transform.parent.gameObject.SetActive(active && !string.IsNullOrEmpty(cue));
            campaignCue.text = cue ?? "";
            for (int routeIndex = 0; routeIndex < entranceMarkers.Length; routeIndex++)
            {
                int route = configuration.Routes[routeIndex].Id;
                bool inUse = session.Enemies.Any(enemy => enemy.RouteId == route);
                bool knownUpcoming = !hiddenRoutes && !session.AllEnemiesSpawned && batch.RouteIds.Contains(route);
                bool potentialEntrance = hiddenRoutes && !inUse;
                entranceMarkers[routeIndex].gameObject.SetActive(active && (inUse || knownUpcoming || potentialEntrance));
                entranceMarkers[routeIndex].GetComponent<Image>().color = potentialEntrance
                    ? new Color(.22f, .28f, .29f, .92f) : new Color(.44f, .24f, .17f, .95f);
            }

            killTallyText.text = "已击杀 " + session.Kills;
            draftScoreText.text = session.DraftPoints + " / " + session.DraftPointsRequired;
            SetProgress(draftProgress, session.DraftPoints / (float)session.DraftPointsRequired);
            int gapSeconds = Mathf.CeilToInt(gap * (FightSession.TickMilliseconds / 1000f));
            reinforcementText.text = planning ? "开战后自动出兵" : gap > 0 ? "增援倒计时 " + gapSeconds + " 秒"
                : session.AllEnemiesSpawned ? "敌军已全部出动" : "敌军持续来袭";

            bool paused = session.IsPaused && (fighting || planning);
            waveStatus.text = paused ? "暂停" : session.Phase switch
            {
                BattlePhase.Planning => "备战",
                BattlePhase.Fighting => configuration.Objective.HasBase ? "防守中" : "拦截中",
                BattlePhase.ChoosingCard => "选强化",
                BattlePhase.Victory => "已完成",
                _ => ObjectiveDefeatCaption
            };
            ApplyHudPiece(stateFrame, (fighting && !paused) || session.Phase == BattlePhase.Victory ? HudPiece.ActiveBadge : HudPiece.Badge);
            waveStatus.color = session.Phase == BattlePhase.Defeat ? danger : ink;
        }

        private string ObjectiveDefeatCaption => configuration.Objective.HasBase ? "基地失守" : "拦截失败";

        private void RefreshBattleResult()
        {
            bool won = session.Phase == BattlePhase.Victory;
            var rule = configuration.Objective;
            string title = rule.HasBase ? configuration.StageName + "已守住" : "拦截成功";
            if (!won)
            {
                title = ObjectiveDefeatCaption;
            }

            string details = "基地耐久耗尽";
            if (!rule.HasBase)
            {
                details = "敌军逃脱 " + session.Leaks + " 名 · " + rule.EscapesToDefeat + " 名即失败";
            }
            else if (won)
            {
                details = rule.Type == BattleObjectiveType.DefendBase ? "已歼灭全部敌军" : "防守完成";
            }

            resultText.text = (won ? "<color=#F0C56B>" + title + "</color>" : title) + "\n<size=60%>" + details + "</size>";
        }

        private static void SetProgress(Image fill, float progress)
        {
            var track = fill.transform.parent.GetComponent<Image>();
            float height = track.rectTransform.rect.height;
            if (height > 0)
            {
                // A 16-pixel cap on a 32-pixel sprite must be half the displayed bar height.
                track.pixelsPerUnitMultiplier = track.sprite.rect.height / height;
                fill.pixelsPerUnitMultiplier = fill.sprite.rect.height / height;
            }

            fill.enabled = progress > 0;
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
        }

        private RectTransform CreateRouteMarker(string caption, Color color)
        {
            var marker = Panel(caption, safeArea, new Vector2(.5f, .5f), new Vector2(.5f, .5f), color);
            marker.sizeDelta = new Vector2(112, 33);
            marker.GetComponent<Image>().raycastTarget = false;
            Label("Caption", marker, new Vector2(.05f, .04f), new Vector2(.95f, .96f), 22, TextAnchor.MiddleCenter).text = caption;
            return marker;
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

            var usable = lastSafeArea.width > 0 && lastSafeArea.height > 0 ? lastSafeArea : new Rect(0, 0, Screen.width, Screen.height);
            var safeMin = new Vector2(Mathf.Clamp01(usable.xMin / Screen.width), Mathf.Clamp01(usable.yMin / Screen.height));
            var safeMax = new Vector2(Mathf.Clamp01(usable.xMax / Screen.width), Mathf.Clamp01(usable.yMax / Screen.height));
            safeArea.anchorMin = safeMin;
            safeArea.anchorMax = safeMax;
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
            var viewport = Rect.MinMaxRect(
                Mathf.Lerp(safeMin.x, safeMax.x, BattlefieldLeft),
                Mathf.Lerp(safeMin.y, safeMax.y, BattlefieldBottom),
                Mathf.Lerp(safeMin.x, safeMax.x, BattlefieldRight),
                Mathf.Lerp(safeMin.y, safeMax.y, BattlefieldTop));
            battlefieldPixels = Rect.MinMaxRect(viewport.xMin * Screen.width, viewport.yMin * Screen.height, viewport.xMax * Screen.width, viewport.yMax * Screen.height);
            float aspect = Mathf.Max(.1f, (float)Screen.width / Screen.height);
            worldCamera.aspect = aspect;
            float halfDepth = .5f * projectedMapDepth;
            float halfSize = Mathf.Max((halfDepth + .20f) / viewport.height, (projectedMapWidth * .5f + .24f) / (aspect * viewport.width));
            worldCamera.orthographicSize = halfSize;
            var camera = worldCamera.transform;
            var offset = viewport.center - new Vector2(.5f, .5f);
            camera.position = -camera.forward * 35 - camera.right * (2 * halfSize * aspect * offset.x) - camera.up * (2 * halfSize * offset.y);
            Canvas.ForceUpdateCanvases();
            var safeSize = safeArea.rect.size;
            float detailsScale = Mathf.Min(1, safeSize.x * .90f / MapDetailsSize.x, safeSize.y * .88f / MapDetailsSize.y);
            mapDetailsPanel.sizeDelta = MapDetailsSize * detailsScale;
            float menuScale = Mathf.Min(1, safeSize.x * .90f / SystemMenuSize.x, safeSize.y * .88f / SystemMenuSize.y);
            ((RectTransform)systemMenuHome.transform).sizeDelta = SystemMenuSize * menuScale;
            ((RectTransform)audioSettingsPanel.transform).sizeDelta = AudioSettingsSize * menuScale;
            FitTowerStatComparison();
            foreach (var frame in hudFrames)
            {
                ResizeHudFrame(frame);
            }

            for (int index = 0; index < entranceMarkers.Length; index++)
            {
                PlaceRouteMarker(entranceMarkers[index], configuration.Routes[index].Points[0], .15f, 27);
            }

            var points = configuration.Routes[0].Points;
            float goalHeight = configuration.Objective.HasBase && configuration.StageId == 4 ? 3.26f : 2.35f;
            PlaceRouteMarker(goalMarker, points[points.Length - 1], goalHeight, 18);
            if (relayMarker != null)
            {
                var relay = configuration.Routes.First(route => route.Id == configuration.RelayRouteId);
                PlaceRouteMarker(relayMarker, RelayPosition(relay), 1.38f, 18);
            }
        }

        private void PlaceRouteMarker(RectTransform marker, Position2 point, float height, float offset)
        {
            var screenPoint = worldCamera.WorldToScreenPoint(field.TransformPoint(World(point, height)));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeArea, screenPoint, null, out var location);
            var bounds = safeArea.rect;
            var halfSize = marker.sizeDelta * .5f;
            float left = bounds.xMin + bounds.width * BattlefieldLeft + halfSize.x + 8;
            float right = bounds.xMin + bounds.width * BattlefieldRight - halfSize.x - 8;
            location.x = Mathf.Clamp(location.x, left, right);
            location.y = Mathf.Clamp(location.y + offset, bounds.yMin + halfSize.y + 5, bounds.yMax - halfSize.y - 5);
            marker.anchoredPosition = location;
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

        private static RectTransform Panel(string label, Transform parent, Vector2 minimum, Vector2 maximum, Color color)
        {
            var value = Area(label, parent, minimum, maximum);
            var image = value.gameObject.AddComponent<Image>();
            image.color = color;
            return value;
        }

        private Sprite CreateHudShape(bool capsule = false)
        {
            const int size = 32;
            int corner = capsule ? size / 2 : 11;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Abs(x + .5f - size * .5f) - (size * .5f - corner));
                    float dy = Mathf.Max(0, Mathf.Abs(y + .5f - size * .5f) - (size * .5f - corner));
                    byte alpha = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(corner + .5f - Mathf.Sqrt(dx * dx + dy * dy)));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            var border = capsule ? new Vector4(corner, 0, corner, 0) : new Vector4(corner, corner, corner, corner);
            var shape = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
            materialAssets.Add(shape);
            materialAssets.Add(texture);
            return shape;
        }

        private Image Icon(string label, Transform parent, Vector2 minimum, Vector2 maximum, int cell)
        {
            var image = Area(label, parent, minimum, maximum).gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.enabled = false;
            atlasImages.Add(image);
            atlasIndices.Add(cell);
            return image;
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

        private Button MakeButton(
            string label,
            Transform parent,
            Vector2 minimum,
            Vector2 maximum,
            UnityEngine.Events.UnityAction callback,
            bool withLabel = true,
            HudPiece style = HudPiece.Control)
        {
            var panel = Area(label, parent, minimum, maximum);
            var image = panel.gameObject.AddComponent<Image>();
            ApplyHudPiece(image, style);
            var button = panel.gameObject.AddComponent<HudPressButton>();
            button.targetGraphic = image;
            button.SetHoverTint(style switch
            {
                HudPiece.Upgrade => HudPressButton.GreenHover,
                HudPiece.Sell => HudPressButton.RedHover,
                _ => HudPressButton.BlueHover
            });
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(.49f, .68f, .76f);
            colors.disabledColor = new Color(.60f, .67f, .72f, .92f);
            colors.fadeDuration = .045f;
            button.colors = colors;
            button.onClick.AddListener(callback);
            if (withLabel)
            {
                Label("Label", panel, new Vector2(.08f, .12f), new Vector2(.92f, .88f), 26, TextAnchor.MiddleCenter).text = label;
            }

            return button;
        }
    }

}
