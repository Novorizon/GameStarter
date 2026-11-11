using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;

var seeds = args.Contains("--all") ? new[] { 11, 97, 713, 20261111, 1701, 991 } : new[] { 11 };
var reports = new List<object>();
bool passed = true;
foreach (int seed in seeds)
{
    var spec = (LabCase)typeof(LabCases).GetMethod("RandomMaze", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { seed })!;
    try
    {
        reports.Add(Construct(spec, seed));
    }
    catch (Exception error)
    {
        passed = false;
        reports.Add(new { name = spec.Name, seed, success = false, error = error.Message });
        Console.WriteLine("FAIL " + spec.Name + " " + error.Message);
    }
}
var large = new[] { 128, 256 }.Select(StaticLarge).ToArray();
File.WriteAllText("summary.json", JsonSerializer.Serialize(new
{
    success = passed,
    scope = "Independent serial existence witness, not an online candidate or a controller-performance result. "
        + "Original geometry, requests, radii, speed caps and range; no route/goal/order is passed to candidates.",
    randomWitnesses = reports,
    largeStaticOnly = large
}, new JsonSerializerOptions { WriteIndented = true }));
return passed ? 0 : 1;

static object Construct(LabCase spec, int seed)
{
    var world = spec.World;
    var originals = spec.Requests;
    Require(originals.Count == 32, "Random witness is for the unmodified32-request fixture.");
    var profile = originals[0].Request.Profile;
    Require(originals.All(order => order.Request.Profile.Equals(profile)) && profile.Equals(new UnitProfile(250, 500, 1000)), "Random profile changed.");
    var target = world.Targets.Single();
    var attackGoals = Enumerable.Range(0, world.CellCount).Where(cell => world.HoldingAllowed(cell)
        && ContinuousOracle.CircleFits(world, world.Center(cell), profile.RadiusMm)
        && ContinuousOracle.CanAttack(world, world.Center(cell), profile, target))
        .OrderBy(cell => Point2.Distance(world.Center(cell), target.Center)).ThenBy(cell => cell).ToList();
    Require(attackGoals.Count == 16, "Expected exactly16 independently checked physical attack centres.");
    var targetCell = world.CellAt(target.Center);
    int targetX = targetCell % world.Width;
    int targetY = targetCell / world.Width;
    var gatherGoals = Enumerable.Range(0, world.CellCount).Where(cell =>
        world.HoldingAllowed(cell) && !attackGoals.Contains(cell)
        && Math.Abs(cell % world.Width - targetX) % 2 == 0 && Math.Abs(cell / world.Width - targetY) % 2 == 0
        && ContinuousOracle.CircleFits(world, world.Center(cell), profile.RadiusMm)
        && !ContinuousOracle.CanAttack(world, world.Center(cell), profile, target)
        && Point2.Distance(world.Center(cell), target.Center) <= target.RadiusMm + world.GatheringDistanceMm)
        .OrderByDescending(cell => Point2.Distance(world.Center(cell), target.Center))
        .ThenByDescending(cell => cell % world.Width).ThenBy(cell => cell).ToList();
    Require(gatherGoals.Count >= 16, "Insufficient sparse gathering centres.");
    var selectedGather = gatherGoals.Take(16).ToList();
    var goals = attackGoals.Concat(selectedGather).ToArray();
    for (int first = 0; first < goals.Length; first++)
    {
        for (int second = first + 1; second < goals.Length; second++)
        {
            Require(Point2.Distance(world.Center(goals[first]), world.Center(goals[second])) >= profile.RadiusMm * 2, "Witness goal discs overlap.");
        }
    }
    var oracle = new ContinuousOracle(world);
    var bodies = new Dictionary<int, UnitSnapshot>();
    var movements = new List<object>();
    long timeMilliseconds = 0;
    long totalEdges = 0;
    var paths = new List<object>();
    int index = 0;
    foreach (var scheduled in originals)
    {
        var request = scheduled.Request;
        int goal = goals[index++];
        var path = Route(world, request.SpawnCell, goal, request.Profile.RadiusMm, bodies.Values.ToArray());
        Require(path != null, "No sequential physical route for ID " + request.Id + " to cell " + goal);
        Require(timeMilliseconds >= scheduled.ReadyTick * 50L, "Spawn before original ready time.");
        var beforeBirth = Snapshot(bodies);
        bodies.Add(request.Id, new UnitSnapshot(request.Id, world.Center(request.SpawnCell), MotionState.Moving, goal, request.TargetId));
        oracle.RegisterBirth(request, beforeBirth, Snapshot(bodies));
        long birthAt = timeMilliseconds;
        for (int edge = 1; edge < path!.Count; edge++)
        {
            var before = Snapshot(bodies);
            Point2 from = bodies[request.Id].Position;
            Point2 to = world.Center(path[edge]);
            int duration = (int)Math.Ceiling(Point2.Distance(from, to) * 50 / request.Profile.SpeedMmPerTick);
            bodies[request.Id] = new UnitSnapshot(request.Id, to, MotionState.Moving, goal, request.TargetId);
            oracle.ValidateTransition(before, Snapshot(bodies), duration);
            movements.Add(new
            {
                id = request.Id,
                from = new { x = from.X, y = from.Y },
                to = new { x = to.X, y = to.Y },
                durationMilliseconds = duration,
                startedMilliseconds = timeMilliseconds
            });
            timeMilliseconds += duration;
            totalEdges++;
        }
        var actual = bodies[request.Id];
        bool canAttack = ContinuousOracle.CanAttack(world, actual.Position, request.Profile, world.Target(request.TargetId));
        MotionState state = canAttack ? MotionState.Attacking : MotionState.Waiting;
        bodies[request.Id] = new UnitSnapshot(request.Id, actual.Position, state, goal, request.TargetId);
        oracle.ValidateTransition(Snapshot(bodies), Snapshot(bodies), 0);
        paths.Add(new { request.Id, request.SpawnCell, goal, birthAtMilliseconds = birthAt, arrivedAtMilliseconds = timeMilliseconds, cells = path });
    }
    var final = Snapshot(bodies);
    Require(
        final.Length == 32 && final.Count(unit => unit.State == MotionState.Attacking) == 16 && final.Count(unit => unit.State == MotionState.Waiting) == 16,
        "Witness must end with all32,16 actual attackers and16 waiting.");
    oracle.ValidateTransition(final, final, 10000);
    timeMilliseconds += 10000;
    Require(timeMilliseconds <= spec.DeadlineTicks * 50L, "The witness exceeds the original deadline.");
    var physicalInput = new { world = WorldDto(world), requests = originals.Select(OrderDto).ToArray() };
    string inputJson = JsonSerializer.Serialize(physicalInput);
    File.WriteAllText(spec.Name + "-input.json", inputJson + "\n");
    var witness = new
    {
        success = true,
        name = spec.Name,
        seed,
        physicalInputSha256 = Hash(inputJson),
        geometrySha256 = Hash(JsonSerializer.Serialize(WorldDto(world))),
        originalDeadlineTicks = spec.DeadlineTicks,
        serialExecutionMilliseconds = timeMilliseconds,
        serialExecutionLogicalTicks = (timeMilliseconds + 49) / 50,
        requested = originals.Count,
        actualAdmitted = final.Length,
        actualAlive = final.Length,
        actualAttackers = 16,
        actualWaiting = 16,
        pending = 0,
        actualBodyRadiusMm = profile.RadiusMm,
        originalSpeedCapMmPer50ms = profile.SpeedMmPerTick,
        actualAttackRangeMm = profile.AttackRangeMm,
        holdMilliseconds = 10000,
        holdLogicalTicks = 200,
        totalStraightEdges = totalEdges,
        checks = new { oracle.BodySweeps, oracle.PairSweeps, oracle.MinimumBodyGapMm, oracle.MaximumObservedStepMm },
        physicalExecutionScope = "Every recorded straight edge is traversed at its explicit duration using the original speed cap; "
            + "the continuous oracle checks the entire segment against static geometry/base and every stationary body. "
            + "This is a serial existence witness, not a5ms candidate controller benchmark.",
        paths,
        movements,
        finalBodies = final.Select(unit => new { unit.Id, x = unit.Position.X, y = unit.Position.Y, unit.State, unit.GoalCell, unit.TargetId }).ToArray()
    };
    WriteCompressed(spec.Name + "-witness.json.gz", witness);
    var summary = new
    {
        success = true,
        spec.Name,
        seed,
        physicalInputSha256 = Hash(inputJson),
        geometrySha256 = Hash(JsonSerializer.Serialize(WorldDto(world))),
        actualAlive = final.Length,
        actualAttackers = 16,
        actualWaiting = 16,
        serialExecutionMilliseconds = timeMilliseconds,
        originalDeadlineMilliseconds = spec.DeadlineTicks * 50L,
        totalStraightEdges = totalEdges,
        oracle.BodySweeps,
        oracle.PairSweeps,
        oracle.MinimumBodyGapMm,
        witnessSha256 = FileHash(spec.Name + "-witness.json.gz")
    };
    Console.WriteLine("PASS " + spec.Name + " alive32 attack16 wait16 edges=" + totalEdges + " ms=" + timeMilliseconds);
    return summary;
}

static object StaticLarge(int count)
{
    var spec = (LabCase)typeof(LabCases).GetMethod("LargeTiled", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { count })!;
    var world = spec.World;
    var profile = spec.Requests[0].Request.Profile;
    var target = world.Targets.Single();
    var fitting = Enumerable.Range(0, world.CellCount).Where(cell => world.HoldingAllowed(cell)
        && ContinuousOracle.CircleFits(world, world.Center(cell), profile.RadiusMm)
        && ContinuousOracle.CanAttack(world, world.Center(cell), profile, target)).ToHashSet();
    var seen = new HashSet<int>();
    var queue = new Queue<int>();
    seen.Add(world.SpawnCells[0]);
    queue.Enqueue(world.SpawnCells[0]);
    long consideredEdges = 0;
    long acceptedTreeEdges = 0;
    while (queue.Count > 0)
    {
        int current = queue.Dequeue();
        foreach (int next in world.Neighbours(current))
        {
            if (!seen.Contains(next))
            {
                consideredEdges++;
                if (ContinuousOracle.SegmentFits(world, world.Center(current), world.Center(next), profile.RadiusMm))
                {
                    seen.Add(next);
                    queue.Enqueue(next);
                    acceptedTreeEdges++;
                }
            }
        }
    }
    Require(world.SpawnCells.All(seen.Contains), "Large input spawn entrances are not in one body-clear connected component.");
    int connected = fitting.Count(seen.Contains);
    double minimumGoalBodyGap = double.PositiveInfinity;
    var goalArray = fitting.ToArray();
    for (int first = 0; first < goalArray.Length; first++)
    {
        for (int second = first + 1; second < goalArray.Length; second++)
        {
            double gap = Point2.Distance(world.Center(goalArray[first]), world.Center(goalArray[second])) - profile.RadiusMm * 2;
            minimumGoalBodyGap = Math.Min(minimumGoalBodyGap, gap);
        }
    }
    Require(minimumGoalBodyGap >= 0, "Large compatible attack centres have overlapping physical discs.");
    Require(connected >= count, "Large input has fewer compatible, statically reachable attack centres than demand.");
    var worldJson = JsonSerializer.Serialize(WorldDto(world));
    var result = new
    {
        name = spec.Name,
        requested = count,
        actualRadiusMm = profile.RadiusMm,
        rangeMm = profile.AttackRangeMm,
        staticFittingAttackCentreCount = fitting.Count,
        staticallyConnectedAttackCentreCount = connected,
        spawnCount = world.SpawnCells.Count,
        sourceConnectedCells = seen.Count,
        consideredStaticEdges = consideredEdges,
        acceptedBfsTreeEdges = acceptedTreeEdges,
        allSpawnEntrancesInSameComponent = true,
        minimumPairwiseGoalBodyGapMm = minimumGoalBodyGap,
        geometrySha256 = Hash(worldJson),
        jointDynamicFeasibilityProved = false,
        scope = "Static body clearance, range/LOS, reachability and candidate-count evidence only. "
            + "This is not simultaneous arrival proof, a packing completeness proof, or an algorithm result."
    };
    File.WriteAllText(spec.Name + "-static.json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("STATIC " + spec.Name + " compatible=" + fitting.Count + " connected=" + connected + " demand=" + count);
    return result;
}

static List<int>? Route(WorldDefinition world, int start, int goal, int radius, UnitSnapshot[] parked)
{
    var parent = Enumerable.Repeat(-1, world.CellCount).ToArray();
    parent[start] = start;
    var queue = new Queue<int>();
    queue.Enqueue(start);
    while (queue.Count > 0)
    {
        int current = queue.Dequeue();
        if (current == goal)
        {
            var path = new List<int> { goal };
            while (path[path.Count - 1] != start)
            {
                path.Add(parent[path[path.Count - 1]]);
            }
            path.Reverse();
            return path;
        }
        foreach (int next in world.Neighbours(current))
        {
            if (parent[next] >= 0 || !ContinuousOracle.SegmentFits(world, world.Center(current), world.Center(next), radius))
            {
                continue;
            }
            bool blockedByParked = parked.Any(unit =>
            {
                return ContinuousOracle.DistanceToSegment(unit.Position, world.Center(current), world.Center(next))
                    < radius * 2 - SimulationProtocol.GeometryToleranceMm;
            });
            if (blockedByParked)
            {
                continue;
            }
            parent[next] = current;
            queue.Enqueue(next);
        }
    }
    return null;
}

static UnitSnapshot[] Snapshot(Dictionary<int, UnitSnapshot> bodies) => bodies.Values.OrderBy(unit => unit.Id).ToArray();
static object OrderDto(ScheduledRequest order) => new
{
    readyTick = order.ReadyTick,
    id = order.Request.Id,
    radiusMm = order.Request.Profile.RadiusMm,
    speedMmPerTick = order.Request.Profile.SpeedMmPerTick,
    attackRangeMm = order.Request.Profile.AttackRangeMm,
    spawnCell = order.Request.SpawnCell,
    targetId = order.Request.TargetId,
    assignedGoalCell = order.Request.AssignedGoalCell
};
static object WorldDto(WorldDefinition world) => new
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
static void WriteCompressed(string path, object value)
{
    using var stream = new GZipStream(File.Create(path), CompressionLevel.Optimal);
    JsonSerializer.Serialize(stream, value);
}
static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
