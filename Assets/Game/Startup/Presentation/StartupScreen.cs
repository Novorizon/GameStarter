using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>AOT 启动展示；在资源系统和热更 DLL 就绪前工作。</summary>
    public sealed class StartupScreen : MonoBehaviour, IDisposable
    {
        private IStartupProgressSource source;
        private TMP_Text message;
        private TMP_Text percent;
        private Image progress;
        private CanvasGroup group;
        private RectTransform safeArea;
        private Texture2D background;
        private bool ownsBackground;
        private bool disposed;
        private float openedAt;
        private float completeAt = -1;
        private Rect previousSafeArea;

        public static StartupScreen Open(IStartupProgressSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var root = new GameObject("StartupScreen", typeof(RectTransform));
            DontDestroyOnLoad(root);
            var screen = root.AddComponent<StartupScreen>();
            screen.source = source;
            try
            {
                screen.CreateInterface();
            }
            catch
            {
                screen.Dispose();
                throw;
            }
            return screen;
        }

        private void CreateInterface()
        {
            openedAt = Time.unscaledTime;
            var backdropCamera = new GameObject("StartupBackdropCamera").AddComponent<Camera>();
            backdropCamera.transform.SetParent(transform, false);
            backdropCamera.clearFlags = CameraClearFlags.SolidColor;
            backdropCamera.backgroundColor = new Color(.035f, .08f, .055f);
            backdropCamera.cullingMask = 0;
            backdropCamera.depth = -100;
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = .5f;
            gameObject.AddComponent<GraphicRaycaster>();
            group = gameObject.AddComponent<CanvasGroup>();
            background = StartupArtwork.LoadBackground(out ownsBackground);
            var back = Area("Background", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
            back.texture = background;
            var fitter = back.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = background != null ? (float)background.width / background.height : 16f / 9;
            var shade = Area("Shade", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            shade.color = new Color(.025f, .07f, .05f, .38f);
            safeArea = Area("SafeArea", transform, Vector2.zero, Vector2.one);
            var font = Resources.Load<TMP_FontAsset>("Bootstrap/StartupFont");
            if (font == null)
            {
                throw new InvalidOperationException("The built-in startup TMP font is missing.");
            }

            Text("Eyebrow", .61f, .58f, .93f, .64f, 23, "FOREST WARDENS", font, new Color(.95f, .78f, .43f));
            Text("Title", .60f, .44f, .94f, .59f, 82, "翠林守卫", font, new Color(.98f, .96f, .85f));
            Text("Subtitle", .61f, .37f, .94f, .44f, 25, "在林间筑起防线，守住每一束星光", font, new Color(.78f, .85f, .75f));
            var track = Area("ProgressTrack", safeArea, new Vector2(.61f, .18f), new Vector2(.94f, .194f)).gameObject.AddComponent<Image>();
            track.color = new Color(.025f, .07f, .05f, .8f);
            progress = Area("Progress", track.transform, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            progress.color = new Color(.90f, .71f, .33f);
            message = Text("Message", .61f, .21f, .89f, .26f, 23, "正在准备游戏", font, Color.white);
            percent = Text("Percent", .89f, .21f, .94f, .26f, 23, "0%", font, new Color(.95f, .78f, .43f));
            Text("Hint", .61f, .11f, .94f, .16f, 19, "选择防御塔，组合强化，守护林间哨站", font, new Color(.68f, .77f, .66f));
            ApplyLayout();
            Refresh();
        }

        private void ApplyLayout()
        {
            foreach (var widget in StartupArtwork.LoadLayout().widgets)
            {
                var target = safeArea.Find(widget.name) as RectTransform;
                if (target == null)
                {
                    continue;
                }

                target.anchorMin = new Vector2(widget.left, widget.bottom);
                target.anchorMax = new Vector2(widget.right, widget.top);
                var text = target.GetComponent<TMP_Text>();
                if (text == null)
                {
                    continue;
                }

                if (widget.size > 0)
                {
                    text.fontSize = widget.size;
                    text.fontSizeMax = widget.size;
                    text.fontSizeMin = widget.size * .65f;
                }

                if (!string.IsNullOrEmpty(widget.text))
                {
                    text.text = widget.text;
                }

                if (!string.IsNullOrEmpty(widget.color) && ColorUtility.TryParseHtmlString(widget.color, out var color))
                {
                    text.color = color;
                }
            }
        }

        private void Update()
        {
            Refresh();
        }

        private void Refresh()
        {
            if (disposed || source == null)
            {
                return;
            }

            if (previousSafeArea != Screen.safeArea && Screen.width > 0 && Screen.height > 0)
            {
                previousSafeArea = Screen.safeArea;
                safeArea.anchorMin = new Vector2(previousSafeArea.xMin / Screen.width, previousSafeArea.yMin / Screen.height);
                safeArea.anchorMax = new Vector2(previousSafeArea.xMax / Screen.width, previousSafeArea.yMax / Screen.height);
            }

            var state = source.ReadStartup();
            float fraction = state.Downloading && state.TotalBytes > 0
                ? (float)state.CompletedBytes / state.TotalBytes : state.Fraction;
            fraction = Mathf.Clamp01(state.Ready ? 1 : fraction);
            progress.rectTransform.anchorMax = new Vector2(fraction, 1);
            percent.text = Mathf.RoundToInt(fraction * 100) + "%";
            message.text = state.Downloading
                ? "更新资源  " + (state.CompletedBytes / 1048576f).ToString("0.0") + " / " + (state.TotalBytes / 1048576f).ToString("0.0") + " MB"
                : state.Message ?? "正在准备游戏";
            if (state.Failed)
            {
                message.text = "启动失败，请重新进入游戏";
                percent.text = "";
                return;
            }

            if (state.Ready && Time.unscaledTime - openedAt >= .45f)
            {
                if (completeAt < 0)
                {
                    completeAt = Time.unscaledTime;
                }

                group.alpha = 1 - Mathf.Clamp01((Time.unscaledTime - completeAt) / .25f);
                if (group.alpha <= 0)
                {
                    Dispose();
                }
            }
        }

        private TMP_Text Text(string label, float left, float bottom, float right, float top, int size, string value, TMP_FontAsset font, Color color)
        {
            var area = Area(label, safeArea, new Vector2(left, bottom), new Vector2(right, top));
            area.gameObject.SetActive(false);
            var text = area.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.enableAutoSizing = true;
            text.fontSizeMin = size * .65f;
            text.fontSizeMax = size;
            text.color = color;
            text.text = value;
            text.raycastTarget = false;
            area.gameObject.SetActive(true);
            return text;
        }

        private static RectTransform Area(string label, Transform parent, Vector2 minimum, Vector2 maximum)
        {
            var area = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(parent, false);
            area.anchorMin = minimum;
            area.anchorMax = maximum;
            area.offsetMin = Vector2.zero;
            area.offsetMax = Vector2.zero;
            return area;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            source = null;
            gameObject.SetActive(false);
            if (ownsBackground && background != null)
            {
                Destroy(background);
            }

            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (ownsBackground && !disposed && background != null)
            {
                Destroy(background);
            }

            disposed = true;
        }
    }
}
