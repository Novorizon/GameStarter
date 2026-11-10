using System.Text.Json;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

int seedArgument = Array.IndexOf(args, "--seed");
int seed = seedArgument >= 0 ? int.Parse(args[seedArgument + 1]) : 1;
bool originalAdmissionOrder = Array.IndexOf(args, "--original-order") >= 0;
int rootArgument = Array.IndexOf(args, "--root");
string repositoryRoot = Path.GetFullPath(rootArgument >= 0 ? args[rootArgument + 1] : Directory.GetCurrentDirectory());
int outputArgument = Array.IndexOf(args, "--output");
string outputRoot = Path.GetFullPath(outputArgument >= 0 ? args[outputArgument + 1] : "Artifacts/NavigationFeasibility");
Directory.CreateDirectory(outputRoot);
string suffix = (seed == 1 ? "" : "-seed" + seed) + (originalAdmissionOrder ? "-original-order" : "");
string output = Path.Combine(outputRoot, "original-4m-72-witness" + suffix + ".json");
File.Delete(output);
var scenario = CreateScenario(seed);
var spawnCells = scenario.SpawnCells.Select(cell => cell.Y * scenario.Width + cell.X).ToArray();
var grid = new NavigationGrid(
    scenario.Width,
    scenario.Height,
    1000,
    new Position2(0, 0),
    scenario.Walkable,
    new Position2(scenario.Target.X * 1000 + 500, scenario.Target.Y * 1000 + 500),
    1000,
    spawnCells: spawnCells);
int radius = scenario.UnitRadius;
const int attackRange = 1000;
var eligible = Enumerable.Range(0, grid.CellCount).Select(cell => grid.HasAttackLine(cell, radius, attackRange)).ToArray();
long reach = grid.TargetRadius + radius + attackRange;
long gathering = grid.TargetRadius + scenario.GatheringDistance;
var goals = Enumerable.Range(0, grid.CellCount).Where(cell => !spawnCells.Contains(cell) && grid.CanOccupy(cell, radius)
    && grid.HoldingAllowed(cell) && ((eligible[cell] && grid.Neighbours(cell).Any(next => grid.CanTraverse(cell, next, radius)
        && Squared(grid.Center(next), grid.TargetCenter) > reach * reach))
        || (!eligible[cell] && Squared(grid.Center(cell), grid.TargetCenter) <= gathering * gathering))).ToArray();
var production = new NavigationCrowd(grid, gatheringDistance: scenario.GatheringDistance);
production.RegisterProfile(radius, attackRange);
Require(goals.Length == production.CountAttackSlots(radius, attackRange) + production.CountGatherSlots(radius, attackRange), "Candidate mismatch.");
Require(goals.Length == 72, "This witness must preserve the original 72-cell fixture.");

var occupied = goals.ToHashSet();
var reverse = new List<WitnessPath>();
var admissionOrder = originalAdmissionOrder ? Enumerable.Range(1, scenario.UnitCount).Reverse().ToArray()
    : Enumerable.Range(1, scenario.UnitCount).ToArray();
foreach (int id in admissionOrder.Reverse())
{
    int spawn = spawnCells[(id + scenario.Seed % spawnCells.Length) % spawnCells.Length];
    var parent = new Dictionary<int, int> { [spawn] = -1 };
    var queue = new Queue<int>();
    queue.Enqueue(spawn);
    while (queue.Count > 0)
    {
        int cell = queue.Dequeue();
        foreach (int next in grid.Neighbours(cell))
        {
            if (occupied.Contains(next) || parent.ContainsKey(next) || !grid.CanTraverse(cell, next, radius))
            {
                continue;
            }

            parent.Add(next, cell);
            queue.Enqueue(next);
        }
    }

    var choice = occupied.OrderBy(cell => cell).Select(cell => new
    {
        Goal = cell,
        Next = grid.Neighbours(cell).Where(next => parent.ContainsKey(next) && grid.CanTraverse(cell, next, radius))
            .Select(next => (int?)next).FirstOrDefault()
    }).FirstOrDefault(value => value.Next.HasValue);
    Require(choice != null, "Reverse evacuation is blocked for fixed request " + id + " at spawn " + spawn);
    var path = new List<int> { choice!.Goal };
    for (int next = choice.Next!.Value; next >= 0; next = parent[next])
    {
        path.Add(next);
    }

    path.Reverse();
    reverse.Add(new WitnessPath(id, spawn, choice.Goal, path.ToArray()));
    occupied.Remove(choice.Goal);
}

Require(occupied.Count == 0, "Not every endpoint was evacuated.");
reverse.Reverse();
var parked = new Dictionary<int, int>();
long tick = 0;
long checkedPairs = 0;
long checkedWalls = 0;
double minimumPairGap = double.PositiveInfinity;
double minimumBaseGap = double.PositiveInfinity;
foreach (var path in reverse)
{
    Require(path.Cells[0] == path.Spawn && path.Cells[^1] == path.Goal, "Path endpoints disagree.");
    path.StartTick = tick;
    for (int step = 1; step < path.Cells.Length; step++)
    {
        var from = grid.Center(path.Cells[step - 1]);
        var to = grid.Center(path.Cells[step]);
        foreach (var other in parked)
        {
            double distance = SegmentDistanceSquared(from, to, grid.Center(other.Value));
            Require(distance >= 4L * radius * radius, "Witness has a swept body collision.");
            minimumPairGap = Math.Min(minimumPairGap, Math.Sqrt(distance) - 2 * radius);
            checkedPairs++;
        }

        double baseDistance = SegmentDistanceSquared(from, to, grid.TargetCenter);
        Require(baseDistance >= (long)(grid.TargetRadius + radius) * (grid.TargetRadius + radius), "Witness penetrates the target.");
        minimumBaseGap = Math.Min(minimumBaseGap, Math.Sqrt(baseDistance) - grid.TargetRadius - radius);
        foreach (int cell in Enumerable.Range(0, grid.CellCount).Where(cell => !grid.IsWalkable(cell)))
        {
            int left = grid.Origin.X + cell % grid.Width * grid.CellSize;
            int bottom = grid.Origin.Y + cell / grid.Width * grid.CellSize;
            double distance = AxisSegmentBoxDistanceSquared(from, to, left, bottom, grid.CellSize);
            Require(distance >= (long)radius * radius, "Witness penetrates blocked terrain.");
            checkedWalls++;
        }

        tick++;
    }

    path.ArrivalTick = tick;
    parked.Add(path.Id, path.Goal);
    tick++;
}

Require(parked.Count == 72 && parked.Values.Distinct().Count() == 72, "Not all 72 bodies genuinely parked.");
var replacementCases = new List<object>();
var completed = parked.ToDictionary(pair => pair.Key, pair => pair.Value);
foreach (var victim in completed.Where(pair => eligible[pair.Value]).OrderBy(pair => pair.Key))
{
    parked = completed.ToDictionary(pair => pair.Key, pair => pair.Value);
    parked.Remove(victim.Key);
    int vacancy = victim.Value;
    int recoveryTicks = 0;
    int spawn = spawnCells[(victim.Key + scenario.Seed % spawnCells.Length) % spawnCells.Length];
    var changes = new List<object>();
    var allParent = new Dictionary<int, int> { [vacancy] = -1 };
    var allQueue = new Queue<int>();
    allQueue.Enqueue(vacancy);
    var free = FreeReachable(spawn, parked.Values.ToHashSet());
    int endpoint = -1;
    while (allQueue.Count > 0)
    {
        int cell = allQueue.Dequeue();
        if (free.ContainsKey(cell))
        {
            endpoint = cell;
            break;
        }

        foreach (int next in grid.Neighbours(cell))
        {
            if (!allParent.ContainsKey(next) && grid.CanTraverse(cell, next, radius))
            {
                allParent.Add(next, cell);
                allQueue.Enqueue(next);
            }
        }
    }

    Require(endpoint >= 0, "Vacancy has no full-graph connection to the spawn component.");
    var vacancyPath = new List<int>();
    for (int cell = endpoint; cell >= 0; cell = allParent[cell])
    {
        vacancyPath.Add(cell);
    }

    vacancyPath.Reverse();
    int previousOccupiedIndex = 0;
    for (int index = 1; index < vacancyPath.Count; index++)
    {
        int source = vacancyPath[index];
        var occupant = parked.Where(pair => pair.Value == source).ToArray();
        if (occupant.Length == 0)
        {
            continue;
        }

        int id = occupant[0].Key;
        var movement = vacancyPath.Skip(previousOccupiedIndex).Take(index - previousOccupiedIndex + 1).Reverse().ToArray();
        Require(movement[^1] == vacancy, "A shift does not end in the actual vacancy.");
        parked.Remove(id);
        CheckMovement(id, movement, parked);
        recoveryTicks += movement.Length - 1;
        parked.Add(id, vacancy);
        changes.Add(new { id, source, destination = vacancy, cells = movement });
        vacancy = source;
        previousOccupiedIndex = index;
    }

    free = FreeReachable(spawn, parked.Values.ToHashSet());
    Require(free.ContainsKey(vacancy), "Vacancy propagation failed to connect the remaining goal to spawn.");
    var reinforcement = new List<int>();
    for (int cell = vacancy; cell >= 0; cell = free[cell])
    {
        reinforcement.Add(cell);
    }

    reinforcement.Reverse();
    CheckMovement(1000 + victim.Key, reinforcement.ToArray(), parked);
    recoveryTicks += reinforcement.Count - 1;
    parked.Add(1000 + victim.Key, vacancy);
    Require(parked.Values.ToHashSet().SetEquals(goals), "Replacement changed or lost final target cells.");
    Require(parked.Count == 72 && parked.Values.Count(cell => eligible[cell]) == 12, "Replacement did not restore full occupancy and attack count.");
    replacementCases.Add(new
    {
        victim = victim.Key,
        victimGoal = victim.Value,
        recoveryTicks,
        changes,
        replacementId = 1000 + victim.Key,
        reinforcement
    });
}

parked = completed;
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    success = true,
    scope = "Independent constructive feasibility witness on the unchanged original 4m/72 fixture, not the production planner.",
    referenceCommit = "f67e8e6cdaea6b89c289433650a7ead8589ec692",
    utc = DateTime.UtcNow.ToString("O"),
    seed,
    originalAdmissionOrder,
    unitCount = 72,
    originalScenario = scenario.Name,
    gatheringDistance = scenario.GatheringDistance,
    unitRadius = radius,
    attackRange,
    fixedSpawnAssignmentPreserved = true,
    cellSize = grid.CellSize,
    width = grid.Width,
    height = grid.Height,
    target = new { grid.TargetCenter.X, grid.TargetCenter.Y, Radius = grid.TargetRadius },
    finalTick = tick,
    attacking = goals.Count(cell => eligible[cell]),
    waiting = goals.Count(cell => !eligible[cell]),
    checkedPairs,
    checkedWalls,
    minimumPairGap,
    minimumBaseGap,
    paths = reverse,
    deathReplacementCases = replacementCases
}, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Console.WriteLine("WITNESS PASS all=" + parked.Count + " ticks=" + tick + " pairSweeps=" + checkedPairs
    + " wallSweeps=" + checkedWalls + " minimumPairGap=" + minimumPairGap + " output=" + output);
Console.WriteLine("FULLY PACKED REPLACEMENT PASS victims=" + replacementCases.Count + " restored=72 attackerCount=12 for every case");

var forest = NavigationLayout.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes")));
int holding = Enumerable.Range(0, forest.CellCount).Count(forest.HoldingAllowed);
int body250Goals = Enumerable.Range(0, forest.CellCount).Count(cell => forest.HoldingAllowed(cell) && forest.CanOccupy(cell, 250));
int body180NearCells = Enumerable.Range(0, forest.CellCount).Count(cell => forest.CanOccupy(cell, 180)
    && Squared(forest.Center(cell), forest.TargetCenter) <= (long)(forest.TargetRadius + 4000) * (forest.TargetRadius + 4000));
Console.WriteLine("FOREST current holding cells=" + holding + " radius250=" + body250Goals + " allWalkableNearBaseForRadius180=" + body180NearCells);
foreach (int body in new[] { 180, 200, 240, 250 })
{
    Console.WriteLine("FOREST body=" + body + " legalHolding=" + Enumerable.Range(0, forest.CellCount)
        .Count(cell => forest.HoldingAllowed(cell) && forest.CanOccupy(cell, body)));
}
Console.WriteLine("FOREST 126 cannot fit at most 46 valid exclusive goal cells for the actual radii under the current discrete goal contract.");
Console.WriteLine("This is not a proof that continuous physical placement is impossible; the radius250 subset has only 44 cells.");

Dictionary<int, int> FreeReachable(int spawn, HashSet<int> occupiedCells)
{
    var parents = new Dictionary<int, int> { [spawn] = -1 };
    var queue = new Queue<int>();
    queue.Enqueue(spawn);
    while (queue.Count > 0)
    {
        int cell = queue.Dequeue();
        foreach (int next in grid.Neighbours(cell))
        {
            if (occupiedCells.Contains(next) || parents.ContainsKey(next) || !grid.CanTraverse(cell, next, radius))
            {
                continue;
            }

            parents.Add(next, cell);
            queue.Enqueue(next);
        }
    }

    return parents;
}

void CheckMovement(int actor, int[] movement, Dictionary<int, int> others)
{
    for (int index = 1; index < movement.Length; index++)
    {
        var from = grid.Center(movement[index - 1]);
        var to = grid.Center(movement[index]);
        Require(grid.CanTraverse(movement[index - 1], movement[index], radius), "A vacancy shift uses a non-edge.");
        Require(!others.Values.Contains(movement[index]), "A vacancy shift crossed an occupied cell.");
        foreach (int occupiedCell in others.Values)
        {
            double distance = SegmentDistanceSquared(from, to, grid.Center(occupiedCell));
            Require(distance >= 4L * radius * radius, "Vacancy shift collision for " + actor);
            minimumPairGap = Math.Min(minimumPairGap, Math.Sqrt(distance) - 2 * radius);
            checkedPairs++;
        }

        double baseDistance = SegmentDistanceSquared(from, to, grid.TargetCenter);
        Require(baseDistance >= (long)(grid.TargetRadius + radius) * (grid.TargetRadius + radius), "Vacancy shift penetrated the base.");
        minimumBaseGap = Math.Min(minimumBaseGap, Math.Sqrt(baseDistance) - grid.TargetRadius - radius);
        foreach (int cell in Enumerable.Range(0, grid.CellCount).Where(cell => !grid.IsWalkable(cell)))
        {
            int left = grid.Origin.X + cell % grid.Width * grid.CellSize;
            int bottom = grid.Origin.Y + cell / grid.Width * grid.CellSize;
            Require(AxisSegmentBoxDistanceSquared(from, to, left, bottom, grid.CellSize) >= (long)radius * radius,
                "Vacancy shift penetrated blocked terrain.");
            checkedWalls++;
        }
    }
}

static long Squared(Position2 first, Position2 second)
{
    long x = (long)first.X - second.X;
    long y = (long)first.Y - second.Y;
    return x * x + y * y;
}

static double SegmentDistanceSquared(Position2 from, Position2 to, Position2 point)
{
    double dx = to.X - from.X;
    double dy = to.Y - from.Y;
    double t = Math.Clamp(((point.X - from.X) * dx + (point.Y - from.Y) * dy) / (dx * dx + dy * dy), 0, 1);
    double x = from.X + t * dx - point.X;
    double y = from.Y + t * dy - point.Y;
    return x * x + y * y;
}

static double AxisSegmentBoxDistanceSquared(Position2 from, Position2 to, int left, int bottom, int size)
{
    Require(from.X == to.X || from.Y == to.Y, "This oracle expects a four-neighbour edge.");
    double dx = Math.Max(0, Math.Max(left - Math.Max(from.X, to.X), Math.Min(from.X, to.X) - left - size));
    double dy = Math.Max(0, Math.Max(bottom - Math.Max(from.Y, to.Y), Math.Min(from.Y, to.Y) - bottom - size));
    return dx * dx + dy * dy;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static WitnessScenario CreateScenario(int seed)
{
    const int width = 37;
    const int height = 37;
    var cells = new bool[width * height];
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            bool blocked = !(y >= 17 && y <= 19) && !(x >= 17 && x <= 19) && !(x >= 22 && y >= 10 && y <= 26);
            cells[y * width + x] = x > 0 && y > 0 && x < width - 1 && y < height - 1 && !blocked;
        }
    }

    return new WitnessScenario
    {
        Width = width,
        Height = height,
        Walkable = cells,
        Target = new Cell(29, 18),
        SpawnCells = new[] { new Cell(2, 18), new Cell(18, 2), new Cell(18, 34) },
        Seed = seed
    };
}

internal sealed record WitnessPath(int Id, int Spawn, int Goal, int[] Cells)
{
    public long StartTick;
    public long ArrivalTick;
}

internal readonly record struct Cell(int X, int Y);

internal sealed class WitnessScenario
{
    public string Name => "cross-junction-original-4m";
    public int UnitCount => 72;
    public int UnitRadius => 250;
    public int GatheringDistance => 4000;
    public int Width { get; init; }
    public int Height { get; init; }
    public bool[] Walkable { get; init; } = Array.Empty<bool>();
    public Cell Target { get; init; }
    public Cell[] SpawnCells { get; init; } = Array.Empty<Cell>();
    public int Seed { get; init; }
}
