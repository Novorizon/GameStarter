using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal static class Program
{
    private static int Main()
    {
        GeometryChecks.VerifyOracle();
        var reports = new List<object>();
        bool success = true;
        Directory.CreateDirectory("Artifacts/RefinedGrid");
        foreach (int seed in new[] { 1, 17, 733, 20261110 })
        {
            foreach (string name in new[]
            {
                "cross-junction-conservative-capacity-4m",
                "explicit-capacity-exhaustion",
                "shared-spawn-original-5m",
                "three-hundred-units-original-12m",
                "single-cell-throat"
            })
            {
                var scenario = ScenarioCatalog.Create(seed).Single(value => value.Name == name);
                foreach (int refinement in new[] { 1, 2 })
                {
                    var report = Run(scenario, refinement);
                    reports.Add(report);
                    Console.WriteLine(JsonSerializer.Serialize(report));
                    if (refinement == 2 && !report.Success)
                    {
                        success = false;
                    }
                }
            }
        }

        File.WriteAllText("Artifacts/RefinedGrid/summary.json", JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }

    private static Result Run(NavigationScenario scenario, int refinement)
    {
        var timer = Stopwatch.StartNew();
        var report = new Result { Name = scenario.Name, Seed = scenario.Seed, Refinement = refinement, Requested = scenario.UnitCount };
        var grid = Refine(scenario, refinement);
        var crowd = new NavigationCrowd(grid, planningHorizon: 4096 * refinement, gatheringDistance: scenario.GatheringDistance);
        crowd.RegisterProfile(scenario.UnitRadius, 1000);
        report.AttackCandidates = crowd.CountAttackSlots(scenario.UnitRadius, 1000);
        report.GatherCandidates = crowd.CountGatherSlots(scenario.UnitRadius, 1000);
        var pending = Enumerable.Range(1, scenario.UnitCount).Reverse().ToList();
        var previous = new Dictionary<int, UnitObservation>();
        var speeds = new Dictionary<int, int>();
        int settled = 0;
        try
        {
            for (int microtick = 1; microtick <= scenario.Deadline * refinement; microtick++)
            {
                if ((microtick - 1) % refinement == 0)
                {
                    var attempted = new HashSet<Cell>();
                    foreach (int id in pending.ToArray())
                    {
                        var spawn = scenario.SpawnCells[(id + scenario.Seed % scenario.SpawnCells.Length) % scenario.SpawnCells.Length];
                        if (!attempted.Add(spawn))
                        {
                            continue;
                        }

                        int moveTicks = scenario.MoveTicks[(id + scenario.Seed % scenario.MoveTicks.Length) % scenario.MoveTicks.Length];
                        int speed = 1000 / moveTicks / refinement;
                        if (crowd.TrySpawn(id, Center(spawn), speed, scenario.UnitRadius, 1000))
                        {
                            pending.Remove(id);
                            speeds.Add(id, speed);
                            previous.Add(id, Observe(crowd.Agents.Last()));
                        }
                    }
                }

                previous = Advance(scenario, crowd, grid, speeds, previous);
                var observed = previous.Values.ToArray();
                if (pending.Count == 0 && observed.All(unit => unit.State != NavigationState.Moving))
                {
                    settled = settled == 0 ? microtick : settled;
                    if (microtick - settled >= 200 * refinement)
                    {
                        report.Success = true;
                        report.Microticks = microtick;
                        break;
                    }
                }

                report.Microticks = microtick;
                if (microtick > 2500 * refinement && observed.All(unit => unit.State != NavigationState.Moving)
                    && crowd.LastFailure == NavigationFailure.CapacityExceeded)
                {
                    report.Error = "Stable conservative refusal; all admitted actors are stationary but the complete cohort did not arrive.";
                    break;
                }
            }

            if (report.Success)
            {
                int attackCount = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
                var waiting = crowd.Agents.Where(agent => agent.Status == NavigationAgentStatus.Waiting).Select(agent => agent.Id).ToHashSet();
                var dead = crowd.Agents.Where(agent => agent.Status == NavigationAgentStatus.Attacking).Take(6).Select(agent => agent.Id).ToArray();
                foreach (int id in dead)
                {
                    if (!crowd.Remove(id))
                    {
                        throw new InvalidOperationException("Could not remove a real attacker.");
                    }

                    previous.Remove(id);
                    speeds.Remove(id);
                }

                bool promotedBeforeNewRequests = false;
                for (int tick = 0; tick < 2000 * refinement; tick++)
                {
                    previous = Advance(scenario, crowd, grid, speeds, previous);
                    int promoted = crowd.Agents.Count(agent => waiting.Contains(agent.Id) && agent.Status == NavigationAgentStatus.Attacking);
                    if (promoted >= dead.Length && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating)
                        && crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking) == attackCount)
                    {
                        report.PromotedAfterDeaths = promoted;
                        report.PromotionMicroticks = tick + 1;
                        promotedBeforeNewRequests = true;
                        break;
                    }
                }

                if (!promotedBeforeNewRequests)
                {
                    throw new InvalidOperationException("Existing waiting actors did not refill the six attack positions before reinforcement requests began.");
                }

                var reinforcements = Enumerable.Range(1001, dead.Length).ToList();
                bool replaced = false;
                for (int tick = 0; tick < 2000 * refinement; tick++)
                {
                    if (tick % refinement == 0)
                    {
                        foreach (int id in reinforcements.ToArray())
                        {
                            var spawn = scenario.SpawnCells[(id + scenario.Seed % scenario.SpawnCells.Length) % scenario.SpawnCells.Length];
                            int moveTicks = scenario.MoveTicks[(id + scenario.Seed % scenario.MoveTicks.Length) % scenario.MoveTicks.Length];
                            int speed = 1000 / moveTicks / refinement;
                            if (crowd.TrySpawn(id, Center(spawn), speed, scenario.UnitRadius, 1000))
                            {
                                reinforcements.Remove(id);
                                previous.Add(id, Observe(crowd.Agents.Last()));
                                speeds.Add(id, speed);
                            }
                        }
                    }

                    previous = Advance(scenario, crowd, grid, speeds, previous);
                    int promoted = crowd.Agents.Count(agent => waiting.Contains(agent.Id) && agent.Status == NavigationAgentStatus.Attacking);
                    if (reinforcements.Count == 0 && promoted >= dead.Length && crowd.Agents.Count == scenario.UnitCount
                        && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating)
                        && crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking) == attackCount)
                    {
                        report.PromotedAfterDeaths = promoted;
                        report.ReplacementMicroticks = tick + 1;
                        report.ReplacementVerified = true;
                        replaced = true;
                        break;
                    }
                }

                if (!replaced)
                {
                    report.Success = false;
                    report.Error = "The complete cohort arrived initially, but deaths and six new requests did not recover the original attack occupancy.";
                }
            }
        }
        catch (Exception error)
        {
            report.Success = false;
            report.Error = error.ToString();
        }

        report.Admitted = crowd.Agents.Count;
        report.Pending = pending.Count;
        report.Attacking = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
        report.Waiting = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Waiting);
        report.LastFailure = crowd.LastFailure.ToString();
        report.ExpandedNodes = crowd.TotalExpandedNodes;
        report.TotalMicroticks = (int)crowd.CurrentTick;
        report.ElapsedMilliseconds = timer.ElapsedMilliseconds;
        report.AllArrivedMicrotick = settled;
        report.PhysicalArrivalMilliseconds = settled * 50 / refinement;
        return report;
    }

    private static Dictionary<int, UnitObservation> Advance(
        NavigationScenario scenario,
        NavigationCrowd crowd,
        NavigationGrid grid,
        Dictionary<int, int> speeds,
        Dictionary<int, UnitObservation> previous)
    {
        crowd.Step(crowd.CurrentTick + 1);
        var observed = crowd.Agents.Select(Observe).ToArray();
        GeometryChecks.VerifyUnitClearance(observed, previous);
        foreach (var unit in observed)
        {
            var before = previous[unit.Id];
            GeometryChecks.VerifyObstacleClearance(scenario, before, unit);
            GeometryChecks.VerifyTargetClearance(scenario, before, unit);
            long dx = unit.X - before.X;
            long dy = unit.Y - before.Y;
            if (dx * dx + dy * dy > (long)speeds[unit.Id] * speeds[unit.Id])
            {
                throw new InvalidOperationException("Physical velocity exceeded the unchanged 1000 mm per original tick.");
            }

            if (unit.State == NavigationState.Attacking)
            {
                GeometryChecks.VerifyAttackLine(scenario, unit);
                long targetX = unit.X - Center(scenario.Target).X;
                long targetY = unit.Y - Center(scenario.Target).Y;
                long reach = 1000 + unit.Radius + 1000;
                if (targetX * targetX + targetY * targetY > reach * reach)
                {
                    throw new InvalidOperationException("An attacker stands beyond its unchanged physical attack distance.");
                }
            }

            if (unit.State == NavigationState.Waiting || unit.State == NavigationState.Attacking)
            {
                var actor = crowd.Agents.Single(value => value.Id == unit.Id);
                var goal = grid.Center(actor.GoalCell);
                if (goal.X != unit.X || goal.Y != unit.Y)
                {
                    throw new InvalidOperationException("Claimed arrival without standing at the reserved goal.");
                }

                long targetX = unit.X - Center(scenario.Target).X;
                long targetY = unit.Y - Center(scenario.Target).Y;
                long reach = 1000 + scenario.GatheringDistance;
                if (targetX * targetX + targetY * targetY > reach * reach)
                {
                    throw new InvalidOperationException("A stationary unit was counted as gathered outside the unchanged gathering radius.");
                }
            }
        }

        return observed.ToDictionary(unit => unit.Id);
    }

    private static NavigationGrid Refine(NavigationScenario scenario, int refinement)
    {
        if (refinement == 1)
        {
            return new NavigationGrid(
                scenario.Width,
                scenario.Height,
                1000,
                new Position2(0, 0),
                scenario.Walkable,
                Center(scenario.Target),
                1000,
                scenario.HoldingAllowed,
                scenario.SpawnCells.Select(cell => cell.Y * scenario.Width + cell.X).ToArray());
        }

        // Extra boundary cells are closed. World-space boundaries and every original blocked square remain authoritative.
        int width = scenario.Width * 2 + 1;
        int height = scenario.Height * 2 + 1;
        var walkable = new bool[width * height];
        var holding = new bool[walkable.Length];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int px = x * 500;
                int py = y * 500;
                bool legal = true;
                for (int cy = (int)Math.Floor((py - 249) / 1000d); cy <= (int)Math.Floor((py + 249) / 1000d); cy++)
                {
                    for (int cx = (int)Math.Floor((px - 249) / 1000d); cx <= (int)Math.Floor((px + 249) / 1000d); cx++)
                    {
                        legal &= scenario.Open(new Cell(cx, cy));
                    }
                }

                int index = y * width + x;
                walkable[index] = legal;
                int coarse = py / 1000 * scenario.Width + px / 1000;
                holding[index] = legal && (scenario.HoldingAllowed == null || scenario.HoldingAllowed[coarse]);
            }
        }

        return new NavigationGrid(
            width,
            height,
            500,
            new Position2(-250, -250),
            walkable,
            Center(scenario.Target),
            1000,
            holding,
            scenario.SpawnCells.Select(cell => (cell.Y * 2 + 1) * width + cell.X * 2 + 1).ToArray());
    }

    private static Position2 Center(Cell cell) => new Position2(cell.X * 1000 + 500, cell.Y * 1000 + 500);

    private static UnitObservation Observe(NavigationAgent agent)
    {
        return new UnitObservation(
            agent.Id,
            new Cell((int)Math.Floor(agent.Position.X / 1000d), (int)Math.Floor(agent.Position.Y / 1000d)),
            agent.Status == NavigationAgentStatus.Attacking ? NavigationState.Attacking
                : agent.Status == NavigationAgentStatus.Waiting ? NavigationState.Waiting : NavigationState.Moving,
            agent.GoalCell,
            1,
            agent.Position.X,
            agent.Position.Y,
            agent.Radius);
    }

    private sealed class Result
    {
        public string Name { get; set; } = "";
        public int Seed { get; set; }
        public int Refinement { get; set; }
        public int Requested { get; set; }
        public int Admitted { get; set; }
        public int Pending { get; set; }
        public int Attacking { get; set; }
        public int Waiting { get; set; }
        public bool Success { get; set; }
        public int Microticks { get; set; }
        public int TotalMicroticks { get; set; }
        public int AllArrivedMicrotick { get; set; }
        public int PhysicalArrivalMilliseconds { get; set; }
        public bool ReplacementVerified { get; set; }
        public int PromotedAfterDeaths { get; set; }
        public int PromotionMicroticks { get; set; }
        public int ReplacementMicroticks { get; set; }
        public int AttackCandidates { get; set; }
        public int GatherCandidates { get; set; }
        public string LastFailure { get; set; } = "";
        public string Error { get; set; } = "";
        public long ExpandedNodes { get; set; }
        public long ElapsedMilliseconds { get; set; }
    }
}
