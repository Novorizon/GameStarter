using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;
using RVO;
using Vector = RVO.Vector2;

internal enum NavigationState
{
    Moving,
    Waiting,
    Attacking
}
internal readonly record struct UnitObservation(int Id, Cell Cell, NavigationState State, int SlotId, int MoveTicks, double X, double Y, int Radius);

internal static class Program
{
    private sealed class Witness
    {
        public bool success { get; set; }
        public List<WitnessPath> paths { get; set; } = new List<WitnessPath>();
    }

    private sealed class WitnessPath
    {
        public int Id { get; set; }
        public int Spawn { get; set; }
        public int Goal { get; set; }
        public int[] Cells { get; set; }
    }

    private sealed class Actor
    {
        internal int Id;
        internal int RvoId;
        internal int Goal;
        internal int[] Path;
        internal int Waypoint;
        internal int RepathTick;
        internal bool Arrived;
        internal Vector? YieldTarget;
        internal int YieldRequester;
        internal int YieldStarted;
        internal double BestGoalError = double.MaxValue;
        internal int LastProgressTick;
    }

    private sealed class Result
    {
        public string Name { get; set; }
        public string UpstreamCommit { get; set; } = "de124a275d51d421bb9e2a77952210527ab60b04";
        public string Scope { get; set; } = "Official RVO2-CS source compiled under .NET 8; "
            + "original unchanged 4m/72 fixture and independent swept geometry oracle.";
        public int Requested { get; set; } = 72;
        public int Spawned { get; set; }
        public int Arrived { get; set; }
        public int Attacking { get; set; }
        public int Waiting { get; set; }
        public int Ticks { get; set; }
        public int LogicalSubsteps { get; set; }
        public float TimeHorizon { get; set; }
        public float TimeHorizonObstacle { get; set; }
        public float NeighborDistance { get; set; }
        public bool SerialAdmission { get; set; }
        public bool RepathSettled { get; set; }
        public float AvoidanceGuardMm { get; set; }
        public float SpawnClearanceMetres { get; set; }
        public bool GoalDependencies { get; set; }
        public bool StaticFunnel { get; set; }
        public int PeakMoving { get; set; }
        public int Seed { get; set; }
        public int GoalAssignmentSeed { get; set; }
        public bool CoordinatedYield { get; set; }
        public int YieldRequests { get; set; }
        public int YieldCompletions { get; set; }
        public int YieldTimeouts { get; set; }
        public int ActualRadiusMm { get; set; } = 250;
        public int MaximumSpeedMmPerLogicalTick { get; set; } = 1000;
        public float LogicalTickSeconds { get; set; } = .05f;
        public bool Success { get; set; }
        public string Error { get; set; }
        public double MaximumGoalErrorMm { get; set; }
        public double MinimumSampledPhysicalGapMm { get; set; } = double.MaxValue;
        public long ElapsedMilliseconds { get; set; }
        public List<Frame> Trajectory { get; } = new List<Frame>();
    }

    private sealed class Frame
    {
        public int Tick { get; set; }
        public UnitObservation[] Units { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int Main(string[] args)
    {
        string output = Value(args, "--output") ?? "Artifacts/OrcaComparison";
        string only = Value(args, "--case");
        int limit = int.Parse(Value(args, "--ticks") ?? "12000");
        float guard = float.Parse(Value(args, "--guard-mm") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        float clearance = float.Parse(Value(args, "--spawn-clearance-m") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        int seed = int.Parse(Value(args, "--seed") ?? "0");
        int goalSeed = int.Parse(Value(args, "--shuffle-goals") ?? "0");
        string witnessFile = Value(args, "--witness") ?? "Experiments~/OrcaComparison/witness.json";
        var witness = JsonSerializer.Deserialize<Witness>(File.ReadAllText(witnessFile));
        if (!witness.success || witness.paths.Count != 72)
        {
            throw new InvalidOperationException("Expected the independently proven original 72-target witness.");
        }

        VerifyWitness(witness);
        VerifyConnectors();

        if (goalSeed != 0)
        {
            var goals = witness.paths.Select(path => path.Goal).ToArray();
            var random = new Random(goalSeed);
            for (int index = goals.Length - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (goals[index], goals[other]) = (goals[other], goals[index]);
            }

            for (int index = 0; index < witness.paths.Count; index++)
            {
                witness.paths[index].Goal = goals[index];
            }
        }

        GeometryChecks.VerifyOracle();
        Directory.CreateDirectory(output);
        var reports = new List<Result>();
        foreach (var spec in new[]
        {
            ("concurrent-bfs-horizon2", false, false, false, 2f, 2f, 10, 12000),
            ("concurrent-bfs-horizon05", false, false, false, .5f, .5f, 10, 12000),
            ("concurrent-witness-route", false, true, false, 2f, 2f, 10, 12000),
            ("concurrent-repath-settled", false, false, true, 2f, 2f, 10, 12000),
            ("serial-witness-route", true, true, false, 2f, 2f, 10, 12000),
            ("serial-short-obstacle-horizon", true, true, false, .5f, .1f, 10, 12000),
            ("concurrent-short-obstacle-horizon", false, false, false, .5f, .1f, 10, 12000),
            ("concurrent-funnel-repath", false, false, true, .5f, .1f, 10, 12000),
            ("concurrent-funnel-dependencies", false, true, false, .5f, .1f, 10, 12000),
            ("concurrent-funnel-repath-yield", false, false, true, .5f, .1f, 10, 12000)
        })
        {
            if (only != null && !spec.Item1.Contains(only, StringComparison.Ordinal))
            {
                continue;
            }

            if (goalSeed != 0 && (spec.Item2 || spec.Item3))
            {
                throw new InvalidOperationException("Shuffled goals require fresh global routing; witness routes and precedence are not reused.");
            }

            var result = Run(
                spec.Item1,
                witness,
                spec.Item2,
                spec.Item3,
                spec.Item4,
                spec.Item5,
                spec.Item6,
                spec.Item7,
                Math.Min(limit, spec.Item8),
                guard,
                clearance,
                seed,
                goalSeed);
            reports.Add(result);
            File.WriteAllText(Path.Combine(output, result.Name + ".json"), JsonSerializer.Serialize(result, JsonOptions));
            Console.WriteLine((result.Success ? "PASS " : "FAIL ") + result.Name + " spawned=" + result.Spawned + "/72 arrived="
                + result.Arrived + " ticks=" + result.Ticks + " ms=" + result.ElapsedMilliseconds + " " + result.Error);
        }

        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
        {
            utc = DateTime.UtcNow.ToString("O"),
            independentGeometryOracleVerified = true,
            allRequestedReached = reports.Count > 0 && reports.All(report => report.Success),
            results = reports.Select(report => new
            {
                report.Name, report.Success, report.Spawned, report.Arrived, report.Ticks, report.Error,
                report.MinimumSampledPhysicalGapMm, report.MaximumGoalErrorMm, report.ElapsedMilliseconds, report.PeakMoving,
                report.Seed, report.GoalAssignmentSeed
            })
        }, JsonOptions));
        return reports.Any() && reports.All(report => report.Success) ? 0 : 1;
    }

    private static Result Run(
        string name,
        Witness witness,
        bool serial,
        bool witnessRoute,
        bool repathSettled,
        float horizon,
        float obstacleHorizon,
        int substeps,
        int deadline,
        float guardMm,
        float minimumSpawnClearance,
        int seed,
        int goalSeed)
    {
        var scenario = ScenarioCatalog.Create(1).Single(item => item.Name == "cross-junction-conservative-capacity-4m");
        var grid = GridFor(scenario);
        var simulator = Simulator.Instance;
        if (witness.paths.Select(path => path.Id).Distinct().Count() != 72 || witness.paths.Select(path => path.Goal).Distinct().Count() != 72)
        {
            throw new InvalidOperationException("The original witness must contain 72 distinct identities and 72 distinct legal goals.");
        }

        var entrances = new HashSet<int>(scenario.SpawnCells.Select(cell => cell.Y * grid.Width + cell.X));
        foreach (var plan in witness.paths)
        {
            bool inRange = grid.HasAttackLine(plan.Goal, 250, 1000);
            bool frontier = inRange && grid.Neighbours(plan.Goal).Any(cell => grid.CanTraverse(plan.Goal, cell, 250)
                && Square(Meter(grid.Center(cell)) - Meter(grid.TargetCenter)) > 2.25f * 2.25f);
            bool gathering = !inRange && Square(Meter(grid.Center(plan.Goal)) - Meter(grid.TargetCenter)) <= 25;
            if (!entrances.Contains(plan.Spawn) || !grid.CanOccupy(plan.Goal, 250) || !(frontier || gathering))
            {
                throw new InvalidOperationException("An input target or spawn differs from the original 4m/72 fixture contract.");
            }
        }

        simulator.Clear();
        simulator.NumWorkers = 1;
        simulator.TimeStep = .05f / substeps;
        float avoidanceRadius = .25f + guardMm / 1000;
        simulator.SetAgentDefaults(5f, 72, horizon, obstacleHorizon, avoidanceRadius, 20f, new Vector());
        BuildObstacles(simulator, scenario);
        simulator.ProcessObstacles();
        var actors = new List<Actor>();
        bool dependencies = name.Contains("dependencies", StringComparison.Ordinal);
        bool funnel = name.Contains("funnel", StringComparison.Ordinal);
        bool coordinatedYield = name.EndsWith("-yield", StringComparison.Ordinal);
        var precedents = witness.paths.ToDictionary(path => path.Id,
            path => witness.paths.Where(prior => prior.Id < path.Id && prior.Cells.Contains(path.Goal)).Select(prior => prior.Id).ToArray());
        var report = new Result
        {
            Name = name,
            LogicalSubsteps = substeps,
            TimeHorizon = horizon,
            TimeHorizonObstacle = obstacleHorizon,
            NeighborDistance = 5,
            SerialAdmission = serial,
            RepathSettled = repathSettled,
            AvoidanceGuardMm = guardMm,
            SpawnClearanceMetres = Math.Max(minimumSpawnClearance, 2 * avoidanceRadius + .0001f),
            GoalDependencies = dependencies,
            StaticFunnel = funnel,
            Seed = seed,
            GoalAssignmentSeed = goalSeed,
            CoordinatedYield = coordinatedYield
        };
        var watch = Stopwatch.StartNew();
        var pending = witness.paths.OrderBy(path => path.Id).ToList();
        if (!serial && seed != 0)
        {
            var random = new Random(seed);
            for (int index = pending.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (pending[index], pending[other]) = (pending[other], pending[index]);
            }
        }
        int stable = 0;
        try
        {
            for (int tick = 0; tick <= deadline; tick++)
            {
                report.Ticks = tick;
                var previous = Observe(simulator, actors, grid);
                var usedSpawn = new HashSet<int>();
                for (int index = 0; index < pending.Count;)
                {
                    var plan = pending[index];
                    if (serial && actors.Any(actor => !AtGoal(simulator.GetAgentPosition(actor.RvoId), actor, grid)))
                    {
                        break;
                    }

                    if (usedSpawn.Contains(plan.Spawn))
                    {
                        index++;
                        continue;
                    }

                    if (dependencies && precedents[plan.Id].Any(id => !actors.Any(actor => actor.Id == id
                        && AtGoal(simulator.GetAgentPosition(actor.RvoId), actor, grid))))
                    {
                        index++;
                        continue;
                    }

                    var start = Meter(grid.Center(plan.Spawn));
                    float spawnClearance = report.SpawnClearanceMetres;
                    if (actors.Any(actor => Square(simulator.GetAgentPosition(actor.RvoId) - start) < spawnClearance * spawnClearance))
                    {
                        index++;
                        continue;
                    }

                    int[] path = witnessRoute ? plan.Cells : Route(grid, plan.Spawn, plan.Goal, null);
                    if (path == null)
                    {
                        throw new InvalidOperationException("Static global path was not found.");
                    }

                    int id = simulator.AddAgent(start);
                    actors.Add(new Actor { Id = plan.Id, RvoId = id, Goal = plan.Goal, Path = path, Waypoint = 1 });
                    usedSpawn.Add(plan.Spawn);
                    previous.Add(plan.Id, Observe(simulator, actors[actors.Count - 1], grid));
                    pending.RemoveAt(index);
                    if (serial)
                    {
                        break;
                    }
                }

                if (coordinatedYield)
                {
                    CoordinateYield(simulator, actors, grid, tick, avoidanceRadius, report);
                }

                for (int step = 0; step < substeps; step++)
                {
                    foreach (var actor in actors)
                    {
                        var position = simulator.GetAgentPosition(actor.RvoId);
                        var destination = Meter(grid.Center(actor.Goal));
                        actor.Arrived = AtGoal(position, actor, grid);
                        if (repathSettled && !actor.Arrived && !actor.YieldTarget.HasValue && tick >= actor.RepathTick)
                        {
                            var blocked = new HashSet<int>(actors.Where(other => other.Id != actor.Id && !other.YieldTarget.HasValue
                                && Square(simulator.GetAgentPosition(other.RvoId) - Meter(grid.Center(other.Goal))) < .01f).Select(other => other.Goal));
                            int start = grid.CellAt(Millimetre(position));
                            var path = start >= 0 && grid.CanOccupy(start, 250) ? Route(grid, start, actor.Goal, blocked) : null;
                            if (path != null)
                            {
                                actor.Path = path;
                                // Join a newly computed cell path from the actual continuous position. Go directly
                                // to the next waypoint only when the physical disc's swept segment is clear.
                                actor.Waypoint = path.Length > 1 && ExactVisible(position, Meter(grid.Center(path[1])), grid, .255) ? 1 : 0;
                            }

                            actor.RepathTick = tick + 20;
                        }

                        while (actor.Waypoint < actor.Path.Length && Square(Meter(grid.Center(actor.Path[actor.Waypoint])) - position) <= .000001f)
                        {
                            actor.Waypoint++;
                        }

                        if (funnel && step == 0 && tick % 5 == 0)
                        {
                            for (int at = actor.Path.Length - 1; at > actor.Waypoint; at--)
                            {
                                if (Visible(position, Meter(grid.Center(actor.Path[at])), grid, avoidanceRadius))
                                {
                                    actor.Waypoint = at;
                                    break;
                                }
                            }
                        }

                        var target = actor.Waypoint < actor.Path.Length ? Meter(grid.Center(actor.Path[actor.Waypoint])) : destination;
                        if (actor.YieldTarget.HasValue)
                        {
                            target = actor.YieldTarget.Value;
                        }
                        var delta = target - position;
                        float distance = MathF.Sqrt(Square(delta));
                        float speed = MathF.Min(20f, distance / (.05f / substeps));
                        var velocity = distance > 0 ? delta * (speed / distance) : new Vector();
                        simulator.SetAgentPrefVelocity(actor.RvoId, velocity);
                    }

                    simulator.DoStep();
                    var observations = Observe(simulator, actors, grid);
                    GeometryChecks.VerifyUnitClearance(observations.Values.ToArray(), previous);
                    foreach (var unit in observations.Values)
                    {
                        var old = previous[unit.Id];
                        GeometryChecks.VerifyObstacleClearance(scenario, old, unit);
                        GeometryChecks.VerifyTargetClearance(scenario, old, unit);
                        double dx = unit.X - old.X;
                        double dy = unit.Y - old.Y;
                        double allowed = 1000d / substeps + .01;
                        if (dx * dx + dy * dy > allowed * allowed)
                        {
                            throw new InvalidOperationException("An ORCA step exceeded the original configured maximum speed.");
                        }
                        if (unit.State == NavigationState.Attacking)
                        {
                            GeometryChecks.VerifyAttackLine(scenario, unit);
                        }
                    }

                    foreach (var first in step == substeps - 1 ? observations.Values : Enumerable.Empty<UnitObservation>())
                    {
                        foreach (var second in observations.Values)
                        {
                            if (first.Id < second.Id)
                            {
                                report.MinimumSampledPhysicalGapMm = Math.Min(report.MinimumSampledPhysicalGapMm,
                                    Math.Sqrt((first.X - second.X) * (first.X - second.X) + (first.Y - second.Y) * (first.Y - second.Y)) - 500);
                            }
                        }
                    }

                    previous = observations;
                }

                report.Spawned = actors.Count;
                report.Arrived = actors.Count(actor => AtGoal(simulator.GetAgentPosition(actor.RvoId), actor, grid));
                report.PeakMoving = Math.Max(report.PeakMoving, actors.Count - report.Arrived);
                report.MaximumGoalErrorMm = actors.Count == 0 ? 0 : actors.Max(actor => Math.Sqrt(
                    Square(simulator.GetAgentPosition(actor.RvoId) - Meter(grid.Center(actor.Goal)))) * 1000);
                if (tick % 20 == 0)
                {
                    report.Trajectory.Add(new Frame { Tick = tick, Units = Observe(simulator, actors, grid).Values.ToArray() });
                }

                if (tick % 500 == 0)
                {
                    Console.WriteLine("PROGRESS " + name + " tick=" + tick + " spawned=" + report.Spawned
                        + " arrived=" + report.Arrived + " maxGoalErrorMm=" + report.MaximumGoalErrorMm.ToString("F1"));
                }

                stable = report.Spawned == 72 && report.Arrived == 72 ? stable + 1 : 0;
                if (stable >= 200)
                {
                    report.Success = true;
                    break;
                }

                if (tick == deadline)
                {
                    report.Error = "Arrival deadline exceeded with all requested units retained; stationary goals are not counted as arrival from far away.";
                }
            }
        }
        catch (Exception error)
        {
            report.Spawned = actors.Count;
            report.Arrived = actors.Count(actor => AtGoal(simulator.GetAgentPosition(actor.RvoId), actor, grid));
            report.Error = error.ToString();
            report.Trajectory.Add(new Frame { Tick = report.Ticks, Units = Observe(simulator, actors, grid).Values.ToArray() });
        }

        report.Attacking = actors.Count(actor => AtGoal(simulator.GetAgentPosition(actor.RvoId), actor, grid)
            && grid.HasAttackLine(actor.Goal, 250, 1000));
        report.Waiting = report.Arrived - report.Attacking;
        report.ElapsedMilliseconds = watch.ElapsedMilliseconds;
        return report;
    }

    private static Dictionary<int, UnitObservation> Observe(Simulator simulator, List<Actor> actors, NavigationGrid grid)
    {
        return actors.ToDictionary(actor => actor.Id, actor => Observe(simulator, actor, grid));
    }

    private static UnitObservation Observe(Simulator simulator, Actor actor, NavigationGrid grid)
    {
        var point = simulator.GetAgentPosition(actor.RvoId);
        int cell = grid.CellAt(Millimetre(point));
        var type = AtGoal(point, actor, grid)
            ? (grid.HasAttackLine(actor.Goal, 250, 1000) ? NavigationState.Attacking : NavigationState.Waiting) : NavigationState.Moving;
        return new UnitObservation(actor.Id, new Cell(cell % grid.Width, cell / grid.Width), type, actor.Goal, 1, point.X * 1000d, point.Y * 1000d, 250);
    }

    private static int[] Route(NavigationGrid grid, int start, int target, HashSet<int> blocked)
    {
        var visited = new bool[grid.CellCount];
        var previous = new int[grid.CellCount];
        Array.Fill(previous, -1);
        var queue = new Queue<int>();
        visited[start] = true;
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            if (cell == target)
            {
                var result = new List<int>();
                for (int at = target; at >= 0; at = previous[at])
                {
                    result.Add(at);
                }

                result.Reverse();
                return result.ToArray();
            }

            foreach (int next in grid.Neighbours(cell))
            {
                if (visited[next] || (blocked != null && blocked.Contains(next)) || !grid.CanTraverse(cell, next, 250))
                {
                    continue;
                }

                visited[next] = true;
                previous[next] = cell;
                queue.Enqueue(next);
            }
        }

        return null;
    }

    private static bool AtGoal(Vector point, Actor actor, NavigationGrid grid)
    {
        if (actor.YieldTarget.HasValue)
        {
            return false;
        }

        if (Square(point - Meter(grid.Center(actor.Goal))) > .000025f)
        {
            return false;
        }

        double x = point.X * 1000d - grid.TargetCenter.X;
        double y = point.Y * 1000d - grid.TargetCenter.Y;
        long range = grid.HasAttackLine(actor.Goal, 250, 1000) ? 2250 : 5000;
        return x * x + y * y <= range * range + .000001;
    }

    private static void CoordinateYield(Simulator simulator, List<Actor> actors, NavigationGrid grid, int tick, float radius, Result report)
    {
        foreach (var actor in actors)
        {
            var position = simulator.GetAgentPosition(actor.RvoId);
            double error = Math.Sqrt(Square(position - Meter(grid.Center(actor.Goal))));
            if (actor.YieldTarget.HasValue)
            {
                var requester = actors.Single(other => other.Id == actor.YieldRequester);
                bool passed = AtGoal(simulator.GetAgentPosition(requester.RvoId), requester, grid);
                if (passed || tick - actor.YieldStarted >= 400)
                {
                    actor.YieldTarget = null;
                    actor.BestGoalError = double.MaxValue;
                    actor.LastProgressTick = tick;
                    actor.RepathTick = tick;
                    requester.LastProgressTick = tick;
                    if (passed)
                    {
                        report.YieldCompletions++;
                    }
                    else
                    {
                        report.YieldTimeouts++;
                    }
                }

                continue;
            }

            if (AtGoal(position, actor, grid) || error < actor.BestGoalError - .01)
            {
                actor.BestGoalError = error;
                actor.LastProgressTick = tick;
            }
        }

        foreach (var requester in actors.OrderBy(actor => actor.Id))
        {
            var position = simulator.GetAgentPosition(requester.RvoId);
            var destination = Meter(grid.Center(requester.Goal));
            if (requester.YieldTarget.HasValue || AtGoal(position, requester, grid) || tick - requester.LastProgressTick < 100
                || actors.Any(actor => actor.YieldTarget.HasValue && actor.YieldRequester == requester.Id))
            {
                continue;
            }

            foreach (var blocker in actors.Where(actor => actor.Id != requester.Id && !actor.YieldTarget.HasValue
                && Square(simulator.GetAgentPosition(actor.RvoId) - Meter(grid.Center(actor.Goal))) <= .01f)
                .OrderBy(actor => Square(simulator.GetAgentPosition(actor.RvoId) - position)))
            {
                var stopped = simulator.GetAgentPosition(blocker.RvoId);
                if (Square(stopped - position) > 2.25f || SegmentDistanceSquared(stopped, position, destination) > .4225f)
                {
                    continue;
                }

                var outward = stopped - Meter(grid.TargetCenter);
                float length = MathF.Sqrt(Square(outward));
                if (length < .001f)
                {
                    continue;
                }

                outward *= 1 / length;
                bool requested = false;
                foreach (float angle in new[] { 0f, .7853982f, -.7853982f, 1.5707964f, -1.5707964f })
                {
                    var direction = new Vector(outward.X * MathF.Cos(angle) - outward.Y * MathF.Sin(angle),
                        outward.X * MathF.Sin(angle) + outward.Y * MathF.Cos(angle));
                    var away = stopped - position;
                    if (direction.X * away.X + direction.Y * away.Y <= 0)
                    {
                        continue;
                    }

                    var target = stopped + direction * .7f;
                    if (!Visible(stopped, target, grid, radius))
                    {
                        continue;
                    }

                    blocker.YieldTarget = target;
                    blocker.YieldRequester = requester.Id;
                    blocker.YieldStarted = tick;
                    report.YieldRequests++;
                    requested = true;
                    break;
                }

                if (requested)
                {
                    break;
                }
            }
        }
    }

    private static float SegmentDistanceSquared(Vector point, Vector first, Vector second)
    {
        var segment = second - first;
        var offset = point - first;
        float t = Square(segment) == 0 ? 0 : Math.Clamp((offset.X * segment.X + offset.Y * segment.Y) / Square(segment), 0, 1);
        return Square(point - (first + segment * t));
    }

    private static bool Visible(Vector first, Vector second, NavigationGrid grid, float radius)
    {
        float distance = MathF.Sqrt(Square(second - first));
        int samples = Math.Max(1, (int)MathF.Ceiling(distance / .05f));
        double clearance = radius + .025;
        for (int index = 0; index <= samples; index++)
        {
            var point = first + (second - first) * (index / (float)samples);
            double targetX = point.X - grid.TargetCenter.X / 1000d;
            double targetY = point.Y - grid.TargetCenter.Y / 1000d;
            if (targetX * targetX + targetY * targetY < (1 + clearance) * (1 + clearance))
            {
                return false;
            }

            int x = (int)Math.Floor(point.X);
            int y = (int)Math.Floor(point.Y);
            for (int cy = y - 1; cy <= y + 1; cy++)
            {
                for (int cx = x - 1; cx <= x + 1; cx++)
                {
                    bool open = cx >= 0 && cy >= 0 && cx < grid.Width && cy < grid.Height && grid.IsWalkable(cy * grid.Width + cx);
                    if (open)
                    {
                        continue;
                    }

                    double dx = point.X - Math.Clamp(point.X, cx, cx + 1);
                    double dy = point.Y - Math.Clamp(point.Y, cy, cy + 1);
                    if (dx * dx + dy * dy < clearance * clearance)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static bool ExactVisible(Vector first, Vector second, NavigationGrid grid, double radius)
    {
        var target = Meter(grid.TargetCenter);
        if (PointSegmentDistance(target.X, target.Y, first.X, first.Y, second.X, second.Y) < (1 + radius) * (1 + radius))
        {
            return false;
        }

        int left = (int)Math.Floor(Math.Min(first.X, second.X) - radius);
        int right = (int)Math.Floor(Math.Max(first.X, second.X) + radius);
        int bottom = (int)Math.Floor(Math.Min(first.Y, second.Y) - radius);
        int top = (int)Math.Floor(Math.Max(first.Y, second.Y) + radius);
        for (int y = bottom; y <= top; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (x >= 0 && y >= 0 && x < grid.Width && y < grid.Height && grid.IsWalkable(y * grid.Width + x))
                {
                    continue;
                }

                var corners = new[] { (X: (double)x, Y: (double)y), (X: (double)x + 1, Y: (double)y),
                    (X: (double)x + 1, Y: (double)y + 1), (X: (double)x, Y: (double)y + 1) };
                double distance = Math.Min(PointBoxDistance(first.X, first.Y, x, y), PointBoxDistance(second.X, second.Y, x, y));
                for (int edge = 0; edge < 4; edge++)
                {
                    var a = corners[edge];
                    var b = corners[(edge + 1) % 4];
                    if (SegmentsIntersect(first.X, first.Y, second.X, second.Y, a.X, a.Y, b.X, b.Y))
                    {
                        distance = 0;
                        break;
                    }

                    distance = Math.Min(distance, PointSegmentDistance(a.X, a.Y, first.X, first.Y, second.X, second.Y));
                }

                if (distance < radius * radius)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static double PointBoxDistance(double x, double y, double left, double bottom)
    {
        double dx = x - Math.Clamp(x, left, left + 1);
        double dy = y - Math.Clamp(y, bottom, bottom + 1);
        return dx * dx + dy * dy;
    }

    private static double PointSegmentDistance(double x, double y, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double length = dx * dx + dy * dy;
        double t = length == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / length, 0, 1);
        double px = x - ax - t * dx;
        double py = y - ay - t * dy;
        return px * px + py * py;
    }

    private static bool SegmentsIntersect(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
    {
        double first = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        double second = (bx - ax) * (dy - ay) - (by - ay) * (dx - ax);
        double third = (dx - cx) * (ay - cy) - (dy - cy) * (ax - cx);
        double fourth = (dx - cx) * (by - cy) - (dy - cy) * (bx - cx);
        return first * second <= 0 && third * fourth <= 0
            && Math.Max(Math.Min(ax, bx), Math.Min(cx, dx)) <= Math.Min(Math.Max(ax, bx), Math.Max(cx, dx))
            && Math.Max(Math.Min(ay, by), Math.Min(cy, dy)) <= Math.Min(Math.Max(ay, by), Math.Max(cy, dy));
    }

    private static void BuildObstacles(Simulator simulator, NavigationScenario scenario)
    {
        for (int y = 0; y < scenario.Height; y++)
        {
            for (int x = 0; x < scenario.Width; x++)
            {
                if (!scenario.Open(new Cell(x, y)))
                {
                    continue;
                }

                if (!scenario.Open(new Cell(x - 1, y)))
                {
                    simulator.AddObstacle(new[] { new Vector(x, y), new Vector(x, y + 1) });
                }

                if (!scenario.Open(new Cell(x + 1, y)))
                {
                    simulator.AddObstacle(new[] { new Vector(x + 1, y + 1), new Vector(x + 1, y) });
                }

                if (!scenario.Open(new Cell(x, y - 1)))
                {
                    simulator.AddObstacle(new[] { new Vector(x + 1, y), new Vector(x, y) });
                }

                if (!scenario.Open(new Cell(x, y + 1)))
                {
                    simulator.AddObstacle(new[] { new Vector(x, y + 1), new Vector(x + 1, y + 1) });
                }
            }
        }

        var polygon = new List<Vector>();
        const int count = 128;
        float radius = 1f / MathF.Cos(MathF.PI / count);
        for (int index = 0; index < count; index++)
        {
            float angle = index * MathF.PI * 2 / count;
            polygon.Add(new Vector(scenario.Target.X + .5f + MathF.Cos(angle) * radius, scenario.Target.Y + .5f + MathF.Sin(angle) * radius));
        }

        simulator.AddObstacle(polygon);
    }

    private static string Value(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void VerifyWitness(Witness witness)
    {
        var scenario = ScenarioCatalog.Create(1).Single(item => item.Name == "cross-junction-conservative-capacity-4m");
        var grid = GridFor(scenario);
        if (!witness.paths.Select(path => path.Id).OrderBy(id => id).SequenceEqual(Enumerable.Range(1, 72)))
        {
            throw new InvalidOperationException("The witness must retain exactly the original identities 1..72.");
        }

        foreach (var path in witness.paths)
        {
            var entry = scenario.SpawnCells[(path.Id + 1) % scenario.SpawnCells.Length];
            if (path.Spawn != entry.Y * grid.Width + entry.X || path.Cells == null || path.Cells.Length == 0
                || path.Cells[0] != path.Spawn || path.Cells[path.Cells.Length - 1] != path.Goal)
            {
                throw new InvalidOperationException("Witness input changed the original fixed spawn mapping or path endpoints.");
            }

            for (int index = 0; index < path.Cells.Length; index++)
            {
                if (!grid.CanOccupy(path.Cells[index], 250)
                    || (index > 0 && !grid.CanTraverse(path.Cells[index - 1], path.Cells[index], 250)))
                {
                    throw new InvalidOperationException("An input witness path uses an invalid physical cell or movement edge.");
                }
            }
        }
    }

    private static void VerifyConnectors()
    {
        var cells = Enumerable.Repeat(true, 64).ToArray();
        cells[1 * 8 + 2] = false;
        var grid = new NavigationGrid(8, 8, 1000, new Position2(0, 0), cells, new Position2(6500, 6500), 1000);
        var current = new Vector(1.7f, 2.1f);
        if (ExactVisible(current, new Vector(2.5f, 2.5f), grid, .255)
            || !ExactVisible(current, new Vector(1.5f, 2.5f), grid, .255))
        {
            throw new InvalidOperationException("The actual-position path connector missed its analytic blocked-corner counterexample.");
        }

        var target = new NavigationGrid(8, 8, 1000, new Position2(0, 0), Enumerable.Repeat(true, 64).ToArray(), new Position2(3250, 3100), 1000);
        if (ExactVisible(new Vector(1.9f, 3), new Vector(4.6f, 3), target, .255))
        {
            throw new InvalidOperationException("The actual-position path connector missed an off-centre base chord.");
        }
    }

    private static NavigationGrid GridFor(NavigationScenario scenario)
    {
        return new NavigationGrid(
            scenario.Width,
            scenario.Height,
            1000,
            new Position2(0, 0),
            scenario.Walkable,
            new Position2(scenario.Target.X * 1000 + 500, scenario.Target.Y * 1000 + 500),
            1000);
    }

    private static Vector Meter(Position2 point) => new Vector(point.X / 1000f, point.Y / 1000f);
    private static Position2 Millimetre(Vector point) => new Position2((int)Math.Round(point.X * 1000d), (int)Math.Round(point.Y * 1000d));
    private static float Square(Vector vector) => vector.X * vector.X + vector.Y * vector.Y;
}
