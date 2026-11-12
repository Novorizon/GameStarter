using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Game.Editor
{
    /// <summary>从随项目分发的 Noto 子集生成 TMP 动态 SDF；字体、材质和图集一起进入资源包。</summary>
    public static class TowerDefenseFontSetup
    {
        public const string SourceFontPath = "Assets/GameAssets/Common/Fonts/GameUI.otf";
        public const string FontAssetPath = "Assets/GameAssets/Common/Fonts/GameUI.asset";
        public const string HudFontAssetPath = "Assets/GameAssets/Common/Fonts/BattleHudMedium.asset";
        public const string HudBoldFontAssetPath = HudFontDirectory + "BattleHudBold.asset";
        private const string HudFontDirectory = "Assets/GameAssets/Common/Fonts/";
        private const string HudFontLicensePath = "Assets/StreamingAssets/ThirdPartyLicenses/Battle-HUD-Sans.txt";
        private const string LicensePath = "Assets/GameAssets/Common/Fonts/LICENSE-Noto.txt";
        private const string MobileShaderName = "TextMeshPro/Mobile/Distance Field";
        private const string SettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private const int PointSize = 48;
        private const int AtlasSize = 1024;
        private const int Padding = 6;

        private const string MobileShaderPath = "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader";
        private static bool operationRunning;
        private static bool generateWhenReady;
        private static double deadline;

        /// <summary>批处理不带 -quit；导入完成后继续生成，最后自行以 0/1 退出。</summary>
        [MenuItem("Tools/Novorizon/Tower Defense/Prepare TMP Font")]
        public static void GenerateFont()
        {
            StartOperation(true);
        }

        /// <summary>异步导入官方 Essentials，不导入 Examples & Extras；批处理不带 -quit。</summary>
        [MenuItem("Tools/Novorizon/Tower Defense/Import TMP Essentials")]
        public static void EnsureEssentialResources()
        {
            StartOperation(false);
        }

        private static void StartOperation(bool generateFont)
        {
            if (operationRunning)
            {
                generateWhenReady |= generateFont;
                return;
            }

            operationRunning = true;
            generateWhenReady = generateFont;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += CheckDeadline;
            try
            {
                RequireEditMode();
                if (EssentialsReady())
                {
                    EditorApplication.delayCall += CompleteAfterImport;
                    return;
                }

                var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_FontAsset).Assembly);
                string essentials = package == null ? null : Path.Combine(package.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (string.IsNullOrEmpty(essentials) || !File.Exists(essentials))
                {
                    throw new FileNotFoundException("Resolve com.unity.textmeshpro 3.0.6 before importing its official Essential Resources.", essentials);
                }

                AssetDatabase.importPackageCompleted += OnImportCompleted;
                AssetDatabase.importPackageFailed += OnImportFailed;
                AssetDatabase.importPackageCancelled += OnImportCancelled;
                AssetDatabase.ImportPackage(essentials, false);
            }
            catch (Exception error)
            {
                Finish(error);
            }
        }

        private static bool IsEssentials(string packageName)
        {
            return string.Equals(Path.GetFileNameWithoutExtension(packageName), "TMP Essential Resources", StringComparison.OrdinalIgnoreCase);
        }

        private static void OnImportCompleted(string packageName)
        {
            if (!IsEssentials(packageName))
            {
                return;
            }

            UnregisterImportEvents();
            EditorApplication.delayCall += CompleteAfterImport;
        }

        private static void OnImportFailed(string packageName, string error)
        {
            if (IsEssentials(packageName))
            {
                Finish(new InvalidOperationException("TMP Essential Resources import failed: " + error));
            }
        }

        private static void OnImportCancelled(string packageName)
        {
            if (IsEssentials(packageName))
            {
                Finish(new OperationCanceledException("TMP Essential Resources import was cancelled."));
            }
        }

        private static void CompleteAfterImport()
        {
            if (!operationRunning)
            {
                return;
            }

            // ImportPackage completion can precede the final AssetDatabase refresh/shader registration.
            if (EditorApplication.isUpdating || EditorApplication.isCompiling || !EssentialsReady())
            {
                EditorApplication.delayCall += CompleteAfterImport;
                return;
            }

            try
            {
                TMP_Settings.LoadDefaultSettings();
                if (generateWhenReady)
                {
                    var font = EnsureFont();
                    Debug.Log("TOWER_DEFENSE_TMP_FONT_READY " + FontAssetPath + " atlases=" + font.atlasTextureCount);
                }
                else
                {
                    Debug.Log("TOWER_DEFENSE_TMP_ESSENTIALS_READY");
                }

                Finish(null);
            }
            catch (Exception error)
            {
                Finish(error);
            }
        }

        private static Shader LoadMobileShader()
        {
            return AssetDatabase.LoadAssetAtPath<Shader>(MobileShaderPath) ?? Shader.Find(MobileShaderName);
        }

        private static bool EssentialsReady()
        {
            return LoadMobileShader() != null && AssetDatabase.LoadAssetAtPath<TMP_Settings>(SettingsPath) != null;
        }

        private static void CheckDeadline()
        {
            if (operationRunning && EditorApplication.timeSinceStartup > deadline)
            {
                Finish(new TimeoutException("TMP Essentials import/font generation did not complete within 180 seconds."));
            }
        }

        private static void UnregisterImportEvents()
        {
            AssetDatabase.importPackageCompleted -= OnImportCompleted;
            AssetDatabase.importPackageFailed -= OnImportFailed;
            AssetDatabase.importPackageCancelled -= OnImportCancelled;
        }

        private static void Finish(Exception error)
        {
            if (!operationRunning)
            {
                return;
            }

            operationRunning = false;
            UnregisterImportEvents();
            EditorApplication.update -= CheckDeadline;
            EditorApplication.delayCall -= CompleteAfterImport;
            if (error != null)
            {
                Debug.LogException(error);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(error == null ? 0 : 1);
            }
        }

        /// <summary>保留已有 font/material/首张 atlas 的 GUID 与子资源引用，可重复在批处理构建前执行。</summary>
        public static TMP_FontAsset EnsureFont()
        {
            if (!File.Exists(LicensePath))
            {
                throw new FileNotFoundException("Keep the Noto font license with the game font.", LicensePath);
            }

            return EnsureFontAsset(SourceFontPath, FontAssetPath, "TowerDefenseUI", WarmCharacters());
        }

        [MenuItem("Tools/Novorizon/Tower Defense/Prepare Battle HUD Fonts")]
        public static void GenerateBattleHudFonts()
        {
            RequireEditMode();
            var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fallback == null || !File.Exists(HudFontLicensePath))
            {
                throw new InvalidOperationException("The battle HUD fonts require the base game font and the bundled Noto Sans SC license.");
            }

            const string warm = " 0123456789./+%×林间哨站基地耐久波次金币防御塔部署防守升级出售攻速射程理论秒伤";
            var medium = EnsureFontAsset(HudFontDirectory + "BattleHudMedium.ttf", HudFontAssetPath, "BattleHudMedium", warm, 512);
            var bold = EnsureFontAsset(HudFontDirectory + "BattleHudBold.ttf", HudBoldFontAssetPath, "BattleHudBold", warm, 512);
            medium.fontWeightTable[(int)FontWeight.Bold / 100].regularTypeface = bold;
            medium.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
            EditorUtility.SetDirty(medium);
            AssetDatabase.SaveAssets();
            ClientResourcesSetup.EnsureConfigured();
            Debug.Log("BATTLE_HUD_FONT_WEIGHTS_READY");
        }

        private static TMP_FontAsset EnsureFontAsset(string sourcePath, string assetPath, string name, string warmCharacters, int atlasSize = AtlasSize)
        {
            RequireEditMode();
            if (!EssentialsReady())
            {
                throw new InvalidOperationException(
                    "TMP Essentials are not imported. Run TowerDefenseFontSetup.GenerateFont without -quit and wait for its import-completed continuation.");
            }

            TMP_Settings.LoadDefaultSettings();
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("The font source is missing.", sourcePath);
            }

            var importer = AssetImporter.GetAtPath(sourcePath) as TrueTypeFontImporter;
            if (importer != null && !importer.includeFontData)
            {
                importer.includeFontData = true;
                importer.SaveAndReimport();
            }

            var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
            if (source == null)
            {
                throw new InvalidOperationException("Unity could not import " + sourcePath);
            }

            var shader = LoadMobileShader();
            var existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
            var font = existing as TMP_FontAsset;
            if (existing != null && font == null)
            {
                throw new InvalidOperationException("The generated TMP font path is occupied by another asset type.");
            }

            if (font == null)
            {
                font = TMP_FontAsset.CreateFontAsset(
                    source,
                    PointSize,
                    Padding,
                    GlyphRenderMode.SDFAA,
                    atlasSize,
                    atlasSize,
                    AtlasPopulationMode.Dynamic,
                    true);
                if (font == null)
                {
                    throw new InvalidOperationException("TextCore could not create the dynamic SDF font from " + sourcePath);
                }

                font.name = name;
                AssetDatabase.CreateAsset(font, assetPath);
            }

            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.isMultiAtlasTexturesEnabled = true;
            if (font.sourceFontFile != source || font.atlasWidth != atlasSize || font.atlasHeight != atlasSize
                || font.atlasPadding != Padding || Math.Abs(font.faceInfo.pointSize - PointSize) > .01f)
            {
                throw new InvalidOperationException(
                    "The generated font uses different source/atlas settings; preserve its GUID when regenerating it in TMP Font Asset Creator.");
            }

            if (font.atlasTextures == null || font.atlasTextures.Length == 0 || font.atlasTextures[0] == null)
            {
                throw new InvalidOperationException("The generated TMP font has no atlas texture.");
            }

            if (font.material == null)
            {
                font.material = new Material(shader);
            }

            font.material.name = name + " Material";
            font.material.shader = shader;
            font.material.SetTexture("_MainTex", font.atlasTextures[0]);
            font.material.SetFloat("_TextureWidth", atlasSize);
            font.material.SetFloat("_TextureHeight", atlasSize);
            font.material.SetFloat("_GradientScale", Padding + 1);
            PersistSubAssets(font);

            // Rebuild the small warm set in place so source font reimport cannot leave stale glyph indices.
            font.ClearFontAssetData();
            if (!font.TryAddCharacters(warmCharacters, out string missingCharacters))
            {
                throw new InvalidOperationException("The Noto subset is missing UI glyphs: " + missingCharacters);
            }

            PersistSubAssets(font);
            EditorUtility.SetDirty(font);
            EditorUtility.SetDirty(font.material);
            foreach (var atlas in font.atlasTextures)
            {
                if (atlas != null)
                {
                    atlas.Apply(false, false);
                    EditorUtility.SetDirty(atlas);
                }
            }

            AssetDatabase.SaveAssets();
            return font;
        }

        private static void PersistSubAssets(TMP_FontAsset font)
        {
            if (!AssetDatabase.Contains(font.material))
            {
                AssetDatabase.AddObjectToAsset(font.material, font);
            }

            for (int index = 0; index < font.atlasTextures.Length; index++)
            {
                var atlas = font.atlasTextures[index];
                if (atlas == null)
                {
                    continue;
                }

                atlas.name = font.name + " Atlas " + index;
                if (!AssetDatabase.Contains(atlas))
                {
                    AssetDatabase.AddObjectToAsset(atlas, font);
                }
            }
        }

        private static string WarmCharacters()
        {
            const string labels = "塔防肉鸽三选一选择本局强化基地金币波次开始战斗暂停继续升级已满级伤害射程攻速建造点防御塔查看确认返回重试胜利失败守护成功失守离线原型账号登录结算设置音量敌人来袭空点击下方圆圈显示实际攻击范围无法检查当前完成解除状态恭喜获得卡牌组合生命地图路线剩余冷却训练挑战准备 →·×：，。！";
            const string client = "翠林守卫森林旅人见习探索经验最高波次累计击败名敌防线旅程任务首战告捷赢得一次哨站防守筑起累计建造三座防御塔战地祝福选择张强化卡本地档案进度保存在此设备已达成出征准备营确认返回未完成的当前记录等级满资源初始化系统检查版本载入玩家页面游戏代码更新完成网络重新绿色区域部署按键取消请选择金币不足实际攻击范围";
            string camp = File.ReadAllText("Assets/GameAssets/Camp/Story/Arrival.json");
            camp += File.ReadAllText("Assets/GameAssets/Camp/Story/Outpost.json");
            foreach (string directory in new[] { "Assets/HotUpdate/Camp", "Assets/HotUpdate/Client", "Assets/HotUpdate/TowerDefense/Presentation" })
            {
                foreach (string file in Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories))
                {
                    camp += File.ReadAllText(file);
                }
            }

            camp += "潮汐营地临时帐篷围栏石砌庭院海岛家园木材石料材料不足完成任务获取建设领取补给首战告捷";
            var characters = new SortedSet<char>(labels + client + camp);
            for (char c = ' '; c <= '~'; c++)
            {
                characters.Add(c);
            }

            var text = new StringBuilder(characters.Count);
            foreach (char c in characters)
            {
                text.Append(c);
            }

            return text.ToString();
        }

        private static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException("Prepare TMP assets after script compilation and outside Play Mode.");
            }
        }
    }
}
