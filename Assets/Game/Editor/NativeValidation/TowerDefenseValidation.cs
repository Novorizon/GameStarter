using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Framework.Event;
using Game.Framework.Timing;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Game.Editor
{
    /// <summary>Real-Unity prototype acceptance. Run in batch mode without -quit; rendering requires a graphics device.</summary>
    public static class TowerDefenseValidation
    {
        private const string Output = "Artifacts/TowerDefense";
        private static readonly List<Shot> shots = new List<Shot>();
        private static bool previousEnabled;
        private static EnterPlayModeOptions previousOptions;
        private static TowerDefensePrototypeView view;
        private static FightSession game;
        private static Result result;
        private static string stage;
        private static string failure;
        private static double deadline;
        private static double delayUntil;
        private static double captureStarted;
        private static double captureRequested;
        private static double stopRequested;
        private static int renderStart;
        private static int frameStart;
        private static int requestRenderFrame;
        private static bool captureIssued;
        private static bool battleCaptured;
        private static bool cardsCaptured;
        private static bool finishing;
        private static bool verifyingDefeat;
        private static bool firstWinRecorded;
        private static bool firstRestartVerified;
        private static bool pendingRestartVisuals;
        private static bool defeatRestartPending;
        private static int restartFrame;
        private static bool failedCreationPending;
        private static int failedCreationFrame;
        private static GameObject failedCreationRoot;
        private static CountingLease failedCreationLease;
        private static Shot pendingShot;
        private static Action afterCapture;
        private static EditorWindow gameView;

        public static void Run()
        {
            previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            result = new Result();
            finishing = false;
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
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Start validation from Edit Mode.");
            }

            Directory.CreateDirectory(Output);
            shots.Clear();
            previousEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            result = new Result
            {
                unity = Application.unityVersion,
                graphicsDeviceNull = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null,
                renderingValidated = false
            };
            result.graphicsDevice = SystemInfo.graphicsDeviceType.ToString();
            result.noGraphicsRequested = Array.IndexOf(Environment.GetCommandLineArgs(), "-nographics") >= 0;
            stage = "waiting-startup";
            finishing = false;
            battleCaptured = false;
            cardsCaptured = false;
            verifyingDefeat = false;
            firstWinRecorded = false;
            firstRestartVerified = false;
            pendingRestartVisuals = false;
            defeatRestartPending = false;
            failedCreationPending = false;
            failedCreationRoot = null;
            failedCreationLease = null;
            failure = null;
            pendingShot = null;
            view = null;
            game = null;
            deadline = EditorApplication.timeSinceStartup + 180;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            if (CanRender)
            {
                Type type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("UnityEditor.GameView", false)).FirstOrDefault(value => value != null);
                if (type == null)
                {
                    throw new InvalidOperationException("Unity GameView type is unavailable.");
                }

                gameView = EditorWindow.GetWindow(type);
                gameView.position = new Rect(20, 20, 1440, 810);
                gameView.Show();
                gameView.Focus();
            }

            Application.logMessageReceived += CaptureError;
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += Poll;
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

        private static bool CanRender => !result.graphicsDeviceNull && !result.noGraphicsRequested;

        private static void CaptureError(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                failure = message + "\n" + stack;
            }
        }

        private static void Poll()
        {
            try
            {
                if (finishing)
                {
                    if (EditorApplication.timeSinceStartup - stopRequested > 8)
                    {
                        result.success = false;
                        result.detail += " Stop Play did not complete within 8 seconds.";
                        Exit();
                    }

                    return;
                }

                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "180-second acceptance deadline exceeded at " + stage);
                if (!EditorApplication.isPlaying)
                {
                    return;
                }

                if (pendingShot != null)
                {
                    PollCapture();
                    return;
                }

                if (EditorApplication.timeSinceStartup < delayUntil)
                {
                    return;
                }

                if (pendingRestartVisuals)
                {
                    if (Time.frameCount - restartFrame < 2)
                    {
                        return;
                    }

                    Require(view.GetComponentsInChildren<PrototypeWorldTarget>(true).All(target => target.TowerId == 0), "Restart retained old tower view objects after rendered frames.");
                    Require(!view.GetComponentsInChildren<Transform>(true).Any(value => value.name.StartsWith("Enemy ", StringComparison.Ordinal)), "Restart retained old enemy view objects after rendered frames.");
                    pendingRestartVisuals = false;
                    if (defeatRestartPending)
                    {
                        result.restartVerified = firstRestartVerified;
                        stage = "defeat-restarted";
                        Finish(true, "Six-wave victory, actual card buttons, victory/defeat restart buttons and central shutdown verified.");
                        return;
                    }

                    firstRestartVerified = true;
                }

                if (failedCreationPending)
                {
                    if (Time.frameCount - failedCreationFrame < 2)
                    {
                        return;
                    }

                    Require(failedCreationLease.DisposeCount == 1, "Failed view creation did not return its owned lease exactly once after native destruction.");
                    Require(failedCreationRoot == null, "Failed creation retained its native prototype root.");
                    Require(UnityEngine.Object.FindObjectsOfType<TowerDefensePrototypeView>(true).Length == 1, "Failed view creation left an extra prototype component.");
                    result.failedCreationCleanupVerified = true;
                    failedCreationPending = false;
                    BuildAndUpgrade();
                    Capture("planning", () =>
                    {
                        Require(game.StartNextWave(), "Could not start the first wave.");
                        stage = "fighting";
                    });
                    return;
                }

                if (game == null)
                {
                    if (!GameEntry.TryGetInstance(out var entry))
                    {
                        return;
                    }

                    Require(entry.State != StartupState.Failed, entry.StartupMessage);
                    if (entry.State != StartupState.Ready)
                    {
                        return;
                    }

                    view = UnityEngine.Object.FindObjectOfType<TowerDefensePrototypeView>();
                    Require(view != null, "Ready entry did not create the tower defense view.");
                    game = view.Session;
                    var labels = view.GetComponentsInChildren<TMP_Text>(true);
                    result.tmpCount = labels.Length;
                    result.legacyTextCount = view.GetComponentsInChildren<Text>(true).Length;
                    Require(labels.Length > 0 && labels.All(label => label.font != null), "TMP controls/font assets are missing.");
                    Require(result.legacyTextCount == 0, "Legacy UI.Text found in the battle view.");
                    Require(game.Configuration.Waves.Length == 6, "Expected the six-wave Luban stage, not a static demo fixture.");
                    result.seed = game.Seed;
                    BeginFailedCreationCheck(labels[0].font);
                    return;
                }

                if (verifyingDefeat)
                {
                    PollDefeatAndRestart();
                    return;
                }

                Require(game.Phase != BattlePhase.Defeat, "Automated build policy lost the real configured stage.");
                if (game.Phase == BattlePhase.Victory)
                {
                    Capture("victory", () =>
                    {
                        Require(battleCaptured && cardsCaptured, "Required battle/card stages were not observed.");
                        Require(result.upgrades > 0, "The automated policy did not exercise tower upgrades.");
                        Require(result.cardChoices == game.Configuration.Waves.Take(game.Configuration.Waves.Length - 1).Count(wave => wave.GiveCard), "A wave's card decision was skipped.");
                        RecordFirstWin();
                        RestartThroughButton();
                        verifyingDefeat = true;
                        stage = "defeat-chain";
                        delayUntil = EditorApplication.timeSinceStartup + .2;
                    });
                    return;
                }

                if (game.Phase == BattlePhase.ChoosingCard)
                {
                    Require(game.CurrentCardOffers.Count == 3, "Expected three offered cards.");
                    Require(game.CurrentCardOffers.Select(card => card.Id).Distinct().Count() == 3, "Offered cards are not distinct.");
                    if (!cardsCaptured)
                    {
                        cardsCaptured = true;
                        Capture("three-cards", ChooseCardThroughButton);
                    }
                    else
                    {
                        ChooseCardThroughButton();
                    }

                    return;
                }

                if (game.Phase == BattlePhase.Planning)
                {
                    BuildAndUpgrade();
                    Require(game.StartNextWave(), "Could not start wave " + (game.WaveIndex + 1));
                    stage = "fighting";
                    return;
                }

                game.Step(10);
                if (!battleCaptured && game.Enemies.Count > 0 && game.Phase == BattlePhase.Fighting)
                {
                    battleCaptured = true;
                    Capture("battle", () => stage = "fighting");
                }

                if (game.Phase == BattlePhase.ChoosingCard)
                {
                    // Let MonoBehaviour.Update publish the modal before inspecting/invoking its buttons.
                    delayUntil = EditorApplication.timeSinceStartup + .20;
                }
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void BuildAndUpgrade()
        {
            game.SetPaused(false);
            var arrow = game.Configuration.Towers.FirstOrDefault(tower => tower.Id == 1) ?? game.Configuration.Towers[0];
            foreach (var slot in game.Configuration.BuildSlots)
            {
                game.BuildTower(slot.Id, arrow.Id);
            }

            foreach (int id in game.Towers.Select(tower => tower.Id).ToArray())
            {
                while (game.UpgradeTower(id))
                {
                    result.upgrades++;
                }
            }
        }

        private static void BeginFailedCreationCheck(TMP_FontAsset font)
        {
            var before = UnityEngine.Object.FindObjectsOfType<TowerDefensePrototypeView>(true);
            int beforeRootCount = UnityEngine.Object.FindObjectsOfType<GameObject>(true).Count(value => value.name == "TowerDefensePrototype");
            var emptyConfiguration = DemoConfiguration.Create();
            emptyConfiguration.Towers = Array.Empty<TowerDefinition>();
            var untransferredLease = new CountingLease();
            var untransferred = new List<IDisposable> { untransferredLease };
            bool rejected = false;
            try
            {
                TowerDefensePrototypeView.Create(emptyConfiguration, font, assetLeases: untransferred);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Require(rejected, "Empty tower configuration was not rejected before root creation.");
            Require(untransferred.Count == 1 && untransferredLease.DisposeCount == 0, "Precondition rejection changed caller lease ownership.");
            Require(UnityEngine.Object.FindObjectsOfType<TowerDefensePrototypeView>(true).Length == before.Length, "Precondition rejection created a prototype root.");
            Require(UnityEngine.Object.FindObjectsOfType<GameObject>(true).Count(value => value.name == "TowerDefensePrototype") == beforeRootCount, "Empty tower rejection leaked a root without a view component.");
            untransferredLease.Dispose();
            untransferred.Clear();

            var invalidConfiguration = DemoConfiguration.Create();
            invalidConfiguration.BaseHealth = 0;
            failedCreationLease = new CountingLease();
            var transferred = new List<IDisposable> { failedCreationLease };
            rejected = false;
            try
            {
                TowerDefensePrototypeView.Create(invalidConfiguration, font, assetLeases: transferred);
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            Require(rejected, "Invalid domain configuration did not fail during view initialization.");
            Require(transferred.Count == 0, "Failed initialization did not transfer its lease list to the native owner.");
            Require(failedCreationLease.DisposeCount == 0, "Failed initialization returned its lease before native destruction.");
            var oldIds = new HashSet<int>(before.Select(candidate => candidate.GetInstanceID()));
            var pending = UnityEngine.Object.FindObjectsOfType<TowerDefensePrototypeView>(true)
                .Where(candidate => !oldIds.Contains(candidate.GetInstanceID())).ToArray();
            Require(pending.Length == 1, "Expected one pending native prototype root after initialization failure.");
            failedCreationRoot = pending[0].gameObject;
            failedCreationFrame = Time.frameCount;
            failedCreationPending = true;
            stage = "failed-creation-cleanup";
            delayUntil = EditorApplication.timeSinceStartup + .1;
        }

        private static void ChooseCardThroughButton()
        {
            game.SetPaused(false);
            int choice = 0;
            for (int index = 0; index < game.CurrentCardOffers.Count; index++)
            {
                if (game.CurrentCardOffers[index].Effect == CardEffect.DamagePercent)
                {
                    choice = index;
                    break;
                }
            }

            var button = view.GetComponentsInChildren<Button>(true).Single(value => value.name == "Card " + choice);
            Require(button.gameObject.activeInHierarchy && button.interactable, "The offered card button is not available.");
            int before = game.ChosenCardStacks.Values.Sum();
            button.onClick.Invoke();
            Require(game.ChosenCardStacks.Values.Sum() == before + 1 && game.Phase == BattlePhase.Planning, "Actual card button did not apply exactly one choice.");
            button.onClick.Invoke();
            Require(game.ChosenCardStacks.Values.Sum() == before + 1, "The same card button applied a choice twice.");
            if (!verifyingDefeat)
            {
                result.cardChoices++;
            }

            stage = "planning";
            delayUntil = EditorApplication.timeSinceStartup + .15;
        }

        private static void RecordFirstWin()
        {
            firstWinRecorded = true;
            result.waves = game.WaveIndex;
            result.kills = game.Kills;
            result.leaks = game.Leaks;
            result.tick = game.Tick;
            result.phase = game.Phase.ToString();
        }

        private static void RestartThroughButton()
        {
            var previous = game;
            var button = view.GetComponentsInChildren<Button>(true).Single(value => value.name == "再试一次");
            Require(button.gameObject.activeInHierarchy && button.interactable, "The result page restart button is not available.");
            button.onClick.Invoke();
            game = view.Session;
            Require(!ReferenceEquals(previous, game), "Restart kept the previous fight session.");
            Require(game.Phase == BattlePhase.Planning && game.Tick == 0 && game.WaveIndex == 0, "Restart did not reset fight progression.");
            Require(game.Gold == game.Configuration.InitialGold && game.Gold == 240, "Restart did not restore initial gold.");
            Require(game.BaseHp == game.Configuration.BaseHealth && game.BaseHp == 20, "Restart did not restore base HP.");
            Require(game.Enemies.Count == 0 && game.Towers.Count == 0 && game.ChosenCardStacks.Count == 0, "Restart retained old entities or card buffs.");
            Require(!game.IsPaused, "Restart kept a paused session.");
            Require(game.Seed == result.seed, "Restart changed the configured battle seed.");
            pendingRestartVisuals = true;
            restartFrame = Time.frameCount;
            delayUntil = EditorApplication.timeSinceStartup + .2;
        }

        private static void PollDefeatAndRestart()
        {
            Require(game.Towers.Count == 0, "The defeat test must not build towers.");
            if (game.Phase == BattlePhase.Defeat)
            {
                var button = view.GetComponentsInChildren<Button>(true).Single(value => value.name == "再试一次");
                if (!button.gameObject.activeInHierarchy)
                {
                    // The domain may have reached defeat between two UI frames.
                    delayUntil = EditorApplication.timeSinceStartup + .2;
                    return;
                }

                Require(game.BaseHp == 0 && game.Leaks > 0, "Defeat did not follow real enemy leaks.");
                result.defeatWave = game.WaveIndex;
                result.defeatLeaks = game.Leaks;
                result.defeatVerified = true;
                RestartThroughButton();
                defeatRestartPending = true;
                stage = "checking-defeat-restart";
                return;
            }

            Require(game.Phase != BattlePhase.Victory, "A fight without towers unexpectedly won.");
            if (game.Phase == BattlePhase.ChoosingCard)
            {
                ChooseCardThroughButton();
                return;
            }

            if (game.Phase == BattlePhase.Planning)
            {
                Require(game.StartNextWave(), "Could not start the no-tower defeat test wave.");
            }

            game.Step(10);
            if (game.Phase == BattlePhase.ChoosingCard || game.Phase == BattlePhase.Defeat)
            {
                delayUntil = EditorApplication.timeSinceStartup + .2;
            }
        }

        private static void Capture(string name, Action continuation)
        {
            stage = name;
            // Planning, draft and result phases already stop combat; keep their normal interactive UI.
            game.SetPaused(game.Phase == BattlePhase.Fighting);
            pendingShot = new Shot
            {
                stage = name,
                path = CanRender ? Path.GetFullPath(Path.Combine(Output, name + ".png")) : "",
                detail = CanRender ? "pending" : "No graphics device; functional validation only."
            };
            if (CanRender && File.Exists(pendingShot.path))
            {
                File.Delete(pendingShot.path);
            }

            captureStarted = EditorApplication.timeSinceStartup;
            renderStart = Time.renderedFrameCount;
            frameStart = Time.frameCount;
            captureIssued = false;
            afterCapture = continuation;
        }

        private static void PollCapture()
        {
            Require(EditorApplication.timeSinceStartup - captureStarted < 20, "Screenshot timed out: " + pendingShot.stage);
            if (EditorApplication.timeSinceStartup - captureStarted < .35 || Time.frameCount - frameStart < 3)
            {
                return;
            }

            if (CanRender)
            {
                if (Time.renderedFrameCount - renderStart < 3)
                {
                    gameView.Repaint();
                    return;
                }

                if (!captureIssued)
                {
                    gameView.Focus();
                    ScreenCapture.CaptureScreenshot(pendingShot.path);
                    captureIssued = true;
                    captureRequested = EditorApplication.timeSinceStartup;
                    requestRenderFrame = Time.renderedFrameCount;
                    return;
                }

                if (EditorApplication.timeSinceStartup - captureRequested < .2 || Time.renderedFrameCount - requestRenderFrame < 2
                    || !File.Exists(pendingShot.path) || new FileInfo(pendingShot.path).Length < 1000)
                {
                    return;
                }

                byte[] png = File.ReadAllBytes(pendingShot.path);
                Require(png.Length > 32 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71, "Screenshot is not PNG: " + pendingShot.path);
                pendingShot.width = BigEndian(png, 16);
                pendingShot.height = BigEndian(png, 20);
                Require(pendingShot.width >= 320 && pendingShot.height >= 180, "Screenshot resolution is too small.");
                pendingShot.captured = true;
                pendingShot.detail = "ScreenCapture after actual rendered frames, including overlay UI.";
            }

            shots.Add(pendingShot);
            pendingShot = null;
            game.SetPaused(false);
            var callback = afterCapture;
            afterCapture = null;
            callback();
        }

        private static int BigEndian(byte[] bytes, int offset)
        {
            return bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "Unity validation failed.");
            }
        }

        private static void Finish(bool success, string detail)
        {
            if (finishing)
            {
                return;
            }

            finishing = true;
            result.success = success;
            result.stage = stage;
            result.detail = detail;
            result.renderingValidated = CanRender && shots.Count == 4 && shots.All(shot => shot.captured);
            if (success && CanRender && !result.renderingValidated)
            {
                result.success = false;
                result.detail += " Graphical validation requires all four real screenshots.";
            }

            if (pendingShot != null)
            {
                pendingShot.detail = "Failed: " + detail;
                shots.Add(pendingShot);
                pendingShot = null;
            }
            if (game != null && !firstWinRecorded)
            {
                result.waves = game.WaveIndex;
                result.kills = game.Kills;
                result.leaks = game.Leaks;
                result.tick = game.Tick;
                result.phase = game.Phase.ToString();
            }

            result.screenshots = shots.ToArray();
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

        private static void OnPlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && finishing)
            {
                try
                {
                    Require(failure == null, failure);
                    Require(UnityEngine.Object.FindObjectsOfType<TowerDefensePrototypeView>(true).Length == 0, "Prototype root survived stopping Play.");
                    Require(!TimerManager.IsCreated && !EventManager.IsCreated, "Central entry shutdown retained Timer/Event managers.");
                    result.shutdownVerified = true;
                }
                catch (Exception error)
                {
                    result.success = false;
                    result.detail += "\nShutdown: " + error;
                }

                Exit();
            }
        }

        private static void Exit()
        {
            EditorApplication.update -= Poll;
            EditorApplication.playModeStateChanged -= OnPlayState;
            Application.logMessageReceived -= CaptureError;
            EditorSettings.enterPlayModeOptionsEnabled = previousEnabled;
            EditorSettings.enterPlayModeOptions = previousOptions;
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "validation.json"), JsonUtility.ToJson(result, true));
            EditorApplication.Exit(result.success ? 0 : 1);
        }

        [Serializable]
        private sealed class Result
        {
            public string unity;
            public bool success;
            public string stage;
            public string detail;
            public string graphicsDevice;
            public bool graphicsDeviceNull;
            public bool noGraphicsRequested;
            public bool renderingValidated;
            public bool shutdownVerified;
            public bool restartVerified;
            public bool defeatVerified;
            public bool failedCreationCleanupVerified;
            public int tmpCount;
            public int legacyTextCount;
            public uint seed;
            public int waves;
            public int kills;
            public int leaks;
            public long tick;
            public int upgrades;
            public int cardChoices;
            public int defeatWave;
            public int defeatLeaks;
            public string phase;
            public Shot[] screenshots;
        }

        [Serializable]
        private sealed class Shot
        {
            public string stage;
            public string path;
            public bool captured;
            public int width;
            public int height;
            public string detail;
        }

        private sealed class CountingLease : IDisposable
        {
            internal int DisposeCount;
            public void Dispose()
            {
                DisposeCount++;
            }
        }
    }
}
