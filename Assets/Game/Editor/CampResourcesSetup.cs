using System;
using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace Game.Editor
{
    [InitializeOnLoad]
    public static class CampResourcesSetup
    {
        static CampResourcesSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
        }

        public static void EnsureConfigured()
        {
            GameDataSetup.EnsureConfigured();
            var package = AssetBundleCollectorSettingData.Setting.Packages.Single(value => value.PackageName == GameDataPaths.PackageName);
            bool changed = ResourceCollectorSetup.SetGroup(
                package,
                "CampMaps",
                "maps",
                "Assets/GameAssets/Camp/Art/Island.png",
                nameof(PackDirectory),
                nameof(CampAddressRule));
            changed |= ResourceCollectorSetup.SetGroup(
                package,
                "CampStory",
                "story",
                "Assets/GameAssets/Camp/Story/Arrival.json",
                nameof(PackDirectory),
                nameof(CampAddressRule));
            changed |= ResourceCollectorSetup.SetGroup(
                package,
                "CampStory",
                "story",
                "Assets/GameAssets/Camp/Story/Outpost.json",
                nameof(PackDirectory),
                nameof(CampAddressRule));
            changed |= ResourceCollectorSetup.SetGroup(
                package,
                "CampStory",
                "story",
                "Assets/GameAssets/Camp/Story/TideCampaign.json",
                nameof(PackDirectory),
                nameof(CampAddressRule));
            if (changed)
            {
                AssetBundleCollectorSettingData.SaveFile();
            }
        }
    }

    public sealed class CampAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            const string prefix = "Assets/GameAssets/";
            string path = data.AssetPath.Replace('\\', '/');
            if (!path.StartsWith(prefix + "Camp/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Camp resources must be below Assets/GameAssets/Camp.");
            }

            string relative = path.Substring(prefix.Length);
            return relative.Substring(0, relative.Length - System.IO.Path.GetExtension(relative).Length);
        }
    }
}
