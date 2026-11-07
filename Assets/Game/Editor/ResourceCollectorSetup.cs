using System.Linq;
using UnityEditor;
using YooAsset.Editor;

namespace Game.Editor
{
    internal static class ResourceCollectorSetup
    {
        internal static bool SetGroup(AssetBundleCollectorPackage package, string name, string tag, string path, string packRule, string addressRule)
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
            if (collector.AddressRuleName != addressRule || collector.FilterRuleName != nameof(CollectAll)
                || collector.PackRuleName != packRule || collector.CollectorGUID != guid)
            {
                collector.CollectorType = ECollectorType.MainAssetCollector;
                collector.AddressRuleName = addressRule;
                collector.FilterRuleName = nameof(CollectAll);
                collector.PackRuleName = packRule;
                collector.CollectorGUID = guid;
                changed = true;
            }

            return changed;
        }
    }
}
