using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class Actor
{
    public int Id { get; init; }
    public int Radius { get; init; }
    public int Range { get; init; }
    public Cell Position { get; set; }
    public int Goal { get; set; }
}

internal sealed class ActionEvidence
{
    public string Kind { get; init; } = "";
    public int Actor { get; init; }
    public int Radius { get; init; }
    public int Range { get; init; }
    public int StartTick { get; init; }
    public int EndTick { get; init; }
    public int[] Cells { get; init; } = Array.Empty<int>();
}

internal sealed class ProfileEvidence
{
    public int Range { get; init; }
    public int Bodies { get; init; }
    public int Attacking { get; init; }
    public int Waiting { get; init; }
}

internal sealed class WitnessResult
{
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public string InputFile { get; set; } = "";
    public string InputSha256 { get; set; } = "";
    public string FixtureSha256 { get; set; } = "";
    public string SourceFailureLogName { get; set; } = "";
    public string SourceFailureLogSha256 { get; set; } = "";
    public string ExecutableFileName { get; set; } = "";
    public string ExecutableSha256 { get; set; } = "";
    public string Scope { get; set; } = "Independent constructive compatibility-exchange witness, not the production planner.";
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public int HistoricalRequested { get; set; }
    public int HistoricalSpawned { get; set; }
    public int HistoricalRemoved { get; set; }
    public int RetainedBodies { get; set; }
    public int EarliestEquivalentSavedPoseTick { get; set; }
    public int OriginalDeadlineTick { get; set; }
    public int SourceFailureTick { get; set; }
    public int WitnessMovementFinishTick { get; set; }
    public int WitnessVerificationFinishTick { get; set; }
    public int StableTicks { get; set; }
    public int GridMoves { get; set; }
    public int ActualRadiusMm { get; set; } = 250;
    public int MaximumSpeedMmPerTick { get; set; } = 1000;
    public int BigAttackEndpoints { get; set; }
    public int ShortAttackEndpoints { get; set; }
    public bool ShortAttackEndpointsSubsetOfBig { get; set; }
    public bool AllIdentitiesAndBodiesRetained { get; set; }
    public bool ContinuousOracleVerified { get; set; }
    public bool WithinOriginalDeadline { get; set; }
    public int CheckedTicks { get; set; }
    public long CheckedPairSweeps { get; set; }
    public double MinimumBodyGapMm { get; set; } = double.MaxValue;
    public double MinimumBaseGapMm { get; set; } = double.MaxValue;
    public long ElapsedMilliseconds { get; set; }
    public ProfileEvidence[] InitialProfiles { get; set; } = Array.Empty<ProfileEvidence>();
    public ProfileEvidence[] FinalProfiles { get; set; } = Array.Empty<ProfileEvidence>();
    public int[] ReservedShortIngressCells { get; set; } = Array.Empty<int>();
    public UnitObservation[] InitialPose { get; set; } = Array.Empty<UnitObservation>();
    public UnitObservation[] FinalPose { get; set; } = Array.Empty<UnitObservation>();
    public List<ActionEvidence> Actions { get; } = new List<ActionEvidence>();
}

internal static class Program
{
    private const int Width = 35;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int Main(string[] args)
    {
        string input = Argument(args, "--input") ?? Path.Combine(AppContext.BaseDirectory, "Inputs");
        string output = Argument(args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "Results");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { success = false, status = "running" }, JsonOptions));
        var watch = Stopwatch.StartNew();
        var sourceHashes = SourceHashes();
        string manifestPath = Path.Combine(AppContext.BaseDirectory, "InputManifest.json");
        byte[] manifestBytes = File.ReadAllBytes(manifestPath);
        string inputManifestSha256 = Hash(manifestBytes);
        Require(inputManifestSha256 == sourceHashes["InputManifest.json"], "The runtime manifest differs from its frozen Sources copy.");
        using var manifest = JsonDocument.Parse(manifestBytes);
        var fixtures = manifest.RootElement.GetProperty("fixtures");
        string executablePath = typeof(Program).Assembly.Location;
        string executableFileName = Path.GetFileName(executablePath);
        string executableSha256 = Hash(File.ReadAllBytes(executablePath));
        GeometryChecks.VerifyOracle();
        var results = new List<WitnessResult>();
        foreach (string name in new[] { "mixed-profile-outer-before-inner", "mixed-profile-inner-before-outer" })
        {
            foreach (int seed in new[] { 1, 17, 733, 20261110 })
            {
                string filename = name + "-" + seed + ".json";
                Require(fixtures.TryGetProperty(filename, out var entry), "An input fixture is missing from the frozen manifest: " + filename);
                var result = Run(Path.Combine(input, filename), entry, executableFileName, executableSha256);
                results.Add(result);
                File.WriteAllText(Path.Combine(output, name + "-" + seed + ".witness.json"), JsonSerializer.Serialize(result, JsonOptions));
                Console.WriteLine((result.Success ? "PASS " : "FAIL ") + name + " seed=" + seed + " retained=" + result.RetainedBodies
                    + " moves=" + result.GridMoves + " start=" + result.EarliestEquivalentSavedPoseTick
                    + " finish=" + result.WitnessVerificationFinishTick + "/" + result.OriginalDeadlineTick + " " + result.Error);
            }
        }

        bool unchanged = sourceHashes.OrderBy(pair => pair.Key).SequenceEqual(SourceHashes().OrderBy(pair => pair.Key));
        bool manifestUnchanged = inputManifestSha256 == Hash(File.ReadAllBytes(manifestPath));
        bool executableUnchanged = executableSha256 == Hash(File.ReadAllBytes(executablePath));
        var summary = new
        {
            success = unchanged && manifestUnchanged && executableUnchanged && results.All(result => result.Success),
            status = "completed",
            count = results.Count,
            passed = results.Count(result => result.Success),
            sourceFilesUnchanged = unchanged,
            sourceHashes,
            inputManifestSha256,
            inputManifestUnchanged = manifestUnchanged,
            executableFileName,
            executableSha256,
            executableUnchanged,
            elapsedMilliseconds = watch.ElapsedMilliseconds,
            scope = "Original failed poses and all bodies retained. One real 1000 mm cardinal move per tick, with continuous independent geometry checks.",
            results = results.Select(result => new
            {
                result.Name, result.Seed, result.Success, result.Error, result.InputFile, result.InputSha256, result.FixtureSha256,
                result.SourceFailureLogName, result.SourceFailureLogSha256, result.RetainedBodies,
                result.EarliestEquivalentSavedPoseTick, result.GridMoves, result.WitnessVerificationFinishTick,
                result.OriginalDeadlineTick, result.FinalProfiles, result.MinimumBodyGapMm, result.MinimumBaseGapMm,
                result.WithinOriginalDeadline, result.ContinuousOracleVerified, result.ElapsedMilliseconds
            })
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
        return summary.success ? 0 : 1;
    }

    private static WitnessResult Run(string input, JsonElement manifestEntry, string executableFileName, string executableSha256)
    {
        var watch = Stopwatch.StartNew();
        byte[] bytes = File.ReadAllBytes(input);
        using var document = JsonDocument.Parse(bytes);
        var source = document.RootElement;
        var result = new WitnessResult
        {
            InputFile = Path.GetFileName(input),
            InputSha256 = Hash(bytes),
            FixtureSha256 = Hash(bytes),
            ExecutableFileName = executableFileName,
            ExecutableSha256 = executableSha256,
            Name = source.GetProperty("name").GetString()!,
            Seed = source.GetProperty("seed").GetInt32(),
            HistoricalRequested = source.GetProperty("requested").GetInt32(),
            HistoricalSpawned = source.GetProperty("spawned").GetInt32(),
            HistoricalRemoved = source.GetProperty("removed").GetInt32(),
            SourceFailureTick = source.GetProperty("ticks").GetInt32()
        };
        try
        {
            string expectedFixture = manifestEntry.GetProperty("fixtureSha256").GetString()!;
            Require(IsSha256(expectedFixture) && string.Equals(result.FixtureSha256, expectedFixture, StringComparison.OrdinalIgnoreCase),
                "The input fixture bytes differ from the frozen manifest: " + result.InputFile);
            string sourceHash = source.GetProperty("sourceFailureLogSha256").GetString()!;
            string sourceName = source.GetProperty("sourceFailureLogName").GetString()!;
            string manifestSourceHash = manifestEntry.GetProperty("sourceFailureLogSha256").GetString()!;
            bool sourceMatches = IsSha256(sourceHash) && IsSha256(manifestSourceHash)
                && string.Equals(sourceHash, manifestSourceHash, StringComparison.OrdinalIgnoreCase);
            Require(sourceMatches, "The original failure log SHA256 differs from the manifest.");
            Require(!string.IsNullOrWhiteSpace(sourceName) && sourceName == Path.GetFileName(sourceName)
                && sourceName.IndexOf('/') < 0 && sourceName.IndexOf('\\') < 0, "The original failure log must be recorded by file name only.");
            result.SourceFailureLogName = sourceName;
            result.SourceFailureLogSha256 = sourceHash.ToLowerInvariant();
            Require(!source.GetProperty("success").GetBoolean(), "The input is not the frozen failed run.");
            Require(
                source.GetProperty("width").GetInt32() == Width && source.GetProperty("height").GetInt32() == Width,
                "Expected the unchanged 35 x 35 arena.");
            var geometry = new NavigationScenario
            {
                Width = Width,
                Height = Width,
                Target = ReadCell(source.GetProperty("target")),
                Walkable = source.GetProperty("walkable").EnumerateArray().Select(value => value.GetBoolean()).ToArray(),
                HoldingAllowed = source.GetProperty("holdingAllowed").EnumerateArray().Select(value => value.GetBoolean()).ToArray(),
                GatheringDistance = source.GetProperty("gatheringDistance").GetInt32()
            };
            Require(geometry.Target == new Cell(17, 17) && geometry.Walkable.All(value => value), "The original target or open terrain changed.");
            Require(geometry.GatheringDistance == 10000, "The original gathering distance changed.");
            var commands = source.GetProperty("commands").EnumerateArray().ToArray();
            var profiles = commands.Where(command => command.GetProperty("kind").GetString() == "spawn")
                .ToDictionary(command => command.GetProperty("id").GetInt32(), command => command.GetProperty("attackRange").GetInt32());
            var frames = source.GetProperty("trajectory").EnumerateArray().ToArray();
            var final = ReadUnits(frames[frames.Length - 1]).OrderBy(unit => unit.Id).ToArray();
            bool outer = result.Name.Contains("outer-before", StringComparison.Ordinal);
            int expected = outer ? 85 : 100;
            int shortId = outer ? 85 : 101;
            Require(final.Length == expected && final.Select(unit => unit.Id).Distinct().Count() == expected, "The input lost or merged an active body.");
            Require(result.HistoricalSpawned == result.HistoricalRequested, "An unspawned request was omitted from the input cohort.");
            Require(result.HistoricalSpawned - result.HistoricalRemoved == expected, "The input lifecycle does not match the preserved active population.");
            Require(final.All(unit => unit.Radius == 250 && unit.State != NavigationState.Moving), "The failed final pose is not static with radius 250 mm.");
            var earliest = frames.OrderBy(frame => frame.GetProperty("tick").GetInt32()).First(frame =>
            {
                var units = ReadUnits(frame).OrderBy(unit => unit.Id).ToArray();
                return units.All(unit => unit.State != NavigationState.Moving) && units.SequenceEqual(final);
            });
            result.EarliestEquivalentSavedPoseTick = earliest.GetProperty("tick").GetInt32();
            Require(
                commands.All(command => command.GetProperty("tick").GetInt32() <= result.EarliestEquivalentSavedPoseTick),
                "An unexecuted input command follows the chosen pose.");
            result.OriginalDeadlineTick = outer ? 5000 : commands.Where(command => command.GetProperty("kind").GetString() == "remove")
                .Max(command => command.GetProperty("tick").GetInt32()) + 2000;
            var actors = final.ToDictionary(unit => unit.Id, unit => new Actor
            {
                Id = unit.Id,
                Radius = unit.Radius,
                Range = profiles[unit.Id],
                Position = unit.Cell,
                Goal = unit.SlotId
            });
            Require(
                actors[shortId].Range == 1000 && actors.Values.All(actor => actor.Range == 1000 || actor.Range == 4000),
                "The input profile catalogue changed.");
            var initialIds = actors.Keys.OrderBy(id => id).ToArray();
            var initialPositions = actors.ToDictionary(pair => pair.Key, pair => pair.Value.Position);
            int quarterTurns = result.Seed == 1 ? 0 : result.Seed == 17 ? 1 : result.Seed == 733 ? 2 : 3;
            Cell World(int x, int y) => Rotate(new Cell(x, y), quarterTurns) with
            {
                X = Rotate(new Cell(x, y), quarterTurns).X + 17,
                Y = Rotate(new Cell(x, y), quarterTurns).Y + 17
            };
            int[] gates = new[] { -5, -4, -3, -2 }.Select(x => actors.Values.Single(actor => actor.Position == World(x, 0)).Id).ToArray();
            Require(gates.All(id => actors[id].Range == 4000), "The proposed corridor is not occupied by the original long-range bodies.");
            var gateGoals = gates.ToDictionary(id => id, id => actors[id].Position);
            var ingress = Route(geometry, actors[shortId].Position, World(-6, 0), Occupied(actors, shortId));
            Require(ingress != null, "The original short-range body cannot reach the exterior corridor entrance.");
            var reserved = ingress!.ToHashSet();
            result.ReservedShortIngressCells = ingress.Select(Index).ToArray();
            result.RetainedBodies = actors.Count;
            CountAttackEndpoints(geometry, result);
            result.InitialProfiles = CountProfiles(geometry, actors);
            int tick = result.EarliestEquivalentSavedPoseTick;
            var previous = new Dictionary<int, UnitObservation>();
            VerifyFrame(geometry, actors, initialIds, null, previous, result);
            previous = Observe(geometry, actors, null).ToDictionary(unit => unit.Id);
            result.InitialPose = previous.Values.OrderBy(unit => unit.Id).ToArray();
            Require(result.InitialPose.SequenceEqual(final), "The witness changed a starting body position, profile state, radius, or endpoint.");

            var hints = new[] { World(-9, 3), World(-9, 2), World(-8, -3), World(-9, -3) };
            var candidates = hints.Concat(Enumerable.Range(-11, 23).SelectMany(y => Enumerable.Range(-11, 23).Select(x => World(x, y)))).Distinct();
            foreach (int id in gates)
            {
                var occupied = Occupied(actors, id);
                List<Cell>? path = null;
                foreach (var candidate in candidates)
                {
                    long distance = TargetDistanceSquared(candidate);
                    if (distance <= 5250L * 5250 || distance > 11000L * 11000 || reserved.Contains(candidate) || occupied.Contains(candidate))
                    {
                        continue;
                    }

                    if (geometry.HoldingAllowed != null && !geometry.HoldingAllowed[Index(candidate)])
                    {
                        continue;
                    }

                    path = Route(geometry, actors[id].Position, candidate, occupied);
                    if (path != null)
                    {
                        break;
                    }
                }

                Require(path != null, "A corridor body has no reachable, distinct legal gathering buffer: " + id);
                Move("withdraw-corridor", id, path!);
            }

            var entering = Route(geometry, actors[shortId].Position, World(-2, 0), Occupied(actors, shortId));
            Require(entering != null, "The short-range body has no route through the actually cleared corridor.");
            Move("short-range-enter", shortId, entering!);
            foreach (int id in gates.Take(3).Reverse())
            {
                var returning = Route(geometry, actors[id].Position, gateGoals[id], Occupied(actors, id));
                Require(returning != null, "An original corridor body cannot return through the remaining free corridor.");
                Move("restore-corridor", id, returning!);
            }

            result.WitnessMovementFinishTick = tick;
            for (int hold = 0; hold < 200; hold++)
            {
                tick++;
                VerifyFrame(geometry, actors, initialIds, null, previous, result);
                previous = Observe(geometry, actors, null).ToDictionary(unit => unit.Id);
            }

            result.StableTicks = 200;
            result.WitnessVerificationFinishTick = tick;
            result.WithinOriginalDeadline = tick <= result.OriginalDeadlineTick;
            Require(result.WithinOriginalDeadline, "The constructive exchange exceeds the original unchanged deadline.");
            Require(actors[shortId].Position == World(-2, 0), "The short-range body did not occupy its compatible attack endpoint.");
            Require(gates.Take(3).All(id => actors[id].Position == gateGoals[id]), "A restored corridor body lost its original endpoint.");
            Require(actors.Where(pair => pair.Key != shortId && pair.Key != gates[3]).All(pair => pair.Value.Position == initialPositions[pair.Key]),
                "An unrelated body moved or an original corridor endpoint was silently changed.");
            result.FinalProfiles = CountProfiles(geometry, actors);
            var big = result.FinalProfiles.Single(profile => profile.Range == 4000);
            var small = result.FinalProfiles.Single(profile => profile.Range == 1000);
            Require(big.Bodies == 84 && big.Attacking == (outer ? 83 : 68) && big.Waiting == (outer ? 1 : 16), "Long-range final matching is incorrect.");
            Require(
                small.Bodies == (outer ? 1 : 16) && small.Attacking == small.Bodies && small.Waiting == 0,
                "A compatible short-range attack request is still excluded.");
            Require(Hash(File.ReadAllBytes(input)) == result.InputSha256, "The frozen input changed during construction.");
            result.FinalPose = Observe(geometry, actors, null).OrderBy(unit => unit.Id).ToArray();
            result.AllIdentitiesAndBodiesRetained = actors.Keys.OrderBy(id => id).SequenceEqual(initialIds);
            result.ContinuousOracleVerified = true;
            result.Success = true;
            result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
            return result;

            void Move(string kind, int id, List<Cell> path)
            {
                Require(path.Count > 1 && path[0] == actors[id].Position, "A movement does not start at the actual body position.");
                int begin = tick;
                actors[id].Goal = Index(path[path.Count - 1]);
                for (int step = 1; step < path.Count; step++)
                {
                    Require(ClearEdge(geometry, path[step - 1], path[step]), "A movement uses a nonadjacent or statically obstructed edge.");
                    actors[id].Position = path[step];
                    tick++;
                    int? moving = step == path.Count - 1 ? null : id;
                    VerifyFrame(geometry, actors, initialIds, moving, previous, result);
                    previous = Observe(geometry, actors, moving).ToDictionary(unit => unit.Id);
                    result.GridMoves++;
                }

                result.Actions.Add(new ActionEvidence
                {
                    Kind = kind, Actor = id, Radius = actors[id].Radius, Range = actors[id].Range,
                    StartTick = begin, EndTick = tick, Cells = path.Select(Index).ToArray()
                });
            }
        }
        catch (Exception error)
        {
            result.Error = PortableError(error, input);
            result.ElapsedMilliseconds = watch.ElapsedMilliseconds;
            return result;
        }
    }

    private static void VerifyFrame(
        NavigationScenario geometry,
        Dictionary<int, Actor> actors,
        int[] identities,
        int? moving,
        Dictionary<int, UnitObservation> previous,
        WitnessResult report)
    {
        Require(actors.Count == identities.Length && actors.Keys.OrderBy(id => id).SequenceEqual(identities), "An input body was added, deleted, or replaced.");
        var units = Observe(geometry, actors, moving);
        Require(units.Select(unit => unit.Cell).Distinct().Count() == units.Length, "Two bodies share an endpoint centre.");
        GeometryChecks.VerifyUnitClearance(units, previous);
        foreach (var unit in units)
        {
            Require(unit.Radius == 250, "The physical radius was reduced.");
            var before = previous.TryGetValue(unit.Id, out var old) ? old : unit;
            long dx = (long)unit.X - before.X;
            long dy = (long)unit.Y - before.Y;
            Require(dx * dx + dy * dy <= 1000L * 1000, "A body exceeded the original speed or teleported.");
            GeometryChecks.VerifyObstacleClearance(geometry, before, unit);
            GeometryChecks.VerifyTargetClearance(geometry, before, unit);
            report.MinimumBaseGapMm = Math.Min(report.MinimumBaseGapMm,
                Math.Sqrt(PointSegmentSquared(17500, 17500, before.X, before.Y, unit.X, unit.Y)) - 1250);
            if (unit.State == NavigationState.Attacking)
            {
                long reach = 1250L + actors[unit.Id].Range;
                Require(TargetDistanceSquared(unit.Cell) <= reach * reach, "A body attacks outside its own profile range.");
                GeometryChecks.VerifyAttackLine(geometry, unit);
            }
            else if (unit.State == NavigationState.Waiting)
            {
                Require(TargetDistanceSquared(unit.Cell) <= 11000L * 11000, "A static waiting body is outside the original gathering bound.");
            }
        }

        for (int i = 0; i < units.Length; i++)
        {
            var first = units[i];
            var firstBefore = previous.TryGetValue(first.Id, out var priorFirst) ? priorFirst : first;
            for (int j = i + 1; j < units.Length; j++)
            {
                var second = units[j];
                var secondBefore = previous.TryGetValue(second.Id, out var priorSecond) ? priorSecond : second;
                double startX = firstBefore.X - secondBefore.X;
                double startY = firstBefore.Y - secondBefore.Y;
                double endX = first.X - second.X;
                double endY = first.Y - second.Y;
                double gap = Math.Sqrt(PointSegmentSquared(0, 0, startX, startY, endX, endY)) - 500;
                Require(gap >= -.000001, "The brute-force continuous pair oracle detected overlap.");
                report.MinimumBodyGapMm = Math.Min(report.MinimumBodyGapMm, gap);
                report.CheckedPairSweeps++;
            }
        }

        report.CheckedTicks++;
    }

    private static UnitObservation[] Observe(NavigationScenario geometry, Dictionary<int, Actor> actors, int? moving)
    {
        return actors.Values.OrderBy(actor => actor.Id).Select(actor => new UnitObservation(
            actor.Id,
            actor.Position,
            moving == actor.Id ? NavigationState.Moving : InAttackRange(actor) ? NavigationState.Attacking : NavigationState.Waiting,
            actor.Goal,
            1,
            actor.Position.X * 1000 + 500,
            actor.Position.Y * 1000 + 500,
            actor.Radius)).ToArray();
    }

    private static ProfileEvidence[] CountProfiles(NavigationScenario geometry, Dictionary<int, Actor> actors)
    {
        return actors.Values.GroupBy(actor => actor.Range).OrderBy(group => group.Key).Select(group => new ProfileEvidence
        {
            Range = group.Key,
            Bodies = group.Count(),
            Attacking = group.Count(InAttackRange),
            Waiting = group.Count(actor => !InAttackRange(actor))
        }).ToArray();
    }

    private static void CountAttackEndpoints(NavigationScenario geometry, WitnessResult report)
    {
        var big = new HashSet<Cell>();
        var small = new HashSet<Cell>();
        for (int y = 0; y < Width; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                var cell = new Cell(x, y);
                if (!CanStand(geometry, cell))
                {
                    continue;
                }

                if (TargetDistanceSquared(cell) <= 5250L * 5250)
                {
                    big.Add(cell);
                }
                if (TargetDistanceSquared(cell) <= 2250L * 2250)
                {
                    small.Add(cell);
                }
            }
        }

        report.BigAttackEndpoints = big.Count;
        report.ShortAttackEndpoints = small.Count;
        report.ShortAttackEndpointsSubsetOfBig = small.IsSubsetOf(big);
        Require(big.Count == 84 && small.Count == 16 && report.ShortAttackEndpointsSubsetOfBig, "The original compatible endpoint sets changed.");
    }

    private static bool InAttackRange(Actor actor)
    {
        long reach = 1250L + actor.Range;
        return TargetDistanceSquared(actor.Position) <= reach * reach;
    }

    private static List<Cell>? Route(NavigationScenario geometry, Cell start, Cell target, HashSet<Cell> occupied)
    {
        if (!CanStand(geometry, start) || !CanStand(geometry, target) || occupied.Contains(target))
        {
            return null;
        }

        var parent = new Dictionary<Cell, Cell?> { [start] = null };
        var queue = new Queue<Cell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == target)
            {
                var path = new List<Cell>();
                for (Cell? next = target; next.HasValue; next = parent[next.Value])
                {
                    path.Add(next.Value);
                }

                path.Reverse();
                return path;
            }

            foreach (var next in Neighbors(current))
            {
                if (!occupied.Contains(next) && !parent.ContainsKey(next) && ClearEdge(geometry, current, next))
                {
                    parent.Add(next, current);
                    queue.Enqueue(next);
                }
            }
        }

        return null;
    }

    private static bool CanStand(NavigationScenario geometry, Cell cell)
    {
        return geometry.Open(cell) && TargetDistanceSquared(cell) >= 1250L * 1250;
    }

    private static bool ClearEdge(NavigationScenario geometry, Cell from, Cell to)
    {
        return Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y) == 1 && CanStand(geometry, from) && CanStand(geometry, to)
            && PointSegmentSquared(17500, 17500, from.X * 1000 + 500, from.Y * 1000 + 500, to.X * 1000 + 500, to.Y * 1000 + 500) >= 1250L * 1250;
    }

    private static IEnumerable<Cell> Neighbors(Cell cell)
    {
        yield return new Cell(cell.X + 1, cell.Y);
        yield return new Cell(cell.X, cell.Y + 1);
        yield return new Cell(cell.X - 1, cell.Y);
        yield return new Cell(cell.X, cell.Y - 1);
    }

    private static HashSet<Cell> Occupied(Dictionary<int, Actor> actors, int except)
    {
        return actors.Values.Where(actor => actor.Id != except).Select(actor => actor.Position).ToHashSet();
    }

    private static Cell Rotate(Cell cell, int turns)
    {
        for (int turn = 0; turn < turns; turn++)
        {
            cell = new Cell(-cell.Y, cell.X);
        }

        return cell;
    }

    private static long TargetDistanceSquared(Cell cell)
    {
        long x = (cell.X - 17) * 1000L;
        long y = (cell.Y - 17) * 1000L;
        return x * x + y * y;
    }

    private static double PointSegmentSquared(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double square = dx * dx + dy * dy;
        double t = square == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / square, 0, 1);
        double x = px - ax - t * dx;
        double y = py - ay - t * dy;
        return x * x + y * y;
    }

    private static UnitObservation[] ReadUnits(JsonElement frame)
    {
        return frame.GetProperty("units").EnumerateArray().Select(unit => new UnitObservation(
            unit.GetProperty("id").GetInt32(),
            ReadCell(unit.GetProperty("cell")),
            Enum.Parse<NavigationState>(unit.GetProperty("state").GetString()!),
            unit.GetProperty("slotId").GetInt32(),
            unit.GetProperty("moveTicks").GetInt32(),
            unit.GetProperty("x").GetInt32(),
            unit.GetProperty("y").GetInt32(),
            unit.GetProperty("radius").GetInt32())).ToArray();
    }

    private static Cell ReadCell(JsonElement cell) => new Cell(cell.GetProperty("x").GetInt32(), cell.GetProperty("y").GetInt32());
    private static int Index(Cell cell) => cell.Y * Width + cell.X;
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Dictionary<string, string> SourceHashes()
    {
        string[] files =
        {
            "Program.cs",
            "Oracle/GeometryTypes.cs",
            "Oracle/GeometryChecks.cs",
            "Oracle/Observations.cs",
            "MixedProfileWitness.csproj",
            "InputManifest.json"
        };
        string root = Path.Combine(AppContext.BaseDirectory, "Sources");
        return files.ToDictionary(file => file, file => Hash(File.ReadAllBytes(Path.Combine(root, file))), StringComparer.Ordinal);
    }

    private static bool IsSha256(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

    private static string PortableError(Exception error, string input)
    {
        string message = error.GetType().Name + ": " + error.Message;
        message = message.Replace(Path.GetFullPath(input), Path.GetFileName(input), StringComparison.Ordinal);
        message = message.Replace(AppContext.BaseDirectory, "[runtime]/", StringComparison.Ordinal);
        message = message.Replace(Directory.GetCurrentDirectory(), "[working-directory]", StringComparison.Ordinal);
        return message;
    }

    private static string? Argument(string[] args, string key)
    {
        int at = Array.IndexOf(args, key);
        return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
    }

    private static void Require(bool condition, string error)
    {
        if (!condition)
        {
            throw new InvalidOperationException(error);
        }
    }
}
