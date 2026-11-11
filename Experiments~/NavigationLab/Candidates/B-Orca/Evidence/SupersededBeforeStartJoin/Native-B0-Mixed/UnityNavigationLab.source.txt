using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using NavigationLab.Core;
using NavigationLab.Oracle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace NavigationLab.UnityValidation
{
    [InitializeOnLoad]
    public static class UnityNavigationLab
    {
        private const string Session = "NavigationLab.Native.Session";
        private static Manifest manifest;
        private static NativeReport report;
        private static Input input;
        private static NativeCase current;
        private static INavigator navigator;
        private static ContinuousOracle oracle;
        private static List<Request> pending;
        private static HashSet<int> worldEvents;
        private static HashSet<int> deaths;
        private static readonly Dictionary<int, GameObject> views = new Dictionary<int, GameObject>();
        private static readonly Dictionary<int, GameObject> targetViews = new Dictionary<int, GameObject>();
        private static Texture2D groundTexture;
        private static WorldDefinition viewedWorld;
        private static readonly List<Object> resources = new List<Object>();
        private static GameObject sceneRoot;
        private static Camera camera;
        private static int nextRun;
        private static int microstep;
        private static int stable;
        private static int nextId;
        private static int issued;
        private static HashSet<int> oldWaiting;
        private static int stableStart = -1;
        private static Dictionary<int, UnitSnapshot> stableAnchor;
        private static double deadline;
        private static int stoppedFrames;
        private static RenderTexture captureTarget;
        private static int captureFrame;
        private static string output;

        static UnityNavigationLab()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(Session, "")))
            {
                EditorApplication.update += Poll;
            }
        }

        public static void Run()
        {
            string file = Argument("--navlab-manifest");
            if (string.IsNullOrEmpty(file))
            {
                throw new ArgumentException("Expected --navlab-manifest.");
            }
            SessionState.EraseString(Session + ".finishing");
            SessionState.SetString(Session, Path.GetFullPath(file));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
            EditorApplication.EnterPlaymode();
        }

        private static void Poll()
        {
            try
            {
                if (SessionState.GetString(Session + ".finishing", "") == "yes")
                {
                    if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                    {
                        if (++stoppedFrames < 3)
                        {
                            return;
                        }
                        FinishShutdown();
                    }
                    return;
                }
                if (!EditorApplication.isPlaying)
                {
                    return;
                }
                if (manifest == null)
                {
                    manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(SessionState.GetString(Session, "")));
                    output = manifest.output;
                    Directory.CreateDirectory(output);
                    report = new NativeReport
                    {
                        runId = manifest.runId,
                        unity = Application.unityVersion,
                        graphics = SystemInfo.graphicsDeviceType.ToString(),
                        device = SystemInfo.graphicsDeviceName,
                        manifestSha256 = Hash(SessionState.GetString(Session, "")),
                        oracleSha256 = Hash(typeof(ContinuousOracle).Assembly.Location),
                        coreSha256 = Hash(typeof(INavigator).Assembly.Location)
                    };
                    Require(report.coreSha256 == manifest.expectedCoreSha256 && report.oracleSha256 == manifest.expectedOracleSha256,
                        "Loaded Core/Oracle DLL differs from the frozen build.");
                    deadline = EditorApplication.timeSinceStartup + manifest.wallDeadlineSeconds;
                    StartNext();
                    return;
                }
                if (captureTarget != null)
                {
                    if (Time.frameCount >= captureFrame + 2)
                    {
                        FinishCapture();
                    }
                    return;
                }
                Require(EditorApplication.timeSinceStartup <= deadline, "Native laboratory wall deadline exceeded.");
                for (int iteration = 0; iteration < manifest.stepsPerEditorUpdate && navigator != null && captureTarget == null; iteration++)
                {
                    Advance();
                }
                UpdateViews();
            }
            catch (Exception error)
            {
                if (current != null)
                {
                    CompleteCurrent(false, error.ToString());
                }
                else
                {
                    Debug.LogException(error);
                    BeginShutdown();
                }
            }
        }

        private static void StartNext()
        {
            CleanupCurrent();
            if (nextRun >= manifest.runs.Length)
            {
                BeginShutdown();
                return;
            }
            var run = manifest.runs[nextRun++];
            input = JsonUtility.FromJson<Input>(File.ReadAllText(run.input));
            current = new NativeCase
            {
                name = run.name,
                inputSha256 = Hash(run.input),
                modelInputSha256 = input.modelInputSha256,
                candidateSha256 = run.expectedCandidateSha256,
                factoryType = run.factory,
                originalRequests = input.requests.Length
            };
            report.cases.Add(current);
            var assembly = AppDomain.CurrentDomain.GetAssemblies().Single(item => item.GetName().Name == run.assemblyName);
            var factory = (INavigationFactory)Activator.CreateInstance(assembly.GetType(run.factory, true));
            current.factoryId = factory.Id;
            Require(Hash(run.assemblyPath) == current.candidateSha256 && Hash(assembly.Location) == current.candidateSha256, "Loaded assembly differs from staged candidate DLL.");
            var required = (NavigationFeatures)Enum.Parse(typeof(NavigationFeatures), input.requiredFeatures);
            Require((required & ~factory.Features) == NavigationFeatures.None, "Selected native case has unsupported capabilities.");
            var world = input.world.ToWorld();
            oracle = new ContinuousOracle(world);
            navigator = factory.Create(world.Clone(), input.requests.Select(request => request.Profile()).Distinct().ToArray());
            Require(navigator.Snapshot().Count == 0, "Factory created unrequested bodies.");
            pending = input.requests.ToList();
            nextId = pending.Max(request => request.id) + 1;
            issued = pending.Count;
            oldWaiting = new HashSet<int>();
            worldEvents = new HashSet<int>();
            deaths = new HashSet<int>();
            microstep = 0;
            stable = 0;
            stableStart = -1;
            stableAnchor = null;
            CreateViews(world);
        }

        private static void Advance()
        {
            int tick = microstep / SimulationProtocol.Substeps;
            var snapshot = navigator.Snapshot().ToArray();
            if (microstep % SimulationProtocol.Substeps == 0)
            {
                for (int index = 0; index < input.worldEvents.Length; index++)
                {
                    var planned = input.worldEvents[index];
                    if (tick >= planned.tick && (!planned.afterSettlement || IsComplete(snapshot, true)) && worldEvents.Add(index))
                    {
                        var before = navigator.Snapshot().ToArray();
                        var change = planned.Change();
                        oracle.ValidateWorldChangeInput(change, before);
                        Require(navigator.ApplyWorldChange(change.DeepClone()), "Advertised world change rejected.");
                        snapshot = navigator.Snapshot().ToArray();
                        oracle.ApplyChange(change, before, snapshot);
                        ResetStability();
                    }
                }
                bool settled = IsComplete(snapshot, true);
                for (int index = 0; index < input.deaths.Length; index++)
                {
                    var planned = input.deaths[index];
                    if (deaths.Contains(index) || tick < planned.tick || planned.afterSettlement && !settled)
                    {
                        continue;
                    }
                    var victims = snapshot.Where(unit => !planned.attackersOnly || unit.State == MotionState.Attacking)
                        .Where(unit => planned.range < 0 || oracle.Live[unit.Id].Profile.AttackRangeMm == planned.range)
                        .Where(unit => !planned.requireMoving || unit.State == MotionState.Moving
                            && Point2.Distance(unit.Position, oracle.World.Center(oracle.World.CellAt(unit.Position))) > .1)
                        .OrderBy(unit => unit.Id).Take(planned.count).ToArray();
                    if (victims.Length != planned.count)
                    {
                        Require(planned.afterSettlement || tick <= planned.tick + 100, "Noncentre moving death never became valid.");
                        continue;
                    }
                    deaths.Add(index);
                    oldWaiting.UnionWith(snapshot.Where(unit => unit.State == MotionState.Waiting).Select(unit => unit.Id));
                    foreach (var victim in victims)
                    {
                        var request = oracle.Live[victim.Id];
                        var before = navigator.Snapshot().ToArray();
                        Require(navigator.Remove(victim.Id), "Explicit live removal failed.");
                        snapshot = navigator.Snapshot().ToArray();
                        oracle.RegisterDeath(victim.Id, before, snapshot);
                        oracle.ValidateTransition(snapshot, snapshot, 0);
                        current.deaths++;
                        if (planned.replace)
                        {
                            pending.Add(Request.Replacement(nextId++, tick, request));
                            issued++;
                        }
                    }
                    ResetStability();
                }
                var tried = new HashSet<int>();
                foreach (var request in pending.Where(request => request.readyTick <= tick).ToArray())
                {
                    if (!tried.Add(request.spawnCell))
                    {
                        continue;
                    }
                    var before = navigator.Snapshot().ToArray();
                    bool admitted = navigator.TrySpawn(request.Command());
                    snapshot = navigator.Snapshot().ToArray();
                    if (admitted)
                    {
                        oracle.RegisterBirth(request.Command(), before, snapshot);
                        pending.Remove(request);
                        current.admitted++;
                    }
                    else
                    {
                        oracle.VerifyRejectedBirth(before, snapshot);
                        oracle.ValidateTransition(snapshot, snapshot, 0);
                        current.rejected++;
                    }
                }
                if (tick == input.initialSettledCheckTick)
                {
                    Require(snapshot.Length == input.requests.Count(request => request.readyTick <= tick)
                        && snapshot.All(unit => unit.State != MotionState.Moving), "Initial crowd did not settle before late requests.");
                    current.initialBatchVerified = true;
                }
                bool complete = IsComplete(snapshot) && deaths.Count == input.deaths.Length && worldEvents.Count == input.worldEvents.Length
                    && (input.initialSettledCheckTick < 0 || current.initialBatchVerified);
                if (complete && stableStart < 0)
                {
                    stableStart = microstep;
                    stableAnchor = snapshot.ToDictionary(unit => unit.Id);
                    current.holdMaximumDriftMm = 0;
                }
                if (!complete)
                {
                    ResetStability();
                }
                stable = stableStart < 0 ? 0 : (microstep - stableStart) / SimulationProtocol.Substeps;
                if (stable >= input.holdTicks)
                {
                    CompleteCurrent(true, "Complete, continuously checked stationary-task residency.");
                    return;
                }
            }
            Require(microstep <= input.deadlineTicks * SimulationProtocol.Substeps, "Arrival deadline: live=" + snapshot.Length + " pending=" + pending.Count
                + " moving=" + snapshot.Count(unit => unit.State == MotionState.Moving) + " failure=" + navigator.LastSpawnFailure);
            var previous = navigator.Snapshot().ToArray();
            long started = Stopwatch.GetTimestamp();
            navigator.Step();
            long elapsed = Stopwatch.GetTimestamp() - started;
            current.maximumStepStopwatchTicks = Math.Max(current.maximumStepStopwatchTicks, elapsed);
            current.stepStopwatchTicks += elapsed;
            snapshot = navigator.Snapshot().ToArray();
            oracle.ValidateTransition(previous, snapshot, SimulationProtocol.MicrostepMilliseconds);
            if (stableAnchor != null)
            {
                bool unchanged = snapshot.Length == stableAnchor.Count && snapshot.All(unit => stableAnchor.ContainsKey(unit.Id)
                    && unit.State != MotionState.Moving && unit.GoalCell == stableAnchor[unit.Id].GoalCell);
                double drift = unchanged && snapshot.Length > 0 ? snapshot.Max(unit => Point2.Distance(unit.Position, stableAnchor[unit.Id].Position)) : 0;
                current.holdMaximumDriftMm = Math.Max(current.holdMaximumDriftMm, drift);
                if (!unchanged || drift > SimulationProtocol.GeometryToleranceMm)
                {
                    ResetStability();
                }
            }
            current.checkedMicrosteps++;
            microstep++;
        }

        private static void ResetStability()
        {
            stable = 0;
            stableStart = -1;
            stableAnchor = null;
        }

        private static bool IsComplete(UnitSnapshot[] snapshot, bool beforeLifecycle = false)
        {
            int expected = beforeLifecycle && input.expectation == "FullArrival" ? issued - current.deaths : input.expectedLive;
            if (snapshot.Length != expected || snapshot.Any(unit => unit.State == MotionState.Moving))
            {
                return false;
            }
            if (input.expectation == "FullArrival" && pending.Count != 0)
            {
                return false;
            }
            if (input.expectation == "StaticUnreachable" && (snapshot.Length != 0 || pending.Count != input.requests.Length))
            {
                return false;
            }
            if (input.expectedAttackers >= 0 && snapshot.Count(unit => unit.State == MotionState.Attacking) != input.expectedAttackers)
            {
                return false;
            }
            if (!beforeLifecycle && snapshot.Count(unit => unit.State == MotionState.Attacking && oldWaiting.Contains(unit.Id)) < input.minimumOldWaitingPromotions)
            {
                return false;
            }
            return input.minimumAttackers.All(item => snapshot.Count(unit => unit.State == MotionState.Attacking
                && oracle.Live[unit.Id].Profile.AttackRangeMm == item.range) >= item.count);
        }

        private static void CompleteCurrent(bool passed, string detail)
        {
            var snapshot = navigator == null ? Array.Empty<UnitSnapshot>() : navigator.Snapshot().ToArray();
            current.success = passed;
            current.detail = detail;
            current.finalTick = microstep / SimulationProtocol.Substeps;
            current.holdTicks = stable;
            current.live = snapshot.Length;
            current.pending = pending == null ? 0 : pending.Count;
            current.attacking = snapshot.Count(unit => unit.State == MotionState.Attacking);
            current.waiting = snapshot.Count(unit => unit.State == MotionState.Waiting);
            current.pairSweeps = oracle == null ? 0 : oracle.PairSweeps;
            current.bodySweeps = oracle == null ? 0 : oracle.BodySweeps;
            current.minimumGapMm = oracle != null && !double.IsInfinity(oracle.MinimumBodyGapMm)
                ? oracle.MinimumBodyGapMm.ToString("R", CultureInfo.InvariantCulture) : "not-observed";
            current.metrics = navigator == null ? Array.Empty<Metric>()
                : navigator.Metrics.Select(pair => new Metric { name = pair.Key, value = pair.Value }).ToArray();
            UpdateViews();
            if (camera != null && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                captureTarget = new RenderTexture(1440, 900, 24);
                captureTarget.Create();
                camera.targetTexture = captureTarget;
                captureFrame = Time.frameCount;
            }
            else
            {
                WriteCaseAndNext();
            }
        }

        private static void CreateViews(WorldDefinition world)
        {
            sceneRoot = new GameObject("NAVLAB_RUNTIME");
            double width = world.Width * world.CellSizeMm * .001;
            double height = world.Height * world.CellSizeMm * .001;
            var ground = new GameObject("NAVLAB_GROUND", typeof(MeshFilter), typeof(MeshRenderer));
            ground.transform.SetParent(sceneRoot.transform);
            ground.transform.position = new Vector3((float)(world.Origin.X * .001), -.02f, (float)(world.Origin.Y * .001));
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, new Vector3((float)width, 0, 0), new Vector3((float)width, 0, (float)height), new Vector3(0, 0, (float)height) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up }
            };
            mesh.RecalculateNormals();
            ground.GetComponent<MeshFilter>().sharedMesh = mesh;
            resources.Add(mesh);
            Paint(ground, Color.white);
            groundTexture = new Texture2D(world.Width, world.Height, TextureFormat.RGB24, false) { filterMode = FilterMode.Point };
            ground.GetComponent<Renderer>().sharedMaterial.mainTexture = groundTexture;
            resources.Add(groundTexture);
            RefreshWorldView(world);
            foreach (var target in world.Targets)
            {
                var baseView = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseView.transform.SetParent(sceneRoot.transform);
                baseView.transform.position = Position(target.Center) + Vector3.up * .4f;
                baseView.transform.localScale = new Vector3(target.RadiusMm * .002f, .4f, target.RadiusMm * .002f);
                Paint(baseView, new Color(.22f, .37f, .64f));
                targetViews.Add(target.Id, baseView);
            }
            var cameraObject = new GameObject("NAVLAB_CAMERA");
            cameraObject.transform.SetParent(sceneRoot.transform);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.aspect = 1.6f;
            camera.orthographicSize = (float)Math.Max(height / 2, width / (2 * camera.aspect)) * 1.1f;
            camera.transform.position = ground.transform.position + new Vector3((float)(width / 2), 60, (float)(height / 2));
            camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .11f, .12f);
        }

        private static void UpdateViews()
        {
            if (navigator == null || sceneRoot == null)
            {
                return;
            }
            RefreshWorldView(oracle.World);
            foreach (var target in oracle.World.Targets)
            {
                var targetView = targetViews[target.Id];
                targetView.transform.position = Position(target.Center) + Vector3.up * .4f;
                targetView.transform.localScale = new Vector3(target.RadiusMm * .002f, .4f, target.RadiusMm * .002f);
            }
            var snapshot = navigator.Snapshot().ToArray();
            foreach (int removed in views.Keys.Except(snapshot.Select(unit => unit.Id)).ToArray())
            {
                Object.DestroyImmediate(views[removed]);
                views.Remove(removed);
            }
            foreach (var unit in snapshot)
            {
                if (!views.TryGetValue(unit.Id, out var view))
                {
                    view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    view.transform.SetParent(sceneRoot.transform);
                    int radius = oracle.Live[unit.Id].Profile.RadiusMm;
                    view.transform.localScale = new Vector3(radius * .002f, radius * .003f, radius * .002f);
                    Paint(view, new Color(.87f, .71f, .28f));
                    views.Add(unit.Id, view);
                }
                view.transform.position = Position(unit.Position) + Vector3.up * (view.transform.localScale.y / 2);
                view.GetComponent<Renderer>().sharedMaterial.color = unit.State == MotionState.Moving ? new Color(.23f, .71f, .83f)
                    : unit.State == MotionState.Attacking ? new Color(.9f, .28f, .22f) : new Color(.9f, .75f, .25f);
            }
        }

        private static void RefreshWorldView(WorldDefinition world)
        {
            if (viewedWorld == world)
            {
                return;
            }
            viewedWorld = world;
            for (int cell = 0; cell < world.CellCount; cell++)
            {
                var colour = !world.IsWalkable(cell) ? new Color(.16f, .18f, .2f)
                    : world.SpawnCells.Contains(cell) ? new Color(.62f, .4f, .82f)
                    : world.HoldingAllowed(cell) ? new Color(.42f, .51f, .26f) : new Color(.68f, .59f, .4f);
                groundTexture.SetPixel(cell % world.Width, cell / world.Width, colour);
            }
            groundTexture.Apply();
        }

        private static void FinishCapture()
        {
            var texture = new Texture2D(1440, 900, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = captureTarget;
                texture.ReadPixels(new Rect(0, 0, 1440, 900), 0, 0);
                texture.Apply();
                string filename = new string(current.name.Select(value => char.IsLetterOrDigit(value) || value == '-' ? value : '_').ToArray()) + ".png";
                File.WriteAllBytes(Path.Combine(output, filename), texture.EncodeToPNG());
                current.screenshot = filename;
                var colours = texture.GetPixels32();
                current.renderingValidated = colours.Where((colour, index) => index % 127 == 0)
                    .Select(colour => colour.r + ":" + colour.g + ":" + colour.b).Distinct().Count() > 2;
            }
            catch (Exception error)
            {
                current.renderingError = error.ToString();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(texture);
                captureTarget.Release();
                Object.DestroyImmediate(captureTarget);
                captureTarget = null;
            }
            WriteCaseAndNext();
        }

        private static void WriteCaseAndNext()
        {
            File.WriteAllText(Path.Combine(output, "validation.json"), JsonUtility.ToJson(report, true));
            current = null;
            StartNext();
        }

        private static void Paint(GameObject view, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            material.color = color;
            view.GetComponent<Renderer>().sharedMaterial = material;
            resources.Add(material);
            var collider = view.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }
        }

        private static Vector3 Position(Point2 point) => new Vector3((float)(point.X * .001), 0, (float)(point.Y * .001));

        private static void CleanupCurrent()
        {
            if (captureTarget != null)
            {
                captureTarget.Release();
                Object.DestroyImmediate(captureTarget);
                captureTarget = null;
            }
            navigator?.Dispose();
            navigator = null;
            if (sceneRoot != null)
            {
                Object.DestroyImmediate(sceneRoot);
            }
            foreach (var resource in resources)
            {
                if (resource != null)
                {
                    Object.DestroyImmediate(resource);
                }
            }
            resources.Clear();
            views.Clear();
            targetViews.Clear();
            viewedWorld = null;
            groundTexture = null;
            sceneRoot = null;
            camera = null;
        }

        private static void BeginShutdown()
        {
            CleanupCurrent();
            if (report != null)
            {
                report.success = report.cases.Count == manifest.runs.Length && report.cases.All(item => item.success && (!manifest.graphicsRequired || item.renderingValidated));
                File.WriteAllText(Path.Combine(output, "validation.json"), JsonUtility.ToJson(report, true));
            }
            SessionState.SetString(Session + ".finishing", "yes");
            EditorApplication.ExitPlaymode();
        }

        private static void FinishShutdown()
        {
            var file = JsonUtility.FromJson<Manifest>(File.ReadAllText(SessionState.GetString(Session, "")));
            string result = Path.Combine(file.output, "validation.json");
            var restored = File.Exists(result) ? JsonUtility.FromJson<NativeReport>(File.ReadAllText(result)) : new NativeReport();
            restored.shutdownVerified = GameObject.Find("NAVLAB_RUNTIME") == null;
            restored.success &= restored.shutdownVerified;
            File.WriteAllText(result, JsonUtility.ToJson(restored, true));
            SessionState.EraseString(Session);
            SessionState.EraseString(Session + ".finishing");
            EditorApplication.update -= Poll;
            EditorApplication.Exit(restored.success ? 0 : 1);
        }

        private static string Argument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }

        private static string Hash(string path)
        {
            using (var digest = SHA256.Create())
            {
                return BitConverter.ToString(digest.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition)
            {
                throw new InvalidOperationException(detail);
            }
        }

        [Serializable]
        public sealed class Manifest
        {
            public string output, runId, expectedCoreSha256, expectedOracleSha256;
            public bool graphicsRequired;
            public int wallDeadlineSeconds = 1200;
            public int stepsPerEditorUpdate = 250;
            public RunInput[] runs;
        }
        [Serializable]
        public sealed class RunInput
        {
            public string name, input, assemblyName, assemblyPath, factory, expectedCandidateSha256;
        }
        [Serializable]
        public sealed class Input
        {
            public string modelInputSha256, expectation, requiredFeatures;
            public World world;
            public Request[] requests;
            public Terrain[] worldEvents;
            public Death[] deaths;
            public Minimum[] minimumAttackers;
            public int expectedLive, expectedAttackers, deadlineTicks, holdTicks, initialSettledCheckTick, minimumOldWaitingPromotions;
        }
        [Serializable]
        public sealed class World
        {
            public int width, height, cellSizeMm, gatheringDistanceMm;
            public Origin origin;
            public string walkable, holding;
            public int[] spawnCells;
            public Target[] targets;
            public WorldDefinition ToWorld() => new WorldDefinition(
                width,
                height,
                cellSizeMm,
                new Point2(origin.x, origin.y),
                walkable.Select(value => value == '1').ToArray(),
                holding.Select(value => value == '1').ToArray(),
                spawnCells,
                targets.Select(target => target.Definition()).ToArray(),
                gatheringDistanceMm);
        }
        [Serializable]
        public sealed class Origin
        {
            public double x, y;
        }
        [Serializable]
        public sealed class Target
        {
            public int id, radiusMm;
            public double x, y;
            public TargetDefinition Definition() => new TargetDefinition(id, new Point2(x, y), radiusMm);
        }
        [Serializable]
        public sealed class Request
        {
            public int readyTick, id, radiusMm, speedMmPerTick, attackRangeMm, spawnCell, targetId, assignedGoalCell;
            public UnitProfile Profile() => new UnitProfile(radiusMm, speedMmPerTick, attackRangeMm);
            public SpawnRequest Command() => new SpawnRequest(id, Profile(), spawnCell, targetId, assignedGoalCell);
            public static Request Replacement(int id, int tick, SpawnRequest original) => new Request
            {
                id = id, readyTick = tick, radiusMm = original.Profile.RadiusMm, speedMmPerTick = original.Profile.SpeedMmPerTick,
                attackRangeMm = original.Profile.AttackRangeMm, spawnCell = original.SpawnCell,
                targetId = original.TargetId, assignedGoalCell = original.AssignedGoalCell
            };
        }
        [Serializable]
        public sealed class Terrain
        {
            public int tick;
            public int[] blockCells, openCells;
            public bool afterSettlement;
            public Target replaceTarget;
            public WorldChange Change() => new WorldChange
            {
                BlockCells = blockCells, OpenCells = openCells,
                ReplaceTarget = replaceTarget == null ? (TargetDefinition?)null : replaceTarget.Definition()
            };
        }
        [Serializable]
        public sealed class Death
        {
            public int tick, count, range;
            public bool afterSettlement, replace, requireMoving, attackersOnly;
        }
        [Serializable]
        public sealed class Minimum
        {
            public int range, count;
        }
        [Serializable]
        public sealed class Metric
        {
            public string name;
            public long value;
        }
        [Serializable]
        public sealed class NativeReport
        {
            public bool success, shutdownVerified;
            public string runId, unity, graphics, device, manifestSha256, coreSha256, oracleSha256;
            public string scope = "Unity Editor Play/Mono independent navigation experiment. Not GameStarter business replacement, IL2CPP or device acceptance.";
            public long stopwatchFrequency = Stopwatch.Frequency;
            public List<NativeCase> cases = new List<NativeCase>();
        }
        [Serializable]
        public sealed class NativeCase
        {
            public string name, factoryId, factoryType, inputSha256, modelInputSha256, candidateSha256, detail, minimumGapMm, screenshot;
            public bool success, initialBatchVerified, renderingValidated;
            public string renderingError;
            public int originalRequests, admitted, rejected, deaths, live, pending, attacking, waiting, finalTick, holdTicks;
            public long checkedMicrosteps, pairSweeps, bodySweeps, maximumStepStopwatchTicks, stepStopwatchTicks;
            public double holdMaximumDriftMm;
            public Metric[] metrics;
        }
    }
}
