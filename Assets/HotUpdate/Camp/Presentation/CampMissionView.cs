using System;
using System.Collections.Generic;
using Game.HotUpdate.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Camp
{
    public sealed class CampMissionView
    {
        private readonly GameObject root;
        private readonly JourneyProgress journey;
        private readonly List<Button> claims = new List<Button>();
        private readonly List<TMP_Text> values = new List<TMP_Text>();

        public CampMissionView(Transform parent, ClientArt art, JourneyProgress journey, Action<int> claim)
        {
            this.journey = journey;
            var ui = new CampUI(art);
            var overlay = CampUI.Overlay("CampMissions", parent);
            root = overlay.gameObject;
            var panel = ui.Panel("MissionJournal", overlay, .20f, .10f, .80f, .90f);
            ui.Text("Heading", panel, .065f, .86f, .84f, .96f, 38, "海岛手记", CampUI.Gold);
            ui.Text("Subtitle", panel, .065f, .80f, .93f, .86f, 20, "一点点建设，把漂流的终点变成家。", CampUI.Muted);
            ui.ActionButton("CloseMissions", panel, .84f, .87f, .95f, .96f, "关闭", Hide);
            for (int i = 0; i < journey.Camp.Tasks.Count; i++)
            {
                var task = journey.Camp.Tasks[i];
                float top = .79f - i * .16f;
                var row = ui.Panel("CampTask " + task.Id, panel, .055f, top - .15f, .945f, top);
                ui.Text("Title", row, .04f, .60f, .68f, .90f, 26, task.Name, CampUI.Cream);
                ui.Text("Goal", row, .04f, .36f, .70f, .60f, 19, task.Description, CampUI.Muted);
                string reward = "木材 " + task.Wood + "  ·  石料 " + task.Stone + "  ·  经验 " + task.Experience;
                ui.Text("Reward", row, .04f, .14f, .72f, .36f, 18, reward, CampUI.Gold);
                values.Add(ui.Text("Progress", row, .77f, .58f, .96f, .88f, 19, "", CampUI.Muted));
                int id = task.Id;
                claims.Add(ui.ActionButton("ClaimCampTask " + id, row, .75f, .16f, .97f, .58f, "领取", () => claim(id)));
            }

            ui.Text("Journey", panel, .065f, .065f, .94f, .13f, 19, "探索经验计入本地档案，旅程目标随塔防结算推进。", CampUI.Muted);
            root.SetActive(false);
        }

        public bool Visible => root.activeSelf;

        public void Show()
        {
            Refresh();
            root.SetActive(true);
        }

        public void Hide()
        {
            root.SetActive(false);
        }

        public void Refresh()
        {
            for (int i = 0; i < claims.Count; i++)
            {
                var task = journey.Camp.Tasks[i];
                bool claimed = journey.Camp.Claimed(task.Id);
                int progress = journey.Camp.Progress(task);
                values[i].text = progress + " / " + task.Target;
                claims[i].interactable = !claimed && progress >= task.Target;
                claims[i].GetComponentInChildren<TMP_Text>().text = claimed ? "已领取" : progress >= task.Target ? "领取奖励" : "未完成";
            }
        }
    }
}
