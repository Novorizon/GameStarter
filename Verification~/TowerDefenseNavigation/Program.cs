using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class Program
{
    private static int Main(string[] args)
    {
        var totalTime = Stopwatch.StartNew();
        string output = Argument(args, "--output") ?? "Artifacts/Navigation";
        string? filter = Argument(args, "--scenario");
        bool stress = Array.IndexOf(args, "--stress") >= 0;
        int[] seeds = stress ? new[] { 1, 17, 733, 20261110 } : new[] { 1 };
        var results = new List<ScenarioResult>();
        PlanningContractReport contracts;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { success = false, status = "running" }, JsonOptions));
        var sourceHashes = SourceSnapshot.Capture();
        try
        {
            GeometryChecks.VerifyOracle();
            GridContracts.Verify();
            LayoutContracts.Verify();
            contracts = PlanningContracts.Verify();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL verification contracts: " + error.Message);
            File.WriteAllText(
                Path.Combine(output, "summary.json"),
                JsonSerializer.Serialize(new { success = false, status = "contract-failed", error = error.Message }, JsonOptions));
            return 1;
        }

        foreach (int seed in seeds)
        {
            foreach (var scenario in ScenarioCatalog.Create(seed).Where(value => filter == null || value.Name.Contains(filter, StringComparison.Ordinal)))
            {
                var result = Execute(scenario, true);
                if (result.Success)
                {
                    var replayTime = Stopwatch.StartNew();
                    result.BatchEquivalent = Replay(scenario, result, 7, out string failure);
                    result.ReplayElapsedMilliseconds = replayTime.ElapsedMilliseconds;
                    if (!result.BatchEquivalent)
                    {
                        result.Success = false;
                        result.Error = "Tick batching changed the outcome: " + failure;
                    }
                }

                results.Add(result);
                string filename = scenario.Name + "-" + seed + ".json";
                File.WriteAllText(Path.Combine(output, filename), JsonSerializer.Serialize(result, JsonOptions));
                Console.WriteLine((result.Success ? "PASS " : "FAIL ") + scenario.Name + " seed=" + seed
                    + " spawned=" + result.Spawned + "/" + result.Requested + " attacking=" + result.Attacking
                    + " waiting=" + result.Waiting + " ticks=" + result.Ticks + (result.Success ? "" : " " + result.Error));
            }

            for (int variant = 0; variant < 3; variant++)
            {
                bool outerFirst = variant == 0;
                bool tailExit = variant == 2;
                string name = tailExit ? "dual-entry-tail-exit-protection"
                    : outerFirst ? "mixed-profile-outer-before-inner" : "mixed-profile-inner-before-outer";
                if (filter != null && !name.Contains(filter, StringComparison.Ordinal))
                {
                    continue;
                }

                var result = MixedProfiles.Execute(seed, outerFirst, tailExit);
                results.Add(result);
                File.WriteAllText(Path.Combine(output, name + "-" + seed + ".json"), JsonSerializer.Serialize(result, JsonOptions));
                Console.WriteLine((result.Success ? "PASS " : "FAIL ") + name + " seed=" + seed
                    + " spawned=" + result.Spawned + "/" + result.Requested + " attacking=" + result.Attacking
                    + " waiting=" + result.Waiting + " ticks=" + result.Ticks + (result.Success ? "" : " " + result.Error));
            }
        }

        bool sourceFilesUnchanged = SourceSnapshot.Matches(sourceHashes);
        if (!sourceFilesUnchanged)
        {
            Console.Error.WriteLine("FAIL source files changed during verification; results must not be attributed to the current source.");
        }

        var summary = new
        {
            utc = DateTime.UtcNow.ToString("O"),
            status = "completed",
            scope = "Actual pure C# navigation source; no Unity rendering or hardware performance claim.",
            geometryOracleVerified = true,
            gridContractsVerified = true,
            layoutContractsVerified = true,
            contracts,
            elapsedMilliseconds = totalTime.ElapsedMilliseconds,
            sourceFilesUnchanged,
            sourceHashes,
            count = results.Count,
            passed = results.Count(result => result.Success),
            failed = results.Count(result => !result.Success),
            success = results.Count > 0 && results.All(result => result.Success) && sourceFilesUnchanged,
            results = results.Select(result => new
            {
                result.Name, result.Seed, result.Success, result.Outcome, result.Error, result.Ticks, result.Spawned,
                result.Attacking, result.Waiting, result.Unreachable, result.Rejected,
                result.AttackSlotCapacity, result.GatherSlotCapacity, result.BatchEquivalent,
                result.ElapsedMilliseconds, result.ExpandedNodes, result.Replans, result.MaximumReservations,
                result.CertifiedParkingCapacity, result.ReplayElapsedMilliseconds
            })
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
        return summary.success ? 0 : 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static ScenarioResult Execute(NavigationScenario scenario, bool capture)
    {
        var result = new ScenarioResult
        {
            Name = scenario.Name,
            Seed = scenario.Seed,
            Requested = scenario.UnitCount,
            Width = scenario.Width,
            Height = scenario.Height,
            Walkable = scenario.Walkable,
            HoldingAllowed = scenario.HoldingAllowed ?? scenario.Walkable,
            Target = scenario.Target,
            GatheringDistance = scenario.GatheringDistance,
            UnitRadius = scenario.UnitRadius
        };
        INavigationUnderTest? crowd = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            crowd = new NavigationAdapter(scenario);
            result.AttackSlotCapacity = crowd.AttackSlotCapacity;
            result.GatherSlotCapacity = crowd.GatherSlotCapacity;
            if (scenario.RequireLaneCertificate)
            {
                result.CertifiedParkingCapacity = ParkingLaneCertificate.Verify(scenario);
                Require(result.CertifiedParkingCapacity >= scenario.UnitCount, "Certified parking capacity is smaller than the requested crowd.");
            }
            int totalCapacity = crowd.AttackSlotCapacity + crowd.GatherSlotCapacity;
            if (!scenario.ExpectUnreachable && !scenario.ExpectCapacityRejection && totalCapacity < scenario.UnitCount)
            {
                throw new InvalidOperationException("Fixture lacks standing capacity: " + totalCapacity + " < " + scenario.UnitCount);
            }

            var pending = Enumerable.Range(1, scenario.UnitCount).ToList();
            var removed = new HashSet<int>();
            var previous = new Dictionary<int, UnitObservation>();
            var stationary = new Dictionary<int, int>();
            var waitingBeforeReplacement = new HashSet<int>();
            bool replacementStarted = false;
            int replacementTick = 0;
            int stableTicks = 0;
            for (int tick = 0; tick <= scenario.Deadline; tick++)
            {
                var blockedEntries = new HashSet<Cell>();
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    int id = pending[i];
                    Cell spawn = scenario.SpawnCells[(id + scenario.Seed % scenario.SpawnCells.Length) % scenario.SpawnCells.Length];
                    int moveTicks = scenario.MoveTicks[(id + scenario.Seed % scenario.MoveTicks.Length) % scenario.MoveTicks.Length];
                    if (blockedEntries.Contains(spawn))
                    {
                        continue;
                    }

                    bool admitted = crowd.TrySpawn(id, spawn, moveTicks);
                    if (!admitted)
                    {
                        result.Commands.Add(new NavigationCommand
                        {
                            Tick = crowd.Tick,
                            Kind = "spawn-rejected",
                            Id = id,
                            Cell = spawn,
                            MoveTicks = moveTicks
                        });
                        result.AdmissionFailures.TryGetValue(crowd.LastFailure, out int failures);
                        result.AdmissionFailures[crowd.LastFailure] = failures + 1;
                    }

                    if (admitted)
                    {
                        pending.RemoveAt(i);
                        result.Spawned++;
                        result.Commands.Add(new NavigationCommand { Tick = crowd.Tick, Kind = "spawn", Id = id, Cell = spawn, MoveTicks = moveTicks });
                        if (scenario.UnitCount >= 120 && result.Spawned % 50 == 0)
                        {
                            Console.WriteLine("PROGRESS " + scenario.Name + " seed=" + scenario.Seed + " tick=" + crowd.Tick
                                + " admitted=" + result.Spawned + "/" + scenario.UnitCount + " expanded=" + crowd.ExpandedNodes);
                        }
                    }
                    else if (crowd.LastFailure == "NoStaticPath" && scenario.ExpectUnreachable)
                    {
                        pending.RemoveAt(i);
                        result.Unreachable++;
                    }
                    else if (scenario.ExpectCapacityRejection && (crowd.LastFailure == "CapacityExceeded" || crowd.LastFailure == "GoalAccessBlocked"))
                    {
                        pending.RemoveAt(i);
                        result.Rejected++;
                    }
                    else if (crowd.LastFailure == "SpawnOccupied" || crowd.LastFailure == "ReservationBlocked")
                    {
                        blockedEntries.Add(spawn);
                    }
                    else
                    {
                        throw new InvalidOperationException("Admission failed for unit " + id + ": " + crowd.LastFailure);
                    }
                }

                CheckInvariants(scenario, crowd.Units, previous, stationary, result);
                result.Ticks = crowd.Tick;
                result.Attacking = crowd.Units.Count(unit => unit.State == NavigationState.Attacking);
                result.Waiting = crowd.Units.Count(unit => unit.State == NavigationState.Waiting);
                result.ExpandedNodes = crowd.ExpandedNodes;
                result.Replans = crowd.Replans;
                result.MaximumReservations = Math.Max(result.MaximumReservations, crowd.ReservationCount);
                if (capture && (tick % 10 == 0 || tick == scenario.Deadline))
                {
                    result.Trajectory.Add(new TrajectoryFrame { Tick = crowd.Tick, Units = crowd.Units.ToArray() });
                }

                bool allSettled = pending.Count == 0 && crowd.Units.All(unit => Settled(scenario, unit))
                    && crowd.Units.Count(unit => unit.State == NavigationState.Attacking) == Math.Min(crowd.AttackSlotCapacity, crowd.Units.Count);
                if (allSettled && scenario.HasDeathReplacement && !replacementStarted)
                {
                    foreach (var unit in crowd.Units.Where(unit => unit.State == NavigationState.Waiting))
                    {
                        waitingBeforeReplacement.Add(unit.Id);
                    }

                    var victims = crowd.Units.Where(unit => unit.State == NavigationState.Attacking).Take(Math.Max(1, crowd.AttackSlotCapacity / 2)).ToArray();
                    if (waitingBeforeReplacement.Count == 0 || victims.Length == 0)
                    {
                        throw new InvalidOperationException("Replacement fixture did not fill attack and waiting slots.");
                    }

                    foreach (var victim in victims)
                    {
                        crowd.Remove(victim.Id);
                        removed.Add(victim.Id);
                        result.Removed++;
                        result.Commands.Add(new NavigationCommand { Tick = crowd.Tick, Kind = "remove", Id = victim.Id });
                    }

                    replacementStarted = true;
                    replacementTick = tick;
                    allSettled = false;
                }

                bool complete = allSettled && (!scenario.HasDeathReplacement
                    || (crowd.Units.Any(unit => waitingBeforeReplacement.Contains(unit.Id) && unit.State == NavigationState.Attacking)
                        && crowd.Units.Count(unit => unit.State == NavigationState.Attacking) == Math.Min(crowd.AttackSlotCapacity, crowd.Units.Count)));
                stableTicks = complete ? stableTicks + 1 : 0;
                if (stableTicks >= 20)
                {
                    Require(result.Spawned + result.Unreachable + result.Rejected == scenario.UnitCount, "Lost a requested unit.");
                    Require(crowd.Units.Count == result.Spawned - removed.Count, "An admitted unit disappeared without explicit removal.");
                    if (scenario.ExpectUnreachable)
                    {
                        Require(result.Unreachable == scenario.UnitCount, "Unreachable spawns were not explicitly rejected.");
                    }
                    if (scenario.ExpectCapacityRejection && scenario.UnitCount > totalCapacity)
                    {
                        Require(result.Rejected > 0, "Capacity exhaustion was not reported.");
                    }

                    result.ReplacementVerified = !scenario.HasDeathReplacement || tick > replacementTick;
                    result.FinalState = Canonical(crowd.Units);
                    result.Outcome = result.Rejected > 0 ? "conservative-admission-limit"
                        : result.Unreachable > 0 ? "unreachable-rejected" : "all-admitted-and-arrived";
                    result.Success = true;
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    if (capture)
                    {
                        result.Trajectory.Add(new TrajectoryFrame { Tick = crowd.Tick, Units = crowd.Units.ToArray() });
                    }
                    return result;
                }

                crowd.Step(1);
            }

            throw new InvalidOperationException("Deadline exceeded: pending=" + pending.Count + ", unresolved="
                + string.Join(",", crowd.Units.Where(unit => !Settled(scenario, unit)).Take(12).Select(unit => unit.Id + ":" + unit.State + "@" + unit.Cell)));
        }
        catch (Exception error)
        {
            result.Error = error.Message;
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            if (crowd != null)
            {
                result.Ticks = crowd.Tick;
                result.Trajectory.Add(new TrajectoryFrame { Tick = crowd.Tick, Units = crowd.Units.ToArray() });
            }
            return result;
        }
    }

    private static bool Settled(NavigationScenario scenario, UnitObservation unit)
    {
        if (unit.State != NavigationState.Attacking && unit.State != NavigationState.Waiting)
        {
            return false;
        }

        int cell = unit.Cell.Y * scenario.Width + unit.Cell.X;
        long dx = unit.X - ((long)scenario.Target.X * 1000 + 500);
        long dy = unit.Y - ((long)scenario.Target.Y * 1000 + 500);
        long maximum = 1000 + scenario.GatheringDistance;
        return cell == unit.SlotId && unit.X == unit.Cell.X * 1000 + 500 && unit.Y == unit.Cell.Y * 1000 + 500
            && dx * dx + dy * dy <= maximum * maximum;
    }

    private static void CheckInvariants(
        NavigationScenario scenario,
        IReadOnlyList<UnitObservation> units,
        Dictionary<int, UnitObservation> previous,
        Dictionary<int, int> stationary,
        ScenarioResult result)
    {
        try
        {
            GeometryChecks.VerifyUnitClearance(units, previous);
        }
        catch
        {
            result.CollisionFree = false;
            throw;
        }

        var occupied = new Dictionary<Cell, int>();
        var transitions = new Dictionary<(Cell, Cell), int>();
        foreach (var unit in units)
        {
            if (!scenario.Open(unit.Cell))
            {
                result.ObstacleFree = false;
                throw new InvalidOperationException("Unit " + unit.Id + " entered a blocked cell " + unit.Cell);
            }
            if (!occupied.TryAdd(unit.Cell, unit.Id))
            {
                result.CollisionFree = false;
                throw new InvalidOperationException("Units " + occupied[unit.Cell] + " and " + unit.Id + " overlap at " + unit.Cell);
            }

            long dx = unit.X - ((long)scenario.Target.X * 1000 + 500);
            long dy = unit.Y - ((long)scenario.Target.Y * 1000 + 500);
            long square = dx * dx + dy * dy;
            long clearance = 1000 + unit.Radius;
            long reach = clearance + 1000;
            Require(square >= clearance * clearance, "A unit penetrated the target footprint.");
            if (unit.State == NavigationState.Attacking && square > reach * reach)
            {
                result.AttackRangeVerified = false;
                throw new InvalidOperationException("Unit " + unit.Id + " attacks outside its actual range.");
            }

            if (previous.TryGetValue(unit.Id, out var before))
            {
                Require(Math.Abs(before.Cell.X - unit.Cell.X) <= 1 && Math.Abs(before.Cell.Y - unit.Cell.Y) <= 1, "A unit teleported across multiple cells.");
                int speed = 1000 / unit.MoveTicks;
                long movedX = (long)unit.X - before.X;
                long movedY = (long)unit.Y - before.Y;
                Require(movedX * movedX + movedY * movedY <= (long)speed * speed + 1, "A unit moved faster than its configured speed.");
                if (before.Cell != unit.Cell && transitions.TryGetValue((unit.Cell, before.Cell), out int other))
                {
                    result.EdgeSwapFree = false;
                    throw new InvalidOperationException("Units " + unit.Id + " and " + other + " exchange occupied cells.");
                }

                if (before.Cell != unit.Cell)
                {
                    transitions[(before.Cell, unit.Cell)] = unit.Id;
                }

                if (unit.State == NavigationState.Moving && before.X == unit.X && before.Y == unit.Y)
                {
                    stationary.TryGetValue(unit.Id, out int ticks);
                    stationary[unit.Id] = ticks + 1;
                    result.MaximumStationaryMovingTicks = Math.Max(result.MaximumStationaryMovingTicks, ticks + 1);
                }
                else
                {
                    stationary[unit.Id] = 0;
                }
            }

            try
            {
                GeometryChecks.VerifyObstacleClearance(scenario, previous.TryGetValue(unit.Id, out var old) ? old : unit, unit);
                GeometryChecks.VerifyTargetClearance(scenario, previous.TryGetValue(unit.Id, out old) ? old : unit, unit);
                if (unit.State == NavigationState.Attacking)
                {
                    GeometryChecks.VerifyAttackLine(scenario, unit);
                }
            }
            catch
            {
                result.ObstacleFree = false;
                throw;
            }
            previous[unit.Id] = unit;
        }
    }

    private static string Canonical(IEnumerable<UnitObservation> units)
    {
        return string.Join("|", units.OrderBy(unit => unit.Id).Select(unit => unit.Id + ":" + unit.X + "," + unit.Y + ":" + unit.State + ":" + unit.SlotId));
    }

    private static bool Replay(NavigationScenario scenario, ScenarioResult expected, int batch, out string failure)
    {
        try
        {
            INavigationUnderTest crowd = new NavigationAdapter(scenario);
            foreach (var command in expected.Commands)
            {
                while (crowd.Tick < command.Tick)
                {
                    crowd.Step(Math.Min(batch, command.Tick - crowd.Tick));
                }

                if (command.Kind == "spawn")
                {
                    Require(crowd.TrySpawn(command.Id, command.Cell, command.MoveTicks), "Replay rejected an accepted spawn: " + crowd.LastFailure);
                }
                else if (command.Kind == "spawn-rejected")
                {
                    Require(!crowd.TrySpawn(command.Id, command.Cell, command.MoveTicks), "Replay admitted a previously queued request.");
                }
                else
                {
                    crowd.Remove(command.Id);
                }
            }

            while (crowd.Tick < expected.Ticks)
            {
                crowd.Step(Math.Min(batch, expected.Ticks - crowd.Tick));
            }

            Require(Canonical(crowd.Units) == expected.FinalState, "Final state differs under identical command timestamps.");
            failure = "";
            return true;
        }
        catch (Exception error)
        {
            failure = error.Message;
            return false;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
