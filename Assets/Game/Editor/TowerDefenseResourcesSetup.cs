using System;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace Game.Editor
{
    [InitializeOnLoad]
    public static class TowerDefenseResourcesSetup
    {
        static TowerDefenseResourcesSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
        }

        public static void EnsureConfigured()
        {
            var package = AssetBundleCollectorSettingData.Setting.Packages.SingleOrDefault(value => value.PackageName == GameDataPaths.PackageName);
            if (package == null)
            {
                GameDataSetup.EnsureConfigured();
                package = AssetBundleCollectorSettingData.Setting.Packages.Single(value => value.PackageName == GameDataPaths.PackageName);
            }

            bool changed = ResourceCollectorSetup.SetGroup(
                package,
                "TowerDefenseMaps",
                "maps",
                "Assets/GameAssets/TowerDefense/Art/Map.png",
                nameof(PackSeparately),
                nameof(TowerDefenseAddressRule));
            if (changed)
            {
                AssetBundleCollectorSettingData.SaveFile();
            }
        }


    }

    public sealed class TowerDefenseAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            const string prefix = "Assets/GameAssets/";
            string path = data.AssetPath.Replace('\\', '/');
            if (!path.StartsWith(prefix + "TowerDefense/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Tower defense resources must be below Assets/GameAssets/TowerDefense");
            }

            string relative = path.Substring(prefix.Length);
            return relative.Substring(0, relative.Length - System.IO.Path.GetExtension(relative).Length);
        }
    }
}
