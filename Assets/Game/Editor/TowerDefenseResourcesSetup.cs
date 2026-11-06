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

            bool changed = SetGroup(package, "TowerDefenseMaps", "maps", "Assets/GameAssets/TowerDefense/Art/Map.png", nameof(PackSeparately));
            changed |= SetGroup(package, "TowerDefenseUI", "ui", "Assets/GameAssets/TowerDefense/Art/CardIcons.png", nameof(PackSeparately));
            changed |= SetGroup(package, "TowerDefenseUI", "ui", "Assets/GameAssets/TowerDefense/Art/CardFrame.png", nameof(PackSeparately));
            // 字体主资源依赖源OTF、材质和图集，由YooAsset随字体一起打包。
            changed |= SetGroup(package, "TowerDefenseFonts", "fonts", "Assets/GameAssets/TowerDefense/Fonts/TowerDefenseUI.asset", nameof(PackSeparately));
            if (changed)
            {
                AssetBundleCollectorSettingData.SaveFile();
            }
        }

        private static bool SetGroup(
            AssetBundleCollectorPackage package,
            string name,
            string tag,
            string path,
            string packRule)
        {
            var group = package.Groups.SingleOrDefault(value => value.GroupName == name);
            bool changed = false;
            if (group == null)
            {
                group = new AssetBundleCollectorGroup { GroupName = name, AssetTags = tag };
                package.Groups.Add(group);
                changed = true;
            }

            var collector = group.Collectors.SingleOrDefault(value => value.CollectPath == path);
            if (collector == null)
            {
                collector = new AssetBundleCollector { CollectPath = path };
                group.Collectors.Add(collector);
                changed = true;
            }

            string guid = AssetDatabase.AssetPathToGUID(path);
            if (collector.AddressRuleName != nameof(TowerDefenseAddressRule) || collector.FilterRuleName != nameof(CollectAll)
                || collector.PackRuleName != packRule || collector.CollectorGUID != guid)
            {
                collector.CollectorType = ECollectorType.MainAssetCollector;
                collector.AddressRuleName = nameof(TowerDefenseAddressRule);
                collector.FilterRuleName = nameof(CollectAll);
                collector.PackRuleName = packRule;
                collector.CollectorGUID = guid;
                changed = true;
            }

            return changed;
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
