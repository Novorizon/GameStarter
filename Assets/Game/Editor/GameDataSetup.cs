using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Game.Framework.Data.Editor;
using Game.Framework.HotUpdate.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace Game.Editor
{
    /// <summary>本工程的数据生成和资源打包接线；默认配置随工程提供，打开后自动检查。</summary>
    [InitializeOnLoad]
    public static class GameDataSetup
    {
        private const string SettingsPath = "Assets/Settings/LubanGenerator.asset";
        private const string GroupName = "GameStarterData";
        private const string BuildStamp = "Library/Novorizon/GameStarter/resources.sha256";
        private static bool generating;

        static GameDataSetup()
        {
            EditorApplication.delayCall += EnsureConfigured;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        public static void EnsureConfigured()
        {
            var settings = AssetDatabase.LoadAssetAtPath<LubanGeneratorSettings>(SettingsPath);
            bool create = settings == null;
            if (create)
            {
                settings = ScriptableObject.CreateInstance<LubanGeneratorSettings>();
            }

            bool changed = create || settings.configFile != GameDataPaths.ConfigFile
                || settings.codeOutput != GameDataPaths.CodeOutput || settings.dataOutput != GameDataPaths.DataOutput
                || settings.locationPrefix != GameDataPaths.LocationPrefix || settings.generatedAssembly != GameDataPaths.GeneratedAssembly || settings.generatedAutoReferenced;
            if (changed)
            {
                settings.configFile = GameDataPaths.ConfigFile;
                settings.codeOutput = GameDataPaths.CodeOutput;
                settings.dataOutput = GameDataPaths.DataOutput;
                settings.locationPrefix = GameDataPaths.LocationPrefix;
                settings.generatedAssembly = GameDataPaths.GeneratedAssembly;
                settings.generatedAutoReferenced = false;
                if (create)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }

                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            var collectorSettings = AssetBundleCollectorSettingData.Setting;
            var package = collectorSettings.Packages.SingleOrDefault(p => p.PackageName == GameDataPaths.PackageName);
            changed = false;
            if (package == null)
            {
                package = new AssetBundleCollectorPackage
                {
                    PackageName = GameDataPaths.PackageName
                };
                collectorSettings.Packages.Add(package);
                changed = true;
            }

            if (!package.EnableAddressable)
            {
                package.EnableAddressable = true;
                changed = true;
            }

            var group = package.Groups.SingleOrDefault(g => g.GroupName == GroupName);
            if (group == null)
            {
                group = new AssetBundleCollectorGroup
                {
                    GroupName = GroupName,
                    AssetTags = "data"
                };
                package.Groups.Add(group);
                changed = true;
            }

            var collector = group.Collectors.SingleOrDefault(c => c.CollectPath == GameDataPaths.DataOutput);
            if (collector == null)
            {
                collector = new AssetBundleCollector
                {
                    CollectPath = GameDataPaths.DataOutput
                };
                group.Collectors.Add(collector);
                changed = true;
            }

            string guid = AssetDatabase.AssetPathToGUID(GameDataPaths.DataOutput);
            if (collector.AddressRuleName != nameof(GameDataAddressRule) || collector.FilterRuleName != nameof(GameDataFilterRule)
                || collector.PackRuleName != nameof(PackDirectory) || collector.CollectorType != ECollectorType.MainAssetCollector
                || collector.CollectorGUID != guid)
            {
                collector.AddressRuleName = nameof(GameDataAddressRule);
                collector.FilterRuleName = nameof(GameDataFilterRule);
                collector.PackRuleName = nameof(PackDirectory);
                collector.CollectorType = ECollectorType.MainAssetCollector;
                collector.CollectorGUID = guid;
                changed = true;
            }

            changed |= GameCodeResources.EnsureCollector(package);
            if (changed)
            {
                AssetBundleCollectorSettingData.SaveFile();
            }
        }

        [MenuItem("Tools/Novorizon/GameStarter/1. Generate Tables")]
        public static async void GenerateTables()
        {
            try
            {
                await GenerateTablesAsync();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }

        public static async Task GenerateTablesAsync()
        {
            if (generating)
            {
                throw new InvalidOperationException("Table generation is already running.");
            }

            generating = true;
            try
            {
                EnsureConfigured();
                var settings = AssetDatabase.LoadAssetAtPath<LubanGeneratorSettings>(SettingsPath);
                string root = Path.GetFullPath(".");
                string cli = string.IsNullOrWhiteSpace(settings.lubanDll)
                    ? await LubanExporter.EnsureToolAsync(root, settings.dotnetExecutable)
                    : Path.GetFullPath(settings.lubanDll);
                await LubanExporter.GenerateAsync(
                    root,
                    settings.configFile,
                    settings.target,
                    settings.codeOutput,
                    settings.dataOutput,
                    settings.locationPrefix,
                    settings.dotnetExecutable,
                    cli,
                    settings.generatedAssembly,
                    settings.generatedAutoReferenced);
                RecordGeneratedSources();
                AssetDatabase.Refresh();
                Debug.Log("LUBAN_GENERATED. YooAsset collector and runtime table loading are already configured.");
            }
            finally
            {
                generating = false;
            }
        }

        public static void RecordGeneratedSources()
        {
            File.WriteAllText(GameDataPaths.SourceStamp, SourceFingerprint());
        }

        public static string SourceFingerprint()
        {
            return DirectoryFingerprint(GameDataPaths.SourceDirectory, _ => true);
        }

        private static string DirectoryFingerprint(string root, Func<string, bool> include)
        {
            var text = new StringBuilder();
            using var hash = SHA256.Create();
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal) && include(f))
                .OrderBy(f => f.Substring(root.Length + 1).Replace('\\', '/'), StringComparer.Ordinal))
            {
                string relative = file.Substring(root.Length + 1).Replace('\\', '/');
                text.Append(relative).Append('\n').Append(Hex(hash.ComputeHash(File.ReadAllBytes(file)))).Append('\n');
            }

            return Hex(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static string Hex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
        }

        public static void ValidateGenerated()
        {
            if (generating || !File.Exists(GameDataPaths.SourceStamp) || File.ReadAllText(GameDataPaths.SourceStamp) != SourceFingerprint())
            {
                throw new InvalidOperationException(
                    "Luban sources have changed. Run Tools/Novorizon/GameStarter/1. Generate Tables, then wait for compilation.");
            }

            if (!File.Exists(GameDataPaths.DataOutput + "/manifest.json") || !File.Exists(GameDataPaths.CodeOutput + "/Tables.cs"))
            {
                throw new InvalidOperationException("Generated tables are missing. Run Tools/Novorizon/GameStarter/1. Generate Tables.");
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode)
            {
                return;
            }

            try
            {
                EnsureConfigured();
                ValidateGenerated();
            }
            catch (Exception error)
            {
                EditorApplication.isPlaying = false;
                Debug.LogException(error);
            }
        }

        [MenuItem("Tools/Novorizon/GameStarter/2. Build Resources")]
        public static void BuildResources()
        {
            BuildResources(true);
        }

        public static void BuildResources(bool copyToBuiltin)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Wait for compilation and leave Play Mode before building resources.");
            }

            EnsureConfigured();
            ValidateGenerated();
            var target = EditorUserBuildSettings.activeBuildTarget;
            var parameters = new BuiltinBuildParameters
            {
                BuildOutputRoot = "Library/Novorizon/YooAssetBuild",
                BuildinFileRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot(),
                BuildPipeline = EBuildPipeline.BuiltinBuildPipeline.ToString(),
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                BuildTarget = target,
                PackageName = GameDataPaths.PackageName,
                PackageVersion = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = EFileNameStyle.HashName,
                BuildinFileCopyOption = copyToBuiltin ? EBuildinFileCopyOption.ClearAndCopyAll : EBuildinFileCopyOption.None,
                CompressOption = ECompressOption.LZ4
            };
            var result = new BuiltinBuildPipeline().Run(parameters, true);
            if (!result.Success)
            {
                throw new BuildFailedException("YooAsset resource build failed: " + result.ErrorInfo + ". Player build was not started.");
            }

            Directory.CreateDirectory("Library/Novorizon/GameStarter");
            GameReleaseBuild.RecordResources(parameters.PackageVersion, result.OutputPackageDirectory);
            if (copyToBuiltin)
            {
                File.WriteAllText(BuildStamp, target + "\n" + BuildFingerprint() + "\n" + BuiltinFingerprint());
            }
            AssetDatabase.Refresh();
        }

        [MenuItem("Tools/Novorizon/GameStarter/3. Build New Player")]
        public static void BuildNewPlayer()
        {
            // Build bundles before Unity starts its Player build; YooAsset forbids nested builds.
            GameReleaseBuild.EnsureSigningKey();
            BuildResources();
            GameBuild.SettingsFactory = GameReleaseBuild.CreateBuildSettings;
            GameBuild.BuildNewPlayer();
        }

        private static string BuiltinFingerprint()
        {
            string package = Path.Combine(AssetBundleBuilderHelper.GetStreamingAssetsRoot(), GameDataPaths.PackageName);
            return DirectoryFingerprint(
                package,
                file => file.EndsWith(".version", StringComparison.Ordinal)
                    || file.EndsWith(".hash", StringComparison.Ordinal));
        }

        private static string BuildFingerprint()
        {
            string data = DirectoryFingerprint(
                GameDataPaths.DataOutput,
                file => file.EndsWith(".json", StringComparison.Ordinal) || file.EndsWith(".bytes", StringComparison.Ordinal));
            string code = DirectoryFingerprint(GameCodeResources.Output, file => file.EndsWith(".dll.bytes", StringComparison.Ordinal));
            return SourceFingerprint() + "\n" + data + "\n" + code;
        }

        public static void ValidatePlayerResources(BuildTarget target)
        {
            ValidateGenerated();
            string package = Path.Combine(AssetBundleBuilderHelper.GetStreamingAssetsRoot(), GameDataPaths.PackageName);
            if (!Directory.Exists(package) || !Directory.EnumerateFiles(package, "*.version", SearchOption.AllDirectories).Any()
                || !File.Exists(BuildStamp) || File.ReadAllText(BuildStamp) != target + "\n" + BuildFingerprint() + "\n" + BuiltinFingerprint())
            {
                throw new BuildFailedException("Build YooAsset resources for this target first, or use Tools/Novorizon/GameStarter/3. Build New Player.");
            }
        }
    }

    public sealed class GameDataAddressRule : IAddressRule
    {
        public string GetAssetAddress(AddressRuleData data)
        {
            string path = data.AssetPath.Replace('\\', '/');
            if (!path.StartsWith(GameDataPaths.DataOutput + "/", StringComparison.Ordinal))
            {
                throw new ArgumentException("The data collector only accepts files beneath " + GameDataPaths.DataOutput);
            }

            string relative = path.Substring(GameDataPaths.AssetRoot.Length + 1);
            return relative.Substring(0, relative.Length - Path.GetExtension(relative).Length);
        }
    }

    public sealed class GameDataFilterRule : IFilterRule
    {
        public string FindAssetType => "TextAsset";

        public bool IsCollectAsset(FilterRuleData data)
        {
            string extension = Path.GetExtension(data.AssetPath);
            return extension == ".bytes" || extension == ".json";
        }
    }

    public sealed class GameDataPlayerBuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder => -20000;

        public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            GameDataSetup.ValidatePlayerResources(report.summary.platform);
        }
    }
}
