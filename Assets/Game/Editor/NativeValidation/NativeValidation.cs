using System;
using System.IO;
using System.Linq;
using HybridCLR.Settings;
using Game.Framework.HotUpdate;
using Game.Framework.HotUpdate.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>仅由真实 Unity 执行；CLI 模型验证不将此结果计为原生验收。</summary>
    public static class NativeValidation
    {
        private static DateTime deadline;
        public static void CompileScripts()
        {
            GameDataSetup.EnsureConfigured();
            GameDataSetup.ValidateGenerated();
            GameBuild.ValidatePlayerScripts();
            WriteReport("compile", true, "PLAYER_SCRIPTS_COMPILED");
            Debug.Log("NATIVE_COMPILE_READY");
        }

        [MenuItem("Tools/Novorizon/GameStarter/Native Validation/Editor Smoke")]
        public static void EditorSmoke()
        {
            GameDataSetup.EnsureConfigured();
            GameDataSetup.ValidateGenerated();
            deadline = DateTime.UtcNow.AddSeconds(40);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            GameBuild.SmokeEditorStartup();
        }

        private static void Poll()
        {
            bool active = GameEntry.TryGetInstance(out var entry);
            bool ready = active && entry.State == StartupState.Ready;
            bool failed = active && entry.State == StartupState.Failed;
            if (!ready && !failed && DateTime.UtcNow < deadline)
            {
                return;
            }

            EditorApplication.update -= Poll;
            WriteReport("editor", ready, ready ? "GAME_STARTER_READY" : active ? entry.StartupMessage : "Startup timed out");
        }

        public static async void GenerateTables()
        {
            try
            {
                await GameDataSetup.GenerateTablesAsync();
                WriteReport("generation", true, "LUBAN_GENERATED");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                WriteReport("generation", false, error.ToString());
                EditorApplication.Exit(1);
            }
        }

        public static void BuildHybridPlayer()
        {
            GameDataSetup.BuildNewPlayer();
            WriteReport("player-build", true, "PLAYER_BUILD_VERIFIED");
        }

        [MenuItem("Tools/Novorizon/GameStarter/Native Validation/Build Local Source Player")]
        public static void BuildLocalPlayer()
        {
            GameDataSetup.BuildResources();
            var hybrid = HybridCLRSettings.instance;
            bool previous = hybrid.enable;
            hybrid.enable = false;
            HybridCLRSettings.Save();
            try
            {
                Directory.CreateDirectory("Builds/LocalSource");
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(), locationPathName = "Builds/LocalSource/GameStarter.exe", target = BuildTarget.StandaloneWindows64, extraScriptingDefines = new[] { "NOVORIZON_COMPILED_GAME" }, options = BuildOptions.StrictMode });
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    throw new InvalidOperationException("Local-source Player build failed.");
                }
            }
            finally
            {
                hybrid.enable = previous;
                HybridCLRSettings.Save();
            }
        }

        public static void ExportPatch()
        {
            GameReleaseBuild.ExportSignedUpdate();
            WriteReport("patch-export", true, "SIGNED_UPDATE_EXPORTED");
        }

        private static void WriteReport(string phase, bool success, string detail)
        {
            Directory.CreateDirectory("Artifacts/NativeValidation");
            File.WriteAllText("Artifacts/NativeValidation/" + phase + ".json", JsonUtility.ToJson(new Report { unity = Application.unityVersion, utc = DateTime.UtcNow.ToString("O"), phase = phase, success = success, detail = detail }, true));
        }

        [Serializable]
        private sealed class Report
        {
            public string unity, utc, phase, detail;
            public bool success;
        }
    }
}
