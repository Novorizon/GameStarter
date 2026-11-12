using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Framework.Event;
using Game.Framework.Timing;
using Game.HotUpdate.Client;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor
{
    /// <summary>Two actual Play sessions; reopen Bootstrap between rounds because Unity destroys DontDestroyOnLoad objects on Stop.</summary>
    public static class GameEntryValidation
    {
        private const string Output = "Artifacts/NativeValidation/entry-module.json";
        private static readonly ResourceMode[] Modes = { ResourceMode.Online, ResourceMode.Offline };
        private static readonly List<RunResult> runs = new List<RunResult>();
        private static readonly FieldInfo ModuleField = typeof(GameEntry).GetField("hotUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
        private static GameEntry entry;
        private static object previousModule;
        private static PlayerHubView previousView;
        private static Result result;
        private static bool previousOptionsEnabled;
        private static EnterPlayModeOptions previousOptions;
        private static bool optionsCaptured;
        private static bool switchesCaptured;
        private static bool originalInitialize;
        private static bool originalSimulation;
        private static int originalMode;
        private static int entryId;
        private static int runIndex;
        private static int readyFrame;
        private static int editUpdates;
        private static double deadline;
        private static double readyAt;
        private static double stopRequested;
        private static double enteredEditAt;
        private static bool enteredEdit;
        private static bool finishing;
        private static bool exited;
        private static string stage;
        private static string failure;
        private static StartupScreen progressProbe;
        private static ProgressSource progressSource;
        private static int progressPhase;
        private static int progressFrame;

        public static void Run()
        {
            result = new Result
            {
                unity = Application.unityVersion,
                modes = Modes.Select(mode => mode.ToString()).ToArray(),
                scope = "Two actual Play sessions with Editor simulation; Bootstrap is reopened between rounds after Unity destroys DontDestroyOnLoad objects. No retained-Entry, offline-bundle or HTTP validation."
            };
            optionsCaptured = false;
            switchesCaptured = false;
            finishing = false;
            exited = false;
            entry = null;
            previousModule = null;
            previousView = null;
            progressProbe = null;
            progressPhase = 0;
            failure = null;
            runIndex = 0;
            runs.Clear();
            stage = "setup";
            try
            {
                Begin();
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void Begin()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start entry validation in Edit Mode.");
            Require(ModuleField != null, "GameEntry.hotUpdate field is missing; update the lifecycle probe.");
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            optionsCaptured = true;
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("UnityEditor.GameView", false))
                    .First(value => value != null);
                var gameView = EditorWindow.GetWindow(type);
                gameView.position = new Rect(20, 20, 1440, 810);
                gameView.Show();
                gameView.Focus();
            }

            deadline = EditorApplication.timeSinceStartup + 120;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            result.disableReloads = true;
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            var entries = UnityEngine.Object.FindObjectsOfType<GameEntry>(true);
            Require(entries.Length == 1, "Bootstrap must contain exactly one GameEntry.");
            entry = entries[0];
            entryId = entry.GetInstanceID();
            var serialized = new SerializedObject(entry);
            originalInitialize = Property(serialized, "initializeHotUpdate").boolValue;
            originalSimulation = Property(serialized, "simulateResourcesInEditor").boolValue;
            originalMode = Property(serialized, "resourceMode").intValue;
            switchesCaptured = true;
            Application.logMessageReceived += OnLog;
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += Poll;
            BeginRound();
        }

        private static void BeginRound()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Mode switches must be changed in Edit Mode.");
            RequireSameEntry();
            var serialized = new SerializedObject(entry);
            Property(serialized, "initializeHotUpdate").boolValue = true;
            Property(serialized, "simulateResourcesInEditor").boolValue = true;
            Property(serialized, "resourceMode").intValue = (int)Modes[runIndex];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            runs.Add(new RunResult { mode = Modes[runIndex].ToString(), entryInstanceId = entryId, editorSimulation = true });
            readyAt = 0;
            enteredEdit = false;
            editUpdates = 0;
            stage = "waiting-ready-" + Modes[runIndex];
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        private static void Poll()
        {
            if (exited)
            {
                return;
            }

            try
            {
                if (finishing)
                {
                    if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        Exit();
                    }
                    else if (EditorApplication.timeSinceStartup - stopRequested > 10)
                    {
                        result.success = false;
                        result.detail += " Stop Play timed out during final cleanup.";
                        Exit();
                    }

                    return;
                }

                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "120-second entry lifecycle deadline exceeded at " + stage);
                Require(
                    EditorSettings.enterPlayModeOptionsEnabled
                    && EditorSettings.enterPlayModeOptions == (EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload),
                    "Both Play reloads must remain disabled throughout the probe.");
                if (stage == "waiting-shutdown")
                {
                    PollShutdown();
                    return;
                }

                PollReady();
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void PollReady()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            RequireSameEntry();
            if (!GameEntry.TryGetInstance(out var active))
            {
                return;
            }

            Require(ReferenceEquals(active, entry), "The singleton selected another Entry instead of resuming the retained component.");
            Require(entry.State != StartupState.Failed, entry.StartupMessage);
            if (entry.State != StartupState.Ready)
            {
                return;
            }

            if (progressProbe != null)
            {
                PollProgressProbe();
                return;
            }

            var startupScreen = UnityEngine.Object.FindObjectOfType<StartupScreen>();
            if (startupScreen != null)
            {
                if (runIndex == 1)
                {
                    var title = startupScreen.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Title");
                    Require(title.text == "林间哨站", "The next startup did not apply the cached UI layout.");
                    result.startupThemeCacheVerified = true;
                }

                return;
            }

            if (runIndex == 0 && !result.startupProgressVerified)
            {
                progressSource = new ProgressSource
                {
                    Snapshot = new StartupSnapshot
                    {
                        Downloading = true,
                        Fraction = .90f,
                        CompletedBytes = 25 * 1048576L,
                        TotalBytes = 100 * 1048576L
                    }
                };
                progressProbe = StartupScreen.Open(progressSource);
                progressPhase = 1;
                progressFrame = Time.frameCount;
                return;
            }

            if (readyAt == 0)
            {
                readyAt = EditorApplication.timeSinceStartup;
                readyFrame = Time.frameCount;
                return;
            }

            if (EditorApplication.timeSinceStartup - readyAt < .15 || Time.frameCount - readyFrame < 2)
            {
                return;
            }

            object module = ModuleField.GetValue(entry);
            Require(module != null && !ReferenceEquals(module, previousModule), "Each retained Entry lifetime must create a fresh hot-update module.");
            var views = UnityEngine.Object.FindObjectsOfType<PlayerHubView>(true);
            Require(views.Length == 1 && views[0].gameObject.activeInHierarchy, "Ready Entry must own exactly one active player page.");
            var view = views[0];
            Require(!ReferenceEquals(view, previousView), "The second run reused the previous native view.");
            var labels = view.GetComponentsInChildren<TMP_Text>(true);
            var run = runs[runIndex];
            run.tmpCount = labels.Length;
            run.legacyTextCount = view.GetComponentsInChildren<Text>(true).Length;
            Require(run.tmpCount > 0 && labels.All(label => label.font != null), "TMP controls or font assets are missing.");
            Require(run.legacyTextCount == 0, "The prototype contains legacy UI.Text.");
            Require(TimerManager.IsCreated && EventManager.IsCreated, "Ready startup did not initialize the managers whose shutdown is being tested.");
            var serialized = new SerializedObject(entry);
            Require(Property(serialized, "resourceMode").intValue == (int)Modes[runIndex], "Entry did not preserve the selected resource mode.");
            Require(Property(serialized, "simulateResourcesInEditor").boolValue, "This probe must use Editor resource simulation.");
            Require(run.gameReadyMarker && run.playerPageReadyMarker, "The real game and player-page startup markers were not observed in this round.");
            run.ready = true;
            run.viewName = view.name;
            run.viewInstanceId = view.GetInstanceID();
            run.freshModule = true;
            run.sameEntry = true;
            previousModule = module;
            previousView = view;
            if (runIndex == 0)
            {
                string layout = File.ReadAllText("Assets/GameAssets/Common/Startup/Layout.json").Replace("翠林守卫", "林间哨站");
                StartupArtwork.CacheTheme(File.ReadAllBytes("Assets/GameAssets/Common/Startup/Background.bytes"), layout);
            }
            stage = "waiting-shutdown";
            stopRequested = EditorApplication.timeSinceStartup;
            EditorApplication.isPlaying = false;
        }

        private static void PollProgressProbe()
        {
            if (Time.frameCount - progressFrame < 3)
            {
                return;
            }

            var percent = progressProbe.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Percent");
            if (progressPhase == 1)
            {
                Require(percent.text == "25%", "Download UI did not use the actual byte fraction.");
                Directory.CreateDirectory(Path.GetDirectoryName(Output));
                ScreenCapture.CaptureScreenshot("Artifacts/NativeValidation/startup-download-probe.png");
                progressPhase = 2;
                progressFrame = Time.frameCount;
                return;
            }

            if (progressPhase == 2)
            {
                progressSource.Snapshot = new StartupSnapshot { Fraction = .60f, Message = "载入玩家页面" };
                progressPhase = 3;
                progressFrame = Time.frameCount;
                return;
            }

            Require(percent.text == "60%", "Normal startup UI did not use the reported step fraction.");
            progressProbe.Dispose();
            progressProbe = null;
            result.startupProgressVerified = true;
        }

        private static void PollShutdown()
        {
            Require(EditorApplication.timeSinceStartup - stopRequested < 10, "Stop Play did not complete within 10 seconds.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || !enteredEdit)
            {
                return;
            }

            editUpdates++;
            if (editUpdates < 2 || EditorApplication.timeSinceStartup - enteredEditAt < .15)
            {
                return;
            }

            var remaining = UnityEngine.Object.FindObjectsOfType<GameEntry>(true);
            Require(
                remaining.All(value => ModuleField.GetValue(value) == null && value.State == StartupState.NotStarted),
                "Stopping Play retained Entry module/state.");
            runs[runIndex].entryDestroyedOnStop = entry == null;
            Require(previousView == null, "The preceding native prototype component survived stopping Play.");
            Require(UnityEngine.Object.FindObjectsOfType<PlayerHubView>(true).Length == 0, "A prototype view survived stopping Play.");
            Require(!TimerManager.IsCreated && !EventManager.IsCreated, "Central shutdown retained Timer/Event manager instances.");
            runs[runIndex].shutdown = true;
            result.runs = runs.Count(run => run.ready && run.shutdown);
            runIndex++;
            if (runIndex < Modes.Length)
            {
                // Unity destroys the root DontDestroyOnLoad Entry on Stop, even with scene reload disabled.
                RestoreSceneEntry();
                result.sceneReopenedBetweenRounds = true;
                BeginRound();
                return;
            }

            result.readyAndShutdown = result.runs == 2;
            result.sameEntryRetained = false;
            result.freshModules = runs.All(run => run.freshModule);
            Require(result.startupProgressVerified, "Startup/download progress presentation was not verified.");
            Require(result.startupThemeCacheVerified, "The second startup did not verify the cached layout.");
            Finish(true, "Two real Play sessions verified fresh modules, TMP views and central shutdown; Bootstrap was reopened between rounds.");
        }

        private static void RestoreSceneEntry()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            var entries = UnityEngine.Object.FindObjectsOfType<GameEntry>(true);
            Require(entries.Length == 1, "Bootstrap must contain exactly one Entry after reopening.");
            entry = entries[0];
            entryId = entry.GetInstanceID();
        }

        private static void RequireSameEntry()
        {
            Require(entry != null && entry.GetInstanceID() == entryId, "The original Entry component was destroyed or replaced.");
            var entries = UnityEngine.Object.FindObjectsOfType<GameEntry>(true);
            Require(entries.Length == 1 && ReferenceEquals(entries[0], entry), "The scene must retain the exact original Entry component.");
        }

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                enteredEdit = true;
                enteredEditAt = EditorApplication.timeSinceStartup;
                editUpdates = 0;
            }
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                if (failure == null)
                {
                    failure = message + "\n" + stack;
                }

                return;
            }

            if (runIndex < runs.Count)
            {
                runs[runIndex].gameReadyMarker |= message.Contains("GAME_STARTER_READY");
                runs[runIndex].playerPageReadyMarker |= message.Contains("PLAYER_PAGE_READY");
            }
        }

        private static SerializedProperty Property(SerializedObject serialized, string name)
        {
            var property = serialized.FindProperty(name);
            Require(property != null, "Missing Entry serialized switch: " + name);
            return property;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "Entry lifecycle validation failed.");
            }
        }

        private static void Finish(bool success, string detail)
        {
            if (finishing || exited)
            {
                return;
            }

            finishing = true;
            result.success = success;
            result.stage = stage;
            result.detail = detail;
            stopRequested = EditorApplication.timeSinceStartup;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
            }
            else
            {
                Exit();
            }
        }

        private static void Exit()
        {
            progressProbe?.Dispose();
            progressProbe = null;
            if (exited)
            {
                return;
            }

            exited = true;
            EditorApplication.update -= Poll;
            EditorApplication.playModeStateChanged -= OnPlayState;
            Application.logMessageReceived -= OnLog;
            try
            {
                if (failure != null)
                {
                    result.success = false;
                    result.detail += "\nUnity error: " + failure;
                }

                if (optionsCaptured)
                {
                    EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
                    EditorSettings.enterPlayModeOptions = previousOptions;
                    result.originalSettingsRestored = true;
                }

                if (switchesCaptured)
                {
                    if (entry == null)
                    {
                        RestoreSceneEntry();
                    }
                    var serialized = new SerializedObject(entry);
                    Property(serialized, "initializeHotUpdate").boolValue = originalInitialize;
                    Property(serialized, "simulateResourcesInEditor").boolValue = originalSimulation;
                    Property(serialized, "resourceMode").intValue = originalMode;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    result.originalSwitchesRestored = true;
                }
            }
            catch (Exception error)
            {
                result.success = false;
                result.detail += "\nRestore: " + error;
            }

            result.runDetails = runs.ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            File.WriteAllText(Output, JsonUtility.ToJson(result, true));
            EditorApplication.Exit(result.success ? 0 : 1);
        }

        private sealed class ProgressSource : IStartupProgressSource
        {
            internal StartupSnapshot Snapshot;
            public StartupSnapshot ReadStartup() => Snapshot;
        }

        [Serializable]
        private sealed class Result
        {
            public string unity;
            public bool success;
            public string stage;
            public string detail;
            public string scope;
            public int runs;
            public bool disableReloads;
            public bool readyAndShutdown;
            public bool startupProgressVerified;
            public bool startupThemeCacheVerified;
            public string[] modes;
            public bool sameEntryRetained;
            public bool sceneReopenedBetweenRounds;
            public bool freshModules;
            public bool originalSettingsRestored;
            public bool originalSwitchesRestored;
            public bool offlineBundleValidated = false;
            public bool httpValidated = false;
            public RunResult[] runDetails;
        }

        [Serializable]
        private sealed class RunResult
        {
            public string mode;
            public bool editorSimulation;
            public int entryInstanceId;
            public int viewInstanceId;
            public string viewName;
            public bool gameReadyMarker;
            public bool playerPageReadyMarker;
            public bool ready;
            public bool shutdown;
            public bool sameEntry;
            public bool entryDestroyedOnStop;
            public bool freshModule;
            public int tmpCount;
            public int legacyTextCount;
        }
    }
}
