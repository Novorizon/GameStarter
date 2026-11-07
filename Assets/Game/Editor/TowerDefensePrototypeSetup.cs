using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Game.Editor
{
    public static class TowerDefensePrototypeSetup
    {
        private const string ArtRoot = "Assets/GameAssets/TowerDefense/Art/";
        public const string ScenePath = "Assets/Scenes/TowerDefensePrototype.unity";

        [MenuItem("Tools/Novorizon/Tower Defense/Create Prototype Scene")]
        public static void CreateScene()
        {
            TowerDefenseFontSetup.EnsureFont();
            ConfigureArt();
            TowerDefenseResourcesSetup.EnsureConfigured();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("GameEntry");
            var entry = root.AddComponent<GameEntry>();
            var serialized = new SerializedObject(entry);
            serialized.FindProperty("inputActions").objectReferenceValue = AssetDatabase.LoadMainAssetAtPath("Assets/Resources/InputActions.inputactions");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Selection.activeGameObject = root;
            Debug.Log("TOWER_DEFENSE_SCENE_READY " + ScenePath);
        }

        [MenuItem("Tools/Novorizon/Tower Defense/Configure Prototype Art")]
        public static void ConfigureArt()
        {
            ConfigureSprite(ArtRoot + "Map.png");
            ConfigureSprite("Assets/GameAssets/Common/UI/Panel.png");
            var atlas = AssetImporter.GetAtPath("Assets/GameAssets/Common/UI/Icons.png") as TextureImporter;
            if (atlas != null)
            {
                atlas.textureType = TextureImporterType.Default;
                atlas.textureShape = TextureImporterShape.Texture2D;
                atlas.alphaIsTransparency = true;
                atlas.mipmapEnabled = false;
                atlas.npotScale = TextureImporterNPOTScale.None;
                atlas.maxTextureSize = 2048;
                atlas.SaveAndReimport();
            }
        }

        private static void ConfigureSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spriteBorder = path.EndsWith("Panel.png", System.StringComparison.Ordinal) ? new Vector4(180, 180, 180, 180) : Vector4.zero;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
            {
                throw new System.InvalidOperationException("Sprite import failed: " + path);
            }
        }
    }
}
