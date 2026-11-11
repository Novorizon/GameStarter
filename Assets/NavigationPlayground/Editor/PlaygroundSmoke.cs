using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavigationLab.UnityPlayground
{
    /// <summary>Explicit command-line smoke run. Normal tool use never enables this state machine.</summary>
    [InitializeOnLoad]
    public static class PlaygroundSmoke
    {
        private const string ActiveKey = "NavigationPlayground.Smoke.Active";
        private const string StateKey = "NavigationPlayground.Smoke.State";
        private const string SingleInput = "forest-single-medium";
        private static SmokeState state;

        static PlaygroundSmoke()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                state = JsonUtility.FromJson<SmokeState>(SessionState.GetString(StateKey, ""));
                Register();
            }
        }

        public static void Run()
        {
            state = new SmokeState
            {
                reportPath = Path.GetFullPath("Logs/navigation-playground-smoke.json"),
                report = new SmokeReport { startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion }
            };
            SessionState.SetBool(ActiveKey, true);
            Register();
            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                int argument = Array.IndexOf(arguments, "--navigation-playground-report");
                Require(argument >= 0 && argument + 1 < arguments.Length, "Missing --navigation-playground-report absolute path.");
                Require(Path.IsPathRooted(arguments[argument + 1]), "The smoke report path must be absolute.");
                state.reportPath = Path.GetFullPath(arguments[argument + 1]);
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start the smoke run from Edit mode.");
                EditorApplication.isPaused = false;
                state.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(10).Ticks;
                SetPhase("StartControls");
                WriteReport();
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void Register()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
        }

        private static void Update()
        {
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }
            try
            {
                Require(state != null, "Smoke state did not survive the domain reload.");
                Require(DateTime.UtcNow.Ticks <= state.deadlineUtcTicks, "Smoke phase timed out: " + state.phase);
                Tick();
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void Tick()
        {
            PlaygroundSnapshot latest = PlaygroundController.Latest;
            switch (state.phase)
            {
                case "StartControls":
                    StartCase(0, SingleInput, "WaitRunningForPause");
                    break;
                case "WaitRunningForPause":
                    if (Running(latest) && latest.Microstep >= 10)
                    {
                        RequirePlayScene();
                        PlaygroundController.SetIsometric(true);
                        PlaygroundController.SetIsometric(false);
                        PlaygroundController.SetPaused(true);
                        SetPhase("WaitPaused");
                    }
                    break;
                case "WaitPaused":
                    if (Running(latest) && latest.Status == "Paused")
                    {
                        state.pausedMicrostep = latest.Microstep;
                        SetPhase("VerifyPaused");
                    }
                    break;
                case "VerifyPaused":
                    Require(latest != null && latest.Status == "Paused" && latest.Microstep == state.pausedMicrostep, "Paused worker advanced.");
                    if (PhaseMilliseconds() >= 200)
                    {
                        Record("pause", latest);
                        SetPhase("WaitSingleStep");
                        PlaygroundController.Step();
                    }
                    break;
                case "WaitSingleStep":
                    Require(latest != null && !latest.Finished && latest.Status == "Paused", "Single-step lost its paused state.");
                    Require(latest.Microstep <= state.pausedMicrostep + 10, "Single 50ms step advanced more than ten 5ms microsteps.");
                    if (latest.Microstep == state.pausedMicrostep + 10)
                    {
                        SetPhase("VerifySingleStep");
                    }
                    break;
                case "VerifySingleStep":
                    Require(latest != null && latest.Status == "Paused" && latest.Microstep == state.pausedMicrostep + 10, "Single-step did not settle.");
                    if (PhaseMilliseconds() >= 200)
                    {
                        Record("single-step-50ms", latest);
                        PlaygroundController.SetPaused(false);
                        SetPhase("WaitResumed");
                    }
                    break;
                case "WaitResumed":
                    if (Running(latest) && latest.Status == "Running" && latest.Microstep > state.pausedMicrostep + 10)
                    {
                        Record("resume-and-camera-toggle", latest);
                        SetPhase("WaitStopped");
                        PlaygroundController.Stop();
                    }
                    break;
                case "WaitStopped":
                    if (Completed(latest, "Stopped"))
                    {
                        Record("stop-before-completion", latest);
                        SetPhase("StartCandidate");
                    }
                    break;
                case "StartCandidate":
                    StartCase(state.candidate, SingleInput, "WaitCandidate");
                    break;
                case "WaitCandidate":
                    if (Completed(latest, "Passed"))
                    {
                        RequirePlayScene();
                        Record(PlaygroundController.CandidateNames[state.candidate] + "/" + SingleInput, latest);
                        state.candidate++;
                        SetPhase(state.candidate < PlaygroundController.CandidateNames.Length ? "StartCandidate" : "StartUnsupported");
                    }
                    break;
                case "StartUnsupported":
                    StartCase(0, "dynamic-route-block-and-open", "WaitUnsupported");
                    break;
                case "WaitUnsupported":
                    if (Completed(latest, "Unsupported"))
                    {
                        Record("A/dynamic-route-block-and-open", latest);
                        ExitPlay("WaitFirstEdit");
                    }
                    break;
                case "WaitFirstEdit":
                    if (InEditMode())
                    {
                        Require(PlaygroundController.Latest == null && !PlaygroundController.IsBusy, "Exiting Play retained a simulation.");
                        Record("exit-play-disposes-worker-and-view", null);
                        StartCase(5, SingleInput, "WaitReentered");
                    }
                    break;
                case "WaitReentered":
                    if (Completed(latest, "Passed"))
                    {
                        RequirePlayScene();
                        Record("E/reenter-play/" + SingleInput, latest);
                        CaptureCamera();
                        ExitPlay("WaitFinalEdit");
                    }
                    break;
                case "WaitFinalEdit":
                    if (InEditMode())
                    {
                        Require(PlaygroundController.Latest == null && !PlaygroundController.IsBusy, "Final Play exit retained a simulation.");
                        PlaygroundController.Dispose();
                        Record("final-exit-and-idempotent-dispose", null);
                        Finish(true, "");
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unknown smoke phase: " + state.phase);
            }
        }

        private static void StartCase(int candidate, string input, string nextPhase)
        {
            Require(!PlaygroundController.IsBusy, "Previous worker is still running before restart.");
            state.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(10).Ticks;
            state.currentCase = PlaygroundController.CandidateNames[candidate] + "/" + input;
            SetPhase(nextPhase);
            PlaygroundController.Begin(candidate, input, 20);
            Require(PlaygroundController.IsBusy || PlaygroundController.Latest != null, "Could not begin observation: " + PlaygroundController.Message);
        }

        private static bool Running(PlaygroundSnapshot latest)
        {
            if (latest == null)
            {
                return false;
            }
            Require(!latest.Finished, "Observation finished during control verification: " + latest.Status + " " + latest.Detail);
            return latest.Status == "Running" || latest.Status == "Paused";
        }

        private static bool Completed(PlaygroundSnapshot latest, string expected)
        {
            if (latest == null || !latest.Finished)
            {
                return false;
            }
            Require(latest.Status == expected, state.currentCase + ": expected " + expected + ", got " + latest.Status + ". " + latest.Detail);
            return !PlaygroundController.IsBusy;
        }

        private static void RequirePlayScene()
        {
            Require(EditorApplication.isPlaying, "Observation ran outside Play mode.");
            Require(SceneManager.GetActiveScene().path == PlaygroundController.ScenePath, "Unexpected active Play scene.");
        }

        private static void ExitPlay(string nextPhase)
        {
            state.deadlineUtcTicks = DateTime.UtcNow.AddMinutes(2).Ticks;
            SetPhase(nextPhase);
            EditorApplication.ExitPlaymode();
        }

        private static bool InEditMode()
        {
            return !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static void SetPhase(string value)
        {
            state.phase = value;
            state.phaseStartedUtcTicks = DateTime.UtcNow.Ticks;
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
        }

        private static double PhaseMilliseconds()
        {
            return TimeSpan.FromTicks(DateTime.UtcNow.Ticks - state.phaseStartedUtcTicks).TotalMilliseconds;
        }

        private static void Record(string name, PlaygroundSnapshot snapshot)
        {
            state.report.checks.Add(new SmokeCheck
            {
                name = name,
                status = snapshot?.Status ?? "Passed",
                microstep = snapshot?.Microstep ?? -1,
                units = snapshot?.Units.Length ?? 0,
                detail = snapshot?.Detail ?? "Lifecycle check completed."
            });
            SessionState.SetString(StateKey, JsonUtility.ToJson(state));
            WriteReport();
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (SessionState.GetBool(ActiveKey, false) && (type == LogType.Exception || type == LogType.Assert))
            {
                Finish(false, condition + "\n" + stackTrace);
            }
        }

        private static void Finish(bool success, string error)
        {
            SessionState.SetBool(ActiveKey, false);
            EditorApplication.update -= Update;
            Application.logMessageReceived -= OnLog;
            try
            {
                PlaygroundController.Stop();
                PlaygroundController.Dispose();
            }
            catch (Exception cleanupError)
            {
                success = false;
                error += "\nCleanup failed: " + cleanupError;
            }
            if (state != null)
            {
                state.report.result = success ? "Passed" : "Failed";
                state.report.finishedUtc = DateTime.UtcNow.ToString("O");
                state.report.error = error;
                state.report.lastPhase = state.phase;
                try
                {
                    WriteReport();
                }
                catch (Exception reportError)
                {
                    success = false;
                    Debug.LogError("Could not write navigation smoke report: " + reportError);
                }
            }
            SessionState.EraseString(StateKey);
            Debug.Log("NAVIGATION_PLAYGROUND_SMOKE " + (success ? "Passed" : "Failed") + " " + error);
            EditorApplication.Exit(success ? 0 : 1);
        }

        private static void WriteReport()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(state.reportPath));
            File.WriteAllText(state.reportPath, JsonUtility.ToJson(state.report, true));
        }

        private static void CaptureCamera()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                state.report.screenshot = "Skipped: Unity uses a null graphics device.";
                WriteReport();
                return;
            }

            Camera camera = Array.Find(Camera.allCameras, item => item.gameObject.name == "Navigation Playground Camera");
            Require(camera != null, "The playground did not create its overview camera.");
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture target = null;
            Texture2D pixels = null;
            try
            {
                const int width = 1280;
                int height = Mathf.Max(1, Mathf.RoundToInt(width / Mathf.Max(camera.aspect, 0.1f)));
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.DontSave };
                Require(target.Create(), "Could not allocate the screenshot render target.");
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.DontSave };
                pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                pixels.Apply(false, false);
                Color32[] colors = pixels.GetPixels32();
                bool varied = false;
                for (int i = 1; i < colors.Length && !varied; i += Mathf.Max(1, colors.Length / 4096))
                {
                    varied = !colors[i].Equals(colors[0]);
                }
                Require(varied, "The overview camera produced a uniform image.");
                state.report.screenshot = Path.ChangeExtension(state.reportPath, ".png");
                File.WriteAllBytes(state.report.screenshot, pixels.EncodeToPNG());
                Record("game-camera-png", PlaygroundController.Latest);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                if (pixels != null)
                {
                    UnityEngine.Object.DestroyImmediate(pixels);
                }
                if (target != null)
                {
                    target.Release();
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        [Serializable]
        private sealed class SmokeState
        {
            public string reportPath;
            public string phase;
            public string currentCase;
            public int candidate;
            public int pausedMicrostep;
            public long phaseStartedUtcTicks;
            public long deadlineUtcTicks;
            public SmokeReport report;
        }

        [Serializable]
        private sealed class SmokeReport
        {
            public string scope = "Unity Editor/Play smoke on one no-tower input per candidate; not the full 49-case regression or device acceptance.";
            public string result = "Running";
            public string startedUtc;
            public string finishedUtc;
            public string unityVersion;
            public string lastPhase;
            public string error;
            public string screenshot;
            public List<SmokeCheck> checks = new List<SmokeCheck>();
        }

        [Serializable]
        private sealed class SmokeCheck
        {
            public string name;
            public string status;
            public int microstep;
            public int units;
            public string detail;
        }
    }
}
