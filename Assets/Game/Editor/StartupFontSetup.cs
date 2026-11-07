using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Game.Editor
{
    public static class StartupFontSetup
    {
        [MenuItem("Tools/Novorizon/GameStarter/Prepare Startup Font")]
        public static void Generate()
        {
            TowerDefenseFontSetup.EnsureFont();
            const string path = "Assets/Resources/Bootstrap/StartupFont.asset";
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Bootstrap/StartupFont.otf");
            var font = TMP_FontAsset.CreateFontAsset(source, 48, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            font.name = "StartupUI";
            font.material.shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            AssetDatabase.CreateAsset(font, path);
            var characters = new SortedSet<char>("翠林守卫在间筑起防线住每一束星光选择御塔组合强化护哨站正在准备游戏初始化资源系统检查版本载入玩家页面代码完成更新失败请网络后重新进入");
            for (char value = ' '; value <= '~'; value++)
            {
                characters.Add(value);
            }

            if (!font.TryAddCharacters(string.Concat(characters), out string missing))
            {
                throw new InvalidOperationException("Startup font is missing glyphs: " + missing);
            }

            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures)
            {
                if (atlas != null)
                {
                    atlas.name = "StartupUI Atlas";
                    atlas.Apply(false, false);
                    AssetDatabase.AddObjectToAsset(atlas, font);
                    EditorUtility.SetDirty(atlas);
                }
            }

            EditorUtility.SetDirty(font);
            EditorUtility.SetDirty(font.material);
            AssetDatabase.SaveAssets();
            Debug.Log("STARTUP_FONT_READY");
        }
    }
}
