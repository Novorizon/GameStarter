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
string asset = Path.Combine(repositoryRoot, "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes");
byte[] bytes = File.ReadAllBytes(asset);
string mapSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var grid = NavigationLayout.Parse(System.Text.Encoding.UTF8.GetString(bytes));
bool allLegalAttackCells = Array.IndexOf(args, "--all-legal-attack-cells") >= 0;
foreach (var spec in new[] { new UnitSpec(250, 550, 55), new UnitSpec(200, 450, 100) })
{
    string suffix = allLegalAttackCells ? "-all-legal" : "";
    string output = Path.Combine(outputRoot, "forest-r" + spec.Radius + "-range" + spec.Range + suffix + "-witness.json");
    File.Delete(output);
    var result = new ForestWitness(grid, spec, allLegalAttackCells).Run(mapSha);
    File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true }));
    Console.WriteLine("FOREST WITNESS PASS r=" + spec.Radius + " range=" + spec.Range + " speed=" + spec.Speed
        + " goals=" + result.Goals.Length + " attack=" + result.AttackCount + " waiting=" + result.WaitingCount
        + " excludedInnerAttack=" + result.ExcludedInnerAttackCells.Length + " allArrivedTick=" + result.AllArrivedTick
        + " continuousPairs=" + result.CheckedRelativeSegments + " deaths=" + result.Recoveries.Count
        + " maxRecoveryTicks=" + result.Recoveries.Max(value => value.Ticks) + " output=" + output);
}

internal readonly record struct UnitSpec(int Radius, int Range, int Speed);

internal sealed class ForestResult
{
    public bool Success = true;
    public string Scope = "Constructive feasibility on the unchanged forest map; not execution by the production crowd planner or Unity.";
    public string ReferenceCommit = "f67e8e6cdaea6b89c289433650a7ead8589ec692";
    public string Utc = DateTime.UtcNow.ToString("O");
    public string MapSha256 = "";
    public UnitSpec Unit;
    public bool FrontierPolicyApplied;
    public int ProductionAttackCandidates;
    public int ProductionGatherCandidates;
    public int[] Goals = Array.Empty<int>();
    public int[] ExcludedInnerAttackCells = Array.Empty<int>();
    public int RawValidHoldingCount;
    public int AllWithinReachHoldingCount;
    public int IndependentAttackVisibilityChecks;
    public int AttackCount;
    public int WaitingCount;
    public long AllArrivedTick;
    public int CheckedTicks;
    public long CheckedRelativeSegments;
    public long CheckedStaticSegments;
    public long CheckedBlockedCells;
    public double MinimumBodyGapMm = double.PositiveInfinity;
    public double MinimumBaseGapMm = double.PositiveInfinity;
    public List<Movement> Admission = new List<Movement>();
    public List<Recovery> Recoveries = new List<Recovery>();
}

internal sealed record Movement(int Id, int[] Cells)
{
    public long Start;
    public long Arrival;
}

internal sealed record Recovery(int Victim, int InitialHole)
{
    public int Replacement;
    public long Ticks;
    public long AttackCountRestoredAtTick = -1;
    public List<Movement> Moves = new List<Movement>();
}

internal sealed class ForestWitness
{
    private const long Forever = long.MaxValue / 4;
    private readonly NavigationGrid grid;
    private readonly UnitSpec spec;
    private readonly int spawn;
    private readonly int duration;
    private readonly ForestResult result = new ForestResult();
    private readonly bool[] eligible;

    public ForestWitness(NavigationGrid grid, UnitSpec spec, bool allLegalAttackCells)
    {
        this.grid = grid;
        this.spec = spec;
        Require(grid.SpawnCells.Count == 1, "The actual forest must have one declared gate.");
        spawn = grid.SpawnCells[0];
        duration = (grid.CellSize + spec.Speed - 1) / spec.Speed;
        eligible = Enumerable.Range(0, grid.CellCount).Select(cell => grid.HasAttackLine(cell, spec.Radius, spec.Range)).ToArray();
        var attack = new bool[grid.CellCount];
        long reach = grid.TargetRadius + spec.Radius + spec.Range;
        long gathering = grid.TargetRadius + 4000;
        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            attack[cell] = eligible[cell] && grid.Neighbours(cell).Any(next => grid.CanTraverse(cell, next, spec.Radius)
                && Squared(grid.Center(next), grid.TargetCenter) > reach * reach);
        }

        result.Goals = Enumerable.Range(0, grid.CellCount).Where(cell => grid.HoldingAllowed(cell) && grid.CanOccupy(cell, spec.Radius)
            && !grid.SpawnCells.Contains(cell) && ((allLegalAttackCells ? eligible[cell] : attack[cell]) || (!eligible[cell]
                && Squared(grid.Center(cell), grid.TargetCenter) <= gathering * gathering))).ToArray();
        result.ExcludedInnerAttackCells = Enumerable.Range(0, grid.CellCount)
            .Where(cell => grid.HoldingAllowed(cell) && eligible[cell] && !attack[cell]).ToArray();
        result.RawValidHoldingCount = Enumerable.Range(0, grid.CellCount).Count(cell => grid.HoldingAllowed(cell) && grid.CanOccupy(cell, spec.Radius));
        result.AllWithinReachHoldingCount = Enumerable.Range(0, grid.CellCount).Count(cell => grid.HoldingAllowed(cell) && eligible[cell]);
        foreach (int cell in Enumerable.Range(0, grid.CellCount).Where(cell => grid.HoldingAllowed(cell) && eligible[cell]))
        {
            var point = grid.Center(cell);
            Require(Squared(point, grid.TargetCenter) <= reach * reach, "A claimed attack cell is physically out of range.");
            foreach (int blocked in Enumerable.Range(0, grid.CellCount).Where(other => !grid.IsWalkable(other)))
            {
                int left = grid.Origin.X + blocked % grid.Width * grid.CellSize;
                int bottom = grid.Origin.Y + blocked / grid.Width * grid.CellSize;
                Require(!SegmentIntersectsBox(point, grid.TargetCenter, left, bottom, grid.CellSize), "A claimed attack ray crosses a blocker.");
            }

            result.IndependentAttackVisibilityChecks++;
        }

        result.AttackCount = result.Goals.Count(cell => eligible[cell]);
        result.WaitingCount = result.Goals.Length - result.AttackCount;
        var production = new NavigationCrowd(grid);
        production.RegisterProfile(spec.Radius, spec.Range);
        result.ProductionAttackCandidates = production.CountAttackSlots(spec.Radius, spec.Range);
        result.ProductionGatherCandidates = production.CountGatherSlots(spec.Radius, spec.Range);
        result.FrontierPolicyApplied = !allLegalAttackCells;
        if (!allLegalAttackCells)
        {
            Require(result.Goals.Length == result.ProductionAttackCandidates + result.ProductionGatherCandidates,
                "The independent goal classifier disagrees with production geometry.");
        }
        else
        {
            result.Scope = "All physically legal original holding cells, with only the frontier-filter target policy relaxed; no map or body changes.";
        }
    }

    public ForestResult Run(string sha)
    {
        result.MapSha256 = sha;
        result.Unit = spec;
        var occupied = result.Goals.ToHashSet();
        var reversed = new List<Movement>();
        for (int id = result.Goals.Length; id > 0; id--)
        {
            var tree = Flood(spawn, occupied);
            var choice = occupied.OrderBy(cell => cell).Select(cell => new
            {
                Cell = cell,
                Next = grid.Neighbours(cell).Where(next => tree.ContainsKey(next) && grid.CanTraverse(cell, next, spec.Radius))
                    .Select(next => (int?)next).FirstOrDefault()
            }).FirstOrDefault(value => value.Next.HasValue);
            Require(choice != null, "A final goal has no reverse evacuation path.");
            var path = new List<int> { choice!.Cell };
            for (int cell = choice.Next!.Value; cell >= 0; cell = tree[cell])
            {
                path.Add(cell);
            }

            path.Reverse();
            reversed.Add(new Movement(id, path.ToArray()));
            occupied.Remove(choice.Cell);
        }

        reversed.Reverse();
        foreach (var move in reversed)
        {
            CheckStatic(move.Cells);
        }

        Schedule(reversed);
        result.Admission = reversed;
        result.AllArrivedTick = reversed.Max(move => move.Arrival);
        CheckTimeline(reversed);
        CheckEveryAttackDeath(reversed.ToDictionary(move => move.Id, move => move.Cells[^1]));
        return result;
    }

    private Dictionary<int, int> Flood(int start, HashSet<int> occupied)
    {
        var parent = new Dictionary<int, int> { [start] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            foreach (int next in grid.Neighbours(cell))
            {
                if (!parent.ContainsKey(next) && !occupied.Contains(next) && grid.CanTraverse(cell, next, spec.Radius))
                {
                    parent.Add(next, cell);
                    queue.Enqueue(next);
                }
            }
        }

        return parent;
    }

    private void Schedule(List<Movement> moves)
    {
        var reservations = new Dictionary<int, List<(long Start, long End)>>();
        long previousStart = -1;
        foreach (var move in moves)
        {
            var slots = new List<(int Cell, long Start, long End)>();
            for (int index = 1; index < move.Cells.Length; index++)
            {
                long from = (index - 1L) * duration;
                long to = index * (long)duration;
                slots.Add((move.Cells[index - 1], from, to));
                slots.Add((move.Cells[index], from, to));
            }

            long arrival = (move.Cells.Length - 1L) * duration;
            slots.Add((move.Cells[^1], arrival, Forever));
            long start = previousStart + 1;
            for (int attempt = 0; ; attempt++)
            {
                Require(attempt < 100000, "Finite launch search did not converge.");
                long nextStart = start;
                foreach (var slot in slots)
                {
                    if (!reservations.TryGetValue(slot.Cell, out var conflicts))
                    {
                        continue;
                    }

                    long end = slot.End == Forever ? Forever : start + slot.End;
                    foreach (var conflict in conflicts)
                    {
                        if (start + slot.Start < conflict.End && conflict.Start < end)
                        {
                            Require(conflict.End != Forever, "A later reverse-fill path crossed an earlier permanent goal.");
                            nextStart = Math.Max(nextStart, conflict.End - slot.Start);
                        }
                    }
                }

                if (nextStart == start)
                {
                    break;
                }

                start = nextStart;
            }

            move.Start = start;
            move.Arrival = start + arrival;
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
    }

    private Position2 Position(Movement move, long tick)
    {
        if (tick >= move.Arrival)
        {
            return grid.Center(move.Cells[^1]);
        }

        long elapsed = Math.Max(0, tick - move.Start);
        int segment = (int)(elapsed / duration);
        long remainder = elapsed % duration;
        var from = grid.Center(move.Cells[segment]);
        var to = grid.Center(move.Cells[segment + 1]);
        return new Position2(from.X + (int)((to.X - from.X) * remainder / duration), from.Y + (int)((to.Y - from.Y) * remainder / duration));
    }

    private void CheckTimeline(List<Movement> moves)
    {
        for (long tick = 0; tick <= result.AllArrivedTick + 200; tick++)
        {
            var active = moves.Where(move => move.Start <= tick).ToArray();
            var from = active.Select(move => Position(move, tick)).ToArray();
            var to = active.Select(move => Position(move, tick + 1)).ToArray();
            for (int first = 0; first < active.Length; first++)
            {
                Require(Squared(from[first], to[first]) <= (long)spec.Speed * spec.Speed, "Movement exceeded actual speed.");
                for (int second = first + 1; second < active.Length; second++)
                {
                    double distance = RelativeDistanceSquared(from[first], to[first], from[second], to[second]);
                    Require(distance + .000001 >= 4L * spec.Radius * spec.Radius, "Two actual moving bodies overlapped.");
                    result.MinimumBodyGapMm = Math.Min(result.MinimumBodyGapMm, Math.Sqrt(distance) - 2 * spec.Radius);
                    result.CheckedRelativeSegments++;
                }
            }

            result.CheckedTicks++;
        }

        Require(moves.Select(move => move.Cells[^1]).ToHashSet().SetEquals(result.Goals), "The actual final occupancy omitted a goal.");
    }

    private void CheckEveryAttackDeath(Dictionary<int, int> final)
    {
        foreach (var victim in final.Where(pair => eligible[pair.Value]).OrderBy(pair => pair.Key))
        {
            var occupied = final.ToDictionary(pair => pair.Key, pair => pair.Value);
            occupied.Remove(victim.Key);
            int hole = victim.Value;
            var free = Flood(spawn, occupied.Values.ToHashSet());
            var parents = new Dictionary<int, int> { [hole] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(hole);
            int last = -1;
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                if (free.ContainsKey(cell))
                {
                    last = cell;
                    break;
                }

                foreach (int next in grid.Neighbours(cell))
                {
                    if (!parents.ContainsKey(next) && grid.CanTraverse(cell, next, spec.Radius))
                    {
                        parents.Add(next, cell);
                        queue.Enqueue(next);
                    }
                }
            }

            Require(last >= 0, "A forest vacancy cannot reach any exterior route even with displacement.");
            var chain = new List<int>();
            for (int cell = last; cell >= 0; cell = parents[cell])
            {
                chain.Add(cell);
            }

            chain.Reverse();
            var recovery = new Recovery(victim.Key, victim.Value) { Replacement = 1000 + victim.Key };
            int previous = 0;
            for (int index = 1; index < chain.Count; index++)
            {
                var occupant = occupied.Where(pair => pair.Value == chain[index]).ToArray();
                if (occupant.Length == 0)
                {
                    continue;
                }

                int owner = occupant[0].Key;
                var move = new Movement(owner, chain.Skip(previous).Take(index - previous + 1).Reverse().ToArray());
                Require(move.Cells[^1] == hole, "A forest displacement does not terminate at the current vacancy.");
                if (eligible[chain[index]])
                {
                    recovery.AttackCountRestoredAtTick = -1;
                }

                occupied.Remove(owner);
                CheckAgainstStationary(move.Cells, occupied.Values);
                move.Start = recovery.Ticks;
                recovery.Ticks += (move.Cells.Length - 1L) * duration;
                move.Arrival = recovery.Ticks;
                occupied.Add(owner, hole);
                recovery.Moves.Add(move);
                if (recovery.AttackCountRestoredAtTick < 0 && occupied.Count(pair => eligible[pair.Value]) == result.AttackCount)
                {
                    recovery.AttackCountRestoredAtTick = recovery.Ticks;
                }
                else if (occupied.Count(pair => eligible[pair.Value]) < result.AttackCount)
                {
                    recovery.AttackCountRestoredAtTick = -1;
                }

                hole = chain[index];
                previous = index;
            }

            free = Flood(spawn, occupied.Values.ToHashSet());
            Require(free.ContainsKey(hole), "The propagated vacancy still cannot be reached by reinforcement.");
            var path = new List<int>();
            for (int cell = hole; cell >= 0; cell = free[cell])
            {
                path.Add(cell);
            }

            path.Reverse();
            var replacement = new Movement(recovery.Replacement, path.ToArray()) { Start = recovery.Ticks };
            CheckAgainstStationary(replacement.Cells, occupied.Values);
            recovery.Ticks += (path.Count - 1L) * duration;
            replacement.Arrival = recovery.Ticks;
            recovery.Moves.Add(replacement);
            occupied.Add(replacement.Id, hole);
            if (recovery.AttackCountRestoredAtTick < 0)
            {
                recovery.AttackCountRestoredAtTick = recovery.Ticks;
            }

            Require(occupied.Values.ToHashSet().SetEquals(result.Goals), "Recovery did not restore the identical full target set.");
            Require(occupied.Count(pair => eligible[pair.Value]) == result.AttackCount, "Recovery failed to restore real attacking capacity.");
            result.Recoveries.Add(recovery);
        }
    }

    private void CheckAgainstStationary(int[] cells, IEnumerable<int> occupied)
    {
        CheckStatic(cells);
        for (int step = 1; step < cells.Length; step++)
        {
            foreach (int other in occupied)
            {
                double distance = RelativeDistanceSquared(grid.Center(cells[step - 1]), grid.Center(cells[step]), grid.Center(other), grid.Center(other));
                Require(distance + .000001 >= 4L * spec.Radius * spec.Radius, "A forest vacancy move overlapped a standing actor.");
                result.MinimumBodyGapMm = Math.Min(result.MinimumBodyGapMm, Math.Sqrt(distance) - 2 * spec.Radius);
                result.CheckedRelativeSegments++;
            }
        }
    }

    private void CheckStatic(int[] cells)
    {
        var blocked = Enumerable.Range(0, grid.CellCount).Where(cell => !grid.IsWalkable(cell)).ToArray();
        for (int step = 1; step < cells.Length; step++)
        {
            var from = grid.Center(cells[step - 1]);
            var to = grid.Center(cells[step]);
            long right = grid.Origin.X + (long)grid.Width * grid.CellSize;
            long top = grid.Origin.Y + (long)grid.Height * grid.CellSize;
            Require(Math.Min(from.X, to.X) - spec.Radius >= grid.Origin.X && Math.Max(from.X, to.X) + spec.Radius <= right,
                "A body crossed the world X boundary.");
            Require(Math.Min(from.Y, to.Y) - spec.Radius >= grid.Origin.Y && Math.Max(from.Y, to.Y) + spec.Radius <= top,
                "A body crossed the world Y boundary.");
            Require(grid.CanTraverse(cells[step - 1], cells[step], spec.Radius), "A witness did not use a legal neighbour edge.");
            double target = RelativeDistanceSquared(from, to, grid.TargetCenter, grid.TargetCenter);
            Require(target + .000001 >= (long)(grid.TargetRadius + spec.Radius) * (grid.TargetRadius + spec.Radius), "A moving body penetrated the base.");
            result.MinimumBaseGapMm = Math.Min(result.MinimumBaseGapMm, Math.Sqrt(target) - grid.TargetRadius - spec.Radius);
            foreach (int cell in blocked)
            {
                int left = grid.Origin.X + cell % grid.Width * grid.CellSize;
                int bottom = grid.Origin.Y + cell / grid.Width * grid.CellSize;
                long dx = Math.Max(0, Math.Max(left - Math.Max(from.X, to.X), Math.Min(from.X, to.X) - left - grid.CellSize));
                long dy = Math.Max(0, Math.Max(bottom - Math.Max(from.Y, to.Y), Math.Min(from.Y, to.Y) - bottom - grid.CellSize));
                Require(dx * dx + dy * dy >= (long)spec.Radius * spec.Radius, "A moving body penetrated unchanged blocked terrain.");
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

    private static double RelativeDistanceSquared(Position2 a0, Position2 a1, Position2 b0, Position2 b1)
    {
        double x = (double)a0.X - b0.X;
        double y = (double)a0.Y - b0.Y;
        double dx = (double)a1.X - a0.X - b1.X + b0.X;
        double dy = (double)a1.Y - a0.Y - b1.Y + b0.Y;
        double denominator = dx * dx + dy * dy;
        double time = denominator == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / denominator, 0, 1);
        x += dx * time;
        y += dy * time;
        return x * x + y * y;
    }

    private static bool SegmentIntersectsBox(Position2 start, Position2 end, int left, int bottom, int size)
    {
        double enter = 0;
        double exit = 1;
        bool Clip(double origin, double delta, double low, double high)
        {
            if (delta == 0)
            {
                return origin >= low && origin <= high;
            }

            double first = (low - origin) / delta;
            double second = (high - origin) / delta;
            enter = Math.Max(enter, Math.Min(first, second));
            exit = Math.Min(exit, Math.Max(first, second));
            return enter <= exit;
        }

        return Clip(start.X, (double)end.X - start.X, left, left + size)
            && Clip(start.Y, (double)end.Y - start.Y, bottom, bottom + size);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
