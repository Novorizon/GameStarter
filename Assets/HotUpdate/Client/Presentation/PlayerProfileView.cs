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
        private readonly JourneyProgress journey;
        private readonly TMP_Text identity;
        private readonly TMP_Text experience;
        private readonly TMP_Text records;
        private readonly RectTransform experienceFill;
        private readonly List<TMP_Text> goals = new List<TMP_Text>();
        public bool Visible => root.activeSelf;

        public PlayerProfileView(Transform parent, ClientArt art, JourneyProgress journey)
        {
            this.journey = journey;
            var ui = new CampUI(art);
            var overlay = CampUI.Overlay("PlayerProfileDetails", parent);
            root = overlay.gameObject;
            var panel = ui.Panel("PlayerArchive", overlay, .17f, .10f, .83f, .90f);
            ui.Text("Heading", panel, .055f, .85f, .77f, .96f, 38, "旅人档案", CampUI.Gold);
            ui.ActionButton("CloseProfile", panel, .82f, .86f, .95f, .96f, "关闭", Hide);
            var avatar = CampUI.Area("Avatar", panel, .06f, .60f, .25f, .84f).gameObject.AddComponent<Image>();
            avatar.sprite = art.Avatar;
            avatar.preserveAspect = true;
            avatar.raycastTarget = false;
            identity = ui.Text("Identity", panel, .28f, .72f, .94f, .84f, 30, "", CampUI.Cream);
            experience = ui.Text("Experience", panel, .28f, .65f, .94f, .73f, 22, "", CampUI.Muted);
            var track = CampUI.Area("ExperienceTrack", panel, .28f, .61f, .93f, .635f);
            track.gameObject.AddComponent<Image>().color = new Color(.08f, .17f, .13f);
            experienceFill = CampUI.Area("ExperienceFill", track, 0, 0, 1, 1);
            experienceFill.gameObject.AddComponent<Image>().color = CampUI.Gold;
            records = ui.Text("Records", panel, .06f, .49f, .94f, .60f, 23, "", CampUI.Cream);
            ui.Text("GoalsHeading", panel, .06f, .43f, .94f, .49f, 25, "旅程目标 · 达成后自动结算经验", CampUI.Gold);
            int count = journey.Tasks.Count;
            var list = CampUI.ScrollList("JourneyGoals", panel, .05f, .105f, .95f, .43f, count * 110);
            for (int i = 0; i < count; i++)
            {
                var task = journey.Tasks[i];
                float top = 1 - (float)i / count;
                var row = ui.Panel("JourneyTask " + task.Id, list, 0, top - .94f / count, 1, top);
                ui.Text("GoalTitle", row, .035f, .55f, .69f, .89f, 25, task.Name, CampUI.Cream);
                ui.Text("GoalDescription", row, .035f, .14f, .69f, .52f, 20, task.Description, CampUI.Muted);
                goals.Add(ui.Text("GoalProgress", row, .70f, .15f, .96f, .87f, 21, "", CampUI.Gold));
            }

            ui.Text("LocalArchive", panel, .06f, .035f, .94f, .095f, 18, "本地探索档案 · 与正式服务器等级和经济独立", CampUI.Muted);
            root.SetActive(false);
        }

        public void Show()
        {
            Refresh();
            root.transform.SetAsLastSibling();
            root.SetActive(true);
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        public void Refresh()
        {
            var level = journey.Level;
            var next = journey.NextLevel;
            identity.text = journey.Nickname + "  Lv." + level.Id + "\n<size=70%>" + level.Title + "</size>";
            experience.text = next == null ? "探索经验 " + journey.Experience + " · 已达当前等级上限"
                : "探索经验 " + journey.Experience + " / " + next.Experience + " · 距下级 " + (next.Experience - journey.Experience);
            float progress = next == null ? 1 : (float)(journey.Experience - level.Experience) / (next.Experience - level.Experience);
            experienceFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
            records.text = "防守胜利 " + journey.Victories + "   ·   最高波次 " + journey.HighestWave + "   ·   击败敌人 " + journey.EnemiesKilled
                + "\n累计建塔 " + journey.TowersBuilt + "   ·   强化选择 " + journey.CardsChosen + "   ·   营地 Lv." + journey.Camp.Level.Id;
            for (int i = 0; i < goals.Count; i++)
            {
                var task = journey.Tasks[i];
                goals[i].text = (journey.Completed(task) ? "已达成" : journey.Progress(task) + " / " + task.Target)
                    + "\n经验 +" + task.Experience;
            }
        }
    }
}
