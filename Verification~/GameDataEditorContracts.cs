// Native Unity/YooAsset build operations are modeled; rule interfaces and enums are compiled from pinned upstream source.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEditor.Build
{
    public sealed class BuildFailedException : Exception
    {
        public BuildFailedException(string message) : base(message)
        {
        }
    }

    public interface IPreprocessBuildWithReport
    {
        int callbackOrder { get; }
        void OnPreprocessBuild(Reporting.BuildReport report);
    }
}

namespace UnityEditor.Build.Reporting
{
    public sealed class BuildReport
    {
        public BuildSummary summary = new BuildSummary();
    }

    public sealed class BuildSummary
    {
        public BuildTarget platform;
    }
}

namespace Game.Framework.HotUpdate.Editor
{
    public static class GameBuild
    {
        public static int PlayerBuilds;
        public static Func<Game.Framework.HotUpdate.UpdateSettings> SettingsFactory;
        public static Func<Game.Framework.HotUpdate.ReleaseManifest, string, string, bool, Game.Framework.HotUpdate.ReleaseContent> BuildContent;
        public static string ExportUpdateToDirectory() => throw new NotSupportedException("Native patch compilation is validated in Unity.");
        public static void BuildNewPlayer()
        {
            Game.Editor.GameDataSetup.ValidatePlayerResources(EditorUserBuildSettings.activeBuildTarget);
            PlayerBuilds++;
        }
    }
}

namespace YooAsset.Editor
{
    // Upstream also defines BuildReport; keep this collision visible to the compiler.
    public sealed class BuildReport
    {
    }

    public sealed class AssetBundleCollectorSetting : ScriptableObject
    {
        public List<AssetBundleCollectorPackage> Packages = new List<AssetBundleCollectorPackage>();
    }

    public sealed class AssetBundleCollectorPackage
    {
        public string PackageName;
        public bool EnableAddressable;
        public List<AssetBundleCollectorGroup> Groups = new List<AssetBundleCollectorGroup>();
    }

    public sealed class AssetBundleCollectorGroup
    {
        public string GroupName, AssetTags;
        public List<AssetBundleCollector> Collectors = new List<AssetBundleCollector>();
    }

    public sealed class AssetBundleCollector
    {
        public string CollectPath, CollectorGUID, AddressRuleName, PackRuleName, FilterRuleName;
        public ECollectorType CollectorType;
    }

    public sealed class PackDirectory
    {
    }

    public sealed class PackSeparately
    {
    }

    public static class AssetBundleCollectorSettingData
    {
        public static AssetBundleCollectorSetting Setting = new AssetBundleCollectorSetting();
        public static int Saves;
        public static void SaveFile()
        {
            Saves++;
        }
    }

    public sealed class BuiltinBuildParameters
    {
        public string BuildOutputRoot, BuildinFileRoot, BuildPipeline, PackageName, PackageVersion;
        public int BuildBundleType;
        public BuildTarget BuildTarget;
        public bool EnableSharePackRule, VerifyBuildingResult;
        public EFileNameStyle FileNameStyle;
        public EBuildinFileCopyOption BuildinFileCopyOption;
        public ECompressOption CompressOption;
    }

    public sealed class BuildResult
    {
        public bool Success;
        public string ErrorInfo;
        public string OutputPackageDirectory;
    }

    public static class AssetBundleBuilderHelper
    {
        public static string BuiltinRoot;
        public static string GetStreamingAssetsRoot() => BuiltinRoot;
    }

    public sealed class BuiltinBuildPipeline
    {
        public static bool Fail;
        public static BuiltinBuildParameters Last;

        public BuildResult Run(BuiltinBuildParameters parameters, bool enableLog)
        {
            Last = parameters;
            if (!Fail)
            {
                string package = Path.Combine(parameters.BuildinFileRoot, parameters.PackageName);
                Directory.CreateDirectory(package);
                File.WriteAllText(Path.Combine(package, parameters.PackageName + ".version"), parameters.PackageVersion);
            }

            string output = Path.Combine(parameters.BuildOutputRoot, parameters.BuildTarget.ToString(), parameters.PackageName, parameters.PackageVersion);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, YooAsset.YooAssetSettingsData.GetManifestBinaryFileName(parameters.PackageName, parameters.PackageVersion)), "manifest");
            return new BuildResult
            {
                OutputPackageDirectory = output,
                Success = !Fail,
                ErrorInfo = Fail ? "Model build failed" : ""
            };
        }
    }
}
