using System;
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
        private static readonly string[] RarityNames = { "普通  ·  战地祝福", "优秀  ·  战地祝福", "稀有  ·  战地祝福", "史诗  ·  战地祝福", "传说  ·  战地祝福" };
        private static readonly Color[] RarityColors =
        {
            new Color(.91f, .89f, .85f), new Color(.36f, .88f, .32f), new Color(.28f, .65f, 1),
            new Color(.75f, .38f, .98f), new Color(1, .59f, .16f)
        };
        private readonly Color gold = new Color(.94f, .77f, .42f);
        private readonly Color muted = new Color(.68f, .77f, .69f);

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

            panelSprite = cardFrame;
            foreach (var panel in skinnedPanels)
            {
                panel.sprite = panelSprite;
                panel.color = Color.white;
                panel.type = Image.Type.Sliced;
                panel.pixelsPerUnitMultiplier = 7;
            }

            for (int index = 0; index < atlasImages.Count; index++)
            {
                int cell = atlasIndices[index];
                atlasImages[index].sprite = cell < generatedIcons.Count ? generatedIcons[cell] : null;
                atlasImages[index].enabled = atlasImages[index].sprite != null;
            }

            RefreshInterface();
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
            var title = Panel("StageTitle", safeArea, new Vector2(.022f, .885f), new Vector2(.255f, .978f), Color.white);
            var chapter = Label("Chapter", title, new Vector2(.09f, .58f), new Vector2(.92f, .87f), 18, TextAnchor.MiddleLeft);
            chapter.text = "第一章  /  FOREST OUTPOST";
            chapter.color = gold;
            Label("StageName", title, new Vector2(.09f, .14f), new Vector2(.92f, .61f), 32, TextAnchor.MiddleLeft).text = "林间哨站";
            stats[0] = Stat("BaseHealth", .36f, .50f, "基地耐久", 7);
            stats[1] = Stat("Gold", .505f, .635f, "可用金币", 6);
            stats[2] = Stat("Waves", .64f, .77f, "防守波次", 8);
            stats[3] = Stat("Buffs", .775f, .885f, "强化", 3);
            var pause = MakeButton("Pause", safeArea, new Vector2(.90f, .895f), new Vector2(.978f, .978f), TogglePause);
            pauseLabel = pause.GetComponentInChildren<TMP_Text>();
            MakeButton("营地", safeArea, new Vector2(.27f, .895f), new Vector2(.345f, .978f), RequestExit);
            var bottom = Panel("BuildBar", safeArea, new Vector2(.022f, .024f), new Vector2(.978f, .18f), Color.white);
            var caption = Label("BuildCaption", bottom, new Vector2(.025f, .77f), new Vector2(.49f, .94f), 20, TextAnchor.MiddleLeft);
            caption.text = "部署防线  <color=#8BA498>·  选塔卡，再点区域</color>";
            caption.color = gold;
            int count = Math.Min(configuration.Towers.Length, 3);
            for (int index = 0; index < count; index++)
            {
                int definitionId = configuration.Towers[index].Id;
                float left = .018f + index * .163f;
                var button = MakeButton(
                    "Build " + index,
                    bottom,
                    new Vector2(left, .12f),
                    new Vector2(left + .155f, .74f),
                    () => SelectDefinition(definitionId),
                    false);
                Icon("TowerIcon", button.transform, new Vector2(.035f, .08f), new Vector2(.43f, .94f), index);
                buildTitles[index] = Label("TowerName", button.transform, new Vector2(.43f, .48f), new Vector2(.95f, .85f), 24, TextAnchor.MiddleLeft);
                buildCosts[index] = Label("TowerCost", button.transform, new Vector2(.43f, .16f), new Vector2(.95f, .50f), 24, TextAnchor.MiddleLeft);
                buildCosts[index].color = gold;
                buildButtons.Add(button);
            }

            var separator = Panel("Divider", bottom, new Vector2(.519f, .16f), new Vector2(.520f, .84f), new Color(.75f, .65f, .38f, .55f), false);
            separator.GetComponent<Image>().raycastTarget = false;
            towerDetails = Label("SelectedTower", bottom, new Vector2(.545f, .52f), new Vector2(.77f, .91f), 22, TextAnchor.MiddleLeft);
            upgrade = MakeButton("升级", bottom, new Vector2(.545f, .12f), new Vector2(.765f, .48f), UpgradeSelected);
            nextWave = MakeButton("Start wave", bottom, new Vector2(.795f, .12f), new Vector2(.978f, .86f), StartWave);
            nextWaveLabel = nextWave.GetComponentInChildren<TMP_Text>();
            nextWaveLabel.color = gold;
            nextWaveLabel.fontSizeMax = 32;
            var help = Panel("HelpStrip", safeArea, new Vector2(.22f, .187f), new Vector2(.78f, .222f), new Color(.07f, .14f, .11f, .9f), false);
            help.GetComponent<Image>().raycastTarget = false;
            notice = Label("Help", help, new Vector2(.02f, .06f), new Vector2(.98f, .94f), 21, TextAnchor.MiddleCenter);
            notice.color = cream;
            notice.fontMaterial.EnableKeyword("OUTLINE_ON");
            notice.outlineColor = new Color(.06f, .12f, .09f);
            notice.outlineWidth = .18f;
            CreateDraftInterface();
            CreateResultInterface();
            CreateExitInterface();
            UpdateSafeArea();
        }

        private TMP_Text Stat(string label, float left, float right, string caption, int icon)
        {
            var panel = Panel(label, safeArea, new Vector2(left, .895f), new Vector2(right, .978f), Color.white);
            Icon("StatIcon", panel, new Vector2(.045f, .17f), new Vector2(.35f, .85f), icon);
            var name = Label("Caption", panel, new Vector2(.39f, .58f), new Vector2(.94f, .86f), 17, TextAnchor.MiddleLeft);
            name.text = caption;
            name.color = muted;
            return Label("Value", panel, new Vector2(.39f, .15f), new Vector2(.94f, .59f), 28, TextAnchor.MiddleLeft);
        }

        private void CreateDraftInterface()
        {
            cardModal = Panel("ChooseOneCard", safeArea, Vector2.zero, Vector2.one, new Color(.035f, .075f, .06f, .88f), false).gameObject;
            var eyebrow = Label("DraftEyebrow", cardModal.transform, new Vector2(.25f, .845f), new Vector2(.75f, .90f), 23, TextAnchor.MiddleCenter);
            eyebrow.text = "波次完成  /  ROGUELIKE UPGRADE";
            eyebrow.color = gold;
            Label("CardTitle", cardModal.transform, new Vector2(.15f, .765f), new Vector2(.85f, .85f), 44, TextAnchor.MiddleCenter).text = "选择一张强化卡";
            cardResume = MakeButton("继续选卡", cardModal.transform, new Vector2(.83f, .86f), new Vector2(.97f, .95f), () =>
            {
                session.SetPaused(false);
                RefreshInterface();
            }).gameObject;
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
                    false);
                cardFrames[index] = card.GetComponent<Image>();
                var tag = Label("Rarity", card.transform, new Vector2(.12f, .85f), new Vector2(.88f, .93f), 19, TextAnchor.MiddleCenter);
                tag.text = "战地祝福";
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

            Label("DraftHint", cardModal.transform, new Vector2(.25f, .09f), new Vector2(.75f, .15f), 23, TextAnchor.MiddleCenter).text = "本局立即生效 · 每波只能选择一张";
            cardModal.SetActive(false);
        }

        private void CreateResultInterface()
        {
            resultModal = Panel("Result", safeArea, Vector2.zero, Vector2.one, new Color(.035f, .075f, .06f, .88f), false).gameObject;
            var panel = Panel("ResultPanel", resultModal.transform, new Vector2(.29f, .22f), new Vector2(.71f, .80f), Color.white);
            Icon("ResultEmblem", panel, new Vector2(.37f, .64f), new Vector2(.63f, .91f), 8);
            resultText = Label("ResultText", panel, new Vector2(.10f, .40f), new Vector2(.90f, .65f), 39, TextAnchor.MiddleCenter);
            var again = MakeButton("再试一次", panel, new Vector2(.20f, .23f), new Vector2(.80f, .37f), Restart);
            again.GetComponentInChildren<TMP_Text>().color = gold;
            MakeButton("返回营地", panel, new Vector2(.20f, .07f), new Vector2(.80f, .20f), RequestExit);
            resultModal.SetActive(false);
        }

        private void CreateExitInterface()
        {
            exitConfirm = Panel("ConfirmReturn", safeArea, Vector2.zero, Vector2.one, new Color(.03f, .07f, .05f, .88f), false).gameObject;
            var panel = Panel("ReturnPanel", exitConfirm.transform, new Vector2(.28f, .29f), new Vector2(.72f, .72f), Color.white);
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

            stats[0].text = session.BaseHp + " <size=65%>/ " + configuration.BaseHealth + "</size>";
            stats[1].text = session.Gold.ToString();
            stats[1].color = gold;
            stats[2].text = session.WaveIndex + " <size=65%>/ " + configuration.Waves.Length + "</size>";
            stats[3].text = cardCount.ToString();
            pauseLabel.text = session.IsPaused ? "继续" : "暂停";
            for (int index = 0; index < buildButtons.Count; index++)
            {
                var definition = configuration.Towers[index];
                buildTitles[index].text = definition.Name;
                buildCosts[index].text = definition.BuildCost + " <size=70%>金币</size>";
                buildButtons[index].GetComponent<Image>().color = buildMode && selectedDefinition == definition.Id ? new Color(1, 1, .78f) : Color.white;
                buildButtons[index].interactable = !session.IsPaused && session.Gold >= definition.BuildCost
                    && (session.Phase == BattlePhase.Planning || session.Phase == BattlePhase.Fighting);
            }

            towerDetails.text = buildMode
                ? FindTower(selectedDefinition).Name + "\n<size=80%>点击绿色区域部署</size>"
                : "选择防御塔\n<size=80%><color=#AEC4B1>查看属性与升级</color></size>";
            upgrade.interactable = false;
            upgrade.GetComponentInChildren<TMP_Text>().text = "选中防御塔后升级";
            foreach (var tower in session.Towers)
            {
                if (tower.Id != selectedTower)
                {
                    continue;
                }

                var definition = FindTower(tower.DefinitionId);
                int cost = definition.UpgradeCost * tower.Level;
                towerDetails.text = definition.Name + " <color=#F0C56B>Lv." + tower.Level + "</color>\n<size=80%>伤害 "
                    + tower.Damage + "  ·  射程 " + (tower.Range / 1000f).ToString("0.0") + "</size>";
                upgrade.GetComponentInChildren<TMP_Text>().text = tower.Level >= definition.MaxLevel ? "已满级" : "升级  ·  " + cost + " 金币";
                upgrade.interactable = !session.IsPaused && tower.Level < definition.MaxLevel && session.Gold >= cost
                    && (session.Phase == BattlePhase.Planning || session.Phase == BattlePhase.Fighting);
                break;
            }

            nextWave.interactable = session.Phase == BattlePhase.Planning && !session.IsPaused;
            nextWaveLabel.text = session.Phase == BattlePhase.Fighting
                ? "防守中\n<size=55%>守住林间哨站</size>"
                : "开始第 " + Math.Min(session.WaveIndex + 1, configuration.Waves.Length) + " 波\n<size=55%>准备迎敌</size>";
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
                int rarity = Mathf.Clamp(card.Rarity, 1, 5) - 1;
                cardRarities[index].text = RarityNames[rarity];
                cardRarities[index].color = RarityColors[rarity];
                cardFrames[index].color = Color.Lerp(Color.white, RarityColors[rarity], .18f);
                int icon = card.IconIndex >= 0 ? card.IconIndex : (int)card.Effect + 2;
                cardImages[index].enabled = icon >= 0 && icon < generatedIcons.Count;
                cardImages[index].sprite = cardImages[index].enabled ? generatedIcons[icon] : null;
            }

            bool finished = session.Phase == BattlePhase.Victory || session.Phase == BattlePhase.Defeat;
            resultModal.SetActive(finished);
            resultText.text = session.Phase == BattlePhase.Victory
                ? "<color=#F0C56B>防线守住了！</color>\n<size=60%>完成全部 " + configuration.Waves.Length + " 波防守</size>"
                : "基地失守\n<size=60%>调整塔阵，再试一次</size>";
            if (finished && !completedReported)
            {
                completedReported = true;
                Finished?.Invoke(session);
            }

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
            safeArea.anchorMin = new Vector2(Mathf.Clamp01(usable.xMin / Screen.width), Mathf.Clamp01(usable.yMin / Screen.height));
            safeArea.anchorMax = new Vector2(Mathf.Clamp01(usable.xMax / Screen.width), Mathf.Clamp01(usable.yMax / Screen.height));
            safeArea.offsetMin = Vector2.zero;
            safeArea.offsetMax = Vector2.zero;
            worldCamera.orthographicSize = Mathf.Max(7.65f, 12.0f / Mathf.Max(.1f, (float)Screen.width / Screen.height));
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

        private RectTransform Panel(string label, Transform parent, Vector2 minimum, Vector2 maximum, Color color, bool framed = true)
        {
            var value = Area(label, parent, minimum, maximum);
            var image = value.gameObject.AddComponent<Image>();
            image.color = color;
            if (framed)
            {
                image.color = panelSprite != null ? color : new Color(.12f, .24f, .20f, color.a);
                image.sprite = panelSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 7;
                skinnedPanels.Add(image);
            }

            return value;
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
            bool withLabel = true)
        {
            var panel = Panel(label, parent, minimum, maximum, Color.white);
            var button = panel.gameObject.AddComponent<Button>();
            button.targetGraphic = panel.GetComponent<Image>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.12f, 1.16f, 1.04f);
            colors.pressedColor = new Color(.79f, .87f, .71f);
            colors.disabledColor = new Color(.52f, .60f, .53f, .9f);
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
