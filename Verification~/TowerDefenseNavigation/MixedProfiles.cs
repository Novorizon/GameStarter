using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal static class MixedProfiles
{
    public static ScenarioResult Execute(int seed, bool outerFirst, bool tailExit = false)
    {
        string name = tailExit ? "dual-entry-tail-exit-protection"
            : outerFirst ? "mixed-profile-outer-before-inner" : "mixed-profile-inner-before-outer";
        Cell[] gates = seed == 1 ? new[] { new Cell(1, 17) }
            : seed == 17 ? new[] { new Cell(17, 1) }
            : seed == 733 ? new[] { new Cell(33, 17) } : new[] { new Cell(17, 33) };
        bool[] walkable = Enumerable.Repeat(true, 35 * 35).ToArray();
        bool[]? holding = null;
        if (tailExit)
        {
            gates = new[] { new Cell(22, 10), new Cell(23, 1) };
            holding = new bool[walkable.Length];
            for (int y = 0; y < 35; y++)
            {
                for (int x = 0; x < 35; x++)
                {
                    walkable[y * 35 + x] = y >= 10 || x == 23 && y >= 1;
                    holding[y * 35 + x] = y >= 10;
                }
            }
        }

        var scenario = new NavigationScenario
        {
            Name = name,
            Seed = seed,
            Width = 35,
            Height = 35,
            Target = tailExit ? new Cell(23, 15) : new Cell(17, 17),
            Walkable = walkable,
            HoldingAllowed = holding,
            SpawnCells = gates,
            GatheringDistance = tailExit ? 4000 : 10000
        };
        var result = new ScenarioResult
        {
            Name = name,
            Seed = seed,
            Width = 35,
            Height = 35,
            Target = scenario.Target,
            Walkable = scenario.Walkable,
            HoldingAllowed = scenario.HoldingAllowed ?? scenario.Walkable,
            GatheringDistance = scenario.GatheringDistance
        };
        var stopwatch = Stopwatch.StartNew();
        var crowd = CreateCrowd(scenario);
        var previous = new Dictionary<int, UnitObservation>();
        var requestedRanges = new Dictionary<int, int>();
        int firstRange = tailExit ? 1000 : outerFirst ? 4000 : 1000;
        int laterRange = tailExit ? 1000 : outerFirst ? 1000 : 4000;
        int firstCount = tailExit ? 40 + seed % 4 : crowd.CountAttackSlots(250, firstRange);
        int laterCount = !tailExit && !outerFirst ? crowd.CountAttackSlots(250, laterRange) : 1;
        int laterId = firstCount + 1;
        int lastId = firstCount + laterCount;
        result.Requested = lastId;
        result.AttackSlotCapacity = crowd.CountAttackSlots(250, firstRange);
        result.GatherSlotCapacity = crowd.CountGatherSlots(250, firstRange);
        result.Profiles.Add(new NavigationProfileResult { Radius = 250, AttackRange = firstRange, Requested = firstCount + (tailExit ? 1 : 0) });
        if (!tailExit)
        {
            result.Profiles.Add(new NavigationProfileResult { Radius = 250, AttackRange = laterRange, Requested = laterCount });
        }
        int next = 1;
        bool secondPhase = false;
        int stable = 0;
        try
        {
            for (int tick = 0; tick <= 5000; tick++)
            {
                if (next <= firstCount || secondPhase && next <= lastId)
                {
                    int range = next >= laterId ? laterRange : firstRange;
                    if (!requestedRanges.ContainsKey(next))
                    {
                        requestedRanges.Add(next, range);
                    }

                    Require(requestedRanges[next] == range, "An initial request changed its original profile.");
                    var spawn = scenario.SpawnCells[next >= laterId ? scenario.SpawnCells.Length - 1 : 0];
                    if (crowd.TrySpawn(next, Center(spawn), 1000, 250, range))
                    {
                        result.Commands.Add(new NavigationCommand
                        {
                            Tick = tick,
                            Kind = "spawn",
                            Id = next,
                            Cell = spawn,
                            MoveTicks = 1,
                            Radius = 250,
                            AttackRange = range
                        });
                        next++;
                        result.Spawned++;
                    }
                    else
                    {
                        string failure = crowd.LastFailure.ToString();
                        result.AdmissionFailures.TryGetValue(failure, out int failures);
                        result.AdmissionFailures[failure] = failures + 1;
                        result.Commands.Add(new NavigationCommand
                        {
                            Tick = tick,
                            Kind = "spawn-rejected",
                            Id = next,
                            Cell = spawn,
                            MoveTicks = 1,
                            Radius = 250,
                            AttackRange = range
                        });
                        Require(failure == "SpawnOccupied" || failure == "ReservationBlocked", "Mixed-profile admission failed: " + failure);
                    }
                }

                Validate(scenario, crowd, previous, requestedRanges);
                result.Ticks = tick;
                result.ExpandedNodes = crowd.TotalExpandedNodes;
                result.MaximumReservations = Math.Max(result.MaximumReservations, crowd.ReservationCount);
                result.Replans = crowd.ReplanCount;
                result.Attacking = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
                result.Waiting = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Waiting);
                if (tick % 10 == 0)
                {
                    result.Trajectory.Add(new TrajectoryFrame { Tick = tick, Units = Observe(crowd) });
                }

                bool settled = crowd.Agents.All(agent => agent.RemainingDistance == 0 && agent.Status != NavigationAgentStatus.Navigating);
                if (!secondPhase && next > firstCount && settled)
                {
                    Require(crowd.Agents.Any(agent => agent.Status == NavigationAgentStatus.Attacking), "First profile never reached an attack position.");
                    secondPhase = true;
                }

                bool laterReady = crowd.Agents.Any(agent => agent.Id >= laterId
                    && (tailExit ? agent.RemainingDistance == 0 && agent.Status != NavigationAgentStatus.Navigating
                        : agent.Status == NavigationAgentStatus.Attacking));
                stable = secondPhase && next > lastId && settled && laterReady ? stable + 1 : 0;
                if (stable >= 20)
                {
                    Require(crowd.Agents.Count == lastId, "An admitted mixed-profile unit disappeared.");
                    if (!tailExit)
                    {
                        VerifyAttackShares(crowd, outerFirst ? 1 : crowd.CountAttackSlots(250, 1000));
                        VerifyReplacements(scenario, crowd, result, previous, requestedRanges, 4000, 1000, outerFirst ? laterId : 1);
                    }

                    foreach (var profile in result.Profiles)
                    {
                        var matching = crowd.Agents.Where(agent => agent.AttackRange == profile.AttackRange).ToArray();
                        profile.Attacking = matching.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
                        profile.Waiting = matching.Count(agent => agent.Status == NavigationAgentStatus.Waiting);
                    }

                    if (!tailExit)
                    {
                        VerifyAttackShares(crowd, outerFirst ? 1 : crowd.CountAttackSlots(250, 1000));
                    }

                    result.FinalState = Canonical(crowd);
                    result.Ticks = (int)crowd.CurrentTick;
                    result.Attacking = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
                    result.Waiting = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Waiting);
                    result.Replans = crowd.ReplanCount;
                    result.ExpandedNodes = crowd.TotalExpandedNodes;
                    Require(crowd.Agents.Count == result.Spawned - result.Removed, "Mixed-profile lifecycle lost an admitted body.");
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    var replayTime = Stopwatch.StartNew();
                    result.BatchEquivalent = Replay(scenario, result);
                    result.ReplayElapsedMilliseconds = replayTime.ElapsedMilliseconds;
                    Require(result.BatchEquivalent, "Mixed-profile command replay changed final state.");
                    result.Success = true;
                    result.Outcome = "all-admitted-and-arrived";
                    result.Trajectory.Add(new TrajectoryFrame { Tick = result.Ticks, Units = Observe(crowd) });
                    return result;
                }

                crowd.Step(tick + 1);
            }

            throw new InvalidOperationException("Earlier occupied goals sealed a later request: admitted=" + result.Spawned
                + ", later=" + crowd.Agents.FirstOrDefault(agent => agent.Id == laterId)?.Status);
        }
        catch (Exception error)
        {
            result.Error = error.Message;
            result.Ticks = (int)crowd.CurrentTick;
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            result.Trajectory.Add(new TrajectoryFrame { Tick = result.Ticks, Units = Observe(crowd) });
            return result;
        }
    }

    private static NavigationCrowd CreateCrowd(NavigationScenario scenario)
    {
        var grid = new NavigationGrid(
            scenario.Width,
            scenario.Height,
            1000,
            new Position2(0, 0),
            scenario.Walkable,
            Center(scenario.Target),
            1000,
            scenario.HoldingAllowed,
            scenario.SpawnCells.Select(cell => cell.Y * scenario.Width + cell.X).ToArray());
        var crowd = new NavigationCrowd(grid, planningHorizon: 10000, gatheringDistance: scenario.GatheringDistance);
        crowd.RegisterProfile(250, 1000);
        if (!scenario.Name.StartsWith("dual-entry", StringComparison.Ordinal))
        {
            crowd.RegisterProfile(250, 4000);
        }
        return crowd;
    }

    private static void VerifyReplacements(
        NavigationScenario scenario,
        NavigationCrowd crowd,
        ScenarioResult result,
        Dictionary<int, UnitObservation> previous,
        Dictionary<int, int> requestedRanges,
        int firstRange,
        int laterRange,
        int laterId)
    {
        int replacementId = crowd.Agents.Max(agent => agent.Id) + 1;
        requestedRanges.Add(replacementId, laterRange);
        Require(crowd.Remove(laterId), "Cannot remove the later-profile attacker.");
        result.Removed++;
        result.Commands.Add(new NavigationCommand { Tick = (int)crowd.CurrentTick, Kind = "remove", Id = laterId });
        bool admitted = false;
        long deadline = crowd.CurrentTick + 2000;
        while (crowd.CurrentTick < deadline)
        {
            if (!admitted)
            {
                Cell spawn = scenario.SpawnCells[0];
                admitted = crowd.TrySpawn(replacementId, Center(spawn), 1000, 250, laterRange);
                if (admitted)
                {
                    result.Requested++;
                    result.Spawned++;
                    result.Profiles.First(profile => profile.AttackRange == laterRange).Requested++;
                    result.Commands.Add(new NavigationCommand
                    {
                        Tick = (int)crowd.CurrentTick,
                        Kind = "spawn",
                        Id = replacementId,
                        Cell = spawn,
                        MoveTicks = 1,
                        Radius = 250,
                        AttackRange = laterRange
                    });
                    Validate(scenario, crowd, previous, requestedRanges);
                }
                else
                {
                    result.Commands.Add(new NavigationCommand
                    {
                        Tick = (int)crowd.CurrentTick,
                        Kind = "spawn-rejected",
                        Id = replacementId,
                        Cell = spawn,
                        MoveTicks = 1,
                        Radius = 250,
                        AttackRange = laterRange
                    });
                    Require(crowd.LastFailure == NavigationFailure.SpawnOccupied || crowd.LastFailure == NavigationFailure.ReservationBlocked,
                        "Replacement profile cannot use the preserved ingress: " + crowd.LastFailure);
                }
            }

            if (admitted && crowd.Agents.First(agent => agent.Id == replacementId).Status == NavigationAgentStatus.Attacking
                && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating))
            {
                break;
            }

            AdvanceChecked(scenario, crowd, result, previous, requestedRanges);
        }

        Require(admitted && crowd.Agents.Any(agent => agent.Id == replacementId && agent.Status == NavigationAgentStatus.Attacking),
            "A removed mixed-profile attacker could not be replaced through the preserved ingress.");
        VerifyAttackShares(crowd, result.Profiles.First(profile => profile.AttackRange == 1000).Requested - 1);
        var waiting = crowd.Agents.Where(agent => agent.AttackRange == firstRange && agent.Status == NavigationAgentStatus.Waiting)
            .Select(agent => agent.Id).ToHashSet();
        if (waiting.Count > 0)
        {
            int attackers = crowd.Agents.Count(agent => agent.AttackRange == firstRange && agent.Status == NavigationAgentStatus.Attacking);
            var victim = crowd.Agents.First(agent => agent.AttackRange == firstRange && agent.Status == NavigationAgentStatus.Attacking);
            Require(crowd.Remove(victim.Id), "Cannot remove an outer-profile attacker.");
            result.Removed++;
            result.Commands.Add(new NavigationCommand { Tick = (int)crowd.CurrentTick, Kind = "remove", Id = victim.Id });
            deadline = crowd.CurrentTick + 2000;
            while (crowd.CurrentTick < deadline)
            {
                bool promoted = crowd.Agents.Any(agent => waiting.Contains(agent.Id) && agent.Status == NavigationAgentStatus.Attacking);
                int current = crowd.Agents.Count(agent => agent.AttackRange == firstRange && agent.Status == NavigationAgentStatus.Attacking);
                if (promoted && current == attackers && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating))
                {
                    VerifyAttackShares(crowd, result.Profiles.First(profile => profile.AttackRange == 1000).Requested - 1);
                    result.ReplacementVerified = true;
                    return;
                }

                AdvanceChecked(scenario, crowd, result, previous, requestedRanges);
            }

            throw new InvalidOperationException("An existing mixed-profile waiting body could not replace the removed attacker.");
        }

        result.ReplacementVerified = true;
    }

    private static void AdvanceChecked(
        NavigationScenario scenario,
        NavigationCrowd crowd,
        ScenarioResult result,
        Dictionary<int, UnitObservation> previous,
        Dictionary<int, int> requestedRanges)
    {
        crowd.Step(crowd.CurrentTick + 1);
        Validate(scenario, crowd, previous, requestedRanges);
        result.MaximumReservations = Math.Max(result.MaximumReservations, crowd.ReservationCount);
        if (crowd.CurrentTick % 10 == 0)
        {
            result.Trajectory.Add(new TrajectoryFrame { Tick = (int)crowd.CurrentTick, Units = Observe(crowd) });
        }
    }

    private static void Validate(
        NavigationScenario scenario,
        NavigationCrowd crowd,
        Dictionary<int, UnitObservation> previous,
        Dictionary<int, int> requestedRanges)
    {
        VerifyRequestedProfiles(crowd, requestedRanges);
        var units = Observe(crowd);
        GeometryChecks.VerifyUnitClearance(units, previous);
        var occupied = new HashSet<Cell>();
        foreach (var unit in units)
        {
            Require(occupied.Add(unit.Cell), "Two mixed-profile bodies occupy one cell.");
            var before = previous.TryGetValue(unit.Id, out var old) ? old : unit;
            GeometryChecks.VerifyObstacleClearance(scenario, before, unit);
            GeometryChecks.VerifyTargetClearance(scenario, before, unit);
            long movedX = (long)unit.X - before.X;
            long movedY = (long)unit.Y - before.Y;
            Require(movedX * movedX + movedY * movedY <= 1000000, "A mixed-profile body exceeded its speed.");
            var agent = crowd.Agents.First(value => value.Id == unit.Id);
            if (agent.Status == NavigationAgentStatus.Attacking)
            {
                long dx = unit.X - Center(scenario.Target).X;
                long dy = unit.Y - Center(scenario.Target).Y;
                long reach = 1000 + agent.Radius + agent.AttackRange;
                Require(dx * dx + dy * dy <= reach * reach, "A mixed-profile attacker exceeds its own attack range.");
                GeometryChecks.VerifyAttackLine(scenario, unit);
            }

            if (agent.Status != NavigationAgentStatus.Navigating)
            {
                var center = crowd.Grid.Center(agent.GoalCell);
                Require(unit.X == center.X && unit.Y == center.Y, "A mixed-profile body stopped outside its owned goal.");
                long dx = (long)unit.X - crowd.Grid.TargetCenter.X;
                long dy = (long)unit.Y - crowd.Grid.TargetCenter.Y;
                long reach = 1000 + scenario.GatheringDistance;
                Require(dx * dx + dy * dy <= reach * reach, "A mixed-profile body waited far from the target.");
            }

            previous[unit.Id] = unit;
        }
    }

    private static void VerifyAttackShares(NavigationCrowd crowd, int shortAttackers)
    {
        int longAttackers = crowd.CountAttackSlots(250, 4000) - shortAttackers;
        Require(crowd.Agents.Count(actor => actor.AttackRange == 1000 && actor.Status == NavigationAgentStatus.Attacking) == shortAttackers,
            "The established Short attack share was not restored.");
        Require(crowd.Agents.Count(actor => actor.AttackRange == 4000 && actor.Status == NavigationAgentStatus.Attacking) == longAttackers,
            "The compatible Long attack share was not restored.");
    }

    private static void VerifyRequestedProfiles(NavigationCrowd crowd, IReadOnlyDictionary<int, int> requestedRanges)
    {
        foreach (var actor in crowd.Agents)
        {
            Require(requestedRanges.TryGetValue(actor.Id, out int range), "An active body has no original request.");
            Require(actor.Radius == 250 && actor.Speed == 1000 && actor.AttackRange == range,
                "A mixed-profile body changed its requested radius, speed or attack range.");
        }
    }

    private static bool Replay(NavigationScenario scenario, ScenarioResult result)
    {
        var crowd = CreateCrowd(scenario);
        var requestedRanges = new Dictionary<int, int>();
        foreach (var command in result.Commands)
        {
            Advance(crowd, command.Tick, requestedRanges);
            if (command.Kind == "remove")
            {
                Require(crowd.Remove(command.Id), "Mixed-profile replay could not remove a body.");
            }
            else if (command.Kind == "spawn" || command.Kind == "spawn-rejected")
            {
                if (!requestedRanges.ContainsKey(command.Id))
                {
                    requestedRanges.Add(command.Id, command.AttackRange);
                }

                Require(command.Radius == 250 && command.MoveTicks == 1 && requestedRanges[command.Id] == command.AttackRange,
                    "A replay request changed its original profile.");
                bool admitted = crowd.TrySpawn(command.Id, Center(command.Cell), 1000, command.Radius, command.AttackRange);
                Require(admitted == (command.Kind == "spawn"), "Mixed-profile replay changed a birth result.");
            }
            else
            {
                throw new InvalidOperationException("Unknown mixed-profile replay command: " + command.Kind);
            }
        }

        Advance(crowd, result.Ticks, requestedRanges);
        VerifyRequestedProfiles(crowd, requestedRanges);
        return Canonical(crowd) == result.FinalState;
    }

    private static void Advance(NavigationCrowd crowd, int target, IReadOnlyDictionary<int, int> requestedRanges)
    {
        while (crowd.CurrentTick < target)
        {
            int batch = (int)Math.Min(7, target - crowd.CurrentTick);
            for (int step = 0; step < batch; step++)
            {
                crowd.Step(crowd.CurrentTick + 1);
                VerifyRequestedProfiles(crowd, requestedRanges);
            }
        }
    }

    private static UnitObservation[] Observe(NavigationCrowd crowd)
    {
        return crowd.Agents.Select(agent => new UnitObservation(
            agent.Id,
            new Cell(agent.Position.X / 1000, agent.Position.Y / 1000),
            agent.Status == NavigationAgentStatus.Attacking ? NavigationState.Attacking
                : agent.Status == NavigationAgentStatus.Waiting ? NavigationState.Waiting : NavigationState.Moving,
            agent.GoalCell,
            1,
            agent.Position.X,
            agent.Position.Y,
            agent.Radius)).ToArray();
    }

    private static string Canonical(NavigationCrowd crowd)
    {
        return string.Join("|", crowd.Agents.OrderBy(agent => agent.Id).Select(agent => agent.Id + ":" + agent.Position.X + ","
            + agent.Position.Y + ":" + agent.Status + ":" + agent.GoalCell));
    }

    private static Position2 Center(Cell cell) => new Position2(cell.X * 1000 + 500, cell.Y * 1000 + 500);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
