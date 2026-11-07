using System.Linq;
using Game.HotUpdate.Camp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    /// <summary>出征准备页；展示实际关卡配置，确认后才申请战场资源。</summary>
    public sealed class ExpeditionView
    {
        private readonly ClientFlow owner;
        private readonly GameObject root;
        private readonly TMP_Text heading;
        private readonly TMP_Text overview;
        private readonly TMP_Text supplies;
        private readonly TMP_Text version;
        private readonly TMP_Text status;
        private readonly RectTransform waveArea;
        private readonly Button confirm;
        private readonly Button cancel;
        public bool Visible => root.activeSelf;

        public ExpeditionView(Transform parent, ClientFlow owner)
        {
            this.owner = owner;
            var ui = new CampUI(owner.Art);
            var overlay = CampUI.Overlay("ExpeditionPreparation", parent);
            root = overlay.gameObject;
            var panel = ui.Panel("ExpeditionPanel", overlay, .13f, .10f, .87f, .90f);
            heading = ui.Text("Heading", panel, .05f, .84f, .94f, .96f, 38, "出征准备", CampUI.Gold);
            ui.Text("Mode", panel, .05f, .78f, .94f, .85f, 20, "单人防守 · 固定路线与建造点 · 波间暂停三选一", CampUI.Muted);
            overview = ui.Text("Overview", panel, .05f, .41f, .55f, .77f, 25, "", CampUI.Cream);
            supplies = ui.Text("CampSupplies", panel, .05f, .24f, .55f, .42f, 23, "", CampUI.Gold);
            ui.Text("WavesHeading", panel, .59f, .70f, .94f, .77f, 26, "波次情报", CampUI.Gold);
            waveArea = CampUI.Area("WaveArea", panel, .58f, .23f, .95f, .70f);
            status = ui.Text("ExpeditionStatus", panel, .05f, .175f, .95f, .24f, 19, "", CampUI.Cream);
            cancel = ui.ActionButton("CancelExpedition", panel, .05f, .06f, .32f, .16f, "返回营地", Hide);
            confirm = ui.ActionButton("ConfirmExpedition", panel, .60f, .06f, .95f, .16f, "确认出征", Confirm);
            version = ui.Text("Version", panel, .05f, .01f, .95f, .055f, 16, "", CampUI.Muted);
            root.SetActive(false);
        }

        public void Show()
        {
            var plan = owner.PrepareExpedition();
            if (plan == null)
            {
                return;
            }

            heading.text = "出征准备 · " + plan.Stage.Name;
            var config = plan.Configuration;
            overview.text = "防守 " + plan.Briefing.Waves.Count + " 波  ·  敌人 " + plan.Briefing.EnemyCount + " 名"
                + "\n初始金币 " + config.InitialGold + "  ·  基地耐久 " + config.BaseHealth
                + "\n可建位置 " + config.BuildSlots.Length + "  ·  路线 " + config.Routes.Length
                + "\n\n可用防御塔\n" + string.Join("\n", config.Towers.Select(tower => tower.Name + "  ·  " + tower.BuildCost + " 金币"));
            var camp = plan.CampLevel;
            supplies.text = "胜利补给  木材 +" + camp.VictoryWood + "  ·  石料 +" + camp.VictoryStone
                + "\n失守补给  木材 +" + camp.DefeatWood + "  ·  石料 +" + camp.DefeatStone;
            version.text = "本地演练  /  配置 " + plan.Stage.ConfigVersion + "  /  种子 " + plan.Seed;
            foreach (Transform child in waveArea)
            {
                child.gameObject.SetActive(false);
                Object.Destroy(child.gameObject);
            }

            var list = CampUI.ScrollList("WaveList", waveArea, 0, 0, 1, 1, plan.Briefing.Waves.Count * 100);
            var ui = new CampUI(owner.Art);
            for (int i = 0; i < plan.Briefing.Waves.Count; i++)
            {
                var wave = plan.Briefing.Waves[i];
                float top = 1 - (float)i / plan.Briefing.Waves.Count;
                string enemies = string.Join(" · ", wave.Enemies.Select(enemy => enemy.Name + " ×" + enemy.Count));
                var row = ui.Panel("Wave " + wave.Number, list, 0, top - .95f / plan.Briefing.Waves.Count, 1, top);
                ui.Text("WaveSummary", row, .055f, .14f, .95f, .86f, 22, "第 " + wave.Number + " 波 · " + wave.EnemyCount + " 名\n" + enemies, CampUI.Cream);
            }

            Refresh();
            root.transform.SetAsLastSibling();
            root.SetActive(true);
        }

        public void Hide()
        {
            if (owner.Entering)
            {
                return;
            }

            owner.CancelExpedition();
            root.SetActive(false);
        }

        public void Refresh()
        {
            if (root == null)
            {
                return;
            }

            confirm.interactable = !owner.Entering;
            cancel.interactable = !owner.Entering;
            confirm.GetComponentInChildren<TMP_Text>().text = owner.Entering ? "正在准备战场…" : "确认出征";
            status.text = owner.ExpeditionError ?? "补给在战斗结束后保存，放弃未结束的防守不结算。";
        }

        private async void Confirm()
        {
            if (owner.Entering)
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
                    root.SetActive(false);
                }

                Refresh();
            }
        }
    }
}
