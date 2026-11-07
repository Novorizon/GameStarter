using System;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace Game.Editor
{
    [InitializeOnLoad]
    public static class ClientResourcesSetup
    {
        static ClientResourcesSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
        }

        public static void EnsureConfigured()
        {
            GameDataSetup.EnsureConfigured();
            var package = AssetBundleCollectorSettingData.Setting.Packages.Single(value => value.PackageName == GameDataPaths.PackageName);
            bool changed = false;
            foreach (var path in new[] { "UI/Icons.png", "UI/Panel.png", "UI/Avatar.png", "Startup/Background.bytes", "Startup/Layout.json" })
            {
                changed |= ResourceCollectorSetup.SetGroup(
                    package,
                    "ClientUI",
                    "ui",
                    "Assets/GameAssets/Common/" + path,
                    nameof(PackDirectory),
                    nameof(ClientAddressRule));
            }

            changed |= ResourceCollectorSetup.SetGroup(
                package,
                "ClientFonts",
                "fonts",
                "Assets/GameAssets/Common/Fonts/GameUI.asset",
                nameof(PackSeparately),
                nameof(ClientAddressRule));
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
