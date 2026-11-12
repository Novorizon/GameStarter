using System;
using System.IO;
using System.Linq;
using HybridCLR.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    public static class CurrentNavigationWindowsBuild
    {
        public static void Run()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                throw new BuildFailedException("Select the Windows 64-bit target before building the local player.");
            }

            if (PlayerSettings.fullScreenMode != FullScreenMode.Windowed)
            {
                throw new BuildFailedException("Select Windowed in Player Settings before building the local player.");
            }

            const string outputOption = "--novorizon-player-output=";
            var arguments = Environment.GetCommandLineArgs();
            string output = arguments.FirstOrDefault(value => value.StartsWith(outputOption, StringComparison.Ordinal));
            string playerPath = Path.GetFullPath(output == null
                ? "Builds/CurrentNavigation-Win64/GameStarter.exe" : output.Substring(outputOption.Length));
            if (!string.Equals(Path.GetExtension(playerPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException("The Windows player output must end in .exe.");
            }

            bool includeProbe = arguments.Contains("--novorizon-enable-a-current-check");
            string[] defines = includeProbe
                ? new[] { "NOVORIZON_COMPILED_GAME", "NOVORIZON_CURRENT_NAVIGATION_CHECK" }
                : new[] { "NOVORIZON_COMPILED_GAME" };
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            GameDataSetup.BuildResources();

            var hybrid = HybridCLRSettings.instance;
            bool previous = hybrid.enable;
            hybrid.enable = false;
            HybridCLRSettings.Save();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(playerPath));
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = playerPath,
                    target = BuildTarget.StandaloneWindows64,
                    extraScriptingDefines = defines,
                    options = BuildOptions.StrictMode
                });
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException("Local Windows player build failed: " + report.summary.result);
                }

                Debug.Log("CURRENT_NAVIGATION_WINDOWS_BUILD_SUCCEEDED " + playerPath);
            }
            finally
            {
                hybrid.enable = previous;
                HybridCLRSettings.Save();
            }
        }
    }
}
