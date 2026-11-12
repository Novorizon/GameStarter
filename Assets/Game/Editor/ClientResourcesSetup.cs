using System;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace Game.Editor
{
    [InitializeOnLoad]
    public static class ClientResourcesSetup
    {
        internal const string HudFontAssetPath = "Assets/GameAssets/Common/Fonts/BattleHudMedium.asset";
        internal const string HudBoldFontAssetPath = "Assets/GameAssets/Common/Fonts/BattleHudBold.asset";

        static ClientResourcesSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
        }

        public static void EnsureConfigured()
        {
            GameDataSetup.EnsureConfigured();
            var package = AssetBundleCollectorSettingData.Setting.Packages.Single(value => value.PackageName == GameDataPaths.PackageName);
            bool changed = false;
            var commonUiPaths = new[]
            {
                "UI/Icons.png", "UI/Panel.png", "UI/Avatar.png", "UI/AvatarShore.png", "UI/AvatarEmber.png", "UI/BattleHud.png", "UI/CampHud.png",
                "Startup/Background.bytes", "Startup/Layout.json"
            };
            foreach (var path in commonUiPaths)
            {
                changed |= ResourceCollectorSetup.SetGroup(
                    package,
                    "ClientUI",
                    "ui",
                    "Assets/GameAssets/Common/" + path,
                    nameof(PackDirectory),
                    nameof(ClientAddressRule));
            }

            foreach (string texture in new[] { "ExpeditionBackdrop", "ExpeditionBanner", "ExpeditionGlyphs" })
            {
                changed |= ResourceCollectorSetup.SetGroup(
                    package,
                    "ExpeditionUI",
                    "ui",
                    "Assets/GameAssets/Common/UI/" + texture + ".png",
                    nameof(PackSeparately),
                    nameof(ClientAddressRule));
            }

            changed |= ResourceCollectorSetup.SetGroup(
                package,
                "ClientFonts",
                "fonts",
                "Assets/GameAssets/Common/Fonts/GameUI.asset",
                nameof(PackSeparately),
                nameof(ClientAddressRule));
            foreach (string font in new[] { HudFontAssetPath, HudBoldFontAssetPath })
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(font)))
                {
                    changed |= ResourceCollectorSetup.SetGroup(package, "ClientFonts", "fonts", font, nameof(PackSeparately), nameof(ClientAddressRule));
                }
            }

            foreach (string material in new[] { "WorldLit", "WorldSprite" })
            {
                changed |= ResourceCollectorSetup.SetGroup(
                    package,
                    "ClientRendering",
                    "rendering",
                    "Assets/GameAssets/Common/Rendering/" + material + ".mat",
                    nameof(PackDirectory),
                    nameof(ClientAddressRule));
            }
            if (changed)
            {
                AssetBundleCollectorSettingData.SaveFile();
            }
        }
    }

    public sealed class ClientAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            const string prefix = "Assets/GameAssets/";
            string path = data.AssetPath.Replace('\\', '/');
            if (!path.StartsWith(prefix + "Common/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Client resources must be below Assets/GameAssets/Common.");
            }

            string relative = path.Substring(prefix.Length);
            return relative.Substring(0, relative.Length - System.IO.Path.GetExtension(relative).Length);
        }
    }
}
