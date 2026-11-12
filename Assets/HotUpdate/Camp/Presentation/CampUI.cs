using System;
using Game.HotUpdate.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Camp
{
    internal enum CampPiece
    {
        Panel, Card, Control, Primary, Badge, Track, Fill, Selected,
        Shelter, Wood, Stone, Journal, Compass, Lock, Arrow, Close,
        Flag, Shield, Coin, Spark, Check, Waves, User, Edit
    }

    /// <summary>营地表现的局部控件工厂；不注册全局窗口服务。</summary>
    public sealed class CampUI
    {
        private readonly ClientArt art;
        public static readonly Color Cream = new Color(.96f, .98f, .93f);
        public static readonly Color Gold = new Color(.99f, .85f, .58f);
        public static readonly Color Muted = new Color(.71f, .83f, .79f);
        internal static readonly Color DarkInk = new Color(.115f, .086f, .045f);
        internal static readonly Color Mint = new Color(.72f, .92f, .81f);
        internal static readonly Color Danger = new Color(1f, .70f, .58f);

        public CampUI(ClientArt art)
        {
            this.art = art;
        }

        internal Color PrimaryInk => art.CampSprite(CampPiece.Primary) != null ? DarkInk : Cream;

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

        internal static Sprite[] SliceSkin(Texture2D atlas)
        {
            const int cell = 128;
            if (atlas == null || atlas.width != 8 * cell || atlas.height != 3 * cell)
            {
                return null;
            }

            var sprites = new Sprite[24];
            for (int index = 0; index < sprites.Length; index++)
            {
                var piece = (CampPiece)index;
                int x = index % 8 * cell;
                int y = (2 - index / 8) * cell;
                var rect = new Rect(x, y, cell, cell);
                var border = Vector4.one * (piece == CampPiece.Panel ? 44 : 34);
                if (piece == CampPiece.Badge || piece == CampPiece.Track || piece == CampPiece.Fill)
                {
                    rect = new Rect(x + 8, y + 48, 112, 32);
                    border = new Vector4(16, 0, 16, 0);
                }
                else if (piece >= CampPiece.Shelter)
                {
                    rect = new Rect(x + 12, y + 12, 104, 104);
                    border = Vector4.zero;
                }

                sprites[index] = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, border);
                sprites[index].name = "Camp HUD " + piece;
            }

            return sprites;
        }

        internal RectTransform Panel(string label, Transform parent, float left, float bottom, float right, float top, CampPiece style = CampPiece.Panel)
        {
            var area = Area(label, parent, left, bottom, right, top);
            Style(area.gameObject.AddComponent<CampFrame>(), style);
            return area;
        }

        internal void Style(Image image, CampPiece style)
        {
            var sprite = art.CampSprite(style);
            bool meter = style == CampPiece.Fill || style == CampPiece.Track;
            image.sprite = sprite != null || meter ? sprite : art.Panel;
            image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = sprite != null ? Color.white : style switch
            {
                CampPiece.Primary => new Color(.96f, .81f, .53f),
                CampPiece.Track => new Color(.07f, .16f, .17f),
                CampPiece.Fill => Mint,
                _ => Color.white
            };
            if (image is CampFrame frame)
            {
                frame.FitBorder();
            }

            if (image.TryGetComponent<HudPressButton>(out var button))
            {
                button.SetHoverTint(style == CampPiece.Primary ? HudPressButton.GoldHover : HudPressButton.GreenHover);
            }
        }

        internal Image Glyph(CampPiece piece, Transform parent, float left, float bottom, float right, float top, Color color)
        {
            var area = Area(piece + "Icon", parent, left, bottom, right, top);
            var image = area.gameObject.AddComponent<Image>();
            image.sprite = art.CampSprite(piece);
            image.enabled = image.sprite != null;
            image.color = color;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        internal static void Divider(Transform parent, float left, float bottom, float right, float top)
        {
            var area = Area("Divider", parent, left, bottom, right, top);
            var image = area.gameObject.AddComponent<Image>();
            image.color = new Color(.61f, .81f, .75f, .29f);
            image.raycastTarget = false;
        }

        internal Image Meter(Transform parent, float left, float bottom, float right, float top)
        {
            var track = Panel("Track", parent, left, bottom, right, top, CampPiece.Track);
            track.GetComponent<Image>().raycastTarget = false;
            var fill = Panel("Fill", track, 0, 0, 1, 1, CampPiece.Fill).GetComponent<Image>();
            fill.raycastTarget = false;
            return fill;
        }

        internal static void SetProgress(Image fill, float progress)
        {
            fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(progress), 1);
            fill.enabled = progress > 0;
        }

        public static RectTransform Overlay(string label, Transform parent, float opacity = .74f)
        {
            var area = Area(label, parent, 0, 0, 1, 1);
            area.gameObject.AddComponent<Image>().color = new Color(.028f, .067f, .075f, opacity);
            return area;
        }

        public static RectTransform ScrollList(string label, Transform parent, float left, float bottom, float right, float top, float height)
        {
            var viewport = Area(label, parent, left, bottom, right, top);
            viewport.gameObject.AddComponent<Image>().color = Color.clear;
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = Area("Content", viewport, 0, 1, .96f, 1);
            content.pivot = new Vector2(.5f, 1);
            content.sizeDelta = new Vector2(0, height);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 35;
            var track = Area("Scrollbar", viewport, .978f, 0, 1, 1);
            track.gameObject.AddComponent<Image>().color = new Color(.38f, .48f, .39f, .30f);
            var handle = Area("Handle", track, 0, 0, 1, 1);
            var handleImage = handle.gameObject.AddComponent<Image>();
            handleImage.color = Gold;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return content;
        }

        public TMP_Text Text(string label, Transform parent, float left, float bottom, float right, float top, int size, string value, Color color, bool bold = false)
        {
            var area = Area(label, parent, left, bottom, right, top);
            area.gameObject.SetActive(false);
            var text = area.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = art.CampFont != null ? art.CampFont : art.Font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .75f;
            text.fontSizeMax = size;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.text = value;
            text.color = color;
            text.raycastTarget = false;
            area.gameObject.SetActive(true);
            return text;
        }

        internal static Button BindButton(RectTransform area, Action action, CampPiece style = CampPiece.Control)
        {
            var button = area.gameObject.AddComponent<HudPressButton>();
            button.targetGraphic = area.GetComponent<Image>();
            button.SetHoverTint(style == CampPiece.Primary ? HudPressButton.GoldHover : HudPressButton.GreenHover);
            var colors = button.colors;
            colors.highlightedColor = Color.white;
            colors.selectedColor = new Color(.93f, 1, .95f);
            colors.pressedColor = new Color(.58f, .73f, .70f);
            colors.disabledColor = new Color(.57f, .64f, .65f, .88f);
            colors.fadeDuration = .055f;
            button.colors = colors;
            button.onClick.AddListener(() => action());
            return button;
        }

        internal Button ActionButton(
            string label,
            Transform parent,
            float left,
            float bottom,
            float right,
            float top,
            string text,
            Action action,
            CampPiece style = CampPiece.Control,
            CampPiece? icon = null)
        {
            var area = Panel(label, parent, left, bottom, right, top, style);
            var button = BindButton(area, action, style);
            Color ink = style == CampPiece.Primary ? PrimaryInk : Cream;
            if (icon.HasValue)
            {
                Glyph(icon.Value, area, .12f, .20f, .31f, .80f, ink);
            }

            var caption = Text("Caption", area, icon.HasValue ? .32f : .07f, .12f, .93f, .88f, 27, text, ink, bold: true);
            caption.alignment = TextAlignmentOptions.Center;
            caption.enableWordWrapping = false;
            return button;
        }
    }

    internal sealed class CampFrame : Image
    {
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            FitBorder();
        }

        internal void FitBorder()
        {
            if (sprite == null)
            {
                return;
            }

            var size = GetPixelAdjustedRect().size;
            if (size.x <= 0 || size.y <= 0)
            {
                return;
            }

            var border = sprite.border;
            float fit = Mathf.Max((border.x + border.z) / size.x, (border.y + border.w) / size.y);
            float scale = canvas != null ? canvas.scaleFactor : 1;
            float preferred = border.y == 0 ? sprite.rect.height / size.y : sprite.rect.width > 128 ? 7 : Mathf.Min(.94f, scale * 1.55f);
            pixelsPerUnitMultiplier = Mathf.Max(fit, preferred);
        }
    }
}
