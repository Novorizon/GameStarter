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
        private readonly CampUI ui;
        private readonly TMP_Text summary;
        private readonly List<(Button Claim, TMP_Text Caption, TMP_Text Progress, Image Fill)> rows =
            new List<(Button, TMP_Text, TMP_Text, Image)>();

        public CampMissionView(Transform parent, ClientArt art, JourneyProgress journey, Action<int> claim)
        {
            this.journey = journey;
            ui = new CampUI(art);
            var overlay = CampUI.Overlay("CampMissions", parent);
            root = overlay.gameObject;
            var panel = ui.Panel("MissionJournal", overlay, .17f, .10f, .83f, .90f);
            ui.Glyph(CampPiece.Journal, panel, .052f, .86f, .105f, .948f, CampUI.Mint);
            ui.Text("Heading", panel, .119f, .891f, .65f, .96f, 40, "海岛手记", CampUI.Cream, bold: true);
            ui.Text("Subtitle", panel, .121f, .838f, .62f, .891f, 20, "营地任务与补给", CampUI.Muted);
            var chip = ui.Panel("JournalProgress", panel, .628f, .87f, .796f, .938f, CampPiece.Badge);
            summary = ui.Text("Summary", chip, .07f, .10f, .93f, .90f, 21, "", CampUI.Mint, bold: true);
            summary.alignment = TextAlignmentOptions.Center;
            ui.ActionButton("CloseMissions", panel, .824f, .86f, .95f, .946f, "关闭", Hide, icon: CampPiece.Close);
            CampUI.Divider(panel, .053f, .826f, .947f, .828f);

            const float rowHeight = 136;
            const float rowStride = 146;
            float contentHeight = Mathf.Max(rowHeight, journey.Camp.Tasks.Count * rowStride);
            var list = CampUI.ScrollList("CampTaskList", panel, .052f, .119f, .96f, .808f, contentHeight);
            for (int i = 0; i < journey.Camp.Tasks.Count; i++)
            {
                var task = journey.Camp.Tasks[i];
                float top = 1 - i * rowStride / contentHeight;
                var row = ui.Panel("CampTask " + task.Id, list, 0, top - rowHeight / contentHeight, 1, top, CampPiece.Card);
                var emblem = ui.Panel("TaskEmblem", row, .035f, .294f, .102f, .806f, CampPiece.Control);
                ui.Glyph(CampPiece.Flag, emblem, .17f, .15f, .83f, .85f, CampUI.Mint);
                ui.Text("Title", row, .125f, .628f, .683f, .919f, 28, task.Name, CampUI.Cream, bold: true);
                ui.Text("Goal", row, .125f, .354f, .689f, .628f, 20, task.Description, CampUI.Muted);
                Reward(row, .125f, CampPiece.Wood, task.Wood, CampUI.Gold);
                Reward(row, .299f, CampPiece.Stone, task.Stone, CampUI.Mint);
                Reward(row, .473f, CampPiece.Spark, task.Experience, CampUI.Cream);
                CampUI.Divider(row, .709f, .16f, .711f, .84f);
                var progress = ui.Text("Progress", row, .745f, .676f, .965f, .898f, 21, "", CampUI.Mint, bold: true);
                progress.alignment = TextAlignmentOptions.Center;
                var fill = ui.Meter(row, .764f, .571f, .945f, .643f);
                int id = task.Id;
                var button = ui.ActionButton("ClaimCampTask " + id, row, .744f, .12f, .967f, .516f, "领取奖励", () => claim(id));
                rows.Add((button, button.GetComponentInChildren<TMP_Text>(), progress, fill));
            }

            ui.Text("Journey", panel, .058f, .049f, .944f, .111f, 19, "领取补给后，探索经验计入旅人档案。", CampUI.Muted);
            root.SetActive(false);
        }

        public bool Visible => root.activeSelf;

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
            int received = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                var task = journey.Camp.Tasks[i];
                bool claimed = journey.Camp.Claimed(task.Id);
                int progress = journey.Camp.Progress(task);
                var row = rows[i];
                bool ready = !claimed && progress >= task.Target;
                received += claimed ? 1 : 0;
                row.Progress.text = progress + " / " + task.Target;
                CampUI.SetProgress(row.Fill, progress / (float)task.Target);
                row.Claim.interactable = ready;
                ui.Style(row.Claim.image, ready ? CampPiece.Primary : CampPiece.Control);
                row.Caption.text = claimed ? "已领取" : ready ? "领取奖励" : "未完成";
                row.Caption.color = ready ? ui.PrimaryInk : CampUI.Muted;
            }

            summary.text = "已领 " + received + " / " + rows.Count;
        }

        private void Reward(Transform row, float left, CampPiece icon, int amount, Color color)
        {
            ui.Glyph(icon, row, left, .112f, left + .04f, .342f, color);
            ui.Text("Reward" + icon, row, left + .045f, .12f, left + .166f, .339f, 20, "+" + amount, color, bold: true);
        }
    }
}
