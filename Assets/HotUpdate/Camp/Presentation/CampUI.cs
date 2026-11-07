using System;
using Game.HotUpdate.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Camp
{
    /// <summary>营地表现的局部控件工厂；不注册全局窗口服务。</summary>
    public sealed class CampUI
    {
        private readonly ClientArt art;
        public static readonly Color Cream = new Color(.98f, .95f, .84f);
        public static readonly Color Gold = new Color(.97f, .79f, .44f);
        public static readonly Color Muted = new Color(.70f, .82f, .74f);

        public CampUI(ClientArt art)
        {
            this.art = art;
        }

        public static RectTransform Area(string label, Transform parent, float left, float bottom, float right, float top)
        {
            var area = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(parent, false);
            area.anchorMin = new Vector2(left, bottom);
            area.anchorMax = new Vector2(right, top);
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            return area;
        }

        public RectTransform Panel(string label, Transform parent, float left, float bottom, float right, float top)
        {
            var area = Area(label, parent, left, bottom, right, top);
            var image = area.gameObject.AddComponent<Image>();
            image.sprite = art.Panel;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 7;
            return area;
        }

        public static RectTransform Overlay(string label, Transform parent)
        {
            var area = Area(label, parent, 0, 0, 1, 1);
            area.gameObject.AddComponent<Image>().color = new Color(.025f, .07f, .06f, .78f);
            return area;
        }

        public static RectTransform ScrollList(string label, Transform parent, float left, float bottom, float right, float top, float height)
        {
            var viewport = Area(label, parent, left, bottom, right, top);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Area("Content", viewport, 0, 1, 1, 1);
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, height);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            return content;
        }

        public TMP_Text Text(string label, Transform parent, float left, float bottom, float right, float top, int size, string value, Color color)
        {
            var area = Area(label, parent, left, bottom, right, top);
            area.gameObject.SetActive(false);
            var text = area.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = art.Font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .75f;
            text.fontSizeMax = size;
            text.text = value;
            text.color = color;
            text.raycastTarget = false;
            area.gameObject.SetActive(true);
            return text;
        }

        public Button ActionButton(string label, Transform parent, float left, float bottom, float right, float top, string text, Action action)
        {
            var area = Panel(label, parent, left, bottom, right, top);
            var button = area.gameObject.AddComponent<Button>();
            button.targetGraphic = area.GetComponent<Image>();
            button.onClick.AddListener(() => action());
            Text("Caption", area, .07f, .12f, .93f, .88f, 28, text, Gold).alignment = TextAlignmentOptions.Center;
            return button;
        }
    }
}
