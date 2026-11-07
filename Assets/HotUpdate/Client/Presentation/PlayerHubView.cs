using System;
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
    public sealed class PlayerHubView : MonoBehaviour
    {
        private ClientFlow owner;
        private CampSceneView scene;
        private CampMissionView missions;
        private CampArrivalView arrival;
        private PlayerProfileView archive;
        private ExpeditionView preparation;
        private RectTransform safe;
        private TMP_Text profile;
        private TMP_Text resources;
        private TMP_Text campName;
        private TMP_Text upgradeInfo;
        private TMP_Text notice;
        private Button upgrade;
        private Button expedition;
        private Button missionButton;
        private Button arrivalRetry;
        private Rect previousSafeArea;
        private bool previousArrival;
        private bool previousArrivalPlaying;
        private int previousLevel;
        private bool stopping;
        private CampProgress Camp => owner.Journey.Camp;

        public CampSceneView Scene => scene;
        public bool ArrivalPlaying => arrival != null && arrival.Playing;

        public static PlayerHubView Create(ClientFlow owner, Transform parent)
        {
            var root = new GameObject("IslandCampHall");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<PlayerHubView>();
            view.owner = owner;
            view.scene = CampSceneView.Create(root.transform, owner.Art.CampMap);
            view.CreateInterface();
            view.scene.Selected = view.SelectCamp;
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
            canvas.sortingOrder = 100;
            var scaler = hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            hud.AddComponent<GraphicRaycaster>();
            safe = CampUI.Area("SafeArea", hud.transform, 0, 0, 1, 1);
            var profilePanel = ui.Panel("PlayerProfile", safe, .025f, .865f, .315f, .975f);
            var profileButton = profilePanel.gameObject.AddComponent<Button>();
            profileButton.targetGraphic = profilePanel.GetComponent<Image>();
            profileButton.onClick.AddListener(OpenProfile);
            var avatar = CampUI.Area("Avatar", profilePanel, .025f, .08f, .23f, .92f).gameObject.AddComponent<Image>();
            avatar.sprite = owner.Art.Avatar;
            avatar.preserveAspect = true;
            avatar.raycastTarget = false;
            profile = ui.Text("Profile", profilePanel, .25f, .13f, .95f, .89f, 25, "", CampUI.Cream);
            var materials = ui.Panel("CampMaterials", safe, .40f, .895f, .715f, .975f);
            resources = ui.Text("Materials", materials, .05f, .18f, .95f, .82f, 27, "", CampUI.Gold);
            missionButton = ui.ActionButton("OpenCampMissions", safe, .78f, .88f, .975f, .975f, "海岛手记", OpenMissions);
            ui.Text("CampTitle", safe, .35f, .77f, .69f, .85f, 38, "潮汐营地", CampUI.Cream).alignment = TextAlignmentOptions.Center;
            var chapter = ui.Text("Chapter", safe, .38f, .73f, .65f, .78f, 18, "漂流之地  /  ISLAND HOMESTEAD", CampUI.Cream);
            chapter.alignment = TextAlignmentOptions.Center;
            var construction = ui.Panel("CampConstruction", safe, .025f, .035f, .43f, .255f);
            campName = ui.Text("CampStage", construction, .045f, .68f, .95f, .93f, 30, "", CampUI.Gold);
            upgradeInfo = ui.Text("UpgradeInfo", construction, .045f, .31f, .95f, .68f, 21, "", CampUI.Cream);
            upgrade = ui.ActionButton("UpgradeCamp", construction, .045f, .065f, .64f, .30f, "建设营地", UpgradeCamp);
            ui.ActionButton("CampJournalShortcut", construction, .67f, .065f, .955f, .30f, "任务", OpenMissions);
            var route = ui.Panel("ExpeditionInfo", safe, .745f, .04f, .975f, .255f);
            ui.Text("Destination", route, .08f, .62f, .94f, .90f, 25, owner.Stage.Name + " · 单人防守", CampUI.Cream);
            ui.Text("Supplies", route, .08f, .42f, .94f, .65f, 18, "赢取建设补给，回到海岛安家", CampUI.Muted);
            expedition = ui.ActionButton("EnterTowerDefense", route, .07f, .08f, .94f, .41f, "出征 · 守卫哨站", OpenExpedition);
            notice = ui.Text("CampNotice", safe, .44f, .05f, .73f, .23f, 23, "", CampUI.Cream);
            notice.alignment = TextAlignmentOptions.Center;
            arrivalRetry = ui.ActionButton("RetryArrival", safe, .44f, .26f, .72f, .33f, "重试抵达序章", owner.RetryArrival);
            ui.Text("LocalProfile", safe, .72f, .27f, .975f, .315f, 17, "本地营地档案 · 建设进度自动保存", CampUI.Cream);
            missions = new CampMissionView(safe, owner.Art, owner.Journey, ClaimTask);
            archive = new PlayerProfileView(safe, owner.Art, owner.Journey);
            preparation = new ExpeditionView(safe, owner);
            arrival = CampArrivalView.Create(safe, owner.Art);
        }

        public async Task PlayArrivalAsync(CancellationToken token)
        {
            if (Camp.ArrivalSeen)
            {
                return;
            }

            var result = await arrival.PlayAsync(token);
            token.ThrowIfCancellationRequested();
            if (result.Status == StoryStatus.Completed || result.Status == StoryStatus.Skipped)
            {
                Camp.CompleteArrival();
                ShowError("欢迎来到海岛。打开海岛手记，领取第一份补给。");
            }
            else if (result.Status == StoryStatus.Failed)
            {
                throw result.Error ?? new InvalidOperationException("Island arrival story failed.");
            }

            Refresh();
        }

        private void OpenProfile()
        {
            missions.Hide();
            archive.Show();
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

        private void UpgradeCamp()
        {
            ApplyChange(() => Camp.Upgrade(), "营地建设完成。点击营地可查看下一步计划。");
        }

        private void ClaimTask(int id)
        {
            ApplyChange(() => Camp.Claim(id), "补给已收好，可以继续建设营地。");
        }

        private void ApplyChange(Func<bool> action, string message)
        {
            try
            {
                ShowError(action() ? message : Camp.UpgradeBlockedReason ?? "当前任务还不能领取。");
                Refresh();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                ShowError("进度未能保存，材料没有扣除，请稍后重试。");
                Debug.LogWarning("Camp save failed: " + error.Message);
            }
        }

        private void SelectCamp()
        {
            ShowError(Camp.NextLevel == null ? Camp.Level.Description : "下一步：" + Camp.NextLevel.Name + "。在左下方确认建设。");
        }

        public void Refresh()
        {
            var journey = owner.Journey;
            profile.text = journey.Nickname + "  Lv." + journey.Level.Id + "\n<size=65%>最高波次 "
                + journey.HighestWave + " · 防守胜利 " + journey.Victories + "</size>";
            resources.text = "木材  " + Camp.Wood + "     石料  " + Camp.Stone;
            campName.text = Camp.Level.Name + "  <size=65%>营地 Lv." + Camp.Level.Id + "</size>";
            var next = Camp.NextLevel;
            upgradeInfo.text = next == null ? Camp.Level.Description
                : "下一步：" + next.Name + "\n木材 " + next.WoodCost + "  ·  石料 " + next.StoneCost
                    + (Camp.UpgradeBlockedReason == null ? "  ·  可以建设" : "\n" + Camp.UpgradeBlockedReason);
            upgrade.interactable = Camp.UpgradeBlockedReason == null && !ArrivalPlaying;
            upgrade.GetComponentInChildren<TMP_Text>().text = next == null ? "家园已建成" : "建设 · " + next.Name;
            expedition.interactable = Camp.ArrivalSeen && !owner.Entering && !ArrivalPlaying;
            expedition.GetComponentInChildren<TMP_Text>().text = owner.Entering ? "正在准备战场…" : "出征 · 守卫哨站";
            int ready = Camp.Tasks.Count(task => !Camp.Claimed(task.Id) && Camp.Progress(task) >= task.Target);
            missionButton.GetComponentInChildren<TMP_Text>().text = "海岛手记" + (ready > 0 ? "  ·  " + ready : "");
            missions.Refresh();
            archive.Refresh();
            preparation.Refresh();
            arrivalRetry.gameObject.SetActive(!Camp.ArrivalSeen && !ArrivalPlaying);
            scene.Refresh(Camp);
            previousLevel = Camp.Level.Id;
            previousArrival = Camp.ArrivalSeen;
            previousArrivalPlaying = ArrivalPlaying;
        }

        public void ShowError(string message)
        {
            notice.text = message;
        }

        private void Update()
        {
            if (stopping)
            {
                return;
            }

            if (previousLevel != Camp.Level.Id || previousArrival != Camp.ArrivalSeen || previousArrivalPlaying != ArrivalPlaying)
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
                    archive.Hide();
                }
                else if (missions.Visible)
                {
                    missions.Hide();
                }
            }

            if (previousSafeArea != Screen.safeArea && Screen.width > 0 && Screen.height > 0)
            {
                previousSafeArea = Screen.safeArea;
                safe.anchorMin = new Vector2(previousSafeArea.xMin / Screen.width, previousSafeArea.yMin / Screen.height);
                safe.anchorMax = new Vector2(previousSafeArea.xMax / Screen.width, previousSafeArea.yMax / Screen.height);
            }
        }

        public void Stop()
        {
            stopping = true;
            arrival?.Dispose();
        }

        private void OnDestroy()
        {
            Stop();
        }
    }
}
