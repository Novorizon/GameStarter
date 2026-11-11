using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Story;
using Game.HotUpdate.Camp;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    /// <summary>海岛大厅 UI；建设规则、任务和故事推进由各自模块负责。</summary>
    public sealed partial class PlayerHubView : MonoBehaviour
    {
        private ClientFlow owner;
        private CampSceneView scene;
        private CampMissionView missions;
        private CampArrivalView arrival;
        private CampArrivalView outpostBriefing;
        private PlayerProfileView archive;
        private ExpeditionView preparation;
        private RectTransform safe;
        private TMP_Text profileName;
        private TMP_Text profileLevel;
        private Image profileAvatar;
        private GameObject profileSetupPrompt;
        private TMP_Text wood;
        private TMP_Text stone;
        private TMP_Text missionCount;
        private TMP_Text campName;
        private TMP_Text campLevel;
        private TMP_Text upgradeInfo;
        private TMP_Text nextTitle;
        private TMP_Text nextHint;
        private Image nextIcon;
        private GameObject noticePlate;
        private TMP_Text notice;
        private float noticeUntil;
        private Button upgrade;
        private Button expedition;
        private Button arrivalRetry;
        private Button campaignAction;
        private GameObject cameraControls;
        private readonly List<(CampCameraMove Move, Button Button)> cameraButtons = new List<(CampCameraMove, Button)>();
        private Rect previousSafeArea;
        private Vector2Int previousScreen;
        private bool previousArrival;
        private bool previousStoryPlaying;
        private int previousLevel;
        private bool stopping;
        private CampProgress Camp => owner.Journey.Camp;

        public CampSceneView Scene => scene;
        public bool ArrivalPlaying => arrival != null && arrival.Playing;
        private bool StoryPlaying => ArrivalPlaying || owner.CampStoryInProgress;

        public static PlayerHubView Create(ClientFlow owner, Transform parent)
        {
            var root = new GameObject("IslandCampHall");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<PlayerHubView>();
            view.owner = owner;
            view.scene = CampSceneView.Create(root.transform, owner.Art);
            view.CreateInterface();
            view.scene.Selected = view.SelectCamp;
            view.scene.WaterSelected = view.SelectWaterPoint;
            view.Refresh();
            return view;
        }

        private void CreateInterface()
        {
            var ui = new CampUI(owner.Art);
            var hud = new GameObject("CampHUD", typeof(RectTransform));
            hud.transform.SetParent(transform, false);
            var canvas = hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.pixelPerfect = true;
            canvas.sortingOrder = 100;
            var scaler = hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            hud.AddComponent<GraphicRaycaster>();
            safe = CampUI.Area("SafeArea", hud.transform, 0, 0, 1, 1);
            CreateProfileDock(ui);
            CreateResourceDock(ui);
            CreateCampStatus(ui);
            CreateNextAction(ui);
            CreateCameraControls(ui);
            var toast = ui.Panel("CampNoticePlate", safe, .343f, .038f, .729f, .113f, CampPiece.Card);
            toast.GetComponent<Image>().raycastTarget = false;
            noticePlate = toast.gameObject;
            notice = ui.Text("CampNotice", toast, .065f, .13f, .935f, .87f, 22, "", CampUI.Cream);
            notice.alignment = TextAlignmentOptions.Center;
            noticePlate.SetActive(false);
            missions = new CampMissionView(safe, owner.Art, owner.Journey, ClaimTask);
            archive = new PlayerProfileView(safe, owner.Art, owner.Journey, owner.RefreshServer, Refresh);
            preparation = new ExpeditionView(safe, owner);
            arrival = CampArrivalView.Create(safe, owner.Art, owner.Art.ArrivalScript, "Arrival", "从这里开始", "跳过序章");
            outpostBriefing = CampArrivalView.Create(safe, owner.Art, owner.Art.OutpostScript, "Outpost", "了解哨站", "跳过对白");
        }

        private void CreateProfileDock(CampUI ui)
        {
            var card = ui.Panel("PlayerProfile", safe, .016f, .884f, .258f, .984f);
            CampUI.BindButton(card, OpenProfile);
            var portrait = ui.Panel("PortraitFrame", card, .04f, .12f, .229f, .88f, CampPiece.Card);
            portrait.GetComponent<Image>().raycastTarget = false;
            profileAvatar = CampUI.Area("Avatar", portrait, .02f, .03f, .98f, .98f).gameObject.AddComponent<Image>();
            profileAvatar.preserveAspect = true;
            profileAvatar.raycastTarget = false;
            profileName = ui.Text("Profile", card, .261f, .43f, .742f, .84f, 31, "", CampUI.Cream, bold: true);
            profileName.richText = false;
            profileName.enableWordWrapping = false;
            profileName.overflowMode = TextOverflowModes.Ellipsis;
            ui.Text("ProfileCaption", card, .264f, .16f, .75f, .43f, 20, "旅人档案", CampUI.Muted);
            var rank = ui.Panel("TravelerLevelChip", card, .748f, .48f, .95f, .83f, CampPiece.Badge);
            rank.GetComponent<Image>().raycastTarget = false;
            profileLevel = ui.Text("TravelerLevel", rank, .07f, .10f, .93f, .90f, 22, "", CampUI.Mint, bold: true);
            profileLevel.alignment = TextAlignmentOptions.Center;
            ui.Glyph(CampPiece.Arrow, card, .838f, .15f, .925f, .42f, CampUI.Muted);
            var prompt = CampUI.Area("TravelerSetupPrompt", safe, .016f, .814f, .258f, .878f);
            profileSetupPrompt = prompt.gameObject;
            ui.ActionButton("SetupTraveler", prompt, 0, 0, .53f, 1, "设定旅人", OpenProfile, CampPiece.Primary);
            ui.ActionButton("SkipTravelerSetup", prompt, .55f, 0, 1, 1, "稍后再说", () => profileSetupPrompt.SetActive(false));
            profileSetupPrompt.SetActive(false);
        }

        private void CreateResourceDock(CampUI ui)
        {
            var dock = ui.Panel("CampMaterials", safe, .609f, .884f, .984f, .984f);
            wood = ResourceValue(ui, dock, CampPiece.Wood, "木材", .029f, .267f, CampUI.Gold);
            stone = ResourceValue(ui, dock, CampPiece.Stone, "石料", .287f, .525f, CampUI.Mint);
            CampUI.Divider(dock, .274f, .19f, .277f, .81f);
            CampUI.Divider(dock, .535f, .19f, .538f, .81f);
            var journal = ui.Panel("OpenCampMissions", dock, .556f, .13f, .975f, .87f, CampPiece.Control);
            CampUI.BindButton(journal, OpenMissions);
            ui.Glyph(CampPiece.Journal, journal, .07f, .19f, .28f, .81f, CampUI.Mint);
            ui.Text("Caption", journal, .296f, .12f, .79f, .88f, 25, "海岛手记", CampUI.Cream, bold: true);
            var badge = ui.Panel("UnclaimedTasks", journal, .79f, .245f, .937f, .755f, CampPiece.Badge);
            badge.GetComponent<Image>().raycastTarget = false;
            missionCount = ui.Text("Count", badge, .08f, .12f, .92f, .88f, 21, "", CampUI.Gold, bold: true);
            missionCount.alignment = TextAlignmentOptions.Center;
        }

        private static TMP_Text ResourceValue(CampUI ui, Transform parent, CampPiece icon, string title, float left, float right, Color tint)
        {
            var block = CampUI.Area(title, parent, left, .13f, right, .87f);
            ui.Glyph(icon, block, .035f, .17f, .37f, .83f, tint);
            ui.Text("Caption", block, .42f, .58f, .985f, .95f, 19, title, CampUI.Muted);
            var value = ui.Text("Value", block, .42f, .06f, .985f, .59f, 32, "", CampUI.Cream, bold: true);
            value.enableWordWrapping = false;
            value.overflowMode = TextOverflowModes.Ellipsis;
            return value;
        }

        private void CreateCampStatus(CampUI ui)
        {
            var card = ui.Panel("CampConstruction", safe, .016f, .034f, .322f, .215f);
            card.GetComponent<Image>().raycastTarget = false;
            ui.Glyph(CampPiece.Shelter, card, .055f, .70f, .155f, .925f, CampUI.Mint);
            ui.Text("CampTitle", card, .161f, .69f, .736f, .927f, 22, "潮汐营地", CampUI.Muted, bold: true);
            var rank = ui.Panel("CampLevelChip", card, .754f, .727f, .942f, .911f, CampPiece.Badge);
            rank.GetComponent<Image>().raycastTarget = false;
            campLevel = ui.Text("CampLevel", rank, .08f, .12f, .92f, .88f, 22, "", CampUI.Mint, bold: true);
            campLevel.alignment = TextAlignmentOptions.Center;
            campName = ui.Text("CampStage", card, .065f, .383f, .94f, .705f, 36, "", CampUI.Cream, bold: true);
            CampUI.Divider(card, .068f, .375f, .938f, .382f);
            upgradeInfo = ui.Text("UpgradeInfo", card, .068f, .085f, .94f, .363f, 21, "", CampUI.Muted);
        }

        private void CreateNextAction(CampUI ui)
        {
            var card = ui.Panel("ExpeditionInfo", safe, .752f, .034f, .984f, .226f);
            nextIcon = ui.Glyph(CampPiece.Compass, card, .059f, .755f, .158f, .944f, CampUI.Gold);
            ui.Text("NextCaption", card, .172f, .76f, .93f, .932f, 20, "下一步", CampUI.Muted, bold: true);
            nextTitle = ui.Text("Destination", card, .07f, .543f, .944f, .765f, 30, "", CampUI.Cream, bold: true);
            nextHint = ui.Text("Supplies", card, .073f, .398f, .944f, .54f, 19, "", CampUI.Muted);
            upgrade = ui.ActionButton("UpgradeCamp", card, .053f, .061f, .947f, .392f, "整备营地", owner.PrepareCamp, CampPiece.Primary, CampPiece.Shelter);
            expedition = ui.ActionButton("EnterTowerDefense", card, .053f, .061f, .947f, .392f, "准备出征", OpenExpedition, CampPiece.Primary, CampPiece.Arrow);
            arrivalRetry = ui.ActionButton("RetryArrival", card, .053f, .061f, .947f, .392f, "重试序章", owner.RetryArrival, CampPiece.Primary, CampPiece.Compass);
            campaignAction = ui.ActionButton("ContinueCampaign", card, .053f, .061f, .947f, .392f, "继续剧情", owner.ContinueCampaign, CampPiece.Primary, CampPiece.Journal);
        }

        private void CreateCameraControls(CampUI ui)
        {
            var card = ui.Panel("CampCameraControls", safe, .805f, .244f, .984f, .351f);
            cameraControls = card.gameObject;
            ui.Glyph(CampPiece.Compass, card, .06f, .55f, .182f, .925f, CampUI.Mint);
            ui.Text("CameraTitle", card, .192f, .55f, .593f, .925f, 23, "镜头", CampUI.Cream, bold: true);
            var reset = ui.ActionButton("CampCameraReset", card, .608f, .55f, .952f, .925f, "复位", () => MoveCampCamera(CampCameraMove.Reset));
            cameraButtons.Add((CampCameraMove.Reset, reset));

            var moves = new[] { (CampCameraMove.Near, "拉近"), (CampCameraMove.Far, "拉远") };
            for (int index = 0; index < moves.Length; index++)
            {
                var (move, caption) = moves[index];
                float left = .048f + index * .457f;
                var button = ui.ActionButton("CampCamera" + move, card, left, .08f, left + .447f, .474f, caption, () => MoveCampCamera(move));
                cameraButtons.Add((move, button));
            }

            RefreshCameraButtons();
        }

        private void MoveCampCamera(CampCameraMove move)
        {
            scene.MoveCamera(move);
            RefreshCameraButtons();
        }

        private void RefreshCameraButtons()
        {
            foreach (var control in cameraButtons)
            {
                control.Button.interactable = scene.CanMoveCamera(control.Move);
            }
        }

        public async Task PlayCampStoryAsync(bool outpost, CancellationToken token)
        {
            if (outpost)
            {
                profileSetupPrompt.SetActive(false);
            }

            var result = await (outpost ? outpostBriefing : arrival).PlayAsync(token);
            token.ThrowIfCancellationRequested();
            if (result.Status == StoryStatus.Completed || result.Status == StoryStatus.Skipped)
            {
                if (outpost)
                {
                    Camp.CompleteOutpostBriefing();
                    ShowError("已解锁出征，可以前往林间哨站。");
                }
                else
                {
                    bool firstArrival = Camp.CompleteArrival();
                    profileSetupPrompt.SetActive(firstArrival && !Camp.ExpeditionUnlocked);
                    ShowError(Camp.ExpeditionUnlocked ? "欢迎回到潮汐营地。" : "篝火已点起，临时帐篷和补给箱已就位。");
                }
            }
            else if (result.Status == StoryStatus.Failed)
            {
                throw result.Error ?? new InvalidOperationException("Camp story failed.");
            }

            Refresh();
        }

        private void OpenProfile()
        {
            missions.Hide();
            bool firstSetup = profileSetupPrompt.activeSelf;
            profileSetupPrompt.SetActive(false);
            archive.Show(firstSetup);
        }

        private void OpenMissions()
        {
            archive.Hide();
            missions.Show();
        }

        private void OpenExpedition()
        {
            missions.Hide();
            archive.Hide();
            preparation.Show();
        }

        private void ClaimTask(int id)
        {
            try
            {
                ShowError(Camp.Claim(id) ? "补给已收好。" : "当前任务还不能领取。");
                Refresh();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                ShowError("领取进度未能保存，请稍后重试。");
                Debug.LogWarning("Camp save failed: " + error.Message);
            }
        }

        private void SelectCamp()
        {
            ShowError(upgradeInfo.text);
        }

        private void SelectWaterPoint()
        {
            if (owner.Journey.StoryStep == CampaignStep.WaterPoint)
            {
                owner.ContinueCampaign();
            }
        }

        public void Refresh()
        {
            var journey = owner.Journey;
            profileAvatar.sprite = owner.Art.Portrait(journey.Appearance);
            profileName.text = journey.Nickname;
            profileLevel.text = "Lv." + journey.Level.Id;
            wood.text = Camp.Wood.ToString();
            stone.text = Camp.Stone.ToString();
            bool arrived = Camp.ArrivalSeen;
            bool unlocked = Camp.ExpeditionUnlocked;
            cameraControls.SetActive(arrived);
            campName.text = arrived ? Camp.Level.Name : "漂流抵达";
            campLevel.transform.parent.gameObject.SetActive(arrived);
            campLevel.text = "Lv." + Camp.Level.Id;
            var next = Camp.NextLevel;
            upgradeInfo.text = !arrived ? "抵达后在空地搭起帐篷。"
                : Camp.Level.Id == 1 ? "下一阶段　工作台与围栏"
                : next == null ? Camp.Level.Description : "下一阶段　" + next.Name + " · 胜利 " + journey.Victories + "/" + next.RequiredVictories;
            RefreshNextAction(arrived, unlocked);
            int ready = Camp.Tasks.Count(task => !Camp.Claimed(task.Id) && Camp.Progress(task) >= task.Target);
            missionCount.text = ready.ToString();
            missionCount.transform.parent.gameObject.SetActive(ready > 0);
            missions.Refresh();
            archive.Refresh();
            preparation.Refresh();
            scene.Refresh(Camp);
            previousLevel = Camp.Level.Id;
            previousArrival = Camp.ArrivalSeen;
            previousStoryPlaying = StoryPlaying;
        }

        private void RefreshNextAction(bool arrived, bool unlocked)
        {
            bool building = arrived && Camp.Level.Id == 1;
            var story = unlocked ? owner.Art.Campaign.Scene(owner.Journey) : null;
            bool complete = owner.Journey.StoryStep == CampaignStep.Complete;
            if (unlocked)
            {
                nextTitle.text = story?.dialogue.title ?? (complete ? "潮汐之灯" : owner.Stage.Name);
                nextHint.text = story?.hint ?? (complete ? EndingCaption() : owner.Art.Campaign.Chapter(owner.Stage.Id).title);
            }
            else
            {
                nextTitle.text = !arrived ? "在海岛安顿" : building ? "布置营地" : "林间信号";
                nextHint.text = !arrived ? "从海滩前往空地" : building ? "搭好工作台和围栏" : "查明林道深处的警钟";
            }

            var icon = !arrived ? CampPiece.Lock : story != null ? CampPiece.Journal : unlocked ? CampPiece.Flag : building ? CampPiece.Shelter : CampPiece.Compass;
            nextIcon.sprite = owner.Art.CampSprite(icon);
            nextIcon.enabled = nextIcon.sprite != null;
            upgrade.gameObject.SetActive(arrived && !unlocked);
            upgrade.interactable = arrived && !unlocked && !StoryPlaying;
            upgrade.GetComponentInChildren<TMP_Text>(true).text = building ? "整备营地" : "查看哨站消息";
            campaignAction.gameObject.SetActive(story != null);
            if (story != null)
            {
                campaignAction.interactable = !StoryPlaying;
                campaignAction.GetComponentInChildren<TMP_Text>(true).text = story.action;
            }

            expedition.gameObject.SetActive(unlocked && story == null);
            expedition.interactable = !owner.Entering && !StoryPlaying && owner.CanSelectStage(owner.Stage.Id);
            expedition.GetComponentInChildren<TMP_Text>(true).text = owner.Entering ? "准备战场中…" : complete ? "重访关卡" : "准备出征";
            arrivalRetry.gameObject.SetActive(!arrived && !ArrivalPlaying);
        }

        private string EndingCaption()
        {
            return owner.Journey.Ending == CampaignEnding.Keeper ? "白崖值守者 · " + owner.Journey.KeeperName : "新航图已送回港口";
        }

        public void ShowError(string message)
        {
            notice.text = message;
            noticePlate.SetActive(!string.IsNullOrWhiteSpace(message));
            noticePlate.transform.SetAsLastSibling();
            noticeUntil = Time.unscaledTime + 5;
        }

        public void SetServerStatus(string status, bool canRefresh)
        {
            archive.SetServerStatus(status, canRefresh);
        }

        private void Update()
        {
            if (stopping)
            {
                return;
            }

            if (previousLevel != Camp.Level.Id || previousArrival != Camp.ArrivalSeen || previousStoryPlaying != StoryPlaying)
            {
                Refresh();
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (preparation.Visible)
                {
                    preparation.Hide();
                }
                else if (archive.Visible)
                {
                    archive.CloseTop();
                }
                else if (missions.Visible)
                {
                    missions.Hide();
                }
            }

            if (noticePlate.activeSelf && Time.unscaledTime >= noticeUntil)
            {
                noticePlate.SetActive(false);
            }

            var screen = new Vector2Int(Screen.width, Screen.height);
            if ((previousScreen != screen || previousSafeArea != Screen.safeArea) && screen.x > 0 && screen.y > 0)
            {
                previousScreen = screen;
                previousSafeArea = Screen.safeArea;
                safe.anchorMin = new Vector2(previousSafeArea.xMin / screen.x, previousSafeArea.yMin / screen.y);
                safe.anchorMax = new Vector2(previousSafeArea.xMax / screen.x, previousSafeArea.yMax / screen.y);
                Canvas.ForceUpdateCanvases();
                foreach (var frame in safe.GetComponentsInChildren<CampFrame>(true))
                {
                    frame.FitBorder();
                }
            }
        }

        public void Stop()
        {
            stopping = true;
            arrival?.Dispose();
            outpostBriefing?.Dispose();
            campaignDialogue?.Dispose();
        }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
