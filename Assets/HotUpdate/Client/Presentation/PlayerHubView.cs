using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    public sealed class PlayerHubView : MonoBehaviour
    {
        private ClientFlow owner;
        private ClientArt art;
        private RectTransform safe;
        private TMP_Text nameLabel;
        private TMP_Text levelLabel;
        private TMP_Text xpLabel;
        private TMP_Text recordLabel;
        private TMP_Text errorLabel;
        private TMP_Text enterLabel;
        private Image xpFill;
        private Button enter;
        private readonly List<TMP_Text> taskValues = new List<TMP_Text>();
        private readonly List<Image> taskBars = new List<Image>();
        private Rect previousSafeArea;
        private readonly Color cream = new Color(.97f, .95f, .85f);
        private readonly Color gold = new Color(.95f, .77f, .41f);
        private readonly Color muted = new Color(.66f, .77f, .68f);

        public static PlayerHubView Create(ClientFlow owner, Transform parent)
        {
            var root = new GameObject("PlayerPage", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<PlayerHubView>();
            view.owner = owner;
            view.art = owner.Art;
            view.CreateInterface();
            view.Refresh();
            return view;
        }

        private void CreateInterface()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            var backdrop = Area("ForestCitadel", transform, 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
            backdrop.texture = art.Background;
            backdrop.raycastTarget = false;
            var ratio = backdrop.gameObject.AddComponent<AspectRatioFitter>();
            ratio.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            ratio.aspectRatio = (float)art.Background.width / art.Background.height;
            var shade = Area("Shade", transform, 0, 0, 1, 1).gameObject.AddComponent<Image>();
            shade.color = new Color(.02f, .065f, .04f, .14f);
            shade.raycastTarget = false;
            safe = Area("SafeArea", transform, 0, 0, 1, 1);
            var titleShade = Area("TitleShade", safe, .035f, .07f, .57f, .32f).gameObject.AddComponent<Image>();
            titleShade.color = new Color(.03f, .09f, .055f, .65f);
            titleShade.raycastTarget = false;
            Text("Brand", safe, .065f, .26f, .55f, .31f, 24, "FOREST WARDENS", gold);
            Text("GameTitle", safe, .06f, .13f, .56f, .275f, 88, art.Title, cream);
            Text("Tagline", safe, .065f, .08f, .55f, .14f, 25, "筑起防线 · 组合强化 · 守住林间哨站", cream);
            var panel = Panel("PlayerDashboard", safe, .635f, .07f, .968f, .94f);
            var avatar = Area("Avatar", panel, .055f, .77f, .285f, .963f).gameObject.AddComponent<Image>();
            avatar.sprite = art.Avatar;
            avatar.preserveAspect = true;
            avatar.raycastTarget = false;
            nameLabel = Text("PlayerName", panel, .32f, .885f, .94f, .952f, 34, "", cream);
            levelLabel = Text("PlayerLevel", panel, .32f, .822f, .94f, .883f, 23, "", gold);
            xpLabel = Text("Experience", panel, .32f, .772f, .94f, .817f, 18, "", muted);
            xpFill = Bar("LevelProgress", panel, .32f, .75f, .93f, .758f, gold);
            Text("LocalProfile", panel, .065f, .694f, .94f, .735f, 17, "本地档案 · 探索进度保存在此设备", muted);
            recordLabel = Text("BattleRecord", panel, .065f, .59f, .94f, .688f, 23, "", cream);
            Divider(panel, .57f);
            Text("MissionHeading", panel, .065f, .515f, .94f, .568f, 25, "旅程任务", gold);
            for (int index = 0; index < owner.Journey.Tasks.Count; index++)
            {
                var task = owner.Journey.Tasks[index];
                float top = .50f - index * .085f;
                Text("Mission " + task.Id, panel, .065f, top - .032f, .69f, top, 22, task.Name, cream);
                Text("TaskGoal", panel, .065f, top - .064f, .94f, top - .034f, 17, task.Description, muted);
                taskValues.Add(Text("MissionValue", panel, .70f, top - .032f, .94f, top, 20, "", gold));
                taskBars.Add(Bar("MissionProgress", panel, .065f, top - .075f, .94f, top - .069f, new Color(.41f, .68f, .42f)));
            }

            Divider(panel, .22f);
            Text("Campaign", panel, .065f, .164f, .94f, .215f, 21, "森林战役  /  " + owner.Stage.Name, muted);
            var start = Panel("EnterTowerDefense", panel, .06f, .047f, .94f, .157f);
            enter = start.gameObject.AddComponent<Button>();
            enter.targetGraphic = start.GetComponent<Image>();
            enter.onClick.AddListener(EnterBattle);
            enterLabel = Text("EnterLabel", start, .06f, .15f, .94f, .85f, 33, "出征 · 守卫哨站", gold);
            errorLabel = Text("Notice", safe, .635f, .015f, .968f, .065f, 20, "", cream);
        }

        private async void EnterBattle()
        {
            await owner.EnterBattleAsync();
        }

        public void Refresh()
        {
            var journey = owner.Journey;
            nameLabel.text = journey.Nickname;
            levelLabel.text = "Lv." + journey.Level.Id + "  " + journey.Level.Title;
            var next = journey.NextLevel;
            int start = journey.Level.Experience;
            xpLabel.text = next == null ? "探索等级已满" : "探索经验  " + journey.Experience + " / " + next.Experience;
            float fraction = next == null ? 1 : (float)(journey.Experience - start) / (next.Experience - start);
            xpFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
            recordLabel.text = "最高波次  <color=#F0C46A>" + journey.HighestWave + "</color>   防守胜利  <color=#F0C46A>" + journey.Victories
                + "</color>\n<size=80%><color=#B0C4B0>累计击败 " + journey.EnemiesKilled + " 名敌人</color></size>";
            for (int index = 0; index < taskValues.Count; index++)
            {
                var task = journey.Tasks[index];
                int value = journey.Progress(task);
                taskValues[index].text = journey.Completed(task) ? "已达成" : value + " / " + task.Target;
                taskBars[index].rectTransform.anchorMax = new Vector2((float)value / task.Target, 1);
            }

            enter.interactable = !owner.Entering;
            enterLabel.text = owner.Entering ? "正在准备战场…" : "出征 · 守卫哨站";
        }

        public void ShowError(string value)
        {
            errorLabel.text = value;
        }

        private void Update()
        {
            if (previousSafeArea == Screen.safeArea || Screen.width == 0 || Screen.height == 0)
            {
                return;
            }

            previousSafeArea = Screen.safeArea;
            safe.anchorMin = new Vector2(previousSafeArea.xMin / Screen.width, previousSafeArea.yMin / Screen.height);
            safe.anchorMax = new Vector2(previousSafeArea.xMax / Screen.width, previousSafeArea.yMax / Screen.height);
        }

        private RectTransform Panel(string label, Transform parent, float left, float bottom, float right, float top)
        {
            var area = Area(label, parent, left, bottom, right, top);
            var image = area.gameObject.AddComponent<Image>();
            image.sprite = art.Panel;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 7;
            return area;
        }

        private void Divider(Transform parent, float bottom)
        {
            var line = Area("Divider", parent, .065f, bottom, .94f, bottom + .0015f).gameObject.AddComponent<Image>();
            line.color = new Color(.60f, .52f, .29f, .65f);
            line.raycastTarget = false;
        }

        private static Image Bar(string label, Transform parent, float left, float bottom, float right, float top, Color color)
        {
            var track = Area(label, parent, left, bottom, right, top).gameObject.AddComponent<Image>();
            track.color = new Color(.025f, .07f, .04f, .8f);
            track.raycastTarget = false;
            var fill = Area("Fill", track.transform, 0, 0, 1, 1).gameObject.AddComponent<Image>();
            fill.color = color;
            fill.raycastTarget = false;
            return fill;
        }

        private TMP_Text Text(string label, Transform parent, float left, float bottom, float right, float top, int size, string value, Color color)
        {
            var area = Area(label, parent, left, bottom, right, top);
            area.gameObject.SetActive(false);
            var text = area.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = art.Font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .7f;
            text.fontSizeMax = size;
            text.text = value;
            text.color = color;
            text.raycastTarget = false;
            area.gameObject.SetActive(true);
            return text;
        }

        private static RectTransform Area(string label, Transform parent, float left, float bottom, float right, float top)
        {
            var area = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(parent, false);
            area.anchorMin = new Vector2(left, bottom);
            area.anchorMax = new Vector2(right, top);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            return area;
        }
    }
}
