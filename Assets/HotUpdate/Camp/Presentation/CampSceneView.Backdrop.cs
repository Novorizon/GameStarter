using UnityEngine;
using UnityEngine.Rendering;

namespace Game.HotUpdate.Camp
{
    public sealed partial class CampSceneView
    {
        private float artworkHalfWidth;

        private void CreateBackdrop(Sprite sprite)
        {
            var backdrop = new GameObject("IslandArtwork").AddComponent<SpriteRenderer>();
            backdrop.transform.SetParent(transform, false);
            var rotation = sceneCamera.transform.localRotation;
            backdrop.transform.localRotation = rotation;
            backdrop.sprite = sprite;
            float scale = 2 * OverviewHalfHeight * sprite.pixelsPerUnit / sprite.rect.height;
            artworkHalfWidth = OverviewHalfHeight * sprite.rect.width / sprite.rect.height;
            backdrop.transform.localPosition = rotation * (Vector3.forward * 18 - sprite.bounds.center * scale);
            backdrop.transform.localScale = new Vector3(scale, scale, 1);
            var material = new Material(worldSprite) { renderQueue = (int)RenderQueue.Geometry - 1 };
            owned.Add(material);
            backdrop.sharedMaterial = material;
        }

        // The orthographic camera projects the artwork onto the camp's ground plane.
        // Artwork coordinates run from the upper-left corner to the lower-right corner.
        private Vector3 ArtworkGroundPoint(Vector2 anchor)
        {
            float x = (2 * anchor.x - 1) * artworkHalfWidth;
            float z = (1 - 2 * anchor.y) * OverviewHalfHeight / Mathf.Sin(OverviewPitch * Mathf.Deg2Rad);
            return new Vector3(x, 0, z);
        }

        private Vector3 CampPosition(Vector2 anchor)
        {
            return settlement.InverseTransformPoint(transform.TransformPoint(ArtworkGroundPoint(anchor)));
        }
    }
}
