using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Framework.HotUpdate;
using UnityEditor;
using YooAsset.Editor;

namespace Game.Editor
{
    /// <summary>DLL/AOT 元数据与游戏资源使用同一 YooAsset 包和精确版本。</summary>
    public static class GameCodeResources
    {
        public const string Output = "Assets/GameAssets/HotUpdate";

        public static ReleaseContent Build(ReleaseManifest manifest, string hotDirectory, string aotDirectory, bool builtin)
        {
            Stage(manifest, hotDirectory, aotDirectory);
            AssetDatabase.Refresh();
            GameDataSetup.BuildResources(builtin);
            return new ReleaseContent
            {
                packageName = GameDataPaths.PackageName,
                version = GameReleaseBuild.LastResourceVersion,
                codeInResources = true
            };
        }

        public static void Stage(ReleaseManifest manifest, string hotDirectory, string aotDirectory)
        {
            ReleasePolicy.Validate(manifest);
            Directory.CreateDirectory(Output);
            string directory = Path.Combine(Output, manifest.baselineId);
            Directory.CreateDirectory(directory);
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in manifest.assemblies.Concat(manifest.aotMetadata))
            {
                string sourceRoot = manifest.assemblies.Contains(file) ? hotDirectory : aotDirectory;
                string source = Path.Combine(sourceRoot, file.assemblyName + ".dll");
                ReleasePolicy.VerifyFile(source, file);
                string target = Path.Combine(directory, file.fileName);
                expected.Add(Path.GetFullPath(target));
                File.Copy(source, target, true);
            }

            // 只移除本工具生成的旧代码资源；同名文件保留 GUID，支持稳定的增量构建。
            foreach (string file in Directory.GetFiles(Output, "*.dll.bytes", SearchOption.AllDirectories))
            {
                if (!expected.Contains(Path.GetFullPath(file)))
                {
                    File.Delete(file);
                    File.Delete(file + ".meta");
                }
            }
        }

        public static bool EnsureCollector(AssetBundleCollectorPackage package)
        {
            bool changed = false;
            var group = package.Groups.SingleOrDefault(value => value.GroupName == "GameStarterCode");
            if (group == null)
            {
                group = new AssetBundleCollectorGroup { GroupName = "GameStarterCode", AssetTags = "code" };
                package.Groups.Add(group);
                changed = true;
            }

            var collector = group.Collectors.SingleOrDefault(value => value.CollectPath == Output);
            if (collector == null)
            {
                collector = new AssetBundleCollector { CollectPath = Output };
                group.Collectors.Add(collector);
                changed = true;
            }

            string guid = AssetDatabase.AssetPathToGUID(Output);
            if (collector.AddressRuleName != nameof(GameCodeAddressRule)
                || collector.FilterRuleName != nameof(GameCodeFilterRule)
                || collector.PackRuleName != nameof(PackSeparately)
                || collector.CollectorType != ECollectorType.MainAssetCollector
                || collector.CollectorGUID != guid)
            {
                collector.AddressRuleName = nameof(GameCodeAddressRule);
                collector.FilterRuleName = nameof(GameCodeFilterRule);
                collector.PackRuleName = nameof(PackSeparately);
                collector.CollectorType = ECollectorType.MainAssetCollector;
                collector.CollectorGUID = guid;
                changed = true;
            }

            return changed;
        }
    }

    public sealed class GameCodeAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            string path = data.AssetPath.Replace('\\', '/');
            if (!path.StartsWith(GameCodeResources.Output + "/", StringComparison.Ordinal))
            {
                throw new ArgumentException("Code resources must be beneath " + GameCodeResources.Output);
            }

            return "HotUpdate/" + path.Substring(GameCodeResources.Output.Length + 1);
        }
    }

    public sealed class GameCodeFilterRule : IFilterRule
    {
        public string FindAssetType => "TextAsset";
        public bool IsCollectAsset(FilterRuleData data)
        {
            return data.AssetPath.EndsWith(".dll.bytes", StringComparison.Ordinal);
        }
    }
}
