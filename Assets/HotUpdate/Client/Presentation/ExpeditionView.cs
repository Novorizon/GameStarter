using System;
using System.Linq;
using Game.HotUpdate.Camp;
using Game.HotUpdate.TowerDefense.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static Game.HotUpdate.Client.ExpeditionChrome;

namespace Game.HotUpdate.Client
{
    /// <summary>出征准备页；展示已锁定的关卡配置和本次出征情报。</summary>
    public sealed class ExpeditionView
    {
        private sealed class StageTab
        {
            internal int StageId;
            internal Button Button;
            internal ExpeditionFrame Frame;
            internal TMP_Text Number;
            internal TMP_Text Name;
            internal Image Lock;
        }

        private readonly ClientFlow owner;
        private readonly ExpeditionChrome ui;
        private readonly GameObject root;
        private GameObject intelRoot;
        private TMP_Text heading;
        private TMP_Text chapter;
        private TMP_Text objective;
        private StageTab[] stageTabs;
        private TMP_Text enemyTotal;
        private TMP_Text gold;
        private TMP_Text objectiveValue;
        private TMP_Text objectiveCaption;
        private (TMP_Text Wood, TMP_Text Stone) victorySupplies;
        private (TMP_Text Wood, TMP_Text Stone) defeatSupplies;
        private TMP_Text victorySupplyCaption;
        private TMP_Text defeatSupplyCaption;
        private TMP_Text goal;
        private TMP_Text goalFailure;
        private TMP_Text suppliesNote;
        private TMP_Text clearGold;
        private TMP_Text cardReward;
        private TMP_Text terrainName;
        private TMP_Text routeCount;
        private TMP_Text reinforcementGap;
        private TMP_Text enemyKinds;
        private TMP_Text strategy;
        private readonly TMP_Text[] tactics = new TMP_Text[3];
        private RectTransform detailsPanel;
        private RectTransform detailsBody;
        private RectTransform detailsChevron;
        private Button detailsToggle;
        private TMP_Text status;
        private RectTransform towerArea;
        private RectTransform enemyArea;
        private Button back;
        private Button cancel;
        private Button confirm;
        private TMP_Text confirmCaption;
        private Button intel;
        private ExpeditionPlan activePlan;
        public bool Visible => root.activeSelf;

        public ExpeditionView(Transform parent, ClientFlow owner)
        {
            this.owner = owner;
            ui = new ExpeditionChrome(owner.Art);
            var overlay = CampUI.Overlay("ExpeditionPreparation", parent, 1);
            root = overlay.gameObject;
            var artwork = CampUI.Area("Landscape", overlay, 0, 0, 1, 1);
            Texture("OutpostPainting", owner.Art.ExpeditionBackdrop, artwork, AspectRatioFitter.AspectMode.EnvelopeParent);
            var page = CampUI.Area("ExpeditionPanel", overlay, 0, 0, 1, 1);
            BuildHeader(page);
            BuildOverview(page);
            BuildSummary(page);
            BuildFooter(page);
            BuildIntel(page);
            root.SetActive(false);
        }

        private void BuildHeader(RectTransform page)
        {
            var backFrame = Frame("BackExpedition", page, .015f, .937f, .071f, .988f, ExpeditionFrameStyle.Secondary);
            ui.Icon(ExpeditionGlyph.Back, backFrame, .20f, .115f, .80f, .885f);
            back = Bind(backFrame, Hide);
            ui.Title("PageTitle", page, .083f, .94f, .421f, .991f, 38, "防御作战 · 关卡准备");
            Rule(page, .078f, .931f, .302f, .933f, new Color(.90f, .75f, .50f, .65f));
            Diamond(page, .303f, .932f, 6);
            var banner = CampUI.Area("ChapterStandard", page, .027f, .768f, .116f, .925f);
            Texture("ForestCrest", owner.Art.ExpeditionBanner, banner, AspectRatioFitter.AspectMode.FitInParent);
            chapter = ui.Title("Chapter", page, .116f, .886f, .547f, .925f, 30, "");
            heading = ui.Title("Heading", page, .114f, .812f, .613f, .892f, 76, "");
            objective = ui.Text("Objective", page, .118f, .762f, .613f, .811f, 24, "", Ink, wrap: true);

            var selector = CampUI.Area("StageSelector", page, .654f, .765f, .985f, .923f);
            stageTabs = new StageTab[owner.Stages.Count];
            const int columns = 4;
            int rows = Mathf.Max(1, Mathf.CeilToInt(stageTabs.Length / (float)columns));
            float height = 1f / rows;
            int sideNumber = 0;
            for (int index = 0; index < stageTabs.Length; index++)
            {
                var stage = owner.Stages[index];
                float left = index % columns / (float)columns;
                float top = 1 - index / columns * height;
                var tab = Frame("Chapter" + stage.Id, selector, left, top - height + .045f, left + .231f, top, ExpeditionFrameStyle.Slate);
                string number = owner.Art.Campaign.IsSideBattle(stage.Id) ? "支线 " + (++sideNumber).ToString("00") : "第 " + stage.Id + " 章";
                var code = ui.Title("StageNumber", tab, .095f, .51f, .905f, .934f, 22, number);
                code.alignment = TextAlignmentOptions.Center;
                var name = ui.Text("StageName", tab, .068f, .085f, .932f, .51f, 23, stage.Name, Ink);
                name.alignment = TextAlignmentOptions.Center;
                var lockIcon = ui.Icon(ExpeditionGlyph.Lock, tab, .787f, .608f, .937f, .904f);
                stageTabs[index] = new StageTab
                {
                    StageId = stage.Id, Button = Bind(tab, () => Show(stage.Id)), Frame = tab.GetComponent<ExpeditionFrame>(),
                    Number = code, Name = name, Lock = lockIcon
                };
            }
        }

        private void BuildOverview(RectTransform page)
        {
            var panel = Frame("BattleBrief", page, .060f, .211f, .579f, .717f, ExpeditionFrameStyle.Panel);
            Section(panel, ExpeditionGlyph.Swords, "关卡概览", .875f, .973f, .252f);
            var metrics = Frame("OverviewMetrics", panel, .041f, .647f, .959f, .866f, ExpeditionFrameStyle.Slate);
            const float third = 1f / 3f;
            var divider = new Color(.76f, .68f, .46f, .40f);
            Rule(metrics, third, .16f, third + .0012f, .84f, divider);
            Rule(metrics, 2 * third, .16f, 2 * third + .0012f, .84f, divider);
            enemyTotal = Metric(metrics, 0, ExpeditionGlyph.Swords, "敌军总数", out _);
            gold = Metric(metrics, third, ExpeditionGlyph.Coins, "初始金币", out _);
            objectiveValue = Metric(metrics, 2 * third, ExpeditionGlyph.Gate, "基地耐久", out objectiveCaption);

            Section(panel, ExpeditionGlyph.Flag, "关卡目标", .525f, .631f, .252f);
            Diamond(panel, .084f, .468f, 10);
            Diamond(panel, .084f, .389f, 10);
            goal = ui.Text("DefenseGoal", panel, .109f, .429f, .947f, .509f, 27, "", Ink);
            goalFailure = ui.Text("FailureCondition", panel, .109f, .350f, .947f, .430f, 27, "", Ink);

            Rule(panel, .041f, .320f, .959f, .322f, divider);
            Section(panel, ExpeditionGlyph.Chest, "局内奖励", .213f, .316f, .252f);
            var rewards = Frame("RewardPreview", panel, .051f, .053f, .949f, .201f, ExpeditionFrameStyle.Slate);
            Rule(rewards, .5f, .22f, .5012f, .78f, divider);
            var coins = Reward(rewards, 0, ExpeditionGlyph.Coins, "防守金币");
            clearGold = ui.Number("RewardAmount", coins, .642f, .12f, .952f, .88f, 31);
            var draft = Reward(rewards, .5f, ExpeditionGlyph.Scroll, "首张强化");
            cardReward = ui.Title("DraftThreshold", draft, .633f, .12f, .972f, .88f, 25, "");
        }

        private TMP_Text Metric(Transform band, float left, ExpeditionGlyph glyph, string label, out TMP_Text caption)
        {
            var cell = CampUI.Area(label, band, left, 0, left + 1f / 3f, 1);
            ui.Icon(glyph, cell, .14f, .18f, .40f, .82f);
            caption = ui.Title("Caption", cell, .445f, .59f, .96f, .917f, 26, label);
            return ui.Number("Value", cell, .445f, .068f, .96f, .595f, 51);
        }

        private (TMP_Text Wood, TMP_Text Stone) Supply(Transform panel, float left, float right, bool won, out TMP_Text caption)
        {
            var style = won ? ExpeditionFrameStyle.Victory : ExpeditionFrameStyle.Defeat;
            var card = Frame(won ? "VictorySupply" : "DefeatSupply", panel, left, 0, right, 1, style);
            ui.Icon(won ? ExpeditionGlyph.Laurel : ExpeditionGlyph.BrokenGate, card, .045f, .375f, .207f, .922f);
            string title = won ? "守住防线" : "基地失守";
            caption = ui.Title("Outcome", card, .226f, .558f, .467f, .941f, 27, title);
            if (!won)
            {
                caption.enableVertexGradient = false;
                caption.color = Loss;
            }

            ui.Text("Result", card, .469f, .562f, .95f, .936f, 20, won ? "（胜利）" : "（失败）", won ? Gold : Loss);
            ui.Icon(ExpeditionGlyph.Wood, card, .216f, .108f, .324f, .499f);
            ui.Icon(ExpeditionGlyph.Stone, card, .592f, .108f, .70f, .499f);
            var wood = ui.Text("Wood", card, .334f, .096f, .588f, .505f, 22, "", Ink);
            var stone = ui.Text("Stone", card, .712f, .096f, .953f, .505f, 22, "", Ink);
            return (wood, stone);
        }

        private RectTransform Reward(Transform band, float left, ExpeditionGlyph icon, string caption)
        {
            var block = CampUI.Area(icon + "Reward", band, left, 0, left + .5f, 1);
            ui.Icon(icon, block, .127f, .16f, .293f, .84f);
            ui.Title("RewardLabel", block, .339f, .12f, .624f, .88f, 27, caption);
            return block;
        }

        private void BuildSummary(RectTransform page)
        {
            detailsPanel = Frame("BattleSummary", page, .602f, .248f, .94f, .717f, ExpeditionFrameStyle.Panel);
            detailsBody = CampUI.Area("StageDetails", detailsPanel, 0, 0, 1, 1);
            var header = CampUI.Area("ToggleStageDetails", page, .614f, .635f, .929f, .710f);
            header.gameObject.AddComponent<Image>().color = Color.clear;
            detailsToggle = CampUI.BindButton(header, ToggleDetails);
            ui.Icon(ExpeditionGlyph.Helmet, header, .028f, .15f, .12f, .85f);
            ui.Title("SummaryHeading", header, .145f, .08f, .48f, .92f, 35, "关卡详情");
            Rule(header, .469f, .487f, .878f, .513f, new Color(.83f, .69f, .44f, .55f));
            Diamond(header, .469f, .5f, 6);
            detailsChevron = CampUI.Area("DetailChevron", header, .937f, .5f, .937f, .5f);
            ChevronStroke(detailsChevron, -5.5f, 42);
            ChevronStroke(detailsChevron, 5.5f, -42);

            var terrain = SummaryRow(detailsBody, "Terrain", .600f, .817f, ExpeditionGlyph.Mountains, "地形类型", out terrainName);
            routeCount = SummaryHint(terrain, "RouteCount", "");
            var enemies = SummaryRow(detailsBody, "Reinforcements", .358f, .575f, ExpeditionGlyph.Helmet, "增援间隔", out reinforcementGap);
            enemyKinds = SummaryHint(enemies, "EnemyKinds", "");
            var strategyRow = SummaryRow(detailsBody, "DefenseTactics", .105f, .333f, ExpeditionGlyph.Swords, "推荐策略", out strategy);
            for (int index = 0; index < tactics.Length; index++)
            {
                float left = .20f + index * .251f;
                var chip = Frame("Tactic" + index, strategyRow, left, .122f, left + .235f, .465f, ExpeditionFrameStyle.Slate);
                var label = ui.Text("Tactic", chip, .065f, .08f, .935f, .92f, 19, "", Ink);
                label.alignment = TextAlignmentOptions.Center;
                tactics[index] = label;
            }

            var detail = Frame("OpenExpeditionIntel", detailsBody, .563f, .022f, .95f, .090f, ExpeditionFrameStyle.Slate);
            intel = LabeledButton(detail, "敌军、塔型与补给", OpenIntel, out _, 18);
        }

        private static void ChevronStroke(Transform parent, float offset, float angle)
        {
            var stroke = CampUI.Area("ChevronStroke", parent, .5f, .5f, .5f, .5f);
            stroke.sizeDelta = new Vector2(16, 3);
            stroke.anchoredPosition = new Vector2(offset, 0);
            stroke.localRotation = Quaternion.Euler(0, 0, angle);
            var image = stroke.gameObject.AddComponent<Image>();
            image.color = Gold;
            image.raycastTarget = false;
        }

        private void ToggleDetails()
        {
            bool expanded = !detailsBody.gameObject.activeSelf;
            detailsBody.gameObject.SetActive(expanded);
            detailsPanel.anchorMin = new Vector2(detailsPanel.anchorMin.x, expanded ? .248f : .629f);
            detailsChevron.localRotation = expanded ? Quaternion.identity : Quaternion.Euler(0, 0, 180);
        }

        private RectTransform SummaryRow(Transform parent, string name, float bottom, float top, ExpeditionGlyph glyph, string label, out TMP_Text value)
        {
            var tile = Frame(name, parent, .036f, bottom, .964f, top, ExpeditionFrameStyle.Slate);
            ui.Icon(glyph, tile, .043f, .126f, .164f, .874f);
            ui.Title("Label", tile, .201f, .526f, .451f, .927f, 26, label);
            value = ui.Text("Value", tile, .455f, .517f, .961f, .941f, 28, "", Ink);
            value.fontStyle = FontStyles.Bold;
            return tile;
        }

        private TMP_Text SummaryHint(Transform row, string name, string text)
        {
            return ui.Text(name, row, .201f, .091f, .96f, .481f, 21, text, Muted, wrap: true);
        }

        private void BuildFooter(RectTransform page)
        {
            status = ui.Text("ExpeditionStatus", page, .15f, .161f, .85f, .204f, 19, "", Ink);
            status.alignment = TextAlignmentOptions.Center;
            var returnFrame = Frame("CancelExpedition", page, .239f, .066f, .471f, .153f, ExpeditionFrameStyle.Secondary);
            cancel = LabeledButton(returnFrame, "返回营地", Hide, out _, 39, ExpeditionGlyph.Tent);
            var goFrame = Frame("ConfirmExpedition", page, .509f, .064f, .774f, .155f, ExpeditionFrameStyle.Primary);
            confirm = LabeledButton(goFrame, "确认出征", Confirm, out confirmCaption, 41, ExpeditionGlyph.Swords, true);
        }

        private void BuildIntel(RectTransform page)
        {
            var overlay = CampUI.Overlay("ExpeditionIntel", page, .88f);
            intelRoot = overlay.gameObject;
            var panel = Frame("IntelPanel", overlay, .113f, .151f, .887f, .853f, ExpeditionFrameStyle.Panel);
            Section(panel, ExpeditionGlyph.Scroll, "战场情报", .872f, .978f, .192f);
            ui.Title("TowersHeading", panel, .051f, .766f, .451f, .856f, 29, "可部署防御塔");
            ui.Title("EnemiesHeading", panel, .494f, .766f, .95f, .856f, 29, "敌军情报");
            towerArea = CampUI.Area("TowerArea", panel, .05f, .386f, .455f, .756f);
            enemyArea = CampUI.Area("EnemyArea", panel, .495f, .386f, .95f, .756f);
            ui.Title("SuppliesHeading", panel, .051f, .304f, .451f, .374f, 29, "营地补给");
            var supplies = CampUI.Area("CampSupplies", panel, .05f, .157f, .95f, .300f);
            victorySupplies = Supply(supplies, 0, .45f, true, out victorySupplyCaption);
            defeatSupplies = Supply(supplies, .494f, 1, false, out defeatSupplyCaption);
            suppliesNote = ui.Text("SuppliesNote", panel, .05f, .049f, .69f, .139f, 21, "", Muted, wrap: true);
            var close = Frame("CloseExpeditionIntel", panel, .717f, .043f, .951f, .144f, ExpeditionFrameStyle.Secondary);
            LabeledButton(close, "返回准备", () => intelRoot.SetActive(false), out _, 25);
            intelRoot.SetActive(false);
        }

        private Button LabeledButton(
            RectTransform rect,
            string label,
            Action action,
            out TMP_Text caption,
            int size,
            ExpeditionGlyph? icon = null,
            bool primary = false)
        {
            float left = icon.HasValue ? .336f : .08f;
            if (icon.HasValue)
            {
                ui.Icon(icon.Value, rect, .159f, .102f, .318f, .898f);
            }

            caption = primary
                ? ui.Text("Caption", rect, left, .10f, .926f, .90f, size, label, Dark)
                : ui.Title("Caption", rect, left, .10f, .926f, .90f, size, label);
            caption.fontStyle = FontStyles.Bold;
            caption.alignment = TextAlignmentOptions.Center;
            return Bind(rect, action);
        }

        private void Section(Transform parent, ExpeditionGlyph glyph, string title, float bottom, float top, float titleRight)
        {
            ui.Icon(glyph, parent, .024f, bottom, .064f, top);
            ui.Title(title, parent, .076f, bottom, titleRight, top, 35, title);
            float middle = (top + bottom) * .5f;
            Rule(parent, titleRight + .008f, middle - .0012f, .973f, middle + .0012f, new Color(.83f, .69f, .44f, .55f));
            Diamond(parent, titleRight + .016f, middle, 6);
        }

        private static void Diamond(Transform parent, float x, float y, float size)
        {
            var rect = CampUI.Area("Diamond", parent, x, y, x, y);
            rect.sizeDelta = Vector2.one * size;
            rect.localRotation = Quaternion.Euler(0, 0, 45);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Gold;
            image.raycastTarget = false;
        }

        public void Show(int stageId = 0)
        {
            var plan = owner.PrepareExpedition(stageId);
            if (plan == null)
            {
                return;
            }

            activePlan = plan;
            intelRoot.SetActive(false);
            heading.text = plan.Stage.Name;
            chapter.text = plan.Chapter.title;
            objective.text = plan.Configuration.RelayRouteId == 0 ? plan.Chapter.objective : "守住主站。林道敌人会先经过点亮的副站。";
            UpdateBrief(plan);
            Refresh();
            root.transform.SetAsLastSibling();
            root.SetActive(true);
        }

        private void UpdateBrief(ExpeditionPlan plan)
        {
            var config = plan.Configuration;
            var brief = plan.Briefing;
            var assault = config.Objective.Assault ?? AssaultRules.Default;
            enemyTotal.text = brief.EnemyCount.ToString();
            gold.text = config.InitialGold.ToString();
            var camp = plan.CampLevel;
            victorySupplies.Wood.text = "木材 +" + camp.VictoryWood;
            victorySupplies.Stone.text = "石料 +" + camp.VictoryStone;
            defeatSupplies.Wood.text = "木材 +" + camp.DefeatWood;
            defeatSupplies.Stone.text = "石料 +" + camp.DefeatStone;
            suppliesNote.text = plan.Supplies ?? "出征补给按营地等级结算。";
            clearGold.text = "+" + brief.Waves.Sum(wave => (long)wave.RewardGold);
            cardReward.text = "击杀积分达 " + assault.FirstDraftPoints;
            terrainName.text = plan.Chapter.terrain ?? "海岛地形";
            int entrances = brief.Waves.SelectMany(wave => wave.RouteIds).Distinct().Count();
            routeCount.text = config.HidePlannedRoutes ? "入口以开战情报为准。" : "本场有 " + entrances + " 条进攻路线。";
            double gapSeconds = (long)assault.ReinforcementGapTicks * FightSession.TickMilliseconds / 1000.0;
            reinforcementGap.text = gapSeconds.ToString("0.##") + " 秒";
            enemyKinds.text = "共 " + brief.Enemies.Count + " 类敌军，持续来袭。";
            strategy.text = plan.Chapter.strategy ?? "沿路分段布防";
            var labels = plan.Chapter.tactics;
            for (int index = 0; index < tactics.Length; index++)
            {
                bool available = labels != null && index < labels.Length && !string.IsNullOrWhiteSpace(labels[index]);
                tactics[index].transform.parent.gameObject.SetActive(available);
                if (available)
                {
                    tactics[index].text = labels[index];
                }
            }

            UpdateObjective(config, brief.EnemyCount);
        }

        private void UpdateObjective(FightConfiguration config, int count)
        {
            var rule = config.Objective;
            bool hasBase = rule.HasBase;
            objectiveCaption.text = hasBase ? "基地耐久" : "逃脱上限";
            objectiveValue.text = (hasBase ? config.BaseHealth : rule.EscapesToDefeat - 1).ToString();
            victorySupplyCaption.text = hasBase ? "守住防线" : "完成拦截";
            defeatSupplyCaption.text = hasBase ? "基地失守" : "拦截失败";
            if (hasBase)
            {
                bool requiresAllKills = rule.Type == BattleObjectiveType.DefendBase;
                goal.text = requiresAllKills ? "击杀全部 " + count + " 名敌军。" : "守住" + rule.TargetName + "。";
                goalFailure.text = rule.TargetName + "耐久归零即失败。";
                return;
            }

            goal.text = "拦截前往" + rule.TargetName + "的敌军。";
            goalFailure.text = "累计逃脱 " + rule.EscapesToDefeat + " 名即失败。";
        }

        private void OpenIntel()
        {
            if (activePlan == null || owner.Entering)
            {
                return;
            }

            PopulateTowers(activePlan);
            PopulateEnemies(activePlan);
            intelRoot.SetActive(true);
        }

        private void PopulateTowers(ExpeditionPlan plan)
        {
            Clear(towerArea);
            var towers = plan.Configuration.Towers;
            const float stride = 76;
            float height = Mathf.Max(stride, towers.Length * stride);
            var list = CampUI.ScrollList("TowerRoster", towerArea, 0, 0, 1, 1, height);
            for (int index = 0; index < towers.Length; index++)
            {
                var tower = towers[index];
                float top = 1 - index * stride / height;
                var row = Frame(tower.Name, list, 0, top - 68 / height, 1, top, ExpeditionFrameStyle.Slate);
                ui.Title("TowerName", row, .060f, .104f, .66f, .897f, 24, tower.Name);
                ui.Icon(ExpeditionGlyph.Coins, row, .667f, .171f, .797f, .829f);
                int cost = tower.Id == plan.Configuration.OpeningDiscountTowerId
                    ? tower.BuildCost - plan.Configuration.OpeningDiscountGold : tower.BuildCost;
                ui.Text("BuildCost", row, .81f, .104f, .966f, .897f, 23, cost.ToString(), Gold);
            }
        }

        private void PopulateEnemies(ExpeditionPlan plan)
        {
            Clear(enemyArea);
            var enemies = plan.Briefing.Enemies;
            const float stride = 71;
            float height = Mathf.Max(stride, enemies.Count * stride);
            var list = CampUI.ScrollList("EnemyRoster", enemyArea, 0, 0, 1, 1, height);
            for (int index = 0; index < enemies.Count; index++)
            {
                var enemy = enemies[index];
                float top = 1 - index * stride / height;
                var row = Frame(enemy.Name, list, 0, top - 65 / height, 1, top, ExpeditionFrameStyle.Slate);
                ui.Title("EnemyName", row, .061f, .135f, .68f, .865f, 24, enemy.Name);
                var count = ui.Text("Count", row, .70f, .135f, .938f, .865f, 23, "×" + enemy.Count, Ink);
                count.alignment = TextAlignmentOptions.MidlineRight;
            }
        }

        private static void Clear(Transform parent)
        {
            foreach (Transform child in parent)
            {
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        public void Hide()
        {
            if (owner.Entering)
            {
                return;
            }

            if (intelRoot.activeSelf)
            {
                intelRoot.SetActive(false);
                return;
            }

            owner.CancelExpedition();
            activePlan = null;
            root.SetActive(false);
        }

        public void Refresh()
        {
            if (root == null)
            {
                return;
            }

            bool ready = !owner.Entering && activePlan != null;
            confirm.interactable = ready;
            intel.interactable = ready;
            detailsToggle.interactable = ready;
            back.interactable = !owner.Entering;
            cancel.interactable = !owner.Entering;
            confirmCaption.text = owner.Entering ? "准备战场中…" : "确认出征";
            foreach (var tab in stageTabs)
            {
                bool selected = tab.StageId == activePlan?.Stage.Id;
                bool available = owner.CanSelectStage(tab.StageId);
                tab.Button.interactable = !owner.Entering && available;
                tab.Frame.Style = selected ? ExpeditionFrameStyle.Selected : ExpeditionFrameStyle.Slate;
                tab.Name.color = available ? Ink : Muted;
                tab.Number.color = available ? Color.white : Muted;
                tab.Lock.gameObject.SetActive(!available);
            }

            status.color = owner.ExpeditionError == null ? Ink : Loss;
            status.text = owner.ExpeditionError ?? (owner.Entering ? "正在载入战场资源" : "");
        }

        private async void Confirm()
        {
            if (owner.Entering || activePlan == null)
            {
                return;
            }

            var opening = owner.EnterBattleAsync();
            Refresh();
            bool started = await opening;
            if (root != null)
            {
                if (started)
                {
                    activePlan = null;
                    root.SetActive(false);
                }

                Refresh();
            }
        }
    }
}
