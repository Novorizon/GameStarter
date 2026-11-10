using System.Security.Cryptography;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

int rootArgument = Array.IndexOf(args, "--root");
string repositoryRoot = Path.GetFullPath(rootArgument >= 0 ? args[rootArgument + 1] : Directory.GetCurrentDirectory());
int outputArgument = Array.IndexOf(args, "--output");
string outputRoot = Path.GetFullPath(outputArgument >= 0 ? args[outputArgument + 1] : "Artifacts/NavigationFeasibility");
Directory.CreateDirectory(outputRoot);
string output = Path.Combine(outputRoot, "forest-44-big-2-late-small-witness.json");
File.Delete(output);
string asset = Path.Combine(repositoryRoot, "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes");
byte[] mapBytes = File.ReadAllBytes(asset);
var grid = NavigationLayout.Parse(System.Text.Encoding.UTF8.GetString(mapBytes));
string initialEvidence = "Verification~/NavigationFeasibility/Evidence/forest-r250-range550-all-legal-witness.json";
using var initial = JsonDocument.Parse(File.ReadAllText(initialEvidence));
string mapSha = Convert.ToHexString(SHA256.HashData(mapBytes)).ToLowerInvariant();
Require(initial.RootElement.GetProperty("MapSha256").GetString() == mapSha, "Initial saturation proof uses another map.");
var actors = initial.RootElement.GetProperty("Admission").EnumerateArray().Select(value =>
    new Actor(value.GetProperty("Id").GetInt32(), 250, 550, 55, value.GetProperty("Cells").EnumerateArray().Last().GetInt32(), true))
    .ToDictionary(value => value.Id);
Require(actors.Count == 44, "The initial proof must have all 44 Big bodies.");
var originalBig = actors.ToDictionary(pair => pair.Key, pair => pair.Value.Cell);
int spawn = grid.SpawnCells.Single();
int[] smallTargets = Enumerable.Range(0, grid.CellCount).Where(cell => grid.HoldingAllowed(cell)
    && grid.CanOccupy(cell, 180) && !grid.CanOccupy(cell, 250)).ToArray();
Require(smallTargets.Length == 2, "Expected exactly two Small-only compatible holding cells.");
Require(smallTargets.All(cell => grid.HasAttackLine(cell, 180, 450)), "A Small-only final cell is not an actual attack position.");
var initialBigCells = originalBig.Values.ToHashSet();
List<int[]> bestSmallPaths = null;
HashSet<int> blockers = null;
long bestScore = long.MaxValue;
foreach (int[] order in new[] { smallTargets, smallTargets.Reverse().ToArray() })
{
    var first = SoftPath(spawn, order[0], 180, initialBigCells, new HashSet<int>());
    var second = SoftPath(spawn, order[1], 180, initialBigCells, new HashSet<int> { order[0] });
    var blocked = first.Concat(second).Where(initialBigCells.Contains).ToHashSet();
    long score = blocked.Count * 1000000L + first.Length + second.Length;
    if (score < bestScore)
    {
        bestScore = score;
        bestSmallPaths = new List<int[]> { first, second };
        blockers = blocked;
    }
}

Require(bestSmallPaths != null && blockers != null, "No prospective Small route was found.");
var pending = originalBig.Where(pair => blockers!.Contains(pair.Value)).Select(pair => pair.Key).ToHashSet();
int[] originalGateOwners = pending.OrderBy(id => id).ToArray();
var unmoved = originalBig.ToDictionary(pair => pair.Key, pair => pair.Value);
var evacuationOrder = new List<int>();
var protectedPaths = bestSmallPaths!.SelectMany(path => path).ToHashSet();
while (pending.Any(unmoved.ContainsKey))
{
    Require(evacuationOrder.Count <= 44, "Evacuation dependency closure did not progress.");
    bool removed = false;
    foreach (int owner in pending.Where(unmoved.ContainsKey).OrderBy(id => id))
    {
        var tree = Flood(unmoved[owner], 250, unmoved.Where(pair => pair.Key != owner).Select(pair => pair.Value).ToHashSet());
        if (!tree.ContainsKey(spawn))
        {
            continue;
        }

        protectedPaths.UnionWith(TracePath(tree, spawn));
        evacuationOrder.Add(owner);
        unmoved.Remove(owner);
        removed = true;
        break;
    }

    if (removed)
    {
        continue;
    }

    int desired = pending.Where(unmoved.ContainsKey).Min();
    var route = SoftPath(unmoved[desired], spawn, 250, unmoved.Values.ToHashSet(), new HashSet<int>());
    int dependencyCell = route.Skip(1).Where(cell => unmoved.Values.Contains(cell)).LastOrDefault(-1);
    Require(dependencyCell >= 0, "A blocked gate owner has no blocking dependency to clear.");
    int dependency = unmoved.Single(pair => pair.Value == dependencyCell).Key;
    Require(pending.Add(dependency), "Evacuation dependency closure repeated an unresolved owner.");
}

var outside = Flood(spawn, 250, initialBigCells).Keys.ToHashSet();
var whole = Flood(spawn, 250, new HashSet<int>()).Keys.ToHashSet();
var buffers = new HashSet<int>();
var bufferCandidates = outside.Where(cell => cell != spawn && !grid.HoldingAllowed(cell) && !protectedPaths.Contains(cell))
    .OrderBy(cell => Squared(grid.Center(cell), grid.TargetCenter)).ThenBy(cell => cell).ToArray();
foreach (int candidate in bufferCandidates)
{
    if (buffers.Count == evacuationOrder.Count)
    {
        break;
    }

    if (grid.Neighbours(candidate).Any(buffers.Contains))
    {
        continue;
    }

    var next = buffers.Append(candidate).ToHashSet();
    var outsideBlocked = initialBigCells.Concat(next).ToHashSet();
    if (Flood(spawn, 250, outsideBlocked).Count != outside.Count - next.Count || Flood(spawn, 250, next).Count != whole.Count - next.Count)
    {
        continue;
    }

    buffers.Add(candidate);
}

Require(buffers.Count == evacuationOrder.Count, "Not enough certified non-holding buffer cells were found.");
long tick = initial.RootElement.GetProperty("AllArrivedTick").GetInt64() + 200;
long lateRequestsTick = tick;
long checkedPairs = 0;
long checkedWalls = 0;
double minimumBodyGap = double.PositiveInfinity;
double minimumBaseGap = double.PositiveInfinity;
var movements = new List<Move>();
var evacuation = new List<Move>();
foreach (int owner in evacuationOrder)
{
    var actor = actors[owner];
    int[] chosen = null;
    foreach (int destination in buffers.Except(actors.Values.Select(value => value.Cell)).OrderBy(cell => cell))
    {
        var blocked = actors.Values.Where(value => value.Id != owner).Select(value => value.Cell)
            .Concat(buffers.Where(cell => cell != destination)).ToHashSet();
        var tree = Flood(actor.Cell, actor.Radius, blocked);
        if (tree.ContainsKey(destination))
        {
            var route = TracePath(tree, destination);
            if (chosen == null || route.Length < chosen.Length)
            {
                chosen = route;
            }
        }
    }

    Require(chosen != null, "A gate dependency cannot physically reach a certified buffer.");
    evacuation.Add(Execute(actor, chosen!, "temporary-yield"));
}

Require(actors.Values.Count(actor => actor.Big) == 44, "The evacuation removed a Big actor.");
for (int index = 0; index < bestSmallPaths.Count; index++)
{
    var small = new Actor(45 + index, 180, 450, 165, spawn, false);
    actors.Add(small.Id, small);
    Execute(small, bestSmallPaths[index], "late-small-entry");
}

foreach (var movement in evacuation.AsEnumerable().Reverse())
{
    Execute(actors[movement.Id], movement.Cells.Reverse().ToArray(), "return-original-goal");
}

Require(actors.Count == 46 && actors.Values.Count(actor => actor.Big) == 44, "Final actor conservation failed.");
Require(originalBig.All(pair => actors[pair.Key].Cell == pair.Value), "A Big did not return to its exact original holding cell.");
Require(actors.Values.All(actor => grid.HoldingAllowed(actor.Cell) && grid.CanOccupy(actor.Cell, actor.Radius)), "A final body is not on a compatible goal.");
Require(actors.Values.Select(actor => actor.Cell).Distinct().Count() == 46, "Two final actors share a cell.");
var result = new
{
    success = true,
    utc = DateTime.UtcNow.ToString("O"),
    scope = "Constructive late-arrival witness; no production or Unity execution claim. "
        + "All 44 initial Big actors are preserved and return to their original goals.",
    mapSha256 = mapSha,
    initialSaturationEvidenceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(initialEvidence))).ToLowerInvariant(),
    initialBigCount = 44,
    initialBigArrivedTick = initial.RootElement.GetProperty("AllArrivedTick").GetInt64(),
    lateRequestsTick,
    finalTick = tick,
    finalCount = actors.Count,
    finalAttacking = actors.Values.Count(actor => grid.HasAttackLine(actor.Cell, actor.Radius, actor.Range)),
    smallOnlyTargets = smallTargets,
    prospectiveSmallRoutes = bestSmallPaths,
    originalGateOwners,
    evacuationOrder,
    temporaryNonHoldingBuffers = buffers.OrderBy(cell => cell).ToArray(),
    checkedPairs,
    checkedWalls,
    minimumBodyGap,
    minimumBaseGap,
    movements,
    finalActors = actors.Values.OrderBy(actor => actor.Id).ToArray()
};
File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
Console.WriteLine("LATE WITNESS PASS initialBig=44 lateSmall=2 final=46 yieldedBig=" + evacuation.Count + " gateOwners=" + originalGateOwners.Length
    + " ticksAfterLateRequest=" + (tick - lateRequestsTick) + " bodyGap=" + minimumBodyGap + " output=" + output);

int[] SoftPath(int start, int target, int radius, HashSet<int> softOccupied, HashSet<int> hardBlocked)
{
    var distance = new Dictionary<int, long> { [start] = 0 };
    var parent = new Dictionary<int, int> { [start] = -1 };
    var frontier = new PriorityQueue<int, (long Cost, int Cell)>();
    frontier.Enqueue(start, (0, start));
    while (frontier.TryDequeue(out int cell, out var priority))
    {
        if (distance[cell] != priority.Cost)
        {
            continue;
        }

        if (cell == target)
        {
            return TracePath(parent, target);
        }

        foreach (int next in grid.Neighbours(cell))
        {
            if (hardBlocked.Contains(next) || !grid.CanTraverse(cell, next, radius))
            {
                continue;
            }

            long cost = priority.Cost + 1 + (softOccupied.Contains(next) ? 10000 : 0);
            if (!distance.TryGetValue(next, out long known) || cost < known)
            {
                distance[next] = cost;
                parent[next] = cell;
                frontier.Enqueue(next, (cost, next));
            }
        }
    }

    throw new InvalidOperationException("A prospective route is statically unreachable.");
}

Dictionary<int, int> Flood(int start, int radius, HashSet<int> blocked)
{
    var parent = new Dictionary<int, int> { [start] = -1 };
    var queue = new Queue<int>();
    queue.Enqueue(start);
    while (queue.Count > 0)
    {
        int cell = queue.Dequeue();
        foreach (int next in grid.Neighbours(cell))
        {
            if (!parent.ContainsKey(next) && !blocked.Contains(next) && grid.CanTraverse(cell, next, radius))
            {
                parent.Add(next, cell);
                queue.Enqueue(next);
            }
        }
    }

    return parent;
}

static int[] TracePath(Dictionary<int, int> parent, int target)
{
    var path = new List<int>();
    for (int cell = target; cell >= 0; cell = parent[cell])
    {
        path.Add(cell);
    }

    path.Reverse();
    return path.ToArray();
}

Move Execute(Actor actor, int[] cells, string phase)
{
    Require(cells[0] == actor.Cell, "A movement did not start at the actual actor cell.");
    var movement = new Move(actor.Id, actor.Radius, actor.Speed, phase, cells, tick);
    int duration = (grid.CellSize + actor.Speed - 1) / actor.Speed;
    for (int index = 1; index < cells.Length; index++)
    {
        Require(grid.CanTraverse(cells[index - 1], cells[index], actor.Radius), "A movement does not use a legal body edge.");
        var from = grid.Center(cells[index - 1]);
        var to = grid.Center(cells[index]);
        foreach (var other in actors.Values.Where(other => other.Id != actor.Id))
        {
            double distance = SegmentDistanceSquared(from, to, grid.Center(other.Cell));
            long radius = actor.Radius + other.Radius;
            Require(distance + .000001 >= radius * radius, "A swept body overlapped actor " + other.Id);
            minimumBodyGap = Math.Min(minimumBodyGap, Math.Sqrt(distance) - radius);
            checkedPairs++;
        }

        double baseDistance = SegmentDistanceSquared(from, to, grid.TargetCenter);
        long clearance = grid.TargetRadius + actor.Radius;
        Require(baseDistance + .000001 >= clearance * clearance, "A body penetrated the base.");
        minimumBaseGap = Math.Min(minimumBaseGap, Math.Sqrt(baseDistance) - clearance);
        Require(Math.Min(from.X, to.X) - actor.Radius >= grid.Origin.X
            && Math.Max(from.X, to.X) + actor.Radius <= grid.Origin.X + (long)grid.Width * grid.CellSize, "A body left world X bounds.");
        Require(Math.Min(from.Y, to.Y) - actor.Radius >= grid.Origin.Y
            && Math.Max(from.Y, to.Y) + actor.Radius <= grid.Origin.Y + (long)grid.Height * grid.CellSize, "A body left world Y bounds.");
        foreach (int blocked in Enumerable.Range(0, grid.CellCount).Where(cell => !grid.IsWalkable(cell)))
        {
            int left = grid.Origin.X + blocked % grid.Width * grid.CellSize;
            int bottom = grid.Origin.Y + blocked / grid.Width * grid.CellSize;
            long dx = Math.Max(0, Math.Max(left - Math.Max(from.X, to.X), Math.Min(from.X, to.X) - left - grid.CellSize));
            long dy = Math.Max(0, Math.Max(bottom - Math.Max(from.Y, to.Y), Math.Min(from.Y, to.Y) - bottom - grid.CellSize));
            Require(dx * dx + dy * dy >= (long)actor.Radius * actor.Radius, "A body swept through blocked terrain.");
            checkedWalls++;
        }

        Require(grid.CellSize <= (long)actor.Speed * duration, "The route timing exceeds the actual movement speed.");
        tick += duration;
        actor.Cell = cells[index];
    }

    movement.Arrival = tick;
    movements.Add(movement);
    return movement;
}

static long Squared(Position2 first, Position2 second)
{
    long x = (long)first.X - second.X;
    long y = (long)first.Y - second.Y;
    return x * x + y * y;
}

static double SegmentDistanceSquared(Position2 from, Position2 to, Position2 point)
{
    double dx = (double)to.X - from.X;
    double dy = (double)to.Y - from.Y;
    double denominator = dx * dx + dy * dy;
    double time = denominator == 0 ? 0 : Math.Clamp(((point.X - from.X) * dx + (point.Y - from.Y) * dy) / denominator, 0, 1);
    double x = from.X + dx * time - point.X;
    double y = from.Y + dy * time - point.Y;
    return x * x + y * y;
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record Actor(int Id, int Radius, int Range, int Speed, int OriginalCell, bool Big)
{
    public int Cell = OriginalCell;
}

internal sealed record Move(int Id, int Radius, int Speed, string Phase, int[] Cells, long Start)
{
    public long Arrival;
}
