using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NavigationLab.Core;
using NavigationLab.Oracle;

internal static class Program
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions Compact = new();
    private static int Main(string[] arguments)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        string root = Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "RepositoryRoot").Value!);
        string suite = Option(arguments, "--suite") ?? "full";
        string output = Path.GetFullPath(Option(arguments, "--output") ?? "Artifacts/NavigationLab");
        string? filter = Option(arguments, "--scenario");
        var cases = LabCases.Build(root, suite).Where(item => filter == null || item.Name.Contains(filter, StringComparison.Ordinal)).ToList();
        if (cases.Count == 0)
        {
            throw new ArgumentException("No scenario matches the filter.");
        }
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(Path.Combine(output, "inputs"));
        foreach (var item in cases)
        {
            ValidateInputCertificate(item);
            WriteJson(Path.Combine(output, "inputs", item.Name + ".json"), Export(item));
        }
        if (arguments.Contains("--list") || arguments.Contains("--export-only"))
        {
            foreach (var item in cases)
            {
                Console.WriteLine(item.Name + " [" + Required(item) + "]");
            }
            return 0;
        }
        string assemblyPath = Path.GetFullPath(Option(arguments, "--assembly")
            ?? Path.Combine(root, "Experiments~/NavigationLab/Candidates/A-Sipp/bin/Release/netstandard2.1/NavigationLab.ASipp.dll"));
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        string? factoryName = Option(arguments, "--factory");
        var factoryType = factoryName == null ? assembly.GetTypes().Single(type => typeof(INavigationFactory).IsAssignableFrom(type) && !type.IsAbstract)
            : assembly.GetType(factoryName, true)!;
        var factory = (INavigationFactory)Activator.CreateInstance(factoryType)!;
        var sourceBefore = Sources(root, assemblyPath, factory.Id);
        string? buildManifest = Option(arguments, "--build-manifest");
        bool provenanceVerified = buildManifest != null && VerifyBuildManifest(buildManifest, assemblyPath);
        if (arguments.Contains("--require-build-manifest") && !provenanceVerified)
        {
            throw new InvalidOperationException("Formal runs require a verified build manifest.");
        }
        var stopwatch = Stopwatch.StartNew();
        var results = new List<CaseResult>();
        bool isolation = false;
        string? isolationError = null;
        try
        {
            isolation = VerifyIsolation(factory);
        }
        catch (Exception error)
        {
            isolationError = error.Message;
        }
        bool replay = !arguments.Contains("--no-replay");
        foreach (var item in cases)
        {
            var result = Execute(factory, item, output, true);
            if (result.Status == "Passed" && replay)
            {
                var repeated = Execute(factory, item, output, false);
                result.ReplayVerified = repeated.Status == "Passed" && result.TraceSha256 == repeated.TraceSha256
                    && result.CommandsSha256 == repeated.CommandsSha256 && result.FinalState == repeated.FinalState;
                if (!result.ReplayVerified)
                {
                    result.Status = "Failed";
                    result.Error = "Fresh-instance replay differs: " + repeated.Status + " " + repeated.Error;
                }
            }
            results.Add(result);
            string individual = Path.Combine(output, item.Name + ".json");
            WriteJson(individual, result);
            using (var compressed = new GZipStream(File.Create(individual + ".gz"), CompressionLevel.Optimal))
            {
                using var raw = File.OpenRead(individual);
                raw.CopyTo(compressed);
            }
            Console.WriteLine(result.Status.ToUpperInvariant() + " " + item.Name + " admitted=" + result.Admitted + " alive=" + result.Alive
                + " pending=" + result.Pending + " attack=" + result.Attacking + " tick=" + result.FinalTick + " " + result.Error);
        }
        var sourceAfter = Sources(root, assemblyPath, factory.Id);
        bool sourcesUnchanged = JsonSerializer.Serialize(sourceBefore) == JsonSerializer.Serialize(sourceAfter);
        int failed = results.Count(item => item.Status == "Failed");
        int unsupported = results.Count(item => item.Status == "Unsupported");
        int invalidInputs = results.Count(item => item.Status == "InvalidInput");
        WriteJson(Path.Combine(output, "summary.json"), new
        {
            schemaVersion = 1,
            utc = DateTime.UtcNow,
            algorithm = factory.Id,
            description = factory.Description,
            actualFactoryType = factoryType.FullName,
            candidateAssemblySha256 = FileSha(assemblyPath),
            runnerAssemblySha256 = FileSha(Assembly.GetExecutingAssembly().Location),
            oracleAssemblySha256 = FileSha(typeof(ContinuousOracle).Assembly.Location),
            coreAssemblySha256 = FileSha(typeof(INavigationFactory).Assembly.Location),
            protocol = new { microstepMilliseconds = 5, logicalTickMilliseconds = 50, stableLogicalTicks = 200, geometryToleranceMm = 0.1 },
            scope = "Independent .NET8 host executing netstandard2.1 candidate DLLs; not a claim of original proprietary game source, Unity integration or device FPS.",
            suite,
            success = failed == 0 && unsupported == 0 && invalidInputs == 0 && isolation && sourcesUnchanged,
            supportedCasesPassed = failed == 0 && invalidInputs == 0 && isolation && sourcesUnchanged,
            allSuiteCasesSupported = unsupported == 0,
            finiteCorpusOnly = true,
            replayRequested = replay,
            buildProvenanceVerified = provenanceVerified,
            formalEvidenceReady = provenanceVerified && replay && sourcesUnchanged,
            buildManifestSha256 = buildManifest == null ? null : FileSha(buildManifest),
            passed = results.Count(item => item.Status == "Passed"),
            failed,
            unsupported,
            invalidInputs,
            cases = results.Select(result => CompactResult(result, output)).ToArray(),
            isolationAndPauseVerified = isolation,
            isolationError,
            sourceFilesUnchanged = sourcesUnchanged,
            sourceBefore,
            sourceAfter,
            elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
            uniqueActualInputCount = results.Select(item => item.InputSha256).Distinct().Count(),
            uniquePhysicalInputCountIgnoringIds = results.Select(item => item.PhysicalInputSha256).Distinct().Count(),
            timingScope = "Candidate Create/TrySpawn/Step/Remove/Apply/Dispose/Snapshot wall time and allocation are measured separately; input clone/Freeze copy/oracle/JSON excluded. Snapshot includes extra validation reads. Concurrent host timings are diagnostic, not device FPS."
        });
        return failed > 0 || invalidInputs > 0 || !isolation || !sourcesUnchanged ? 1 : unsupported > 0 ? 2 : 0;
    }

    private static CaseResult Execute(INavigationFactory factory, LabCase spec, string output, bool capture)
    {
        var exported = Export(spec);
        var result = new CaseResult
        {
            Name = spec.Name,
            Family = spec.Family,
            InputSha256 = Sha(JsonSerializer.Serialize(exported)),
            PhysicalInputSha256 = Sha(JsonSerializer.Serialize(Export(spec, true))),
            GeometrySha256 = Sha(JsonSerializer.Serialize(Geometry(spec.World))),
            Expectation = spec.Expectation.ToString(),
            OriginalRequests = spec.Requests.Count,
            PhysicalAreaUpperBound = spec.PhysicalAreaUpperBound,
            KnownEndpointPopulation = spec.ExpectedLive,
            FeasibilityBasis = spec.Feasibility
        };
        var missing = Required(spec) & ~factory.Features;
        if (missing != NavigationFeatures.None)
        {
            result.Status = missing.HasFlag(NavigationFeatures.DeathAndReinforcement) ? "Failed" : "Unsupported";
            result.Error = "Factory does not declare " + missing;
            return result;
        }
        using var traceHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var commandHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string traceDirectory = Path.Combine(output, "traces");
        Directory.CreateDirectory(traceDirectory);
        result.Trace = "traces/" + spec.Name + ".jsonl.gz";
        using Stream traceStream = capture ? new GZipStream(File.Create(Path.Combine(output, result.Trace)), CompressionLevel.Optimal) : Stream.Null;
        using var trace = new StreamWriter(traceStream, new UTF8Encoding(false));
        var watch = Stopwatch.StartNew();
        var pending = spec.Requests.Select(item => item with { }).ToList();
        var profiles = spec.Requests.Select(item => item.Request.Profile).Distinct().ToArray();
        var oracle = new ContinuousOracle(spec.World);
        var triggeredWorld = new HashSet<int>();
        var triggeredDeaths = new HashSet<int>();
        int nextId = pending.Max(item => item.Request.Id) + 1;
        int issuedRequests = spec.Requests.Count;
        var oldWaitingIds = new HashSet<int>();
        int stable = 0;
        var holdAnchor = new Dictionary<int, Point2>();
        double holdMaximumDrift = 0;
        int microstep = 0;
        int initialCheckTick = spec.RequireInitialSettledAtTick ?? -1;
        bool initialChecked = initialCheckTick < 0;
        var lastTrace = new Dictionary<int, UnitSnapshot>();
        var commands = new List<object>();
        void RecordCommand(object value)
        {
            string json = JsonSerializer.Serialize(value);
            commandHash.AppendData(Encoding.UTF8.GetBytes(json + "\n"));
            commands.Add(value);
        }
        var current = Array.Empty<UnitSnapshot>();
        try
        {
            var worldInput = spec.World.Clone();
            var profileInput = (UnitProfile[])profiles.Clone();
            using var nav = new TimedNavigator(Measure(result, "Create", () => factory.Create(worldInput, profileInput)), result);
            current = Freeze(nav);
            oracle.VerifyIdentity(current);
            for (microstep = 0; microstep <= spec.DeadlineTicks * SimulationProtocol.Substeps; microstep++)
            {
                if (microstep % SimulationProtocol.Substeps == 0)
                {
                    int tick = microstep / SimulationProtocol.Substeps;
                    for (int index = 0; index < spec.TerrainEvents.Count; index++)
                    {
                        var planned = spec.TerrainEvents[index];
                        if (tick >= planned.Tick && !triggeredWorld.Contains(index)
                            && (!planned.AfterSettlement || Complete(spec, pending, current, oracle, false, issuedRequests - result.Deaths)))
                        {
                            triggeredWorld.Add(index);
                            var before = Freeze(nav);
                            oracle.ValidateWorldChangeInput(planned.Change.DeepClone(), before);
                            bool accepted = nav.ApplyWorldChange(planned.Change.DeepClone());
                            var after = Freeze(nav);
                            current = after;
                            RecordCommand(new { microstep, kind = "world-change", index, accepted });
                            Require(accepted, "Advertised dynamic capability rejected the actual event.");
                            oracle.ApplyChange(planned.Change.DeepClone(), before, after);
                            current = after;
                            stable = 0;
                        }
                    }
                    bool settledNow = Complete(spec, pending, current, oracle, false, issuedRequests - result.Deaths);
                    for (int index = 0; index < spec.DeathEvents.Count; index++)
                    {
                        var planned = spec.DeathEvents[index];
                        if (triggeredDeaths.Contains(index) || tick < planned.Tick || planned.AfterSettlement && !settledNow)
                        {
                            continue;
                        }
                        var selected = current.Where(unit => planned.Range < 0 || oracle.Live[unit.Id].Profile.AttackRangeMm == planned.Range)
                            .Where(unit => !planned.AttackersOnly || unit.State == MotionState.Attacking)
                            .Where(unit => !planned.RequireMoving || unit.State == MotionState.Moving
                                && Point2.Distance(unit.Position, oracle.World.Center(oracle.World.CellAt(unit.Position))) > 0.1)
                            .OrderBy(unit => unit.Id).Take(planned.Count).ToArray();
                        if (selected.Length != planned.Count)
                        {
                            if (tick > planned.Tick + 100 && !planned.AfterSettlement)
                            {
                                throw new InvalidOperationException("Actual noncentre moving death trigger never became valid.");
                            }
                            continue;
                        }
                        triggeredDeaths.Add(index);
                        oldWaitingIds.UnionWith(current.Where(unit => unit.State == MotionState.Waiting).Select(unit => unit.Id));
                        foreach (var victim in selected)
                        {
                            var original = oracle.Live[victim.Id];
                            var before = Freeze(nav);
                            Require(nav.Remove(victim.Id), "Failed to remove a live explicit victim.");
                            current = Freeze(nav);
                            oracle.RegisterDeath(victim.Id, before, current);
                            result.Deaths++;
                            RecordCommand(new { microstep, kind = "death", victim.Id, x = victim.Position.X, y = victim.Position.Y });
                            if (planned.Replace)
                            {
                                var replacement = new SpawnRequest(
                                    nextId++,
                                    original.Profile,
                                    original.SpawnCell,
                                    original.TargetId,
                                    original.AssignedGoalCell);
                                pending.Add(new ScheduledRequest(tick, replacement));
                                issuedRequests++;
                            }
                        }
                        stable = 0;
                    }
                    var triedEntries = new HashSet<int>();
                    foreach (var order in pending.Where(order => order.ReadyTick <= tick).ToArray())
                    {
                        if (!triedEntries.Add(order.Request.SpawnCell))
                        {
                            continue;
                        }
                        var before = Freeze(nav);
                        bool accepted = nav.TrySpawn(order.Request);
                        current = Freeze(nav);
                        RecordCommand(new { microstep, kind = accepted ? "spawn" : "spawn-rejected", order.Request.Id,
                            radius = order.Request.Profile.RadiusMm, speed = order.Request.Profile.SpeedMmPerTick,
                            range = order.Request.Profile.AttackRangeMm, spawn = order.Request.SpawnCell, failure = accepted ? null : nav.LastSpawnFailure });
                        if (accepted)
                        {
                            oracle.RegisterBirth(order.Request, before, current);
                            pending.Remove(order);
                            result.Admitted++;
                        }
                        else
                        {
                            oracle.VerifyRejectedBirth(before, current);
                            result.RejectedAttempts++;
                            result.Failures.TryGetValue(nav.LastSpawnFailure, out int count);
                            result.Failures[nav.LastSpawnFailure] = count + 1;
                        }
                    }
                    if (!initialChecked && tick == initialCheckTick)
                    {
                        int due = spec.Requests.Count(order => order.ReadyTick <= tick);
                        Require(
                            current.Length == due && current.All(unit => unit.State != MotionState.Moving),
                            "Initial crowd did not physically settle before late arrivals.");
                        initialChecked = true;
                        result.InitialBatchVerified = true;
                    }
                    int promotedOld = current.Count(unit => oldWaitingIds.Contains(unit.Id) && unit.State == MotionState.Attacking);
                    bool complete = Complete(spec, pending, current, oracle, true) && triggeredDeaths.Count == spec.DeathEvents.Count
                        && triggeredWorld.Count == spec.TerrainEvents.Count && initialChecked && promotedOld >= spec.MinimumOldWaitingPromotions;
                    if (complete)
                    {
                        if (stable == 0)
                        {
                            holdAnchor = current.ToDictionary(unit => unit.Id, unit => unit.Position);
                            holdMaximumDrift = 0;
                        }
                        stable++;
                    }
                    else
                    {
                        stable = 0;
                    }
                    if (stable > spec.HoldTicks)
                    {
                        result.Status = "Passed";
                        result.FinalTick = tick;
                        result.HoldTicks = stable - 1;
                        result.HoldMaximumDriftMm = holdMaximumDrift;
                        result.Alive = current.Length;
                        result.Pending = pending.Count;
                        result.Attacking = current.Count(unit => unit.State == MotionState.Attacking);
                        result.Waiting = current.Count(unit => unit.State == MotionState.Waiting);
                        result.FinalState = Canonical(current);
                        result.Metrics = nav.Metrics.ToDictionary(pair => pair.Key, pair => pair.Value);
                        result.OldWaitingBeforeDeaths = oldWaitingIds.Count;
                        result.PromotedOldWaiting = promotedOld;
                        result.ByRange = current.GroupBy(unit => oracle.Live[unit.Id].Profile.AttackRangeMm).ToDictionary(
                            group => group.Key, group => new[] { group.Count(), group.Count(unit => unit.State == MotionState.Attacking) });
                        break;
                    }
                }
                var previous = Freeze(nav);
                nav.Step();
                current = Freeze(nav);
                oracle.ValidateTransition(previous, current, SimulationProtocol.MicrostepMilliseconds);
                if (stable > 0)
                {
                    bool uninterrupted = current.Length == holdAnchor.Count
                        && current.All(unit => unit.State != MotionState.Moving && holdAnchor.ContainsKey(unit.Id));
                    double drift = uninterrupted && current.Length > 0 ? current.Max(unit => Point2.Distance(unit.Position, holdAnchor[unit.Id])) : 0;
                    if (!uninterrupted || drift > SimulationProtocol.GeometryToleranceMm)
                    {
                        stable = 0;
                    }
                    else
                    {
                        holdMaximumDrift = Math.Max(holdMaximumDrift, drift);
                    }
                }
                string physical = Canonical(current);
                traceHash.AppendData(Encoding.UTF8.GetBytes(microstep + ":" + physical + "\n"));
                var changed = current.Where(unit => !lastTrace.TryGetValue(unit.Id, out var prior) || !Same(unit, prior)).ToArray();
                var removed = lastTrace.Keys.Except(current.Select(unit => unit.Id)).ToArray();
                trace.WriteLine(JsonSerializer.Serialize(new
                {
                    step = microstep + 1,
                    changed = changed.Select(unit => new object[] { unit.Id, unit.Position.X, unit.Position.Y, (int)unit.State, unit.GoalCell, unit.TargetId }),
                    removed
                }));
                lastTrace = current.ToDictionary(unit => unit.Id);
                result.CheckedMicrosteps++;
                result.Alive = current.Length;
                result.Pending = pending.Count;
            }
            if (result.Status != "Passed")
            {
                result.Status = "Failed";
                result.Error = "Deadline: alive=" + current.Length + ", pending=" + pending.Count + ", moving=" + current.Count(unit => unit.State == MotionState.Moving)
                    + ", attack=" + current.Count(unit => unit.State == MotionState.Attacking) + ", failure=" + nav.LastSpawnFailure;
                result.FinalTick = microstep / SimulationProtocol.Substeps;
                result.FinalState = Canonical(current);
                result.Attacking = current.Count(unit => unit.State == MotionState.Attacking);
                result.Waiting = current.Count(unit => unit.State == MotionState.Waiting);
                result.Metrics = nav.Metrics.ToDictionary(pair => pair.Key, pair => pair.Value);
            }
        }
        catch (Exception error)
        {
            result.Status = error.Message.StartsWith("INVALID TEST INPUT:", StringComparison.Ordinal) ? "InvalidInput" : "Failed";
            result.Error = error.Message;
            result.FinalTick = microstep / SimulationProtocol.Substeps;
            result.FailureSnapshot = current.Select(unit => new object[]
            {
                unit.Id, SafeNumber(unit.Position.X), SafeNumber(unit.Position.Y), (int)unit.State, unit.GoalCell, unit.TargetId
            }).ToArray();
            trace.WriteLine(JsonSerializer.Serialize(new { step = microstep + 1, failure = result.Error, unsafeSnapshot = result.FailureSnapshot }));
        }
        result.TraceSha256 = Convert.ToHexString(traceHash.GetHashAndReset()).ToLowerInvariant();
        result.CommandsSha256 = Convert.ToHexString(commandHash.GetHashAndReset()).ToLowerInvariant();
        result.PairSweeps = oracle.PairSweeps;
        result.BodySweeps = oracle.BodySweeps;
        result.MinimumBodyGapMm = double.IsFinite(oracle.MinimumBodyGapMm) ? oracle.MinimumBodyGapMm : null;
        result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
        result.Commands = commands;
        result.StopwatchFrequency = Stopwatch.Frequency;
        result.StepStopwatchTicks = result.ApiStopwatchTicks.GetValueOrDefault("Step");
        result.MaximumStepStopwatchTicks = result.ApiMaximumStopwatchTicks.GetValueOrDefault("Step");
        result.StepAllocatedBytes = result.ApiAllocatedBytes.GetValueOrDefault("Step");
        result.TotalCandidateApiStopwatchTicks = result.ApiStopwatchTicks.Values.Sum();
        result.TotalCandidateApiAllocatedBytes = result.ApiAllocatedBytes.Values.Sum();
        return result;
    }

    private static bool Complete(
        LabCase spec,
        List<ScheduledRequest> pending,
        UnitSnapshot[] snapshot,
        ContinuousOracle oracle,
        bool final,
        int? expectedAliveBeforeLifecycle = null)
    {
        int expected = !final && spec.Expectation == CaseExpectation.FullArrival && expectedAliveBeforeLifecycle.HasValue
            ? expectedAliveBeforeLifecycle.Value : spec.ExpectedLive;
        if (snapshot.Length != expected || snapshot.Any(unit => unit.State == MotionState.Moving))
        {
            return false;
        }
        if (spec.Expectation == CaseExpectation.FullArrival && pending.Count != 0)
        {
            return false;
        }
        if (spec.Expectation == CaseExpectation.StaticUnreachable && (snapshot.Length != 0 || pending.Count != spec.Requests.Count))
        {
            return false;
        }
        if (!spec.ExpectedAttackers.HasValue && snapshot.Length > 0 && spec.Requests.Any(order => order.Request.AssignedGoalCell < 0)
            && !snapshot.Any(unit => unit.State == MotionState.Attacking))
        {
            return false;
        }
        if (spec.ExpectedAttackers.HasValue && snapshot.Count(unit => unit.State == MotionState.Attacking) != spec.ExpectedAttackers)
        {
            return false;
        }
        foreach (var pair in spec.MinimumAttackersByRange)
        {
            if (snapshot.Count(unit => unit.State == MotionState.Attacking && oracle.Live[unit.Id].Profile.AttackRangeMm == pair.Key) < pair.Value)
            {
                return false;
            }
        }
        return !final || pending.All(order => order.ReadyTick <= spec.DeadlineTicks);
    }

    private static T Measure<T>(CaseResult result, string operation, Func<T> callback)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        try
        {
            return callback();
        }
        finally
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
            result.ApiStopwatchTicks[operation] = result.ApiStopwatchTicks.GetValueOrDefault(operation) + elapsed;
            result.ApiAllocatedBytes[operation] = result.ApiAllocatedBytes.GetValueOrDefault(operation) + bytes;
            result.ApiCalls[operation] = result.ApiCalls.GetValueOrDefault(operation) + 1;
            result.ApiMaximumStopwatchTicks[operation] = Math.Max(result.ApiMaximumStopwatchTicks.GetValueOrDefault(operation), elapsed);
        }
    }

    private sealed class TimedNavigator : INavigator
    {
        private readonly INavigator inner;
        private readonly CaseResult result;
        internal TimedNavigator(INavigator inner, CaseResult result)
        {
            this.inner = inner;
            this.result = result;
        }
        public bool TrySpawn(SpawnRequest request) => Measure(result, "TrySpawn", () => inner.TrySpawn(request));
        public void Step() => Measure(result, "Step", () =>
        {
            inner.Step();
            return true;
        });
        public bool Remove(int id) => Measure(result, "Remove", () => inner.Remove(id));
        public bool ApplyWorldChange(WorldChange change) => Measure(result, "Apply", () => inner.ApplyWorldChange(change));
        public IReadOnlyList<UnitSnapshot> Snapshot() => Measure(result, "Snapshot", () => inner.Snapshot());
        public IReadOnlyDictionary<string, long> Metrics => inner.Metrics;
        public string LastSpawnFailure => inner.LastSpawnFailure;
        public void Dispose() => Measure(result, "Dispose", () =>
        {
            inner.Dispose();
            return true;
        });
    }

    private static bool VerifyIsolation(INavigationFactory factory)
    {
        var walkable = Enumerable.Repeat(true, 35 * 25).ToArray();
        var world = new WorldDefinition(35, 25, 1000, default, walkable, walkable, new[] { 2 + 12 * 35 },
            new[] { new TargetDefinition(1, new Point2(28500, 12500), 1000) }, 5000);
        var profile = new UnitProfile(250, 333, 1000);
        var request = new SpawnRequest(1, profile, world.SpawnCells[0]);
        using var reference = factory.Create(world.Clone(), new[] { profile });
        Require(reference.TrySpawn(request), "Isolation reference spawn failed.");
        for (int tick = 0; tick < 100; tick++)
        {
            reference.Step();
        }
        string expected = Canonical(Freeze(reference));
        using var first = factory.Create(world.Clone(), new[] { profile });
        Require(first.TrySpawn(request), "First isolated spawn failed.");
        for (int tick = 0; tick < 35; tick++)
        {
            first.Step();
        }
        string paused = Canonical(Freeze(first));
        var alternateMask = (bool[])walkable.Clone();
        for (int y = 0; y < 25; y++)
        {
            if (y < 11 || y > 13)
            {
                alternateMask[15 + y * 35] = false;
            }
        }
        var alternate = new WorldDefinition(35, 25, 1000, default, alternateMask, alternateMask, new[] { 2 + 18 * 35 },
            new[] { new TargetDefinition(1, new Point2(28500, 6500), 1000) }, 7000);
        var alternateProfile = new UnitProfile(300, 500, 2000);
        using var second = factory.Create(alternate, new[] { alternateProfile });
        Require(second.TrySpawn(new SpawnRequest(999, alternateProfile, alternate.SpawnCells[0])), "Second isolated spawn failed.");
        for (int tick = 0; tick < 75; tick++)
        {
            second.Step();
        }
        Require(Canonical(Freeze(first)) == paused, "Other instance advanced or reset paused instance.");
        Require(second.Remove(999), "Second-instance removal failed.");
        Require(second.TrySpawn(new SpawnRequest(1001, alternateProfile, alternate.SpawnCells[0])), "Second-instance reentry failed.");
        if (factory.Features.HasFlag(NavigationFeatures.DynamicTerrain))
        {
            Require(second.ApplyWorldChange(new WorldChange { BlockCells = new[] { 10 + 4 * 35 } }), "Second-instance dynamic event failed.");
        }
        if (factory.Features.HasFlag(NavigationFeatures.MovingTarget))
        {
            Require(second.ApplyWorldChange(new WorldChange { ReplaceTarget = new TargetDefinition(1, new Point2(30500, 8500), 1000) }),
                "Second-instance target event failed.");
        }
        Require(Canonical(Freeze(first)) == paused, "Other-instance removal/rebirth/cache invalidation changed paused world.");
        for (int tick = 35; tick < 100; tick++)
        {
            first.Step();
            if (tick % 2 == 0)
            {
                second.Step();
            }
        }
        Require(Canonical(Freeze(first)) == expected, "Interleaved instance differs from isolated reference.");
        return true;
    }

    private static void ValidateInputCertificate(LabCase spec)
    {
        Require(spec.Requests.Select(order => order.Request.Id).Distinct().Count() == spec.Requests.Count, "Input has duplicate original IDs.");
        foreach (var order in spec.Requests)
        {
            Require(ContinuousOracle.CircleFits(spec.World, spec.World.Center(order.Request.SpawnCell), order.Request.Profile.RadiusMm),
                "Invalid original spawn geometry in " + spec.Name);
        }
        if (spec.Name == "cross-junction-original-4m")
        {
            var profile = spec.Requests[0].Request.Profile;
            var target = spec.World.Target(1);
            int endpoints = Enumerable.Range(0, spec.World.CellCount).Count(cell => spec.World.HoldingAllowed(cell)
                && ContinuousOracle.CircleFits(spec.World, spec.World.Center(cell), profile.RadiusMm)
                && (ContinuousOracle.CanAttack(spec.World, spec.World.Center(cell), profile, target)
                    || Point2.Distance(spec.World.Center(cell), target.Center) <= target.RadiusMm + spec.World.GatheringDistanceMm));
            Require(endpoints == 72, "Original72 endpoint set changed.");
        }
        if (spec.PhysicalAreaUpperBound > 0)
        {
            Require(spec.World.Width == 9 && spec.World.Height == 9 && spec.World.CellSizeMm == 1000, "Unexpected physical-capacity fixture.");
            var profile = spec.Requests[0].Request.Profile;
            Require(profile.RadiusMm == 490, "Area proof requires fixed490mm bodies.");
            long upper = (49L * 1000000 * 100 - 314L * 1000 * 1000) / (314L * 490 * 490);
            Require(upper == spec.PhysicalAreaUpperBound && upper == 60, "Physical area bound changed.");
            int endpoints = Enumerable.Range(0, spec.World.CellCount).Count(cell => spec.World.HoldingAllowed(cell)
                && ContinuousOracle.CircleFits(spec.World, spec.World.Center(cell), profile.RadiusMm));
            Require(endpoints == 39 && spec.ExpectedLive == endpoints, "Discrete endpoint bound changed.");
            for (int y = 0; y < 9; y++)
            {
                for (int x = 0; x < 9; x++)
                {
                    Require(spec.World.IsWalkable(y * 9 + x) == (x > 0 && x < 8 && y > 0 && y < 8), "Area proof rectangle changed.");
                }
            }
        }
        if (spec.Family == "large-body-group")
        {
            var options = spec.Requests.ToDictionary(order => order.Request.Id, order => Enumerable.Range(0, spec.World.CellCount)
                .Where(cell => spec.World.HoldingAllowed(cell) && ContinuousOracle.CircleFits(spec.World, spec.World.Center(cell), order.Request.Profile.RadiusMm)
                    && ContinuousOracle.CanAttack(spec.World, spec.World.Center(cell), order.Request.Profile, spec.World.Target(order.Request.TargetId))).ToArray());
            var goals = new Dictionary<int, int>();
            bool Match(int id, HashSet<int> seen)
            {
                foreach (int cell in options[id])
                {
                    if (seen.Add(cell) && (!goals.TryGetValue(cell, out int owner) || Match(owner, seen)))
                    {
                        goals[cell] = id;
                        return true;
                    }
                }
                return false;
            }
            foreach (var request in spec.Requests)
            {
                Require(Match(request.Request.Id, new HashSet<int>()), "Sparse large-body attack assignment certificate failed.");
            }
            var requested = spec.Requests.ToDictionary(order => order.Request.Id, order => order.Request);
            foreach (var first in goals)
            {
                foreach (var second in goals.Where(pair => pair.Key != first.Key))
                {
                    Require(Point2.Distance(spec.World.Center(first.Key), spec.World.Center(second.Key))
                        >= requested[first.Value].Profile.RadiusMm + requested[second.Value].Profile.RadiusMm,
                        "Sparse endpoint physical packing certificate failed.");
                }
                var request = requested[first.Value];
                var visited = new HashSet<int> { request.SpawnCell };
                var queue = new Queue<int>(visited);
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    foreach (int next in spec.World.Neighbours(cell))
                    {
                        var from = spec.World.Center(cell);
                        var to = spec.World.Center(next);
                        if (!visited.Contains(next) && ContinuousOracle.SegmentFits(spec.World, from, to, request.Profile.RadiusMm)
                            && goals.Where(pair => pair.Value != request.Id).All(pair => ContinuousOracle.DistanceToSegment(spec.World.Center(pair.Key), from, to)
                                + 0.1 >= request.Profile.RadiusMm + requested[pair.Value].Profile.RadiusMm))
                        {
                            visited.Add(next);
                            queue.Enqueue(next);
                        }
                    }
                }
                Require(visited.Contains(first.Key), "Sparse large-body permanent access certificate failed.");
            }
        }
        foreach (var planned in spec.TerrainEvents.Where(item => item.Change.ReplaceTarget.HasValue))
        {
            var replacement = planned.Change.ReplaceTarget!.Value;
            var updated = spec.World.WithChange(planned.Change);
            var profile = spec.Requests[0].Request.Profile;
            int slots = Enumerable.Range(0, updated.CellCount).Count(cell => updated.HoldingAllowed(cell)
                && ContinuousOracle.CircleFits(updated, updated.Center(cell), profile.RadiusMm)
                && ContinuousOracle.CanAttack(updated, updated.Center(cell), profile, replacement));
            Require(slots == 16, "Relocated target16-slot certificate changed.");
            if (planned.AfterSettlement)
            {
                var original = spec.World.Target(replacement.Id);
                double oldRadius = original.RadiusMm + Math.Max(spec.World.GatheringDistanceMm, profile.RadiusMm + profile.AttackRangeMm);
                Require(Point2.Distance(original.Center, replacement.Center) - oldRadius - SimulationProtocol.GoalToleranceMm
                    > replacement.RadiusMm + profile.RadiusMm, "Relocation could cover an old settled body.");
            }
        }
        if (spec.Expectation == CaseExpectation.StaticUnreachable)
        {
            var profile = spec.Requests[0].Request.Profile;
            var seen = new HashSet<int> { spec.Requests[0].Request.SpawnCell };
            var queue = new Queue<int>(seen);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                Require(
                    !ContinuousOracle.CanAttack(spec.World, spec.World.Center(cell), profile, spec.World.Target(1)),
                    "Unreachable certificate has a reachable attack position.");
                foreach (int next in spec.World.Neighbours(cell))
                {
                    if (!seen.Contains(next) && ContinuousOracle.SegmentFits(spec.World, spec.World.Center(cell), spec.World.Center(next), profile.RadiusMm))
                    {
                        seen.Add(next);
                        queue.Enqueue(next);
                    }
                }
            }
        }
    }

    private static NavigationFeatures Required(LabCase spec)
    {
        var features = spec.Features;
        if (spec.World.Targets.Count > 1)
        {
            features |= NavigationFeatures.MultipleTargets;
        }
        if (spec.Requests.Any(order => order.Request.AssignedGoalCell >= 0))
        {
            features |= NavigationFeatures.AssignedGoals;
        }
        if (spec.DeathEvents.Count > 0)
        {
            features |= NavigationFeatures.DeathAndReinforcement;
        }
        if (spec.TerrainEvents.Any(planned => planned.Change.BlockCells.Length > 0 || planned.Change.OpenCells.Length > 0))
        {
            features |= NavigationFeatures.DynamicTerrain;
        }
        if (spec.TerrainEvents.Any(planned => planned.Change.ReplaceTarget.HasValue))
        {
            features |= NavigationFeatures.MovingTarget;
        }
        if (spec.Requests.Any(order => order.Request.Profile.RadiusMm > spec.World.CellSizeMm / 2))
        {
            features |= NavigationFeatures.LargeBodies;
        }
        if (spec.Requests.Any(order => order.Request.Profile.SpeedMmPerTick > spec.World.CellSizeMm))
        {
            features |= NavigationFeatures.FastPerTick;
        }
        return features;
    }

    private static object Geometry(WorldDefinition world) => new
    {
        width = world.Width,
        height = world.Height,
        cellSizeMm = world.CellSizeMm,
        origin = new { x = world.Origin.X, y = world.Origin.Y },
        walkable = string.Concat(world.CopyWalkable().Select(value => value ? '1' : '0')),
        holding = string.Concat(world.CopyHolding().Select(value => value ? '1' : '0')),
        spawnCells = world.SpawnCells,
        targets = world.Targets.Select(target => new { id = target.Id, x = target.Center.X, y = target.Center.Y, radiusMm = target.RadiusMm }),
        gatheringDistanceMm = world.GatheringDistanceMm
    };
    private static object Export(LabCase item, bool omitIds = false) => new
    {
        world = Geometry(item.World),
        requests = item.Requests.Select(order => new { readyTick = order.ReadyTick, id = omitIds ? 0 : order.Request.Id,
            radiusMm = order.Request.Profile.RadiusMm, speedMmPerTick = order.Request.Profile.SpeedMmPerTick,
            attackRangeMm = order.Request.Profile.AttackRangeMm, spawnCell = order.Request.SpawnCell,
            targetId = order.Request.TargetId, assignedGoalCell = order.Request.AssignedGoalCell }),
        worldEvents = item.TerrainEvents.Select(planned => new { planned.Tick, planned.AfterSettlement, planned.Change.BlockCells, planned.Change.OpenCells,
            replaceTarget = planned.Change.ReplaceTarget.HasValue ? new
            {
                id = planned.Change.ReplaceTarget.Value.Id,
                x = planned.Change.ReplaceTarget.Value.Center.X,
                y = planned.Change.ReplaceTarget.Value.Center.Y,
                radiusMm = planned.Change.ReplaceTarget.Value.RadiusMm
            } : null }),
        deaths = item.DeathEvents,
        expectation = item.Expectation.ToString(),
        expectedLive = item.ExpectedLive,
        expectedAttackers = item.ExpectedAttackers,
        minimumAttackersByRange = item.MinimumAttackersByRange,
        deadlineTicks = item.DeadlineTicks,
        holdTicks = item.HoldTicks,
        initialSettledCheckTick = item.RequireInitialSettledAtTick,
        minimumOldWaitingPromotions = item.MinimumOldWaitingPromotions,
        requiredFeatures = Required(item).ToString(),
        microstepMilliseconds = 5,
        logicalTickMilliseconds = 50
    };
    private static JsonObject CompactResult(CaseResult result, string output)
    {
        var value = JsonSerializer.SerializeToNode(result)!.AsObject();
        value.Remove(nameof(CaseResult.Commands));
        value["FullReport"] = result.Name + ".json.gz";
        value["FullReportSha256"] = FileSha(Path.Combine(output, result.Name + ".json.gz"));
        string trace = Path.Combine(output, result.Trace);
        value["TraceFileSha256"] = File.Exists(trace) ? FileSha(trace) : null;
        return value;
    }
    private static object SafeNumber(double value) => double.IsFinite(value) ? value : value.ToString("R", CultureInfo.InvariantCulture);
    private static UnitSnapshot[] Freeze(INavigator navigator) => navigator.Snapshot().ToArray();
    private static bool Same(UnitSnapshot first, UnitSnapshot second) => first.Id == second.Id && first.Position == second.Position
        && first.GoalCell == second.GoalCell && first.State == second.State && first.TargetId == second.TargetId;
    private static string Canonical(UnitSnapshot[] snapshot) => string.Join("|", snapshot.OrderBy(unit => unit.Id).Select(UnitCanonical));
    private static string UnitCanonical(UnitSnapshot unit) => unit.Id + ":" + unit.Position.X.ToString("R") + "," + unit.Position.Y.ToString("R")
        + ":" + (int)unit.State + ":" + unit.GoalCell + ":" + unit.TargetId;
    private static object Sources(string root, string candidateAssembly, string factory)
    {
        string candidate = Path.GetDirectoryName(candidateAssembly)!;
        while (!Directory.GetFiles(candidate, "*.csproj").Any())
        {
            candidate = Directory.GetParent(candidate)?.FullName ?? throw new DirectoryNotFoundException("Cannot locate candidate source project.");
        }
        var lab = Directory.GetFiles(Path.Combine(root, "Experiments~/NavigationLab"), "*", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")
                && (Path.GetExtension(path) is ".cs" or ".csproj" || path.EndsWith("authored-forest.json", StringComparison.Ordinal)))
            .ToDictionary(path => "harness/" + Path.GetRelativePath(Path.Combine(root, "Experiments~/NavigationLab"), path), FileSha);
        foreach (string path in Directory.GetFiles(candidate, "*", SearchOption.AllDirectories).Where(path =>
            !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")
            && (Path.GetExtension(path) is ".cs" or ".csproj" || Path.GetFileName(path) == "LICENSE")))
        {
            lab["candidate/" + Path.GetRelativePath(candidate, path)] = FileSha(path);
        }
        foreach (string relative in new[] { "Verification~/TowerDefenseNavigation/Scenarios.cs", "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes",
            "Config/Luban/Data/tdenemy.xlsx", "Config/Luban/Data/tdspawn.xlsx", "Config/Luban/Data/tdwave.xlsx" })
        {
            lab["input/" + relative] = FileSha(Path.Combine(root, relative));
        }
        if (factory == "A-Sipp")
        {
            string domain = Path.Combine(root, "Assets/HotUpdate/TowerDefense/Domain");
            foreach (string path in Directory.GetFiles(domain, "*.cs", SearchOption.AllDirectories))
            {
                lab["frozen-A/" + Path.GetRelativePath(domain, path)] = FileSha(path);
            }
        }
        return new SortedDictionary<string, string>(lab, StringComparer.Ordinal);
    }
    private static bool VerifyBuildManifest(string path, string candidateAssembly)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Require(root.GetProperty("sourceFiles").EnumerateObject().Count() >= 3, "Build manifest has no meaningful source list.");
        foreach (var source in root.GetProperty("sourceFiles").EnumerateObject())
        {
            Require(FileSha(source.Name) == source.Value.GetString(), "Build source no longer matches: " + source.Name);
        }
        var assemblies = new Dictionary<string, string>
        {
            ["candidateAssemblySha256"] = candidateAssembly,
            ["coreAssemblySha256"] = typeof(INavigationFactory).Assembly.Location,
            ["oracleAssemblySha256"] = typeof(ContinuousOracle).Assembly.Location,
            ["runnerAssemblySha256"] = Assembly.GetExecutingAssembly().Location
        };
        foreach (var pair in assemblies)
        {
            Require(FileSha(pair.Value) == root.GetProperty(pair.Key).GetString(), "Build assembly no longer matches: " + pair.Key);
        }
        return true;
    }
    private static string Sha(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    private static string FileSha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Pretty) + "\n");
    private static string? Option(string[] arguments, string name)
    {
        int index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed class CaseResult
{
    public string Name { get; set; } = "";
    public string Family { get; set; } = "";
    public string Status { get; set; } = "Running";
    public string? Error { get; set; }
    public string InputSha256 { get; set; } = "";
    public string PhysicalInputSha256 { get; set; } = "";
    public string GeometrySha256 { get; set; } = "";
    public string Expectation { get; set; } = "";
    public string FeasibilityBasis { get; set; } = "";
    public string Trace { get; set; } = "";
    public string TraceSha256 { get; set; } = "";
    public string CommandsSha256 { get; set; } = "";
    public string? FinalState { get; set; }
    public int OriginalRequests { get; set; }
    public int Admitted { get; set; }
    public int Alive { get; set; }
    public int Pending { get; set; }
    public int Attacking { get; set; }
    public int Waiting { get; set; }
    public int Deaths { get; set; }
    public int OldWaitingBeforeDeaths { get; set; }
    public int PromotedOldWaiting { get; set; }
    public int RejectedAttempts { get; set; }
    public int FinalTick { get; set; }
    public int HoldTicks { get; set; }
    public double HoldMaximumDriftMm { get; set; }
    public int KnownEndpointPopulation { get; set; }
    public int PhysicalAreaUpperBound { get; set; }
    public bool InitialBatchVerified { get; set; }
    public bool ReplayVerified { get; set; }
    public long CheckedMicrosteps { get; set; }
    public long PairSweeps { get; set; }
    public long BodySweeps { get; set; }
    public double? MinimumBodyGapMm { get; set; }
    public Dictionary<string, long> ApiStopwatchTicks { get; set; } = new();
    public Dictionary<string, long> ApiMaximumStopwatchTicks { get; set; } = new();
    public Dictionary<string, long> ApiAllocatedBytes { get; set; } = new();
    public Dictionary<string, long> ApiCalls { get; set; } = new();
    public long TotalCandidateApiStopwatchTicks { get; set; }
    public long TotalCandidateApiAllocatedBytes { get; set; }
    public long StepStopwatchTicks { get; set; }
    public long MaximumStepStopwatchTicks { get; set; }
    public long StepAllocatedBytes { get; set; }
    public long StopwatchFrequency { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public Dictionary<string, int> Failures { get; set; } = new();
    public Dictionary<string, long> Metrics { get; set; } = new();
    public Dictionary<int, int[]> ByRange { get; set; } = new();
    public List<object> Commands { get; set; } = new();
    public object[][]? FailureSnapshot { get; set; }
}
