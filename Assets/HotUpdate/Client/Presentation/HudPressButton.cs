using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    internal sealed class HudPressButton : Button
    {
        internal static readonly Color BlueHover = new Color(.47f, .78f, 1f, .30f);
        internal static readonly Color GreenHover = new Color(.38f, 1f, .68f, .30f);
        internal static readonly Color RedHover = new Color(1f, .51f, .54f, .33f);
        internal static readonly Color GoldHover = new Color(1f, .95f, .61f, .36f);
        private const float PressDuration = .11f;
        private float pressedUntil;
        private bool pendingRelease;
        private bool pointerInside;
        private HudHoverFill hoverFill;
        private Color hoverTint = BlueHover;

        internal void SetHoverTint(Color tint)
        {
            hoverTint = tint;
            if (hoverFill != null)
            {
                hoverFill.color = tint;
            }
        }

        public override void OnPointerEnter(PointerEventData eventData)
        {
            pointerInside = true;
            base.OnPointerEnter(eventData);
        }

        public override void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            base.OnPointerExit(eventData);
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            if (state == SelectionState.Pressed && !instant)
            {
                pressedUntil = Time.unscaledTime + PressDuration;
                pendingRelease = false;
                SetHoverVisible(false);
                base.DoStateTransition(state, true);
                return;
            }

            if (!instant && state != SelectionState.Disabled && Time.unscaledTime < pressedUntil)
            {
                pendingRelease = true;
                return;
            }

            pendingRelease = false;
            pressedUntil = 0;
            // Unity prioritizes selection over hover, including after a mouse click.
            if (state == SelectionState.Selected && pointerInside)
            {
                state = SelectionState.Highlighted;
            }

            base.DoStateTransition(state, instant);
            SetHoverVisible(state == SelectionState.Highlighted && pointerInside && isActiveAndEnabled);
        }

        private void SetHoverVisible(bool visible)
        {
            if (visible && hoverFill == null)
            {
                var layer = new GameObject("HoverFill", typeof(RectTransform));
                var rect = (RectTransform)layer.transform;
                rect.SetParent(transform, false);
                rect.SetAsFirstSibling();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(6f, 6f);
                rect.offsetMax = -rect.offsetMin;
                hoverFill = layer.AddComponent<HudHoverFill>();
                hoverFill.color = hoverTint;
                hoverFill.raycastTarget = false;
            }

            if (hoverFill != null && hoverFill.enabled != visible)
            {
                hoverFill.enabled = visible;
            }
        }

        private void LateUpdate()
        {
            if (pendingRelease && Time.unscaledTime >= pressedUntil)
            {
                pendingRelease = false;
                pressedUntil = 0;
                DoStateTransition(currentSelectionState, false);
            }
        }

        protected override void OnDisable()
        {
            pendingRelease = false;
            pressedUntil = 0;
            pointerInside = false;
            SetHoverVisible(false);
            base.OnDisable();
        }
    }

    // The translucent fill is composited above the existing sprite. Multiplicative tint can only darken a dark button.
    internal sealed class HudHoverFill : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0)
            {
                return;
            }

            const int cornerSteps = 4;
            float radius = Mathf.Min(16f, Mathf.Min(rect.width, rect.height) * .38f);
            var inset = rect.size * .5f - Vector2.one * radius;
            Color32 tint = color;
            mesh.AddVert(rect.center, tint, Vector2.zero);
            for (int corner = 0; corner < 4; corner++)
            {
                float x = corner == 0 || corner == 3 ? inset.x : -inset.x;
                float y = corner < 2 ? inset.y : -inset.y;
                var center = rect.center + new Vector2(x, y);
                for (int step = 0; step <= cornerSteps; step++)
                {
                    float angle = (corner + step / (float)cornerSteps) * Mathf.PI * .5f;
                    var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                    mesh.AddVert(center + offset, tint, Vector2.zero);
                }
            }

            int outlineCount = mesh.currentVertCount - 1;
            for (int index = 0; index < outlineCount; index++)
            {
                mesh.AddTriangle(0, index + 1, (index + 1) % outlineCount + 1);
            }
        }
    }
}
