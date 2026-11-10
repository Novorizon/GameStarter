using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Game.Framework.Data;
using Game.HotUpdate.Client;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;
using Game.HotUpdate.TowerDefense.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Game.Editor
{
    /// <summary>真实表与逻辑地图的独立无塔验收。批处理不要加 -quit 或 -nographics；不使用 Defeat 清理证明到达。</summary>
    public static class TowerDefenseNavigationValidation
    {
        private const string Output = "Artifacts/TowerDefense/Navigation";
        private const string NavigationAsset = "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes";
        private const int MaximumTicks = 12000;
        private const int HoldTicks = 200;
        private static readonly List<Scenario> scenarios = new List<Scenario>();
        private static readonly List<CaseResult> cases = new List<CaseResult>();
        private static readonly List<Shot> shots = new List<Shot>();
        private static readonly Dictionary<int, EnemySnapshot> previous = new Dictionary<int, EnemySnapshot>();
        private static readonly MethodInfo synchronize = typeof(TowerDefensePrototypeView).GetMethod(
            "SynchronizeWorld",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo refresh = typeof(TowerDefensePrototypeView).GetMethod(
            "RefreshInterface",
            BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool previousOptionsEnabled;
        private static EnterPlayModeOptions previousOptions;
        private static Result result;
        private static CaseResult current;
        private static TowerDefensePrototypeView view;
        private static FightSession session;
        private static cfg.Tables tables;
        private static NavigationGrid grid;
        private static EditorWindow gameView;
        private static StreamWriter trace;
        private static string failure;
        private static string stage;
        private static int scenarioIndex;
        private static int settledHp;
        private static int settledAttackSequence;
        private static long settledTick;
        private static int destroyFrame;
        private static bool finishing;
        private static double deadline;
        private static double stopRequested;
        private static Shot pendingShot;
        private static Action afterCapture;
        private static double captureStarted;
        private static double captureRequested;
        private static int captureFrame;
        private static int captureRenderFrame;
        private static int requestedRenderFrame;
        private static bool captureIssued;

        public static void Run()
        {
            previousOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            previousOptions = EditorSettings.enterPlayModeOptions;
            result = new Result { unity = Application.unityVersion, tickMilliseconds = FightSession.TickMilliseconds };
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
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start navigation acceptance from Edit Mode.");
            Directory.CreateDirectory(Output);
            scenarios.Clear();
            cases.Clear();
            shots.Clear();
            previous.Clear();
            failure = null;
            view = null;
            session = null;
            current = null;
            pendingShot = null;
            trace = null;
            scenarioIndex = 0;
            result.graphicsDevice = SystemInfo.graphicsDeviceType.ToString();
            result.stopwatchFrequency = Stopwatch.Frequency;
            result.timingScope = "Step timings exclude geometry verification and presentation synchronization."
                + " Presentation timings measure CPU synchronization, not deferred rendering or GPU time.";
            result.renderPipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.GetType().Name : "BuiltIn";
            result.assetSource = "Actual Bootstrap Luban tables and Editor assets through AssetDatabase."
                + " Player bundles, IL2CPP and devices are not covered.";
            Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "A real graphics device is required for navigation screenshot acceptance.");
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), "-nographics") < 0, "Do not use -nographics for navigation acceptance.");
            Require(synchronize != null && refresh != null, "Presentation synchronization methods were not found.");
            var gameViewType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("UnityEditor.GameView", false))
                .FirstOrDefault(value => value != null);
            Require(gameViewType != null, "Unity GameView is unavailable.");
            gameView = EditorWindow.GetWindow(gameViewType);
            gameView.position = new Rect(20, 20, 1440, 810);
            gameView.Show();
            gameView.Focus();
            deadline = EditorApplication.timeSinceStartup + 600;
            stage = "waiting-startup";
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene("Assets/Scenes/Bootstrap.unity");
            Application.logMessageReceived += CaptureError;
            EditorApplication.playModeStateChanged += OnPlayState;
            EditorApplication.update += Poll;
            EditorApplication.isPaused = false;
            EditorApplication.isPlaying = true;
        }

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
                    if (EditorApplication.timeSinceStartup - stopRequested > 10)
                    {
                        result.success = false;
                        result.detail += " Stop Play did not complete within 10 seconds.";
                        Exit();
                    }

                    return;
                }

                Require(failure == null, failure);
                Require(EditorApplication.timeSinceStartup < deadline, "600-second navigation acceptance deadline exceeded at " + stage);
                if (!EditorApplication.isPlaying)
                {
                    return;
                }

                if (pendingShot != null)
                {
                    PollCapture();
                    return;
                }

                if (stage == "waiting-startup")
                {
                    if (!GameEntry.TryGetInstance(out var entry))
                    {
                        return;
                    }

                    Require(entry.State != StartupState.Failed, entry.StartupMessage);
                    if (entry.State != StartupState.Ready || UnityEngine.Object.FindObjectOfType<StartupScreen>() != null)
                    {
                        return;
                    }

                    PrepareScenarios();
                    CreateScenario();
                    return;
                }

                if (stage == "between-cases")
                {
                    if (Time.frameCount - destroyFrame < 2)
                    {
                        return;
                    }

                    CreateScenario();
                    return;
                }

                Simulate();
            }
            catch (Exception error)
            {
                Finish(false, error.ToString());
            }
        }

        private static void PrepareScenarios()
        {
            tables = DataManager.Instance.Get<cfg.Tables>();
            Require(tables != null, "Bootstrap did not register actual Luban tables.");
            result.configurationVersion = tables.TbTdStage.Get(1).ConfigVersion;
            var terrain = AssetDatabase.LoadAssetAtPath<TextAsset>(NavigationAsset);
            Require(terrain != null, "Actual logical navigation asset was not imported: " + NavigationAsset);
            grid = NavigationLayout.Parse(terrain.text);
            using (var sha = SHA256.Create())
            {
                result.navigationSha256 = BitConverter.ToString(sha.ComputeHash(terrain.bytes)).Replace("-", "").ToLowerInvariant();
            }

            result.gridWidth = grid.Width;
            result.gridHeight = grid.Height;
            result.cellMillimetres = grid.CellSize;
            result.targetRadius = grid.TargetRadius;
            var config = TowerDefenseTables.Create(tables, 1, grid);
            // Independently matched and constructively witnessed for these exact authored waves; see Navigation/Experiments.
            int[] expectedWaveAttackers = { 8, 13, 14, 15, 15, 15 };
            Require(config.Waves.Length == expectedWaveAttackers.Length, "The authored-wave acceptance fixture must be updated after content changes.");
            foreach (var wave in config.Waves)
            {
                Require(wave.Id > 0 && wave.Id <= expectedWaveAttackers.Length, "Unexpected authored wave identifier.");
                scenarios.Add(new Scenario { Name = "wave-" + wave.Id, Wave = wave, ExpectedAttackers = expectedWaveAttackers[wave.Id - 1] });
            }

            var burstSource = config.Waves.OrderByDescending(wave => wave.Groups.Sum(group => group.Count)).First();
            int maximumWave = burstSource.Groups.Sum(group => group.Count);
            Require(maximumWave >= 30, "Expected the authored maximum wave to include at least 30 enemies.");
            scenarios.Add(new Scenario
            {
                Name = "burst-" + maximumWave,
                ExpectedAttackers = expectedWaveAttackers[burstSource.Id - 1],
                Wave = new WaveDefinition
                {
                    Id = burstSource.Id,
                    GiveCard = false,
                    Groups = burstSource.Groups.Select(group => new SpawnGroup
                    {
                        EnemyId = group.EnemyId,
                        RouteId = group.RouteId,
                        Count = group.Count,
                        StartTick = 0,
                        IntervalTicks = 1
                    }).ToArray()
                }
            });
            AddSaturationScenario(config, "full-44-large", 250, 550, 44, 18);
            AddSaturationScenario(config, "full-45-medium", 200, 450, 45, 12);
            var large = config.Enemies.Single(enemy => enemy.Radius == 250 && enemy.AttackRange == 550);
            var small = config.Enemies.Single(enemy => enemy.Radius == 180 && enemy.AttackRange == 450);
            scenarios.Add(new Scenario
            {
                Name = "full-44-large-two-late-small",
                Description = "Stress override: actual large/small profiles, 44 large settled first, then 2 small from the original entrance at tick 4000.",
                EnemyIds = new[] { large.Id, small.Id },
                ExpectedAttackers = 20,
                InitialLargeCount = 44,
                LateStartTick = 4000,
                Wave = new WaveDefinition
                {
                    Id = 1,
                    GiveCard = false,
                    Groups = new[]
                    {
                        new SpawnGroup { EnemyId = large.Id, RouteId = config.Routes[0].Id, Count = 44, IntervalTicks = 1 },
                        new SpawnGroup { EnemyId = small.Id, RouteId = config.Routes[0].Id, Count = 2, StartTick = 4000, IntervalTicks = 1 }
                    }
                }
            });
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--nav-profile-late-only") >= 0)
            {
                var late = scenarios[scenarios.Count - 1];
                scenarios.Clear();
                scenarios.Add(late);
                scenarios.Add(new Scenario
                {
                    Name = late.Name + "-warm-replay",
                    Description = late.Description,
                    EnemyIds = late.EnemyIds,
                    ExpectedAttackers = late.ExpectedAttackers,
                    InitialLargeCount = late.InitialLargeCount,
                    LateStartTick = late.LateStartTick,
                    Wave = late.Wave
                });
            }

            result.authoredWaves = config.Waves.Length;
            result.expectedCases = scenarios.Count;
            foreach (var hub in UnityEngine.Object.FindObjectsOfType<PlayerHubView>())
            {
                hub.gameObject.SetActive(false);
            }
        }

        private static void AddSaturationScenario(FightConfiguration config, string name, int radius, int range, int count, int attackers)
        {
            var enemy = config.Enemies.Single(value => value.Radius == radius && value.AttackRange == range);
            scenarios.Add(new Scenario
            {
                Name = name,
                Description = "Stress override: single actual enemy profile and rapid spawn count; original map, radius, range and speed are unchanged.",
                EnemyIds = new[] { enemy.Id },
                ExpectedAttackers = attackers,
                Wave = new WaveDefinition
                {
                    Id = 1,
                    GiveCard = false,
                    Groups = new[] { new SpawnGroup { EnemyId = enemy.Id, RouteId = config.Routes[0].Id, Count = count, IntervalTicks = 1 } }
                }
            });
        }

        private static void CreateScenario()
        {
            var scenario = scenarios[scenarioIndex];
            var config = TowerDefenseTables.Create(tables, 1, grid);
            config.BaseHealth = 100000000;
            if (scenario.EnemyIds != null)
            {
                config.Enemies = config.Enemies.Where(enemy => scenario.EnemyIds.Contains(enemy.Id)).ToArray();
            }
            config.Waves = new[] { scenario.Wave };
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/GameAssets/Common/Fonts/GameUI.asset");
            var map = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/GameAssets/TowerDefense/Art/Map.png");
            var icons = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/GameAssets/Common/UI/Icons.png");
            var panel = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/GameAssets/Common/UI/Panel.png");
            Require(font != null && map != null && icons != null && panel != null, "Actual map/UI/TMP assets must be imported before native acceptance.");
            view = TowerDefensePrototypeView.Create(config, font, map, 64029);
            view.SetArt(map, icons, panel, font);
            // 探针自己逐tick驱动领域；反射只用于验收呈现，不添加业务专用验收API。
            view.enabled = false;
            var group = view.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            session = view.Session;
            current = new CaseResult
            {
                name = scenario.Name,
                setup = scenario.Description ?? "Actual authored wave profiles and group counts.",
                registeredEnemyIds = config.Enemies.Select(enemy => enemy.Id).ToArray(),
                expectedAttackers = scenario.ExpectedAttackers,
                expected = scenario.Wave.Groups.Sum(value => value.Count),
                initialHp = config.BaseHealth,
                minimumBodyGapMm = int.MaxValue,
                minimumContinuousBodyGapMm = int.MaxValue,
                minimumBaseSurfaceGapMm = int.MaxValue,
                trace = Path.GetFullPath(Path.Combine(Output, scenario.Name + ".csv"))
            };
            cases.Add(current);
            trace = new StreamWriter(current.trace, false);
            trace.WriteLine("tick,id,xMm,yMm,radiusMm,state,goalCell,attackSequence,baseHp");
            previous.Clear();
            settledTick = 0;
            settledHp = 0;
            settledAttackSequence = 0;
            session.SetPaused(false);
            Require(session.StartNextWave(), "Could not start " + scenario.Name);
            stage = "simulating-" + scenario.Name;
        }

        private static void Simulate()
        {
            for (int iteration = 0; iteration < 25; iteration++)
            {
                session.SetPaused(false);
                long nodesBefore = session.Navigation.TotalExpandedNodes;
                long gateBefore = session.Navigation.AdmissionGateExpandedCells;
                long recoveryBefore = session.Navigation.RecoveryExpandedCells;
                int gcBefore = GC.CollectionCount(0);
                long heapBefore = GC.GetTotalMemory(false);
                long cpuBefore = CurrentThreadCpuNanoseconds();
                long started = Stopwatch.GetTimestamp();
                try
                {
                    session.Step();
                }
                finally
                {
                    long elapsed = Stopwatch.GetTimestamp() - started;
                    current.stepStopwatchTicks += elapsed;
                    current.maximumStepStopwatchTicks = Math.Max(current.maximumStepStopwatchTicks, elapsed);
                    if (elapsed * 1000.0 / Stopwatch.Frequency >= 20)
                    {
                        current.slowSteps.Add(new SlowStep
                        {
                            tick = session.Tick,
                            wallMilliseconds = elapsed * 1000.0 / Stopwatch.Frequency,
                            threadCpuMilliseconds = (CurrentThreadCpuNanoseconds() - cpuBefore) / 1000000.0,
                            expanded = session.Navigation.TotalExpandedNodes - nodesBefore,
                            gateExpanded = session.Navigation.AdmissionGateExpandedCells - gateBefore,
                            recoveryExpanded = session.Navigation.RecoveryExpandedCells - recoveryBefore,
                            generationZeroCollections = GC.CollectionCount(0) - gcBefore,
                            managedHeapDeltaBytes = GC.GetTotalMemory(false) - heapBefore,
                            active = session.Enemies.Count,
                            pending = session.PendingSpawns,
                            recoveryActive = session.Navigation.RecoveryActive,
                            failure = session.Navigation.LastFailure.ToString()
                        });
                    }
                }

                started = Stopwatch.GetTimestamp();
                try
                {
                    ValidateTick();
                }
                finally
                {
                    long elapsed = Stopwatch.GetTimestamp() - started;
                    current.verificationStopwatchTicks += elapsed;
                    current.maximumVerificationStopwatchTicks = Math.Max(current.maximumVerificationStopwatchTicks, elapsed);
                }
                Require(session.Tick <= MaximumTicks, "Arrival deadline exceeded; " + Diagnostics());
                var scenario = scenarios[scenarioIndex];
                if (scenario.LateStartTick > 0 && session.Tick == scenario.LateStartTick - 1)
                {
                    Require(session.TotalSpawned == scenario.InitialLargeCount && session.Enemies.Count == scenario.InitialLargeCount
                        && session.Enemies.All(enemy => enemy.MovementState != NavigationAgentStatus.Navigating),
                        "Late-small stress must start from an actually settled full large-body crowd; " + Diagnostics());
                    current.initialLargeSettledBeforeLateArrival = true;
                }
                bool allSettled = session.TotalSpawned == current.expected && !session.Navigation.RecoveryActive
                    && session.Enemies.All(enemy => enemy.MovementState != NavigationAgentStatus.Navigating);
                if (!allSettled)
                {
                    settledTick = 0;
                }

                if (settledTick == 0 && allSettled)
                {
                    settledTick = session.Tick;
                    settledHp = session.BaseHp;
                    settledAttackSequence = session.Enemies.Sum(enemy => enemy.AttackSequence);
                    current.allArrivedTick = settledTick;
                }

                if (settledTick > 0 && session.Tick - settledTick >= HoldTicks)
                {
                    CompleteScenario();
                    return;
                }
            }

            SynchronizePresentation();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeTimespec
        {
            internal long Seconds;
            internal long Nanoseconds;
        }

#if UNITY_EDITOR_LINUX
        [DllImport("libc", EntryPoint = "clock_gettime")]
        private static extern int ReadClock(int clock, out NativeTimespec value);
#endif

        private static long CurrentThreadCpuNanoseconds()
        {
#if UNITY_EDITOR_LINUX
            if (ReadClock(3, out var value) == 0)
            {
                return value.Seconds * 1000000000L + value.Nanoseconds;
            }
#endif
            return 0;
        }

        private static void ValidateTick()
        {
            Require(session.Phase == BattlePhase.Fighting && session.BaseHp > 0, "No-tower acceptance must retain living enemies; " + Diagnostics());
            Require(session.Towers.Count == 0 && session.Kills == 0 && session.Leaks == 0, "No-tower acceptance must not remove enemies or build towers.");
            Require(
                session.Enemies.Count == session.TotalSpawned && session.Navigation.Agents.Count == session.TotalSpawned,
                "An enemy disappeared before arrival.");
            current.checkedTicks++;
            var agents = session.Navigation.Agents.ToDictionary(agent => agent.Id);
            var starts = new Dictionary<int, Position2>();
            foreach (var enemy in session.Enemies)
            {
                var definition = session.Configuration.Enemies.Single(value => value.Id == enemy.DefinitionId);
                ValidateGeometry(enemy);
                Position2 start;
                if (previous.TryGetValue(enemy.Id, out var old))
                {
                    start = old.Position;
                    Require(enemy.AttackSequence >= old.AttackSequence, "Attack sequence moved backwards.");
                    if (old.AttackSequence != enemy.AttackSequence)
                    {
                        Require(enemy.MovementState == NavigationAgentStatus.Attacking, "An enemy damaged the base outside its valid attack slot.");
                    }
                }
                else
                {
                    var route = session.Configuration.Routes.Single(value => value.Id == enemy.RouteId);
                    start = grid.Center(grid.CellAt(route.Points[0]));
                    current.checkedSpawnSegments++;
                }

                starts.Add(enemy.Id, start);
                ValidateStaticSegment(start, enemy.Position, enemy.Radius);
                Require(
                    DistanceSquared(start, enemy.Position) <= (long)(definition.SpeedPerTick + 1) * (definition.SpeedPerTick + 1),
                    "Movement exceeded configured speed, including birth: enemy " + enemy.Id + "; " + Diagnostics());
                long clearance = grid.TargetRadius + enemy.Radius;
                double baseSquare = MinimumRelativeDistanceSquared(start, enemy.Position, grid.TargetCenter, grid.TargetCenter);
                Require(baseSquare + .000001 >= clearance * clearance, "A moving body crossed the base between ticks; " + Diagnostics());
                current.minimumBaseSurfaceGapMm = Math.Min(current.minimumBaseSurfaceGapMm, Math.Sqrt(baseSquare) - clearance);
                current.checkedBaseSegments++;

                var agent = agents[enemy.Id];
                Require(enemy.Position.X == agent.Position.X && enemy.Position.Y == agent.Position.Y && enemy.MovementState == agent.Status,
                    "Presentation snapshot differs from actual navigation state.");
                if (enemy.MovementState != NavigationAgentStatus.Navigating)
                {
                    var goal = grid.Center(agent.GoalCell);
                    Require(enemy.Position.X == goal.X && enemy.Position.Y == goal.Y, "A supposedly settled unit has not reached its reserved goal centre.");
                }

                if (enemy.MovementState == NavigationAgentStatus.Attacking)
                {
                    Require(
                        grid.HasAttackLine(grid.CellAt(enemy.Position), enemy.Radius, definition.AttackRange),
                        "An attacking enemy has no valid reach or sight.");
                }
                else if (enemy.MovementState == NavigationAgentStatus.Waiting)
                {
                    long distance = grid.TargetRadius + session.Navigation.GatheringDistance;
                    Require(grid.HoldingAllowed(agent.GoalCell) && DistanceSquared(enemy.Position, grid.TargetCenter) <= distance * distance,
                        "A remote stalled enemy was incorrectly counted as gathered.");
                }

                if (session.Tick % 20 == 0)
                {
                    trace.WriteLine(session.Tick + "," + enemy.Id + "," + enemy.Position.X + "," + enemy.Position.Y + "," + enemy.Radius + ","
                        + enemy.MovementState + "," + agent.GoalCell + "," + enemy.AttackSequence + "," + session.BaseHp);
                }
            }

            for (int i = 0; i < session.Enemies.Count; i++)
            {
                var first = session.Enemies[i];
                for (int j = i + 1; j < session.Enemies.Count; j++)
                {
                    var second = session.Enemies[j];
                    long radius = first.Radius + second.Radius;
                    long square = DistanceSquared(first.Position, second.Position);
                    Require(square >= radius * radius, "Physical bodies overlapped: " + first.Id + "/" + second.Id + "; " + Diagnostics());
                    current.minimumBodyGapMm = Math.Min(current.minimumBodyGapMm, (int)Math.Sqrt(square) - (int)radius);
                    double closest = MinimumRelativeDistanceSquared(starts[first.Id], first.Position, starts[second.Id], second.Position);
                    Require(
                        closest + .000001 >= radius * radius,
                        "Moving bodies overlapped between ticks: " + first.Id + "/" + second.Id + "; " + Diagnostics());
                    current.minimumContinuousBodyGapMm = Math.Min(current.minimumContinuousBodyGapMm, Math.Sqrt(closest) - radius);
                    current.checkedRelativeSegments++;
                }
            }

            previous.Clear();
            foreach (var enemy in session.Enemies)
            {
                previous.Add(enemy.Id, enemy);
            }
        }

        private static void ValidateStaticSegment(Position2 start, Position2 end, int radius)
        {
            Require(start.X == end.X || start.Y == end.Y, "The four-neighbour domain produced an unverified diagonal movement.");
            long minimumX = Math.Min(start.X, end.X);
            long maximumX = Math.Max(start.X, end.X);
            long minimumY = Math.Min(start.Y, end.Y);
            long maximumY = Math.Max(start.Y, end.Y);
            int firstX = Math.Max(0, (int)((minimumX - radius - grid.Origin.X) / grid.CellSize));
            int lastX = Math.Min(grid.Width - 1, (int)((maximumX + radius - grid.Origin.X) / grid.CellSize));
            int firstY = Math.Max(0, (int)((minimumY - radius - grid.Origin.Y) / grid.CellSize));
            int lastY = Math.Min(grid.Height - 1, (int)((maximumY + radius - grid.Origin.Y) / grid.CellSize));
            for (int y = firstY; y <= lastY; y++)
            {
                for (int x = firstX; x <= lastX; x++)
                {
                    if (grid.IsWalkable(y * grid.Width + x))
                    {
                        continue;
                    }

                    long left = grid.Origin.X + (long)x * grid.CellSize;
                    long bottom = grid.Origin.Y + (long)y * grid.CellSize;
                    // One segment dimension is zero; rectangle-to-rectangle separation is its exact swept distance.
                    long dx = Math.Max(0, Math.Max(left - maximumX, minimumX - (left + grid.CellSize)));
                    long dy = Math.Max(0, Math.Max(bottom - maximumY, minimumY - (bottom + grid.CellSize)));
                    Require(dx * dx + dy * dy >= (long)radius * radius, "A moving body crossed blocked terrain; " + Diagnostics());
                    current.checkedStaticSegments++;
                }
            }
        }

        private static void ValidateGeometry(EnemySnapshot enemy)
        {
            var point = enemy.Position;
            long radius = enemy.Radius;
            Require(radius > 0 && grid.CellAt(point) >= 0, "An enemy left the logical map.");
            long targetClearance = grid.TargetRadius + radius;
            Require(DistanceSquared(point, grid.TargetCenter) >= targetClearance * targetClearance, "An enemy body penetrated the base.");
            Require(point.X - radius >= grid.Origin.X && point.Y - radius >= grid.Origin.Y
                && point.X + radius <= grid.Origin.X + (long)grid.Width * grid.CellSize
                && point.Y + radius <= grid.Origin.Y + (long)grid.Height * grid.CellSize, "An enemy body crossed the map boundary.");
            int center = grid.CellAt(point);
            int centerX = center % grid.Width;
            int centerY = center / grid.Width;
            for (int y = Math.Max(0, centerY - 1); y <= Math.Min(grid.Height - 1, centerY + 1); y++)
            {
                for (int x = Math.Max(0, centerX - 1); x <= Math.Min(grid.Width - 1, centerX + 1); x++)
                {
                    if (grid.IsWalkable(y * grid.Width + x))
                    {
                        continue;
                    }

                    long left = grid.Origin.X + (long)x * grid.CellSize;
                    long bottom = grid.Origin.Y + (long)y * grid.CellSize;
                    long closestX = Math.Max(left, Math.Min(left + grid.CellSize, point.X));
                    long closestY = Math.Max(bottom, Math.Min(bottom + grid.CellSize, point.Y));
                    long dx = point.X - closestX;
                    long dy = point.Y - closestY;
                    Require(dx * dx + dy * dy >= radius * radius, "An enemy body penetrated blocked terrain at " + x + "," + y + "; " + Diagnostics());
                }
            }
        }

        private static void CompleteScenario()
        {
            Require(session.TotalSpawned == current.expected && session.Enemies.Count == current.expected, "Not every authored enemy arrived alive.");
            Require(
                session.Enemies.All(enemy => enemy.MovementState != NavigationAgentStatus.Navigating),
                "A unit resumed navigation during the settled hold.");
            Require(session.BaseHp < settledHp && session.Enemies.Sum(enemy => enemy.AttackSequence) > settledAttackSequence,
                "Settled enemies did not continue attacking during the 200-tick hold.");
            current.attacking = session.Enemies.Count(enemy => enemy.MovementState == NavigationAgentStatus.Attacking);
            current.waiting = session.Enemies.Count(enemy => enemy.MovementState == NavigationAgentStatus.Waiting);
            Require(current.attacking > 0, "No physical attacker reached the base.");
            if (current.expectedAttackers > 0)
            {
                Require(current.attacking == current.expectedAttackers, "Not all independently witnessed attack positions are filled; " + Diagnostics());
            }

            if (scenarios[scenarioIndex].LateStartTick > 0)
            {
                Require(current.initialLargeSettledBeforeLateArrival, "Late-arrival prerequisite was not checked.");
            }
            var coverage = new bool[4];
            foreach (var enemy in session.Enemies)
            {
                long dx = enemy.Position.X - grid.TargetCenter.X;
                long dy = enemy.Position.Y - grid.TargetCenter.Y;
                int side = Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? 0 : 2) : (dy >= 0 ? 1 : 3);
                coverage[side] = true;
            }

            current.coverageEastNorthWestSouth = coverage;
            if (current.expected >= 30)
            {
                Require(coverage.All(value => value), "A large wave did not physically gather on all four sides of the base; " + Diagnostics());
            }

            current.success = true;
            current.finalTick = session.Tick;
            current.holdTicks = session.Tick - settledTick;
            current.finalHp = session.BaseHp;
            current.damageDuringHold = settledHp - session.BaseHp;
            current.spawnBlockedTicks = session.SpawnBlockedTicks;
            current.rejectedSpawnAttempts = session.Navigation.RejectedSpawns;
            current.totalExpandedNodes = session.Navigation.TotalExpandedNodes;
            current.finalEnemies = SnapshotEnemies();
            trace.Dispose();
            trace = null;
            SynchronizePresentation();
            view.SetNavigationDebug(false);
            Capture(current.name + "-settled", () =>
            {
                view.SetNavigationDebug(true);
                SynchronizePresentation();
                Capture(current.name + "-logic", NextScenario);
            });
        }

        private static void SynchronizePresentation()
        {
            long started = Stopwatch.GetTimestamp();
            try
            {
                synchronize.Invoke(view, new object[] { .05f });
                refresh.Invoke(view, null);
                gameView.Repaint();
            }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                current.presentationStopwatchTicks += elapsed;
                current.maximumPresentationStopwatchTicks = Math.Max(current.maximumPresentationStopwatchTicks, elapsed);
            }
        }

        private static EnemyResult[] SnapshotEnemies()
        {
            if (session == null)
            {
                return Array.Empty<EnemyResult>();
            }

            return session.Enemies.Select(enemy => new EnemyResult
            {
                id = enemy.Id,
                definition = enemy.DefinitionId,
                x = enemy.Position.X,
                y = enemy.Position.Y,
                radius = enemy.Radius,
                state = enemy.MovementState.ToString(),
                goalCell = session.Navigation.Agents.Single(agent => agent.Id == enemy.Id).GoalCell,
                attackSequence = enemy.AttackSequence
            }).ToArray();
        }

        private static string Diagnostics()
        {
            return current?.name + " tick=" + session?.Tick + " spawned=" + session?.TotalSpawned + "/" + current?.expected
                + " live=" + session?.Enemies.Count + " navigation=" + session?.Navigation.LastFailure;
        }

        private static void NextScenario()
        {
            view.Dispose();
            view = null;
            session = null;
            scenarioIndex++;
            if (scenarioIndex == scenarios.Count)
            {
                Finish(true, "All authored and stress cases gathered alive without towers and attacked for 200 uninterrupted ticks.");
                return;
            }

            current = null;
            stage = "between-cases";
            destroyFrame = Time.frameCount;
        }

        private static void Capture(string name, Action continuation)
        {
            stage = name;
            pendingShot = new Shot { stage = name, path = Path.GetFullPath(Path.Combine(Output, name + ".png")) };
            if (File.Exists(pendingShot.path))
            {
                File.Delete(pendingShot.path);
            }

            captureStarted = EditorApplication.timeSinceStartup;
            captureFrame = Time.frameCount;
            captureRenderFrame = Time.renderedFrameCount;
            captureIssued = false;
            afterCapture = continuation;
        }

        private static void PollCapture()
        {
            Require(EditorApplication.timeSinceStartup - captureStarted < 20, "Screenshot timed out: " + pendingShot.stage);
            if (EditorApplication.timeSinceStartup - captureStarted < .35 || Time.frameCount - captureFrame < 3
                || Time.renderedFrameCount - captureRenderFrame < 3)
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
                requestedRenderFrame = Time.renderedFrameCount;
                return;
            }

            if (EditorApplication.timeSinceStartup - captureRequested < .2 || Time.renderedFrameCount - requestedRenderFrame < 2
                || !File.Exists(pendingShot.path) || new FileInfo(pendingShot.path).Length < 1000)
            {
                return;
            }

            byte[] png = File.ReadAllBytes(pendingShot.path);
            Require(png.Length > 32 && png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71, "Captured file is not PNG.");
            pendingShot.width = BigEndian(png, 16);
            pendingShot.height = BigEndian(png, 20);
            Require(pendingShot.width >= 320 && pendingShot.height >= 180, "Captured image is too small.");
            pendingShot.captured = true;
            shots.Add(pendingShot);
            pendingShot = null;
            var continuation = afterCapture;
            afterCapture = null;
            continuation();
        }

        private static int BigEndian(byte[] bytes, int offset)
        {
            return bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
        }

        private static long DistanceSquared(Position2 first, Position2 second)
        {
            long dx = (long)first.X - second.X;
            long dy = (long)first.Y - second.Y;
            return dx * dx + dy * dy;
        }

        /// <summary>独立连续圆盘oracle：在同一tick的归一化时间内，求相对运动线段到原点的最近点。</summary>
        private static double MinimumRelativeDistanceSquared(Position2 firstStart, Position2 firstEnd, Position2 secondStart, Position2 secondEnd)
        {
            double x = (long)firstStart.X - secondStart.X;
            double y = (long)firstStart.Y - secondStart.Y;
            double dx = ((long)firstEnd.X - firstStart.X) - ((long)secondEnd.X - secondStart.X);
            double dy = ((long)firstEnd.Y - firstStart.Y) - ((long)secondEnd.Y - secondStart.Y);
            double velocitySquare = dx * dx + dy * dy;
            double time = velocitySquare == 0 ? 0 : Math.Max(0, Math.Min(1, -(x * dx + y * dy) / velocitySquare));
            x += dx * time;
            y += dy * time;
            return x * x + y * y;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message ?? "Native navigation validation failed.");
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
            result.renderingValidated = scenarios.Count > 0 && shots.Count == scenarios.Count * 2 && shots.All(shot => shot.captured);
            if (success && (!result.renderingValidated || cases.Count != scenarios.Count || cases.Any(value => !value.success)))
            {
                result.success = false;
                result.detail += " The complete scenario/screenshot set is required.";
            }

            if (current != null && !success)
            {
                current.success = false;
                current.detail = detail;
                current.finalTick = session?.Tick ?? 0;
                current.finalEnemies = SnapshotEnemies();
            }

            trace?.Dispose();
            trace = null;
            view?.Dispose();
            view = null;
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
                result.shutdownVerified = UnityEngine.Object.FindObjectsOfType<TowerDefensePrototypeView>(true).Length == 0;
                if (!result.shutdownVerified || failure != null)
                {
                    result.success = false;
                    result.detail += " Shutdown or Unity logged error: " + failure;
                }

                Exit();
            }
        }

        private static void Exit()
        {
            EditorApplication.update -= Poll;
            EditorApplication.playModeStateChanged -= OnPlayState;
            Application.logMessageReceived -= CaptureError;
            EditorSettings.enterPlayModeOptionsEnabled = previousOptionsEnabled;
            EditorSettings.enterPlayModeOptions = previousOptions;
            result.cases = cases.ToArray();
            result.screenshots = shots.ToArray();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Path.Combine(Output, "validation.json"), JsonUtility.ToJson(result, true));
            EditorApplication.Exit(result.success ? 0 : 1);
        }

        private sealed class Scenario
        {
            internal string Name;
            internal WaveDefinition Wave;
            internal string Description;
            internal int[] EnemyIds;
            internal int ExpectedAttackers;
            internal int InitialLargeCount;
            internal int LateStartTick;
        }

        [Serializable]
        private sealed class Result
        {
            public bool success;
            public string unity;
            public string stage;
            public string detail;
            public string graphicsDevice;
            public string renderPipeline;
            public string assetSource;
            public string navigationSha256;
            public string configurationVersion;
            public int tickMilliseconds;
            public long stopwatchFrequency;
            public string timingScope;
            public int gridWidth;
            public int gridHeight;
            public int cellMillimetres;
            public int targetRadius;
            public int authoredWaves;
            public int expectedCases;
            public bool renderingValidated;
            public bool shutdownVerified;
            public CaseResult[] cases;
            public Shot[] screenshots;
        }

        [Serializable]
        private sealed class CaseResult
        {
            public string name;
            public bool success;
            public string detail;
            public int expected;
            public string setup;
            public int[] registeredEnemyIds;
            public int expectedAttackers;
            public bool initialLargeSettledBeforeLateArrival;
            public int attacking;
            public int waiting;
            public long allArrivedTick;
            public long finalTick;
            public long checkedTicks;
            public long holdTicks;
            public int initialHp;
            public int finalHp;
            public int damageDuringHold;
            public int minimumBodyGapMm;
            public double minimumContinuousBodyGapMm;
            public double minimumBaseSurfaceGapMm;
            public long checkedSpawnSegments;
            public long checkedBaseSegments;
            public long checkedStaticSegments;
            public long checkedRelativeSegments;
            public long stepStopwatchTicks;
            public long maximumStepStopwatchTicks;
            public long verificationStopwatchTicks;
            public long maximumVerificationStopwatchTicks;
            public long presentationStopwatchTicks;
            public long maximumPresentationStopwatchTicks;
            public bool[] coverageEastNorthWestSouth;
            public long spawnBlockedTicks;
            public int rejectedSpawnAttempts;
            public long totalExpandedNodes;
            public string trace;
            public EnemyResult[] finalEnemies;
            public List<SlowStep> slowSteps = new List<SlowStep>();
        }

        [Serializable]
        private sealed class SlowStep
        {
            public long tick;
            public double wallMilliseconds;
            public double threadCpuMilliseconds;
            public long expanded;
            public long gateExpanded;
            public long recoveryExpanded;
            public int generationZeroCollections;
            public long managedHeapDeltaBytes;
            public int active;
            public int pending;
            public bool recoveryActive;
            public string failure;
        }

        [Serializable]
        private sealed class EnemyResult
        {
            public int id;
            public int definition;
            public int x;
            public int y;
            public int radius;
            public string state;
            public int goalCell;
            public int attackSequence;
        }

        [Serializable]
        private sealed class Shot
        {
            public string stage;
            public string path;
            public bool captured;
            public int width;
            public int height;
        }
    }
}
