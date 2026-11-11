using System;
using System.Collections.Generic;
using Game.HotUpdate.Camp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    /// <summary>玩家成长与长期目标展示；经验和任务完成仍由 JourneyProgress 结算。</summary>
    public sealed class PlayerProfileView
    {
        private readonly GameObject root;
        private readonly GameObject details;
        private readonly ClientArt art;
        private readonly JourneyProgress journey;
        private readonly TravelerIdentityView editor;
        private readonly Image avatar;
        private readonly Button editProfile;
        private readonly TMP_Text identity;
        private readonly TMP_Text rank;
        private readonly TMP_Text experience;
        private readonly TMP_Text[] statistics = new TMP_Text[6];
        private readonly TMP_Text serverStatus;
        private readonly Button refreshServer;
        private readonly Image experienceFill;
        private readonly List<(TMP_Text Progress, Image Fill)> goals = new List<(TMP_Text, Image)>();
        private bool firstSetup;
        public bool Visible => root.activeSelf;

        public PlayerProfileView(Transform parent, ClientArt art, JourneyProgress journey, Action refreshServer, Action refreshHub)
        {
            this.art = art;
            this.journey = journey;
            var ui = new CampUI(art);
            var overlay = CampUI.Overlay("PlayerProfileDetails", parent);
            root = overlay.gameObject;
            var panel = ui.Panel("PlayerArchive", overlay, .125f, .092f, .875f, .908f);
            details = panel.gameObject;
            ui.Glyph(CampPiece.User, panel, .049f, .856f, .098f, .946f, CampUI.Mint);
            ui.Text("Heading", panel, .111f, .884f, .63f, .963f, 40, "旅人档案", CampUI.Cream, bold: true);
            ui.Text("Subtitle", panel, .114f, .838f, .63f, .889f, 20, "探索成长与防守战绩", CampUI.Muted);
            editProfile = ui.ActionButton("EditProfile", panel, .66f, .858f, .819f, .945f, "编辑资料", () => OpenEditor(false), icon: CampPiece.Edit);
            ui.ActionButton("CloseProfile", panel, .833f, .858f, .953f, .945f, "关闭", Hide, icon: CampPiece.Close);
            CampUI.Divider(panel, .047f, .817f, .949f, .819f);

            var traveler = ui.Panel("Traveler", panel, .044f, .176f, .335f, .799f, CampPiece.Card);
            avatar = CampUI.Area("Avatar", traveler, .094f, .338f, .906f, .96f).gameObject.AddComponent<Image>();
            avatar.preserveAspect = true;
            avatar.raycastTarget = false;
            identity = ui.Text("Identity", traveler, .06f, .247f, .94f, .345f, 34, "", CampUI.Cream, bold: true);
            identity.richText = false;
            identity.alignment = TextAlignmentOptions.Center;
            identity.enableWordWrapping = false;
            identity.overflowMode = TextOverflowModes.Ellipsis;
            rank = ui.Text("Rank", traveler, .06f, .175f, .94f, .247f, 22, "", CampUI.Gold, bold: true);
            rank.alignment = TextAlignmentOptions.Center;
            experience = ui.Text("Experience", traveler, .09f, .09f, .91f, .162f, 19, "", CampUI.Muted);
            experience.alignment = TextAlignmentOptions.Center;
            experienceFill = ui.Meter(traveler, .104f, .059f, .896f, .085f);

            var statNames = new[] { "防守胜利", "主线通关", "击败敌人", "累计建塔", "强化选择", "营地等级" };
            for (int i = 0; i < statistics.Length; i++)
            {
                float left = .357f + i % 3 * .201f;
                float top = .799f - i / 3 * .119f;
                var tile = ui.Panel(statNames[i], panel, left, top - .109f, left + .192f, top, CampPiece.Card);
                ui.Text("Caption", tile, .105f, .557f, .915f, .886f, 19, statNames[i], CampUI.Muted);
                statistics[i] = ui.Text("Value", tile, .105f, .094f, .915f, .589f, 31, "", CampUI.Cream, bold: true);
            }

            ui.Text("GoalsHeading", panel, .368f, .506f, .72f, .564f, 25, "旅程目标", CampUI.Cream, bold: true);
            var goalsHint = ui.Text("GoalsHint", panel, .723f, .512f, .946f, .564f, 18, "达成后自动结算经验", CampUI.Muted);
            goalsHint.alignment = TextAlignmentOptions.MidlineRight;
            int count = journey.Tasks.Count;
            var list = CampUI.ScrollList("JourneyGoals", panel, .358f, .178f, .962f, .501f, Mathf.Max(113, count * 113));
            for (int i = 0; i < count; i++)
            {
                var task = journey.Tasks[i];
                float top = 1 - (float)i / count;
                var row = ui.Panel("JourneyTask " + task.Id, list, 0, top - .93f / count, 1, top, CampPiece.Card);
                ui.Text("GoalTitle", row, .05f, .55f, .68f, .905f, 24, task.Name, CampUI.Cream, bold: true);
                ui.Text("GoalDescription", row, .05f, .135f, .682f, .55f, 19, task.Description, CampUI.Muted);
                var progress = ui.Text("GoalProgress", row, .711f, .342f, .952f, .87f, 19, "", CampUI.Mint);
                progress.alignment = TextAlignmentOptions.MidlineRight;
                goals.Add((progress, ui.Meter(row, .718f, .159f, .951f, .256f)));
            }

            CampUI.Divider(panel, .047f, .157f, .949f, .159f);
            ui.Text("LocalArchive", panel, .051f, .09f, .75f, .141f, 19, "本地探索档案", CampUI.Muted);
            serverStatus = ui.Text("ServerStatus", panel, .051f, .042f, .75f, .093f, 19, "", CampUI.Cream);
            this.refreshServer = ui.ActionButton("RefreshServer", panel, .789f, .043f, .949f, .135f, "刷新服务器", refreshServer);
            SetServerStatus("服务器未配置", false);
            editor = new TravelerIdentityView(overlay, art, journey, () =>
            {
                CloseEditor();
                refreshHub();
            }, CloseEditor);
            root.SetActive(false);
        }

        public void SetServerStatus(string status, bool canRefresh)
        {
            serverStatus.text = status;
            refreshServer.interactable = canRefresh;
        }

        public void Show(bool showFirstSetup = false)
        {
            Refresh();
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            if (showFirstSetup)
            {
                OpenEditor(true);
            }
            else
            {
                editor.Hide();
                details.SetActive(true);
            }
        }

        public void Hide()
        {
            editor.Hide();
            root.SetActive(false);
        }

        public void CloseTop()
        {
            if (editor.Visible)
            {
                CloseEditor();
            }
            else
            {
                Hide();
            }
        }

        private void OpenEditor(bool showFirstSetup)
        {
            firstSetup = showFirstSetup;
            details.SetActive(false);
            editor.Show(firstSetup);
        }

        private void CloseEditor()
        {
            editor.Hide();
            if (firstSetup)
            {
                root.SetActive(false);
            }
            else
            {
                details.SetActive(true);
            }
        }

        public void Refresh()
        {
            var level = journey.Level;
            var next = journey.NextLevel;
            editProfile.interactable = journey.Camp.ArrivalSeen;
            avatar.sprite = art.Portrait(journey.Appearance);
            identity.text = journey.Nickname;
            rank.text = "Lv." + level.Id + "  " + level.Title;
            experience.text = next == null ? "探索经验 " + journey.Experience + " · 已满级" : "探索经验 " + journey.Experience + " / " + next.Experience;
            float progress = next == null ? 1 : (float)(journey.Experience - level.Experience) / (next.Experience - level.Experience);
            CampUI.SetProgress(experienceFill, progress);
            statistics[0].text = journey.Victories.ToString();
            statistics[1].text = journey.ClearedStages.ToString();
            statistics[2].text = journey.EnemiesKilled.ToString();
            statistics[3].text = journey.TowersBuilt.ToString();
            statistics[4].text = journey.CardsChosen.ToString();
            statistics[5].text = "Lv." + journey.Camp.Level.Id;
            for (int i = 0; i < goals.Count; i++)
            {
                var task = journey.Tasks[i];
                int current = journey.Progress(task);
                bool completed = journey.Completed(task);
                goals[i].Progress.text = (completed ? "已达成" : current + " / " + task.Target)
                    + "\n经验 +" + task.Experience;
                CampUI.SetProgress(goals[i].Fill, completed ? 1 : current / (float)task.Target);
            }
        }
    }
}
