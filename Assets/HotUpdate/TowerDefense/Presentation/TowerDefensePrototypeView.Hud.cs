using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.Camp;
using Game.HotUpdate.TowerDefense.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.HotUpdate.TowerDefense.Presentation
{
    public sealed partial class TowerDefensePrototypeView
    {
        internal const string HudAssetAddress = "Common/UI/BattleHud";
        internal const string HudFontAddress = "Common/Fonts/BattleHudMedium";

        // Slots in Tools~/build-battle-hud.py, read from the top left of the texture.
        private enum HudPiece
        {
            Panel, Card, Selected, Unavailable, Control, Upgrade, Sell, Badge, Track, Fill, Inset, ActiveBadge, LowHpFill,
            Wave = 16, Heart, Battle, Pause, Play, Camp, Menu, Deploy, Damage, Range, Speed, Output, UpArrow, Trade, Close, Insight
        }

        private readonly Sprite[] hudSprites = new Sprite[32];
        private readonly List<Image> hudFrames = new List<Image>();
        private static readonly Vector2 HudReferenceResolution = new Vector2(1920, 1080);
        private const float StatComparisonLeft = .304f;
        private const float StatComparisonRight = .585f;
        private const float StatComparisonWidth = 452f;
        private const float CurrentStatLeft = .396f;
        private const float CurrentStatRight = .618f;
        private const float NextStatLeft = .715f;
        private const float NextStatRight = .937f;
        private Sprite meterShape;
        private readonly Color cyan = new Color(.67f, .87f, 1f);
        private Image stateFrame;
        private Image pauseFrame;
        private Image pauseGlyph;
        private TMP_Text[] buildTags;
        private string[] towerRoles;
        private RectTransform openingCountdown;
        private TMP_Text openingCountdownText;
        private TMP_Text towerRole;
        private RectTransform statComparison;
        private (TMP_Text Current, TMP_Text Next) damageRow;
        private (TMP_Text Current, TMP_Text Next) attackSpeedRow;
        private (TMP_Text Current, TMP_Text Next) rangeRow;
        private RectTransform quickActions;
        private TMP_Text quickTitle;
        private Button quickUpgrade;
        private Button quickSell;
        private TMP_Text quickPrice;
        private TMP_Text quickRefund;
        private bool useQuickActions;
        private RectTransform baseInspector;
        private Button baseMarkerButton;
        private TMP_Text baseMarkerCaption;
        private TMP_Text baseLevelCaption;
        private TMP_Text baseDurability;
        private Image baseDurabilityFill;
        private TMP_Text baseUpgradeTitle;
        private TMP_Text baseUpgradePreview;
        private TMP_Text baseUpgradePrice;
        private TMP_Text baseUpgradeStatus;
        private Button baseUpgradeButton;

        private void PrepareHudSprites(Texture2D atlas)
        {
            const int cell = 128;
            if (atlas == null || atlas.width != 8 * cell || atlas.height != 4 * cell)
            {
                meterShape = CreateHudShape(capsule: true);
                return;
            }

            foreach (HudPiece piece in Enum.GetValues(typeof(HudPiece)))
            {
                int index = (int)piece;
                bool slice = index < (int)HudPiece.Wave;
                int inset = slice ? 0 : 12;
                int size = cell - inset * 2;
                int x = index % 8 * cell;
                int y = (3 - index / 8) * cell;
                var rect = new Rect(x + inset, y + inset, size, size);
                var border = slice ? new Vector4(44, 44, 44, 44) : Vector4.zero;
                if (IsCapsule(piece))
                {
                    rect = new Rect(x + 8, y + 48, 112, 32);
                    border = new Vector4(16, 0, 16, 0);
                }

                var sprite = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
                sprite.name = "Battle HUD " + piece;
                hudSprites[index] = sprite;
                materialAssets.Add(sprite);
            }
        }

        private static bool IsMeter(HudPiece piece)
        {
            return piece == HudPiece.Track || piece == HudPiece.Fill || piece == HudPiece.LowHpFill;
        }

        private static bool IsCapsule(HudPiece piece)
        {
            return IsMeter(piece) || piece == HudPiece.Badge || piece == HudPiece.ActiveBadge;
        }

        private void CreateBattleStatus()
        {
            var status = HudPlate("BattleStatus", safeArea, .014f, .882f, .457f, .986f, HudPiece.Panel);
            var map = HudButton("OpenMapDetails", status, .018f, .113f, .308f, .887f, HudPiece.Control, OpenMapDetails).transform;
            Glyph(map, HudPiece.Wave, .046f, .165f, .258f, .835f, cyan);
            var mapName = StatusValue(map, "MapName", .287f, .860f, 30);
            mapName.text = configuration.StageName;
            mapName.overflowMode = TextOverflowModes.Ellipsis;
            Glyph(map, HudPiece.Insight, .876f, .315f, .972f, .685f, cyan);

            bool hasBase = configuration.Objective.HasBase;
            var objective = Area("Objective", status, new Vector2(.345f, .135f), new Vector2(.715f, .865f));
            if (hasBase)
            {
                Glyph(objective, HudPiece.Camp, 0, .045f, .220f, .955f, cyan);
            }
            else
            {
                Glyph(objective, HudPiece.Range, .025f, .14f, .195f, .86f, cyan);
            }

            objectiveText = Label("ObjectiveProgress", objective, new Vector2(.225f, .29f), new Vector2(.960f, .88f), 31, TextAnchor.MiddleCenter);
            objectiveText.alignment = TextAlignmentOptions.Midline;
            objectiveText.fontStyle = FontStyles.Bold;
            objectiveText.enableWordWrapping = false;
            objectiveFill = HudMeter(objective, .225f, .12f, .960f, .24f);

            var funds = Area("Funds", status, new Vector2(.748f, .135f), new Vector2(.976f, .865f));
            Icon("Coin", funds, new Vector2(-.030f, .045f), new Vector2(.345f, .955f), 6);
            goldText = StatusValue(funds, "Gold", .365f, .980f, 35);
            goldText.alignment = TextAlignmentOptions.MidlineLeft;
            goldText.color = gold;

            Separator(status, .325f, .20f, .327f, .80f);
            Separator(status, .728f, .20f, .730f, .80f);
        }

        private TMP_Text StatusValue(Transform block, string name, float left, float right, int size)
        {
            var value = Label(name, block, new Vector2(left, .10f), new Vector2(right, .90f), size, TextAnchor.MiddleLeft);
            value.fontStyle = FontStyles.Bold;
            value.enableWordWrapping = false;
            return value;
        }

        private void CreateSystemControls()
        {
            var dock = HudPlate("SystemControls", safeArea, .780f, .899f, .988f, .985f, HudPiece.Panel);
            var pause = SystemButton(dock, "Pause", "暂停", HudPiece.Pause, .025f, .333f, TogglePause, out pauseGlyph);
            pauseFrame = pause.image;
            pauseLabel = pause.GetComponentInChildren<TMP_Text>();
            SystemButton(dock, "营地", "营地", HudPiece.Camp, .346f, .654f, RequestExit, out _);
            SystemButton(dock, "系统菜单", "菜单", HudPiece.Menu, .667f, .975f, ToggleSystemMenu, out _);
        }

        private Button SystemButton(Transform parent, string name, string caption, HudPiece icon, float left, float right, UnityAction click, out Image glyph)
        {
            var button = HudButton(name, parent, left, .10f, right, .90f, HudPiece.Control, click);
            glyph = Glyph(button.transform, icon, .08f, .16f, .40f, .84f);
            Label("Caption", button.transform, new Vector2(.41f, .11f), new Vector2(.93f, .89f), 25, TextAnchor.MiddleCenter).text = caption;
            return button;
        }

        private void CreateBuildSidebar()
        {
            var side = HudPlate("BuildBar", safeArea, .780f, .450f, .988f, .889f, HudPiece.Panel);
            Glyph(side, HudPiece.Deploy, .048f, .898f, .133f, .974f, cyan);
            var heading = Label("BuildCaption", side, new Vector2(.152f, .886f), new Vector2(.946f, .981f), 31, TextAnchor.MiddleLeft);
            heading.text = "防御塔部署";
            heading.fontStyle = FontStyles.Bold;
            notice = Label("Help", side, new Vector2(.056f, .026f), new Vector2(.945f, .113f), 19, TextAnchor.MiddleLeft);
            notice.color = muted;

            var definitions = configuration.Towers;
            int fastest = definitions.Min(tower => tower.AttackIntervalTicks);
            int highestDamage = definitions.Max(tower => tower.Damage);
            int longestRange = definitions.Max(tower => tower.Range);
            towerRoles = new string[definitions.Length];
            buildTags = new TMP_Text[definitions.Length];
            buildPrices = new TMP_Text[definitions.Length];
            const float height = 118;
            const float stride = 120;
            var list = CampUI.ScrollList("TowerCards", side, .045f, .125f, .982f, .889f, (definitions.Length - 1) * stride + height);
            list.parent.GetComponent<ScrollRect>().verticalScrollbar.targetGraphic.color = cyan;
            for (int index = 0; index < definitions.Length; index++)
            {
                var definition = definitions[index];
                int id = definition.Id;
                bool hasPortrait = index < 3;
                towerRoles[index] = definitions.Length == 1 ? "单体" : definition.AttackIntervalTicks == fastest ? "速射"
                    : definition.Damage == highestDamage ? "重击" : definition.Range == longestRange ? "远射" : "单体";
                var button = HudButton("Build " + index, list, 0, 1, 1, 1, HudPiece.Card, () => SelectDefinition(id));
                var rect = (RectTransform)button.transform;
                rect.pivot = new Vector2(.5f, 1);
                rect.sizeDelta = new Vector2(0, height);
                rect.anchoredPosition = new Vector2(0, -index * stride);
                if (hasPortrait)
                {
                    Icon("TowerIcon", rect, new Vector2(.04f, .075f), new Vector2(.31f, .925f), index);
                }

                float left = hasPortrait ? .34f : .075f;
                var name = Label("TowerName", rect, new Vector2(left, .49f), new Vector2(.94f, .88f), 30, TextAnchor.MiddleLeft);
                name.text = definition.Name;
                name.fontStyle = FontStyles.Bold;
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                Icon("Coin", rect, new Vector2(left, .14f), new Vector2(left + .075f, .44f), 6);
                var price = Label("TowerCost", rect, new Vector2(left + .085f, .11f), new Vector2(.665f, .46f), 28, TextAnchor.MiddleLeft);
                price.text = definition.BuildCost.ToString();
                price.color = gold;
                price.fontStyle = FontStyles.Bold;
                buildPrices[index] = price;
                var chip = HudPlate("TowerRole", rect, .680f, .145f, .950f, .430f, HudPiece.Badge);
                buildTags[index] = Label("Role", chip, new Vector2(.04f, .025f), new Vector2(.96f, .975f), 19, TextAnchor.MiddleCenter);
                buildButtons.Add(button);
            }

            CreateAssaultControl();
        }

        private void CreateAssaultControl()
        {
            var panel = HudPlate("AssaultControl", safeArea, .780f, .228f, .988f, .435f, HudPiece.Panel);
            Glyph(panel, HudPiece.Insight, .048f, .808f, .133f, .958f, cyan);
            var title = Label("AssaultCaption", panel, new Vector2(.152f, .800f), new Vector2(.375f, .970f), 28, TextAnchor.MiddleLeft);
            title.text = "战况";
            title.fontStyle = FontStyles.Bold;
            var state = HudPlate("State", panel, .639f, .818f, .950f, .948f, HudPiece.Badge);
            stateFrame = state.GetComponent<Image>();
            waveStatus = Label("WaveStatus", state, new Vector2(.06f, .04f), new Vector2(.94f, .96f), 21, TextAnchor.MiddleCenter);
            waveStatus.alignment = TextAlignmentOptions.Midline;
            waveStatus.fontStyle = FontStyles.Bold;
            waveStatus.enableWordWrapping = false;
            var buff = HudPlate("Enhancements", panel, .390f, .818f, .620f, .948f, HudPiece.Badge);
            buffsText = Label("Buffs", buff, new Vector2(.04f, .06f), new Vector2(.96f, .94f), 18, TextAnchor.MiddleCenter);
            buffsText.color = cyan;
            Separator(panel, .052f, .783f, .948f, .789f);

            var scoreCaption = Label("DraftScoreCaption", panel, new Vector2(.052f, .596f), new Vector2(.56f, .768f), 23, TextAnchor.MiddleLeft);
            scoreCaption.text = "击杀积分";
            scoreCaption.fontStyle = FontStyles.Bold;
            draftScoreText = Label("DraftScore", panel, new Vector2(.56f, .596f), new Vector2(.945f, .768f), 23, TextAnchor.MiddleLeft);
            draftScoreText.alignment = TextAlignmentOptions.MidlineRight;
            draftScoreText.color = gold;
            draftProgress = HudMeter(panel, .052f, .543f, .946f, .594f);
            reinforcementText = Label("Reinforcement", panel, new Vector2(.052f, .326f), new Vector2(.946f, .530f), 22, TextAnchor.MiddleLeft);
            reinforcementText.color = muted;
            killTallyText = Label("KillTally", panel, new Vector2(.052f, .065f), new Vector2(.51f, .294f), 20, TextAnchor.MiddleLeft);
            killTallyText.color = muted;
            openingCountdown = HudPlate("OpeningCountdown", panel, .052f, .048f, .426f, .306f, HudPiece.Inset);
            var countdownCaption = Label("Caption", openingCountdown, new Vector2(.05f, .54f), new Vector2(.95f, .94f), 16, TextAnchor.MiddleCenter);
            countdownCaption.text = "进攻倒计时";
            countdownCaption.color = muted;
            openingCountdownText = Label("Seconds", openingCountdown, new Vector2(.05f, .07f), new Vector2(.95f, .54f), 24, TextAnchor.MiddleCenter);
            openingCountdownText.color = gold;
            openingCountdownText.fontStyle = FontStyles.Bold;
            startBattle = HudButton("StartAssault", panel, .452f, .048f, .948f, .306f, HudPiece.Upgrade, StartBattle);
            var startCaption = Label("Label", startBattle.transform, new Vector2(.08f, .08f), new Vector2(.92f, .92f), 26, TextAnchor.MiddleCenter);
            startCaption.text = "立即开战";
            startCaption.fontStyle = FontStyles.Bold;
            if (configuration.Objective.HasBase && configuration.PlanningRepair > 0)
            {
                repairBase = HudButton("RepairBase", panel, .54f, .048f, .948f, .306f, HudPiece.Control, RepairBase);
                var repair = Label("Label", repairBase.transform, new Vector2(.05f, .08f), new Vector2(.95f, .92f), 22, TextAnchor.MiddleCenter);
                repair.text = "修理 +" + configuration.PlanningRepair;
                repair.fontStyle = FontStyles.Bold;
            }
        }

        private void CreateTowerInspector()
        {
            var panel = HudPlate("TowerInspector", safeArea, .014f, .017f, .988f, .174f, HudPiece.Panel);
            towerInspector = panel.gameObject;
            var portrait = HudPlate("PortraitFrame", panel, .012f, .095f, .105f, .905f, HudPiece.Selected);
            towerPortrait = Area("Portrait", portrait, new Vector2(.045f, .045f), new Vector2(.955f, .955f)).gameObject.AddComponent<Image>();
            towerPortrait.preserveAspect = true;
            towerPortrait.raycastTarget = false;

            towerName = Label("SelectedTower", panel, new Vector2(.117f, .625f), new Vector2(.294f, .905f), 38, TextAnchor.MiddleLeft);
            towerName.fontStyle = FontStyles.Bold;
            towerName.enableWordWrapping = false;
            towerName.overflowMode = TextOverflowModes.Ellipsis;
            var level = HudPlate("Level", panel, .117f, .388f, .173f, .593f, HudPiece.ActiveBadge);
            towerLevel = Label("LevelText", level, new Vector2(.06f, .06f), new Vector2(.94f, .94f), 22, TextAnchor.MiddleCenter);
            var role = HudPlate("Role", panel, .179f, .388f, .242f, .593f, HudPiece.Badge);
            towerRole = Label("RoleText", role, new Vector2(.04f, .06f), new Vector2(.96f, .94f), 21, TextAnchor.MiddleCenter);
            towerRole.color = cyan;
            var targetRule = Label("TargetRule", panel, new Vector2(.117f, .095f), new Vector2(.286f, .355f), 20, TextAnchor.MiddleLeft);
            targetRule.text = configuration.Objective.HasBase ? "优先攻击靠近基地的敌人" : "优先攻击靠近传送门的敌人";
            targetRule.color = muted;
            Separator(panel, .291f, .13f, .292f, .87f);
            var stats = Area("StatComparison", panel, new Vector2(StatComparisonLeft, .080f), new Vector2(StatComparisonLeft, .920f));
            stats.pivot = new Vector2(0, .5f);
            statComparison = stats;
            var nextColumn = Panel("NextLevelTint", stats, new Vector2(.706f, .020f), new Vector2(.949f, .980f), new Color(.48f, .76f, .91f, .09f));
            nextColumn.GetComponent<Image>().raycastTarget = false;
            var statTitle = Label("StatTitle", stats, new Vector2(.026f, .758f), new Vector2(.443f, .968f), 21, TextAnchor.MiddleLeft);
            statTitle.text = "属性";
            statTitle.color = muted;
            var currentTitle = Label(
                "CurrentTitle",
                stats,
                new Vector2(CurrentStatLeft, .758f),
                new Vector2(CurrentStatRight, .968f),
                21,
                TextAnchor.MiddleCenter);
            currentTitle.text = "当前";
            currentTitle.color = muted;
            var nextTitle = Label("NextTitle", stats, new Vector2(NextStatLeft, .758f), new Vector2(NextStatRight, .968f), 21, TextAnchor.MiddleCenter);
            nextTitle.text = "下一级";
            nextTitle.color = cyan;
            statTitle.enableWordWrapping = currentTitle.enableWordWrapping = nextTitle.enableWordWrapping = false;
            Separator(stats, .026f, .747f, .949f, .754f);
            damageRow = CreateTowerStatRow(stats, "攻击力", "", HudPiece.Damage, .505f, .746f);
            attackSpeedRow = CreateTowerStatRow(stats, "攻速", "次/秒", HudPiece.Speed, .259f, .500f);
            rangeRow = CreateTowerStatRow(stats, "射程", "米", HudPiece.Range, .013f, .254f);
            Separator(stats, .026f, .499f, .949f, .506f);
            Separator(stats, .026f, .253f, .949f, .260f);

            upgrade = HudButton("Upgrade", panel, .601f, .114f, .713f, .962f, HudPiece.Upgrade, UpgradeSelected);
            upgradePrice = TowerActionContent(upgrade.transform, "升级", HudPiece.UpArrow);
            upgradeStatus = Label("UpgradeStatus", panel, new Vector2(.601f, .007f), new Vector2(.713f, .114f), 17, TextAnchor.MiddleCenter);
            sell = HudButton("Sell", panel, .723f, .114f, .835f, .962f, HudPiece.Sell, SellSelected);
            sellRefund = TowerActionContent(sell.transform, "出售", HudPiece.Trade);
            var close = HudButton("Close", panel, .967f, .687f, .995f, .961f, HudPiece.Control, CloseSelection);
            var closeIcon = Glyph(close.transform, HudPiece.Close, .13f, .13f, .87f, .87f);
            if (!closeIcon.enabled)
            {
                Label("CloseMark", close.transform, Vector2.zero, Vector2.one, 25, TextAnchor.MiddleCenter).text = "×";
            }

            towerInspector.SetActive(false);
        }

        private void FitTowerStatComparison()
        {
            var panel = (RectTransform)statComparison.parent;
            float width = panel.rect.width * (StatComparisonRight - StatComparisonLeft);
            statComparison.sizeDelta = new Vector2(Mathf.Min(StatComparisonWidth, width), 0);
        }

        private (TMP_Text Current, TMP_Text Next) CreateTowerStatRow(Transform parent, string caption, string unit, HudPiece icon, float bottom, float top)
        {
            var row = Area(caption, parent, new Vector2(0, bottom), new Vector2(1, top));
            Glyph(row, icon, .026f, .145f, .079f, .855f, cyan);
            var name = Label("StatName", row, new Vector2(.098f, 0), new Vector2(.443f, 1), 24, TextAnchor.MiddleLeft);
            name.enableWordWrapping = false;
            name.text = caption;
            if (unit.Length != 0)
            {
                name.text += $"  <size=78%><color=#{ColorUtility.ToHtmlStringRGB(muted)}>{unit}</color></size>";
            }

            var current = Label("CurrentValue", row, new Vector2(CurrentStatLeft, 0), new Vector2(CurrentStatRight, 1), 28, TextAnchor.MiddleCenter);
            var next = Label("NextValue", row, new Vector2(NextStatLeft, 0), new Vector2(NextStatRight, 1), 28, TextAnchor.MiddleCenter);
            current.fontStyle = next.fontStyle = FontStyles.Bold;
            current.enableWordWrapping = next.enableWordWrapping = false;
            return (current, next);
        }

        private void SetTowerStat((TMP_Text Current, TMP_Text Next) row, string current, string next)
        {
            bool changed = next != null && next != current;
            row.Current.text = current;
            row.Next.text = next ?? "—";
            row.Next.color = changed ? healthColor : muted;
        }

        private void CreateObjectiveControls()
        {
            string targetName = configuration.Objective.TargetName;
            if (configuration.Objective.BaseUpgrades.Count == 0)
            {
                goalMarker = CreateRouteMarker(targetName, new Color(.15f, .31f, .36f, .95f));
                return;
            }

            baseMarkerButton = HudButton("BaseMarker", safeArea, .5f, .5f, .5f, .5f, HudPiece.Control, SelectBase);
            goalMarker = (RectTransform)baseMarkerButton.transform;
            goalMarker.sizeDelta = new Vector2(170, 38);
            Glyph(goalMarker, HudPiece.UpArrow, .050f, .15f, .214f, .85f, cyan);
            baseMarkerCaption = Label("BaseMarkerCaption", goalMarker, new Vector2(.22f, .07f), new Vector2(.955f, .93f), 21, TextAnchor.MiddleCenter);
            baseMarkerCaption.enableWordWrapping = false;

            baseInspector = HudPlate("BaseInspector", safeArea, .014f, .017f, .454f, .186f, HudPiece.Panel);
            Glyph(baseInspector, HudPiece.Camp, .026f, .188f, .118f, .846f, cyan);
            const float identityCenter = .261f;
            var heading = Area("BaseHeading", baseInspector, new Vector2(identityCenter, .572f), new Vector2(identityCenter, .902f));
            var title = Label("BaseName", heading, Vector2.zero, Vector2.up, 35, TextAnchor.MiddleLeft);
            title.text = targetName;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.MidlineLeft;
            title.enableWordWrapping = false;
            float titleWidth = Mathf.Ceil(title.GetPreferredValues(targetName).x) + 4;
            const float badgeGap = 12;
            const float badgeWidth = 74;
            heading.sizeDelta = new Vector2(titleWidth + badgeGap + badgeWidth, 0);
            title.rectTransform.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, 0, titleWidth);
            var badge = HudPlate("BaseLevel", heading, 0, .185f, 0, .815f, HudPiece.ActiveBadge);
            badge.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Left, titleWidth + badgeGap, badgeWidth);
            baseLevelCaption = Label("LevelCaption", badge, new Vector2(.06f, .065f), new Vector2(.94f, .935f), 22, TextAnchor.MiddleCenter);
            baseLevelCaption.alignment = TextAlignmentOptions.Midline;
            baseLevelCaption.enableWordWrapping = false;
            baseDurability = Label(
                "BaseDurability", baseInspector, new Vector2(identityCenter - .17f, .306f), new Vector2(identityCenter + .17f, .564f),
                30, TextAnchor.MiddleCenter);
            baseDurability.alignment = TextAlignmentOptions.Midline;
            baseDurability.fontStyle = FontStyles.Bold;
            baseDurability.enableWordWrapping = false;
            baseDurabilityFill = HudMeter(baseInspector, identityCenter - .136f, .162f, identityCenter + .136f, .250f);

            Separator(baseInspector, .476f, .136f, .478f, .868f);
            baseUpgradeButton = HudButton("FortifyBase", baseInspector, .514f, .103f, .960f, .918f, HudPiece.Upgrade, UpgradeBase);
            var action = baseUpgradeButton.transform;
            Glyph(action, HudPiece.UpArrow, .056f, .23f, .240f, .77f);
            baseUpgradeTitle = Label("BaseUpgradeTitle", action, new Vector2(.265f, .640f), new Vector2(.960f, .930f), 27, TextAnchor.MiddleCenter);
            baseUpgradeTitle.alignment = TextAlignmentOptions.Midline;
            baseUpgradeTitle.fontStyle = FontStyles.Bold;
            baseUpgradeTitle.enableWordWrapping = false;
            baseUpgradePreview = Label("NextDurability", action, new Vector2(.265f, .345f), new Vector2(.960f, .635f), 28, TextAnchor.MiddleCenter);
            baseUpgradePreview.alignment = TextAlignmentOptions.Midline;
            baseUpgradePreview.fontStyle = FontStyles.Bold;
            baseUpgradePreview.enableWordWrapping = false;

            var amount = Area("CoinValue", action, new Vector2(.265f, .060f), new Vector2(.960f, .340f));
            var amountLayout = amount.gameObject.AddComponent<HorizontalLayoutGroup>();
            amountLayout.childAlignment = TextAnchor.MiddleCenter;
            amountLayout.spacing = 6;
            amountLayout.childControlWidth = true;
            amountLayout.childControlHeight = true;
            amountLayout.childForceExpandWidth = false;
            amountLayout.childForceExpandHeight = false;
            var coin = Icon("Coin", amount, Vector2.zero, Vector2.one, 6).gameObject.AddComponent<LayoutElement>();
            coin.preferredWidth = 25;
            coin.preferredHeight = 25;
            baseUpgradePrice = Label("Amount", amount, Vector2.zero, Vector2.one, 28, TextAnchor.MiddleCenter);
            baseUpgradePrice.alignment = TextAlignmentOptions.Midline;
            baseUpgradePrice.enableAutoSizing = false;
            baseUpgradePrice.enableWordWrapping = false;
            baseUpgradePrice.fontStyle = FontStyles.Bold;
            baseUpgradePrice.color = gold;
            baseUpgradeStatus = Label("BaseUpgradeStatus", action, new Vector2(.026f, .069f), new Vector2(.270f, .228f), 18, TextAnchor.MiddleCenter);
            baseUpgradeStatus.text = "金币不足";
            baseUpgradeStatus.alignment = TextAlignmentOptions.Midline;
            baseUpgradeStatus.color = danger;
            baseUpgradeStatus.enableWordWrapping = false;
            baseInspector.gameObject.SetActive(false);
        }

        private void RefreshBaseInspector(bool activePhase)
        {
            if (baseInspector == null)
            {
                return;
            }

            string level = "Lv." + session.BaseLevel;
            baseMarkerCaption.text = configuration.Objective.TargetName + " · " + level;
            baseMarkerButton.image.raycastTarget = !buildMode;
            ApplyHudPiece(baseMarkerButton.image, selectedBase ? HudPiece.Selected : HudPiece.Control);
            bool visible = selectedBase && activePhase;
            baseInspector.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            baseLevelCaption.text = level;
            baseDurability.text = session.BaseHp + " / " + session.BaseMaxHp;
            float durability = session.BaseHp / (float)session.BaseMaxHp;
            ApplyHudPiece(baseDurabilityFill, durability <= .5f ? HudPiece.LowHpFill : HudPiece.Fill);
            if (durability > .25f && durability <= .5f)
            {
                baseDurabilityFill.color = hudSprites[(int)HudPiece.LowHpFill] != null ? new Color(1f, .92f, .42f) : gold;
            }

            SetProgress(baseDurabilityFill, durability);
            var next = session.NextBaseUpgrade;
            baseUpgradePrice.transform.parent.gameObject.SetActive(next.HasValue);
            baseUpgradeStatus.gameObject.SetActive(next.HasValue && session.Gold < next.Value.GoldCost);
            if (next is BaseUpgrade upgrade)
            {
                baseUpgradeTitle.text = "加固基地";
                baseUpgradePreview.text = session.BaseMaxHp + " → <color=#9AF2B5>" + (session.BaseMaxHp + upgrade.DurabilityIncrease) + "</color>";
                baseUpgradePrice.text = upgrade.GoldCost.ToString();
            }
            else
            {
                baseUpgradeTitle.text = "加固完成";
                baseUpgradePreview.text = "已达最高等级";
            }

            baseUpgradeButton.interactable = session.CanUpgradeBase;
            ApplyHudPiece(baseUpgradeButton.image, baseUpgradeButton.interactable ? HudPiece.Upgrade : HudPiece.Unavailable);
        }

        private TMP_Text TowerActionContent(Transform button, string caption, HudPiece icon)
        {
            Glyph(button, icon, .31f, .560f, .69f, .914f);
            var label = Label("Action", button, new Vector2(.10f, .302f), new Vector2(.90f, .579f), 28, TextAnchor.MiddleCenter);
            label.text = caption;
            label.fontStyle = FontStyles.Bold;
            var amount = Area("CoinValue", button, new Vector2(.20f, .080f), new Vector2(.86f, .318f));
            return CoinValue(amount);
        }

        private void CreateTowerQuickActions()
        {
            quickActions = HudPlate("TowerQuickActions", safeArea, .50f, .50f, .50f, .50f, HudPiece.Selected);
            quickActions.pivot = new Vector2(0, .5f);
            quickTitle = Label("TowerAndLevel", quickActions, new Vector2(.075f, .720f), new Vector2(.935f, .965f), 23, TextAnchor.MiddleLeft);
            quickTitle.fontStyle = FontStyles.Bold;
            quickTitle.enableWordWrapping = false;
            quickUpgrade = HudButton("Quick upgrade", quickActions, .044f, .392f, .956f, .722f, HudPiece.Upgrade, UpgradeSelected);
            quickPrice = QuickActionContent(quickUpgrade.transform, "升级", HudPiece.UpArrow);
            quickSell = HudButton("Quick sell", quickActions, .044f, .050f, .956f, .380f, HudPiece.Sell, SellSelected);
            quickRefund = QuickActionContent(quickSell.transform, "出售", HudPiece.Trade);
            quickActions.gameObject.SetActive(false);
        }

        private TMP_Text QuickActionContent(Transform button, string caption, HudPiece icon)
        {
            Glyph(button, icon, .056f, .14f, .250f, .86f);
            var label = Label("Action", button, new Vector2(.27f, .13f), new Vector2(.55f, .87f), 24, TextAnchor.MiddleLeft);
            label.text = caption;
            label.fontStyle = FontStyles.Bold;
            var amount = Area("CoinValue", button, new Vector2(.57f, .19f), new Vector2(.95f, .81f));
            return CoinValue(amount);
        }

        private TMP_Text CoinValue(RectTransform amount)
        {
            Icon("Coin", amount, new Vector2(0, .07f), new Vector2(.29f, .93f), 6);
            var value = Label("Amount", amount, new Vector2(.32f, .02f), Vector2.one, 24, TextAnchor.MiddleLeft);
            value.color = gold;
            value.fontStyle = FontStyles.Bold;
            value.fontSizeMin = 14;
            value.enableWordWrapping = false;
            return value;
        }

        private void RefreshSelectedTower(bool canBuild)
        {
            bool selected = false;
            foreach (var tower in session.Towers)
            {
                if (tower.Id != selectedTower)
                {
                    continue;
                }

                selected = true;
                var definition = FindTower(tower.DefinitionId);
                int index = Array.IndexOf(configuration.Towers, definition);
                towerPortrait.sprite = index >= 0 && index < Math.Min(3, generatedIcons.Count) ? generatedIcons[index] : null;
                towerPortrait.enabled = towerPortrait.sprite != null;
                towerName.text = definition.Name;
                towerLevel.text = "Lv." + tower.Level;
                towerRole.text = towerRoles[index];
                int cost = definition.UpgradeCost * tower.Level;
                bool max = tower.Level >= definition.MaxLevel;
                bool affordable = session.Gold >= cost;
                var (nextDamage, nextRange) = max ? (0, 0) : session.PreviewTowerUpgrade(tower);
                string speed = (1000f / (tower.AttackIntervalTicks * FightSession.TickMilliseconds)).ToString("0.00");
                SetTowerStat(damageRow, tower.Damage.ToString(), max ? null : nextDamage.ToString());
                SetTowerStat(attackSpeedRow, speed, max ? null : speed);
                SetTowerStat(rangeRow, (tower.Range / 1000f).ToString("0.00"), max ? null : (nextRange / 1000f).ToString("0.00"));
                string price = cost.ToString();
                string refund = "+" + tower.SellValue;
                upgradePrice.text = quickPrice.text = price;
                sellRefund.text = quickRefund.text = refund;
                upgradePrice.transform.parent.gameObject.SetActive(!max);
                quickPrice.transform.parent.gameObject.SetActive(!max);
                upgradeStatus.text = max ? "已满级" : "金币不足";
                upgradeStatus.color = max ? muted : danger;
                upgradeStatus.gameObject.SetActive(max || !affordable);
                upgrade.interactable = quickUpgrade.interactable = canBuild && !max && affordable;
                sell.interactable = quickSell.interactable = canBuild;
                var style = max || !affordable ? HudPiece.Unavailable : HudPiece.Upgrade;
                ApplyHudPiece(upgrade.image, style);
                ApplyHudPiece(quickUpgrade.image, style);
                if (useQuickActions)
                {
                    quickTitle.text = "Lv." + tower.Level + "  " + definition.Name;
                    PositionQuickActions(tower.Position);
                }

                break;
            }

            towerInspector.SetActive(selected && !useQuickActions);
            quickActions.gameObject.SetActive(selected && useQuickActions);
        }

        private void PositionQuickActions(Position2 tower)
        {
            var screen = worldCamera.WorldToScreenPoint(field.TransformPoint(World(tower, .75f)));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(safeArea, screen, null, out var point);
            var bounds = safeArea.rect;
            float width = Mathf.Min(239, bounds.width * .30f);
            float height = Mathf.Min(174, bounds.height * .26f);
            float right = bounds.xMin + bounds.width * BattlefieldRight - 10;
            float top = bounds.yMin + bounds.height * BattlefieldTop;
            float gap = Mathf.Min(66, bounds.width * .047f);
            float x = point.x + gap;
            if (x + width > right)
            {
                x = point.x - gap - width;
            }

            quickActions.sizeDelta = new Vector2(width, height);
            quickActions.anchoredPosition = new Vector2(
                Mathf.Clamp(x, bounds.xMin + 9, right - width),
                Mathf.Clamp(point.y, bounds.yMin + height * .5f + 9, top - height * .5f));
        }

        private Image HudMeter(Transform parent, float left, float bottom, float right, float top)
        {
            var track = HudPlate("Track", parent, left, bottom, right, top, HudPiece.Track);
            track.GetComponent<Image>().raycastTarget = false;
            var fill = HudPlate("Fill", track, 0, 0, 1, 1, HudPiece.Fill).GetComponent<Image>();
            fill.raycastTarget = false;
            return fill;
        }

        private void Separator(Transform parent, float left, float bottom, float right, float top)
        {
            var image = Panel("Separator", parent, new Vector2(left, bottom), new Vector2(right, top), divider).GetComponent<Image>();
            image.raycastTarget = false;
        }

        private Image Glyph(Transform parent, HudPiece piece, float left, float bottom, float right, float top, Color? tint = null)
        {
            var image = Area(piece + " glyph", parent, new Vector2(left, bottom), new Vector2(right, top)).gameObject.AddComponent<Image>();
            image.sprite = hudSprites[(int)piece];
            image.enabled = image.sprite != null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = tint ?? Color.white;
            return image;
        }

        private RectTransform HudPlate(string name, Transform parent, float left, float bottom, float right, float top, HudPiece piece)
        {
            var rect = Area(name, parent, new Vector2(left, bottom), new Vector2(right, top));
            ApplyHudPiece(rect.gameObject.AddComponent<Image>(), piece);
            return rect;
        }

        private Button HudButton(string name, Transform parent, float left, float bottom, float right, float top, HudPiece piece, UnityAction click)
        {
            return MakeButton(name, parent, new Vector2(left, bottom), new Vector2(right, top), click, false, piece);
        }

        private void ApplyHudPiece(Image image, HudPiece piece)
        {
            var sprite = hudSprites[(int)piece];
            bool meter = IsMeter(piece);
            if (!meter && image.sprite == null)
            {
                hudFrames.Add(image);
            }

            image.sprite = sprite != null ? sprite : IsCapsule(piece) ? meterShape : hudShape;
            image.type = Image.Type.Sliced;
            if (!meter)
            {
                ResizeHudFrame(image);
            }

            image.color = sprite != null ? Color.white : piece switch
            {
                HudPiece.Panel => hud,
                HudPiece.Selected or HudPiece.ActiveBadge or HudPiece.Upgrade => action,
                HudPiece.Sell => new Color(.51f, .25f, .31f),
                HudPiece.Unavailable or HudPiece.Track => new Color(.089f, .172f, .211f),
                HudPiece.Fill => healthColor,
                HudPiece.LowHpFill => danger,
                _ => surface
            };
        }

        private void ResizeHudFrame(Image image)
        {
            var size = image.GetPixelAdjustedRect().size;
            if (size.x <= 0 || size.y <= 0)
            {
                return;
            }

            var border = image.sprite.border;
            if (border.y == 0 && border.w == 0)
            {
                image.pixelsPerUnitMultiplier = image.sprite.rect.height / size.y;
                return;
            }

            float canvasScale = Mathf.Sqrt(Screen.width / HudReferenceResolution.x * (Screen.height / HudReferenceResolution.y));
            float scale = (image.sprite == hudShape ? 1f : 1.27f) * Mathf.Min(1, canvasScale * 1.5f);
            float uniformFit = Mathf.Max((border.x + border.z) / size.x, (border.y + border.w) / size.y);
            // Keep the corner circular even when one side of the control is short.
            image.pixelsPerUnitMultiplier = Mathf.Max(scale, uniformFit);
        }
    }
}
