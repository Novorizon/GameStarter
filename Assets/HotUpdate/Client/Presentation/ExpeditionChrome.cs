using System;
using Game.HotUpdate.Camp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    internal enum ExpeditionGlyph
    {
        Swords, Coins, Gate, Compass,
        Wood, Stone, Laurel, BrokenGate,
        Scroll, Chest, Mountains, Helmet,
        Flag, Tent, Back, Lock
    }

    internal enum ExpeditionFrameStyle
    {
        Panel, Slate, Victory, Defeat, Selected, Secondary, Primary
    }

    internal sealed class ExpeditionChrome
    {
        internal static readonly Color Ink = new Color(.94f, .91f, .83f);
        internal static readonly Color Muted = new Color(.72f, .80f, .76f);
        internal static readonly Color Gold = new Color(.97f, .85f, .59f);
        internal static readonly Color Dark = new Color(.16f, .10f, .04f);
        internal static readonly Color Loss = new Color(.95f, .75f, .65f);
        private static readonly Color TitleLight = new Color(1f, .95f, .76f);
        private static readonly Color TitleGold = new Color(.87f, .68f, .38f);
        private readonly ClientArt art;
        private readonly CampUI ui;

        internal ExpeditionChrome(ClientArt art)
        {
            this.art = art;
            ui = new CampUI(art);
        }

        internal static Sprite[] SliceIcons(Texture2D atlas)
        {
            const int columns = 4;
            if (atlas == null || atlas.width != atlas.height)
            {
                return null;
            }

            var sprites = new Sprite[columns * columns];
            float cell = atlas.width / (float)columns;
            float gutter = Mathf.Ceil(cell * .105f);
            for (int index = 0; index < sprites.Length; index++)
            {
                int x = index % columns;
                int y = columns - 1 - index / columns;
                float size = Mathf.Floor(cell - 2 * gutter);
                var rect = new Rect(Mathf.Round(cell * x) + gutter, Mathf.Round(cell * y) + gutter, size, size);
                sprites[index] = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                sprites[index].name = "Expedition " + (ExpeditionGlyph)index;
            }

            return sprites;
        }

        internal static RectTransform Frame(string name, Transform parent, float left, float bottom, float right, float top, ExpeditionFrameStyle style)
        {
            var rect = CampUI.Area(name, parent, left, bottom, right, top);
            var frame = rect.gameObject.AddComponent<ExpeditionFrame>();
            frame.Style = style;
            frame.raycastTarget = false;
            return rect;
        }

        internal static Button Bind(RectTransform rect, Action action)
        {
            var target = rect.GetComponent<ExpeditionFrame>();
            target.raycastTarget = true;
            var button = rect.gameObject.AddComponent<HudPressButton>();
            button.targetGraphic = target;
            button.SetHoverTint(target.HoverTint);
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = Color.white;
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(.59f, .65f, .65f);
            colors.disabledColor = new Color(.61f, .66f, .68f, .91f);
            colors.fadeDuration = .08f;
            button.colors = colors;
            button.onClick.AddListener(() => action());
            return button;
        }

        internal TMP_Text Text(string name, Transform parent, float l, float b, float r, float t, int size, string value, Color color, bool wrap = false)
        {
            var text = ui.Text(name, parent, l, b, r, t, size, value, color);
            text.richText = false;
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        internal TMP_Text Title(string name, Transform parent, float l, float b, float r, float t, int size, string value)
        {
            var text = Text(name, parent, l, b, r, t, size, value, Color.white);
            text.fontStyle = FontStyles.Bold;
            text.enableVertexGradient = true;
            text.colorGradient = new VertexGradient(TitleLight, TitleLight, TitleGold, TitleGold);
            return text;
        }

        internal TMP_Text Number(string name, Transform parent, float left, float bottom, float right, float top, int size)
        {
            var text = Title(name, parent, left, bottom, right, top, size, "");
            if (art.ExpeditionNumberFont != null)
            {
                text.font = art.ExpeditionNumberFont;
                text.fontStyle = FontStyles.Normal;
            }

            return text;
        }

        internal Image Icon(ExpeditionGlyph glyph, Transform parent, float left, float bottom, float right, float top)
        {
            var rect = CampUI.Area(glyph + "Icon", parent, left, bottom, right, top);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = art.ExpeditionIcon(glyph);
            image.preserveAspect = true;
            image.enabled = image.sprite != null;
            image.raycastTarget = false;
            return image;
        }

        internal static void Texture(string name, Texture2D texture, RectTransform parent, AspectRatioFitter.AspectMode mode)
        {
            if (texture == null)
            {
                return;
            }

            if (mode == AspectRatioFitter.AspectMode.EnvelopeParent)
            {
                parent.gameObject.AddComponent<RectMask2D>();
            }

            var area = CampUI.Area(name, parent, 0, 0, 1, 1);
            var image = area.gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
            var fitter = area.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectRatio = texture.width / (float)texture.height;
            fitter.aspectMode = mode;
        }

        internal static void Rule(Transform parent, float left, float bottom, float right, float top, Color color)
        {
            var line = CampUI.Area("Rule", parent, left, bottom, right, top).gameObject.AddComponent<Image>();
            line.raycastTarget = false;
            line.color = color;
        }
    }

    /// <summary>在同一网格上绘制切角底色和相接的边线，缩放时保留边缘像素宽度。</summary>
    internal sealed class ExpeditionFrame : MaskableGraphic
    {
        private struct Surface
        {
            public Color Bottom, Center, Top, RimDark, RimLight;
            public float Cut, Rim;
            public bool DoubleTrim;

            public Surface(uint bottom, uint center, uint top, uint dark, uint light, float cut, float rim, bool doubleTrim = false)
            {
                Bottom = Hex(bottom);
                Center = Hex(center);
                Top = Hex(top);
                RimDark = Hex(dark);
                RimLight = Hex(light);
                Cut = cut;
                Rim = rim;
                DoubleTrim = doubleTrim;
            }
        }

        private static readonly Surface[] Surfaces =
        {
            new Surface(0x071c20f5, 0x0a2529f5, 0x113337f8, 0x8f7247ff, 0xe4c894ff, 17, 1.9f, true),
            new Surface(0x102a2df5, 0x16373bf5, 0x284649f8, 0x638078ff, 0xb0b59bff, 12, 1.5f),
            new Surface(0x0b2b2af7, 0x174239f7, 0x2c5145fc, 0x467e6bff, 0xa0c7a8ff, 13, 1.7f),
            new Surface(0x231f20f7, 0x342c28f7, 0x4a3b32fc, 0x8f5547ff, 0xc78c73ff, 13, 1.7f),
            new Surface(0x3c2b16ff, 0x694b21ff, 0x89622cff, 0xc28b38ff, 0xffecb0ff, 13, 2.6f, true),
            new Surface(0x08252bff, 0x143e43ff, 0x345859ff, 0xa17e46ff, 0xf3d49aff, 21, 2.7f, true),
            new Surface(0xa5722bff, 0xe3ad4fff, 0xf8d887ff, 0xe2b659ff, 0xfff1bdff, 22, 3.2f, true)
        };

        private ExpeditionFrameStyle style;

        internal Color HoverTint => style switch
        {
            ExpeditionFrameStyle.Primary or ExpeditionFrameStyle.Selected => HudPressButton.GoldHover,
            ExpeditionFrameStyle.Victory => HudPressButton.GreenHover,
            ExpeditionFrameStyle.Defeat => HudPressButton.RedHover,
            _ => HudPressButton.BlueHover
        };

        internal ExpeditionFrameStyle Style
        {
            get => style;
            set
            {
                if (style != value)
                {
                    style = value;
                    SetVerticesDirty();
                    if (TryGetComponent<HudPressButton>(out var button))
                    {
                        button.SetHoverTint(HoverTint);
                    }
                }
            }
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            float size = Mathf.Min(rect.width, rect.height);
            if (size < 1)
            {
                return;
            }

            var surface = Surfaces[(int)style];
            float screenPixel = 1 / Mathf.Max(.15f, canvas != null ? canvas.scaleFactor : 1);
            float line = Mathf.Min(Mathf.Max(screenPixel, surface.Rim), size * .11f);
            float cut = Mathf.Min(surface.Cut, size * .26f);
            AddFill(mesh, rect, cut, surface);
            AddRim(mesh, rect, cut, line, surface.RimDark, surface.RimLight);
            if (surface.DoubleTrim && size > 11 * line)
            {
                var inner = Inset(rect, 2.1f * line);
                Color shadow = new Color(.07f, .09f, .07f, style == ExpeditionFrameStyle.Primary ? .36f : .30f);
                Color gloss = new Color(1f, .95f, .77f, style == ExpeditionFrameStyle.Primary ? .83f : .36f);
                AddRim(mesh, inner, Mathf.Max(1, cut - line), line * .73f, shadow, gloss);
            }
        }

        private void AddFill(VertexHelper mesh, Rect rect, float cut, Surface surface)
        {
            int center = mesh.currentVertCount;
            mesh.AddVert(rect.center, surface.Center * color, Vector2.zero);
            for (int index = 0; index < 8; index++)
            {
                var point = Corner(rect, cut, index);
                float amount = Mathf.InverseLerp(rect.yMin, rect.yMax, point.y);
                mesh.AddVert(point, Color.Lerp(surface.Bottom, surface.Top, amount) * color, Vector2.zero);
            }

            for (int index = 0; index < 8; index++)
            {
                mesh.AddTriangle(center, center + 1 + index, center + 1 + (index + 1) % 8);
            }
        }

        private void AddRim(VertexHelper mesh, Rect rect, float cut, float width, Color dark, Color light)
        {
            var inner = Inset(rect, width);
            float innerCut = Mathf.Max(0, cut - .59f * width);
            int start = mesh.currentVertCount;
            for (int index = 0; index < 8; index++)
            {
                var point = Corner(rect, cut, index);
                var edgeColor = Color.Lerp(dark, light, Mathf.InverseLerp(rect.yMin, rect.yMax, point.y)) * color;
                mesh.AddVert(point, edgeColor, Vector2.zero);
                mesh.AddVert(Corner(inner, innerCut, index), edgeColor, Vector2.zero);
            }

            for (int index = 0; index < 8; index++)
            {
                int a = start + index * 2;
                int b = start + (index + 1) % 8 * 2;
                mesh.AddTriangle(a, b, b + 1);
                mesh.AddTriangle(a, b + 1, a + 1);
            }
        }

        private static Rect Inset(Rect rect, float width) => Rect.MinMaxRect(rect.xMin + width, rect.yMin + width, rect.xMax - width, rect.yMax - width);

        private static Vector2 Corner(Rect rect, float cut, int index)
        {
            return index switch
            {
                0 => new Vector2(rect.xMin + cut, rect.yMax),
                1 => new Vector2(rect.xMax - cut, rect.yMax),
                2 => new Vector2(rect.xMax, rect.yMax - cut),
                3 => new Vector2(rect.xMax, rect.yMin + cut),
                4 => new Vector2(rect.xMax - cut, rect.yMin),
                5 => new Vector2(rect.xMin + cut, rect.yMin),
                6 => new Vector2(rect.xMin, rect.yMin + cut),
                _ => new Vector2(rect.xMin, rect.yMax - cut)
            };
        }

        private static Color32 Hex(uint rgba) => new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
    }
}
