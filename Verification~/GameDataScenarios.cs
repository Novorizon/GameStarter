using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Game.Editor;
using Game.Framework.Data;
using UnityEditor;
using UnityEditor.Build;
using YooAsset.Editor;

internal static partial class Program
{
    private static void GameDataScenarios()
    {
        Check("compiled game startup loads Tables through YooAsset before modules", () =>
        {
            ResetEntry();
            var entry = CreateEntry(hotUpdate: false);
            Complete(entry.WaitUntilReadyAsync());
            var tables = DataManager.Instance.Get<cfg.Tables>();
            Equal("示例配置", tables.TbExample.Get(1).Name);
            Equal("Second entry", tables.TbExample.Get(2).Name);
            True(ManagerContracts.Calls.IndexOf("tables") < ManagerContracts.Calls.IndexOf("module"));
            Equal("Data/Luban/manifest,Data/Luban/tbexample", string.Join(",", ManagerContracts.DataAcquisitions));
            Equal(2, ManagerContracts.LeasesReleased);
            Complete(entry.ShutdownAsync());
            True(!DataManager.IsCreated);
            ResetEntry();
        });
        Check("hot startup waits for actual table decoding before reporting ready", () =>
        {
            ResetEntry();
            var read = ManagerContracts.LoadDataBytes;
            var pending = new TaskCompletionSource<byte[]>();
            ManagerContracts.LoadDataBytes = (path, token) => path == GameDataPaths.ManifestLocation ? read(path, token) : pending.Task;
            var entry = CreateEntry();
            Equal(StartupState.Starting, entry.State);
            True(!ManagerContracts.Calls.Contains("module"));
            True(!DataManager.Instance.TryGet<cfg.Tables>(out _));
            pending.SetResult(File.ReadAllBytes(GameDataPaths.DataOutput + "/tbexample.bytes"));
            Complete(entry.WaitUntilReadyAsync());
            Equal(StartupState.Ready, entry.State);
            Equal(2, DataManager.Instance.Get<cfg.Tables>().TbExample.DataList.Count);
            Equal(2, ManagerContracts.LeasesReleased);
            ResetEntry();
        });
        Check("invalid table bytes fail startup before any business module runs", () =>
        {
            ResetEntry();
            var read = ManagerContracts.LoadDataBytes;
            ManagerContracts.LoadDataBytes = (path, token) => path == GameDataPaths.ManifestLocation ? read(path, token) : Task.FromResult(Array.Empty<byte>());
            var entry = CreateEntry();
            Throws<InvalidOperationException>(() => Complete(entry.WaitUntilReadyAsync()));
            Equal(StartupState.Failed, entry.State);
            True(!ManagerContracts.Calls.Contains("module"));
            ResetEntry();
        });
        Check("shutdown during a table load prevents late tables and ready acknowledgement", () =>
        {
            ResetEntry();
            var read = ManagerContracts.LoadDataBytes;
            var pending = new TaskCompletionSource<byte[]>();
            ManagerContracts.LoadDataBytes = (path, token) => path == GameDataPaths.ManifestLocation ? read(path, token) : pending.Task;
            var entry = CreateEntry();
            Equal(StartupState.Starting, entry.State);
            Task ready = entry.WaitUntilReadyAsync();
            Complete(entry.ShutdownAsync());
            pending.SetResult(File.ReadAllBytes(GameDataPaths.DataOutput + "/tbexample.bytes"));
            Throws<OperationCanceledException>(() => Complete(ready));
            Equal(StartupState.NotStarted, entry.State);
            True(!DataManager.IsCreated);
            True(!ManagerContracts.Calls.Contains("module"));
            ResetEntry();
        });
        Check("YooAsset addresses match the manifest and exclude generated marker/meta files", () =>
        {
            var address = new GameDataAddressRule();
            var filter = new GameDataFilterRule();
            foreach (string file in Directory.GetFiles(GameDataPaths.DataOutput).Where(f => f.EndsWith(".json") || f.EndsWith(".bytes")))
            {
                string path = file.Replace('\\', '/');
                string location = address.GetAssetAddress(new AddressRuleData
                {
                    AssetPath = path
                });
                True(filter.IsCollectAsset(new FilterRuleData
                {
                    AssetPath = path
                }));
                True(location.StartsWith(GameDataPaths.LocationPrefix + "/", StringComparison.Ordinal));
            }

            Equal(GameDataPaths.ManifestLocation, address.GetAssetAddress(new AddressRuleData
                {
                    AssetPath = GameDataPaths.DataOutput + "/manifest.json"
                }));
            Equal("Data/Luban/tbexample", address.GetAssetAddress(new AddressRuleData
                {
                    AssetPath = GameDataPaths.DataOutput + "/tbexample.bytes"
                }));
            True(!filter.IsCollectAsset(new FilterRuleData
                {
                    AssetPath = ".novorizon-luban-output"
                }));
            True(!filter.IsCollectAsset(new FilterRuleData
                {
                    AssetPath = "tbexample.bytes.meta"
                }));
            Throws<ArgumentException>(() => address.GetAssetAddress(new AddressRuleData
                {
                    AssetPath = "Assets/Elsewhere/table.bytes"
                }));
        });
        Check("automatic collector setup is idempotent and keeps unrelated groups", () =>
        {
            var settings = AssetBundleCollectorSettingData.Setting;
            settings.Packages.Clear();
            AssetBundleCollectorSettingData.Saves = 0;
            var package = new AssetBundleCollectorPackage
            {
                PackageName = GameDataPaths.PackageName
            };
            var unrelated = new AssetBundleCollectorGroup
            {
                GroupName = "UserAssets"
            };
            package.Groups.Add(unrelated);
            settings.Packages.Add(package);
            GameDataSetup.EnsureConfigured();
            GameDataSetup.EnsureConfigured();
            Equal(1, AssetBundleCollectorSettingData.Saves);
            Equal(2, package.Groups.Count);
            True(package.Groups.Contains(unrelated));
            var collector = package.Groups.Single(g => g.GroupName == "GameStarterData").Collectors.Single();
            Equal(ECollectorType.MainAssetCollector, collector.CollectorType);
            Equal(nameof(GameDataAddressRule), collector.AddressRuleName);
            Equal(nameof(GameDataFilterRule), collector.FilterRuleName);
            GameDataSetup.ValidateGenerated();
        });
        Check("source edits block stale data until tables are generated", () =>
        {
            string extra = GameDataPaths.SourceDirectory + "/verification-input.txt";
            try
            {
                GameDataSetup.ValidateGenerated();
                File.WriteAllText(extra, "changed input");
                Throws<InvalidOperationException>(() => GameDataSetup.ValidateGenerated());
            }
            finally
            {
                File.Delete(extra);
            }

            GameDataSetup.ValidateGenerated();
        });
        Check("project Player build packages YooAsset first and blocks failed or wrong-target resources", () =>
        {
            string temp = Path.Combine(Path.GetTempPath(), "game-starter-yoo-" + Guid.NewGuid().ToString("N"));
            AssetBundleBuilderHelper.BuiltinRoot = temp;
            Game.Framework.HotUpdate.Editor.GameBuild.PlayerBuilds = 0;
            try
            {
                BuiltinBuildPipeline.Fail = true;
                Throws<BuildFailedException>(() => GameDataSetup.BuildNewPlayer());
                Equal(0, Game.Framework.HotUpdate.Editor.GameBuild.PlayerBuilds);
                BuiltinBuildPipeline.Fail = false;
                GameDataSetup.BuildNewPlayer();
                Equal(1, Game.Framework.HotUpdate.Editor.GameBuild.PlayerBuilds);
                Equal(GameDataPaths.PackageName, BuiltinBuildPipeline.Last.PackageName);
                Equal(EBuildinFileCopyOption.ClearAndCopyAll, BuiltinBuildPipeline.Last.BuildinFileCopyOption);
                GameDataSetup.ValidatePlayerResources(BuildTarget.StandaloneLinux64);
                Throws<BuildFailedException>(() => GameDataSetup.ValidatePlayerResources(BuildTarget.Android));
                EditorUserBuildSettings.activeBuildTarget = BuildTarget.Android;
                GameDataSetup.BuildResources();
                GameDataSetup.ValidatePlayerResources(BuildTarget.Android);
                // StreamingAssets now contains Android bundles; an older Linux stamp must not authorize them.
                Throws<BuildFailedException>(() => GameDataSetup.ValidatePlayerResources(BuildTarget.StandaloneLinux64));
                File.WriteAllText(Path.Combine(temp, GameDataPaths.PackageName, GameDataPaths.PackageName + ".version"), "replaced outside this builder");
                Throws<BuildFailedException>(() => GameDataSetup.ValidatePlayerResources(BuildTarget.Android));
            }
            finally
            {
                BuiltinBuildPipeline.Fail = false;
                EditorUserBuildSettings.activeBuildTarget = BuildTarget.StandaloneLinux64;
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, true);
                }
            }
        });
    }
}
