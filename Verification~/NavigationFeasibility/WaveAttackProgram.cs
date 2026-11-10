using System.Security.Cryptography;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

int inputArgument = Array.IndexOf(args, "--input");
int rootArgument = Array.IndexOf(args, "--root");
int outputArgument = Array.IndexOf(args, "--output");
string root = rootArgument >= 0 ? args[rootArgument + 1] : Directory.GetCurrentDirectory();
string input = inputArgument >= 0 ? args[inputArgument + 1] : "Artifacts/NavigationFeasibility/wave-matching-input.json";
string outputRoot = outputArgument >= 0 ? args[outputArgument + 1] : "Artifacts/NavigationFeasibility/Waves";
Directory.CreateDirectory(outputRoot);
string summaryPath = Path.Combine(outputRoot, "summary.json");
File.Delete(summaryPath);
byte[] mapBytes = File.ReadAllBytes(Path.Combine(root, "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
var grid = NavigationLayout.Parse(System.Text.Encoding.UTF8.GetString(mapBytes));
using var document = JsonDocument.Parse(File.ReadAllText(input));
string mapSha = Convert.ToHexString(SHA256.HashData(mapBytes)).ToLowerInvariant();
if (document.RootElement.GetProperty("mapSha256").GetString() != mapSha)
{
    throw new InvalidDataException("Regenerate the typed assignment for the current navigation map.");
}

foreach (var source in document.RootElement.GetProperty("sourceSha256").EnumerateObject())
{
    byte[] current = File.ReadAllBytes(Path.Combine(root, "Config/Luban/Data", source.Name + ".xlsx"));
    if (Convert.ToHexString(SHA256.HashData(current)).ToLowerInvariant() != source.Value.GetString())
    {
        throw new InvalidDataException("Regenerate the typed assignment for the current table: " + source.Name);
    }
}

var independentCells = document.RootElement.GetProperty("independentAttackCells").EnumerateObject()
    .ToDictionary(value => int.Parse(value.Name), value => value.Value.EnumerateArray().Select(cell => cell.GetInt32()).ToArray());
var allResults = new List<WaveResult>();
foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
{
    string name = item.GetProperty("name").GetString()!;
    string output = Path.Combine(outputRoot, name + ".json");
    File.Delete(output);
    var units = item.GetProperty("units").EnumerateArray().Select(value => new Unit(
        value.GetProperty("id").GetInt32(),
        value.GetProperty("enemyId").GetInt32(),
        value.GetProperty("radius").GetInt32(),
        value.GetProperty("range").GetInt32(),
        value.GetProperty("speed").GetInt32(),
        value.GetProperty("release").GetInt32(),
        value.GetProperty("goal").GetInt32(),
        value.GetProperty("attack").GetBoolean())).ToArray();
    var witness = new WaveWitness(grid, units, independentCells);
    var result = witness.Run(name, item.GetProperty("expectedMaximumAttack").GetInt32());
    result.MapSha256 = mapSha;
    File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
    allResults.Add(result);
    Console.WriteLine("WAVE WITNESS PASS " + name + " units=" + units.Length + " maximumAttack=" + result.MaximumAttack
        + " waiting=" + (units.Length - result.MaximumAttack) + " allArrivedTick=" + result.AllArrivedTick
        + " pairSweeps=" + result.CheckedRelativeSegments + " minimumGap=" + result.MinimumBodyGap);
}

File.WriteAllText(summaryPath, JsonSerializer.Serialize(new
{
    success = allResults.Count == 7,
    utc = DateTime.UtcNow.ToString("O"),
    scope = "Maximum typed attack matching plus actual collision-free admission schedules; not production or Unity execution.",
    sourceTables = document.RootElement.GetProperty("sourceSha256"),
    cases = allResults.Select(result => new
    {
        result.Name, result.UnitCount, result.MaximumAttack, result.AllArrivedTick, result.CheckedRelativeSegments,
        result.MinimumBodyGap, result.MinimumBaseGap, result.MapSha256
    })
}, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));

internal sealed record Unit(int Id, int Enemy, int Radius, int Range, int Speed, int Release, int Goal, bool Attack);
internal sealed record Route(Unit Unit, int[] Cells)
{
    public long Start;
    public long Arrival;
}

internal sealed class WaveResult
{
    public bool Success = true;
    public string Name = "";
    public string MapSha256 = "";
    public int UnitCount;
    public int MaximumAttack;
    public bool IndependentAttackCellSetsAgree = true;
    public Dictionary<int, int[]> AttackCells = new Dictionary<int, int[]>();
    public long AllArrivedTick;
    public long CheckedRelativeSegments;
    public long CheckedStaticSegments;
    public long CheckedBlockedCells;
    public double MinimumBodyGap = double.PositiveInfinity;
    public double MinimumBaseGap = double.PositiveInfinity;
    public List<Route> Routes = new List<Route>();
}

internal sealed class WaveWitness
{
    private const long Forever = long.MaxValue / 4;
    private readonly NavigationGrid grid;
    private readonly Unit[] units;
    private readonly int spawn;
    private readonly Dictionary<int, int[]> attacks = new Dictionary<int, int[]>();
    private readonly WaveResult result = new WaveResult();

    public WaveWitness(NavigationGrid grid, Unit[] units, Dictionary<int, int[]> independent)
    {
        this.grid = grid;
        this.units = units;
        spawn = grid.SpawnCells.Single();
        foreach (var unit in units.GroupBy(unit => unit.Enemy).Select(group => group.First()))
        {
            attacks[unit.Enemy] = Enumerable.Range(0, grid.CellCount).Where(cell => grid.HoldingAllowed(cell)
                && grid.HasAttackLine(cell, unit.Radius, unit.Range)).ToArray();
            Require(attacks[unit.Enemy].ToHashSet().SetEquals(independent[unit.Enemy]),
                "Independent ray/circle geometry disagrees with the actual C# attack-cell set.");
        }

        result.AttackCells = attacks;
    }

    public WaveResult Run(string name, int expected)
    {
        result.Name = name;
        result.UnitCount = units.Length;
        var owners = new Dictionary<int, Unit>();
        foreach (var unit in units.OrderBy(unit => attacks[unit.Enemy].Length).ThenBy(unit => unit.Id))
        {
            if (Augment(unit, new HashSet<int>(), owners))
            {
                result.MaximumAttack++;
            }
        }

        Require(result.MaximumAttack == expected, "Independent C# maximum matching disagrees with input.");
        Require(units.Count(unit => grid.HasAttackLine(unit.Goal, unit.Radius, unit.Range)) == result.MaximumAttack,
            "The supplied full assignment does not attain maximum attack count.");
        Require(units.Select(unit => unit.Goal).Distinct().Count() == units.Length, "Assigned targets overlap.");
        foreach (var unit in units)
        {
            Require(grid.HoldingAllowed(unit.Goal) && grid.CanOccupy(unit.Goal, unit.Radius), "A body is assigned an invalid target.");
            Require(Squared(grid.Center(unit.Goal), grid.TargetCenter) <= (long)(grid.TargetRadius + 4000) * (grid.TargetRadius + 4000),
                "A waiting target is outside the unchanged gathering range.");
        }

        var remaining = units.ToDictionary(unit => unit.Id);
        var reversed = new List<Route>();
        while (remaining.Count > 0)
        {
            Route chosen = null;
            foreach (var unit in remaining.Values.OrderByDescending(unit => unit.Release).ThenByDescending(unit => unit.Id))
            {
                var tree = Flood(spawn, unit.Radius, remaining.Values.Where(other => other.Id != unit.Id).Select(other => other.Goal).ToHashSet());
                if (tree.ContainsKey(unit.Goal))
                {
                    chosen = new Route(unit, Path(tree, unit.Goal));
                    break;
                }
            }

            Require(chosen != null, "Typed final positions have no constructive evacuation step.");
            reversed.Add(chosen!);
            remaining.Remove(chosen!.Unit.Id);
        }

        reversed.Reverse();
        var reservations = new Dictionary<int, List<(long Start, long End)>>();
        long previousStart = -1;
        foreach (var route in reversed)
        {
            CheckStatic(route);
            int duration = (grid.CellSize + route.Unit.Speed - 1) / route.Unit.Speed;
            var slots = new List<(int Cell, long Start, long End)>();
            for (int index = 1; index < route.Cells.Length; index++)
            {
                slots.Add((route.Cells[index - 1], (index - 1L) * duration, index * (long)duration));
                slots.Add((route.Cells[index], (index - 1L) * duration, index * (long)duration));
            }

            long arrival = (route.Cells.Length - 1L) * duration;
            slots.Add((route.Unit.Goal, arrival, Forever));
            long start = Math.Max(previousStart + 1, route.Unit.Release);
            for (int attempt = 0; ; attempt++)
            {
                Require(attempt < 100000, "Finite timed admission search did not finish.");
                long nextStart = start;
                foreach (var slot in slots)
                {
                    if (!reservations.TryGetValue(slot.Cell, out var intervals))
                    {
                        continue;
                    }

                    long end = slot.End == Forever ? Forever : start + slot.End;
                    foreach (var old in intervals)
                    {
                        if (start + slot.Start < old.End && old.Start < end)
                        {
                            Require(old.End != Forever, "A constructive path crosses an earlier actor's final target.");
                            nextStart = Math.Max(nextStart, old.End - slot.Start);
                        }
                    }
                }

                if (nextStart == start)
                {
                    break;
                }

                start = nextStart;
            }

            route.Start = start;
            route.Arrival = start + arrival;
            previousStart = start;
            foreach (var slot in slots)
            {
                if (!reservations.TryGetValue(slot.Cell, out var intervals))
                {
                    intervals = new List<(long, long)>();
                    reservations.Add(slot.Cell, intervals);
                }

                intervals.Add((start + slot.Start, slot.End == Forever ? Forever : start + slot.End));
            }
        }

        result.Routes = reversed;
        result.AllArrivedTick = reversed.Max(route => route.Arrival);
        CheckTimeline();
        return result;
    }

    private bool Augment(Unit unit, HashSet<int> seen, Dictionary<int, Unit> owners)
    {
        foreach (int cell in attacks[unit.Enemy])
        {
            if (seen.Add(cell) && (!owners.TryGetValue(cell, out var previous) || Augment(previous, seen, owners)))
            {
                owners[cell] = unit;
                return true;
            }
        }

        return false;
    }

    private Dictionary<int, int> Flood(int start, int radius, HashSet<int> blocked)
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

    private static int[] Path(Dictionary<int, int> parent, int target)
    {
        var path = new List<int>();
        for (int cell = target; cell >= 0; cell = parent[cell])
        {
            path.Add(cell);
        }

        path.Reverse();
        return path.ToArray();
    }

    private Position2 Position(Route route, long tick)
    {
        if (tick >= route.Arrival)
        {
            return grid.Center(route.Unit.Goal);
        }

        int duration = (grid.CellSize + route.Unit.Speed - 1) / route.Unit.Speed;
        long elapsed = tick - route.Start;
        int segment = (int)(elapsed / duration);
        var from = grid.Center(route.Cells[segment]);
        var to = grid.Center(route.Cells[segment + 1]);
        long remainder = elapsed % duration;
        return new Position2(from.X + (int)((to.X - from.X) * remainder / duration), from.Y + (int)((to.Y - from.Y) * remainder / duration));
    }

    private void CheckTimeline()
    {
        for (long tick = 0; tick <= result.AllArrivedTick + 200; tick++)
        {
            var active = result.Routes.Where(route => route.Start <= tick).ToArray();
            var from = active.Select(route => Position(route, tick)).ToArray();
            var to = active.Select(route => Position(route, tick + 1)).ToArray();
            for (int first = 0; first < active.Length; first++)
            {
                Require(active[first].Start >= active[first].Unit.Release, "A request spawned before its actual authored release tick.");
                Require(Squared(from[first], to[first]) <= (long)active[first].Unit.Speed * active[first].Unit.Speed, "Actual speed was exceeded.");
                for (int second = first + 1; second < active.Length; second++)
                {
                    long radius = active[first].Unit.Radius + active[second].Unit.Radius;
                    double distance = Relative(from[first], to[first], from[second], to[second]);
                    Require(distance + .000001 >= radius * radius, "Two actual moving bodies overlapped.");
                    result.MinimumBodyGap = Math.Min(result.MinimumBodyGap, Math.Sqrt(distance) - radius);
                    result.CheckedRelativeSegments++;
                }
            }
        }

        Require(result.Routes.Count == units.Length, "The constructive schedule dropped a requested unit.");
    }

    private void CheckStatic(Route route)
    {
        int radius = route.Unit.Radius;
        var blocked = Enumerable.Range(0, grid.CellCount).Where(cell => !grid.IsWalkable(cell)).ToArray();
        for (int index = 1; index < route.Cells.Length; index++)
        {
            var from = grid.Center(route.Cells[index - 1]);
            var to = grid.Center(route.Cells[index]);
            Require(grid.CanTraverse(route.Cells[index - 1], route.Cells[index], radius), "A planned body used a non-traversable edge.");
            long clearance = grid.TargetRadius + radius;
            double distance = Relative(from, to, grid.TargetCenter, grid.TargetCenter);
            Require(distance + .000001 >= clearance * clearance, "A body swept through the base.");
            result.MinimumBaseGap = Math.Min(result.MinimumBaseGap, Math.Sqrt(distance) - clearance);
            foreach (int cell in blocked)
            {
                int left = grid.Origin.X + cell % grid.Width * grid.CellSize;
                int bottom = grid.Origin.Y + cell / grid.Width * grid.CellSize;
                long dx = Math.Max(0, Math.Max(left - Math.Max(from.X, to.X), Math.Min(from.X, to.X) - left - grid.CellSize));
                long dy = Math.Max(0, Math.Max(bottom - Math.Max(from.Y, to.Y), Math.Min(from.Y, to.Y) - bottom - grid.CellSize));
                Require(dx * dx + dy * dy >= (long)radius * radius, "A body swept through a blocked tile.");
                result.CheckedBlockedCells++;
            }

            result.CheckedStaticSegments++;
        }
    }

    private static long Squared(Position2 first, Position2 second)
    {
        long x = (long)first.X - second.X;
        long y = (long)first.Y - second.Y;
        return x * x + y * y;
    }

    private static double Relative(Position2 a0, Position2 a1, Position2 b0, Position2 b1)
    {
        double x = (double)a0.X - b0.X;
        double y = (double)a0.Y - b0.Y;
        double dx = (double)a1.X - a0.X - b1.X + b0.X;
        double dy = (double)a1.Y - a0.Y - b1.Y + b0.Y;
        double denominator = dx * dx + dy * dy;
        double time = denominator == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / denominator, 0, 1);
        x += time * dx;
        y += time * dy;
        return x * x + y * y;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
