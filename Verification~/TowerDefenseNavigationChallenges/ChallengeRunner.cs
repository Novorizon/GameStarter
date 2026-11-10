using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal static class ChallengeRunner
{
    public static ChallengeResult Execute(Challenge challenge, int seed, int? maxTicks)
    {
        var result = new ChallengeResult
        {
            Name = challenge.Name,
            Seed = seed,
            Width = challenge.Geometry.Width,
            Height = challenge.Geometry.Height,
            Target = challenge.Geometry.Target,
            Walkable = challenge.Geometry.Walkable,
            HoldingAllowed = challenge.Geometry.HoldingAllowed ?? challenge.Geometry.Walkable,
            GatheringDistance = challenge.Geometry.GatheringDistance,
            PlannedRecoveryPhases = challenge.Recovery.Length
        };
        var stopwatch = Stopwatch.StartNew();
        var orders = challenge.Orders.ToDictionary(order => order.Id);
        var pending = challenge.Orders.ToList();
        var admitted = new HashSet<int>();
        var deaths = new HashSet<int>();
        var previous = new Dictionary<int, UnitObservation>();
        int currentGroup = 0;
        int lastGroup = challenge.Orders.Max(order => order.Group);
        var groupTicks = new Dictionary<int, int> { [0] = 0 };
        int nextId = challenge.Orders.Max(order => order.Id) + 1;
        int startedRecovery = 0;
        int completedRecovery = 0;
        bool recovering = false;
        int stableTicks = 0;
        int blockerFollower = -1;
        Position2 followerStart = default;
        bool blockerRemoved = challenge.Blocker == null;
        bool followerMoved = challenge.Blocker == null;
        var crowd = CreateCrowd(challenge);
        try
        {
            if (challenge.PhysicalOverflow)
            {
                result.CapacityEvidence = PhysicalCapacityProof.Create(challenge.Geometry, challenge.Orders[0].Radius);
                Require(result.CapacityEvidence.PhysicalAreaUpperBound < challenge.Orders.Count, "The overflow fixture lacks a physical upper-bound proof.");
            }

            if (challenge.Blocker != null)
            {
                bool blockerSpawned = TryBirth(crowd, challenge.Blocker);
                Require(blockerSpawned,
                    "The deliberate temporary blocker was not admitted: " + crowd.LastFailure);
                Record(result, crowd, challenge.Blocker, "spawn", "blocker");
            }

            int deadline = maxTicks ?? challenge.MaxTicks;
            for (int tick = 0; tick <= deadline; tick++)
            {
                Validate(challenge, crowd, orders, previous);
                if (!blockerRemoved && tick >= challenge.RemoveBlockerTick)
                {
                    var follower = crowd.Agents.Where(agent => agent.Id != challenge.Blocker!.Id && agent.Position.X < 15500)
                        .OrderByDescending(agent => agent.Position.X).FirstOrDefault();
                    if (follower != null)
                    {
                        blockerFollower = follower.Id;
                        followerStart = follower.Position;
                    }

                    Require(Remove(crowd, challenge.Blocker!.Id), "The temporary blocker disappeared without its explicit removal command.");
                    Record(result, crowd, challenge.Blocker, "remove", "blocker");
                    blockerRemoved = true;
                }

                var blocked = new HashSet<Cell>();
                for (int index = pending.Count - 1; index >= 0; index--)
                {
                    var order = pending[index];
                    if (order.Group > currentGroup || tick < groupTicks[order.Group] + order.ReleaseTick || blocked.Contains(order.Spawn))
                    {
                        continue;
                    }

                    if (TryBirth(crowd, order))
                    {
                        pending.RemoveAt(index);
                        Require(admitted.Add(order.Id), "A request was admitted twice.");
                        Record(result, crowd, order, "spawn");
                        if (blockerRemoved && challenge.Blocker != null && blockerFollower < 0)
                        {
                            blockerFollower = order.Id;
                            followerStart = Center(order.Spawn);
                        }
                    }
                    else
                    {
                        string failure = crowd.LastFailure.ToString();
                        result.AdmissionFailures.TryGetValue(failure, out int count);
                        result.AdmissionFailures[failure] = count + 1;
                        Require(failure != "InvalidSpawn" && failure != "NoStaticPath" && failure != "NoAttackPositions",
                            "A statically reachable challenge request was rejected as " + failure + ", id=" + order.Id);
                        blocked.Add(order.Spawn);
                    }
                }

                Validate(challenge, crowd, orders, previous);
                var regular = crowd.Agents.Where(agent => orders.ContainsKey(agent.Id)).ToArray();
                Require(
                    orders.Count == regular.Length + pending.Count + deaths.Count,
                    "Request conservation failed: active + queued + explicit deaths != all requests.");
                Require(admitted.Count == regular.Length + deaths.Count, "An admitted unit vanished or an unadmitted unit appeared.");
                var activeIds = regular.Select(agent => agent.Id).ToHashSet();
                Require(activeIds.Count == regular.Length && activeIds.SetEquals(admitted.Except(deaths)),
                    "The active ID set differs from admitted IDs minus explicit deaths.");
                Require(pending.All(order => !admitted.Contains(order.Id) && !deaths.Contains(order.Id)), "A queued request was counted as active or dead.");
                result.QueueConserved = true;

                bool currentGroupReady = pending.All(order => order.Group > currentGroup) && regular.All(agent => Arrived(challenge, crowd, agent));
                if (currentGroupReady && currentGroup < lastGroup)
                {
                    Require(
                        regular.Count(agent => agent.Status == NavigationAgentStatus.Attacking) >= 27,
                        "The initial outer ring did not establish the intended dense attacking formation.");
                    currentGroup++;
                    groupTicks[currentGroup] = tick;
                }

                bool ready = pending.Count == 0 && currentGroup == lastGroup && regular.All(agent => Arrived(challenge, crowd, agent))
                    && AttackRequirement(challenge, crowd, regular);
                if (blockerRemoved && challenge.Blocker != null && blockerFollower >= 0)
                {
                    var follower = regular.FirstOrDefault(agent => agent.Id == blockerFollower);
                    followerMoved |= follower != null && (follower.Position.X != followerStart.X || follower.Position.Y != followerStart.Y);
                    Require(
                        followerMoved || tick <= challenge.RemoveBlockerTick + 20,
                        "The first following unit kept its obsolete waiting schedule after the blocker was removed.");
                    Require(ready || tick <= challenge.RemoveBlockerTick + challenge.RecoveryAfterBlockerTicks,
                        "The crowd did not recover within the generous travel budget after the blocker was removed.");
                }

                if (startedRecovery < challenge.Recovery.Length && !recovering)
                {
                    var phase = challenge.Recovery[startedRecovery];
                    bool duringArrival = phase.DuringArrival && regular.Any(agent => agent.Status == NavigationAgentStatus.Attacking)
                        && (pending.Count > 0 || regular.Any(agent => agent.Status == NavigationAgentStatus.Navigating));
                    if (ready && !phase.DuringArrival || duringArrival)
                    {
                        var victims = SelectVictims(phase, regular, challenge.Geometry.Target);
                        Require(victims.Length > 0, "The requested death/recovery fixture selected no live victim.");
                        foreach (var victim in victims)
                        {
                            var prior = orders[victim.Id];
                            Require(Remove(crowd, victim.Id) && deaths.Add(victim.Id), "A death command failed or was applied twice.");
                            Record(result, crowd, prior, "remove");
                            var replacement = new AgentOrder
                            {
                                Id = nextId++,
                                Spawn = prior.Spawn,
                                Radius = prior.Radius,
                                Range = prior.Range,
                                Speed = prior.Speed,
                                Group = currentGroup
                            };
                            orders.Add(replacement.Id, replacement);
                            pending.Add(replacement);
                        }

                        startedRecovery++;
                        recovering = true;
                        stableTicks = 0;
                        ready = false;
                        result.Stage = "recovery-" + startedRecovery;
                    }
                }

                if (ready && recovering)
                {
                    recovering = false;
                    completedRecovery++;
                    stableTicks = 0;
                }

                bool overflowComplete = challenge.PhysicalOverflow && regular.Length >= result.CapacityEvidence!.WitnessEndpointCount
                    && pending.Count > 0 && regular.All(agent => Arrived(challenge, crowd, agent)) && AttackRequirement(challenge, crowd, regular);
                bool complete = (ready && completedRecovery == challenge.Recovery.Length || overflowComplete) && blockerRemoved && followerMoved;
                stableTicks = complete ? stableTicks + 1 : 0;
                Update(result, crowd, orders.Count, admitted.Count, deaths.Count, pending.Count, completedRecovery, regular, challenge);
                if (tick % 25 == 0 || stableTicks == 1)
                {
                    result.Trajectory.Add(new TrajectoryFrame { Tick = tick, Units = Observe(crowd) });
                }

                if (stableTicks >= challenge.IdleTicks)
                {
                    Require(challenge.PhysicalOverflow || pending.Count == 0, "Unadmitted requests cannot count as successful gathering.");
                    result.Success = true;
                    result.Outcome = challenge.PhysicalOverflow ? "physical-overflow-proved-and-queue-preserved" : "all-requested-arrived-after-every-recovery";
                    result.ContinuousOracleVerified = true;
                    result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
                    result.Trajectory.Add(new TrajectoryFrame { Tick = tick, Units = Observe(crowd) });
                    return result;
                }

                crowd.Step(tick + 1);
            }

            throw new InvalidOperationException("Deadline: " + result.Stage + ", active=" + result.Active + ", queued=" + result.Pending
                + ", arrived=" + result.Arrived + ", attack=" + result.Attacking + ", recovery=" + result.CompletedRecoveryPhases
                + "/" + result.PlannedRecoveryPhases + ". A queued or rejected request was not counted as completion.");
        }
        catch (Exception error)
        {
            Update(result, crowd, orders.Count, admitted.Count, deaths.Count, pending.Count, completedRecovery,
                crowd.Agents.Where(agent => orders.ContainsKey(agent.Id)).ToArray(), challenge);
            result.Error = error.Message;
            result.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
            result.Trajectory.Add(new TrajectoryFrame { Tick = (int)crowd.CurrentTick, Units = Observe(crowd) });
            return result;
        }
    }

    private static NavigationCrowd CreateCrowd(Challenge challenge)
    {
        var geometry = challenge.Geometry;
        var grid = new NavigationGrid(
            geometry.Width,
            geometry.Height,
            1000,
            new Position2(0, 0),
            geometry.Walkable,
            Center(geometry.Target),
            1000,
            geometry.HoldingAllowed,
            geometry.SpawnCells.Select(cell => cell.Y * geometry.Width + cell.X).ToArray());
        var crowd = new NavigationCrowd(grid, planningHorizon: challenge.PlanningHorizon, gatheringDistance: geometry.GatheringDistance);
        var profiles = challenge.Orders.Select(order => (order.Radius, order.Range)).ToList();
        if (challenge.Blocker != null)
        {
            profiles.Add((challenge.Blocker.Radius, challenge.Blocker.Range));
        }

        foreach (var profile in profiles.Distinct())
        {
            crowd.RegisterProfile(profile.Radius, profile.Range);
        }

        if (geometry.Width == 37 && geometry.Height == 37 && geometry.GatheringDistance == 4000)
        {
            Require(crowd.CountAttackSlots(250, 1000) == 12, "The original 4m attack target set changed.");
            Require(crowd.CountAttackSlots(250, 1000) + crowd.CountGatherSlots(250, 1000) == 72,
                "The original 72 parking targets were not kept fixed.");
        }

        return crowd;
    }

    private static bool Arrived(Challenge challenge, NavigationCrowd crowd, NavigationAgent agent)
    {
        if (agent.Status == NavigationAgentStatus.Navigating || agent.RemainingDistance != 0 || !crowd.Grid.HoldingAllowed(agent.GoalCell))
        {
            return false;
        }

        var goal = crowd.Grid.Center(agent.GoalCell);
        long dx = (long)goal.X - crowd.Grid.TargetCenter.X;
        long dy = (long)goal.Y - crowd.Grid.TargetCenter.Y;
        long radius = 1000 + challenge.Geometry.GatheringDistance;
        return agent.Position.X == goal.X && agent.Position.Y == goal.Y && dx * dx + dy * dy <= radius * radius;
    }

    private static bool AttackRequirement(Challenge challenge, NavigationCrowd crowd, NavigationAgent[] regular)
    {
        if (challenge.RequiredShortRangeAttackers > 0)
        {
            int attacking = regular.Count(agent => agent.AttackRange == 1000 && agent.Status == NavigationAgentStatus.Attacking);
            return attacking >= challenge.RequiredShortRangeAttackers;
        }

        var profile = challenge.Orders[0];
        int expected = Math.Min(crowd.CountAttackSlots(profile.Radius, profile.Range), regular.Length);
        return regular.Count(agent => agent.Status == NavigationAgentStatus.Attacking) >= expected;
    }

    private static NavigationAgent[] SelectVictims(RecoveryPhase phase, NavigationAgent[] agents, Cell target)
    {
        var attackers = agents.Where(agent => agent.Status == NavigationAgentStatus.Attacking).OrderBy(agent => agent.GoalCell).ToArray();
        var waiting = agents.Where(agent => agent.Status == NavigationAgentStatus.Waiting)
            .OrderByDescending(agent => DistanceSquared(agent.Position, Center(target))).ToArray();
        return phase.Pattern switch
        {
            DeathPattern.AttackGoalOrdinal => attackers.Skip(phase.Ordinal % Math.Max(1, attackers.Length)).Take(1).ToArray(),
            DeathPattern.AllAttackers => attackers,
            DeathPattern.AlternatingAttackers => attackers.Where((agent, index) => index % 2 == 0).ToArray(),
            DeathPattern.FarthestWaiting => waiting.Take(1).ToArray(),
            DeathPattern.MixedSix => attackers.Take(3).Concat(waiting.Take(3)).ToArray(),
            DeathPattern.AllShortRange => agents.Where(agent => agent.AttackRange == 1000).ToArray(),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }

    private static void Validate(Challenge challenge, NavigationCrowd crowd, Dictionary<int, AgentOrder> orders, Dictionary<int, UnitObservation> previous)
    {
        var units = Observe(crowd);
        GeometryChecks.VerifyUnitClearance(units, previous);
        foreach (var unit in units)
        {
            var order = orders.TryGetValue(unit.Id, out var request) ? request : challenge.Blocker;
            Require(order != null && order.Id == unit.Id, "An agent appeared without a regular request or explicit blocker command.");
            var agent = crowd.Agents.Single(value => value.Id == unit.Id);
            Require(agent.Radius == order!.Radius && agent.AttackRange == order.Range && agent.Speed == order.Speed,
                "An agent silently changed its requested body, range or speed profile.");
            var before = previous.TryGetValue(unit.Id, out var prior) ? prior : unit;
            GeometryChecks.VerifyObstacleClearance(challenge.Geometry, before, unit);
            GeometryChecks.VerifyTargetClearance(challenge.Geometry, before, unit);
            long dx = (long)unit.X - before.X;
            long dy = (long)unit.Y - before.Y;
            Require(dx * dx + dy * dy <= (long)(order!.Speed + 1) * (order.Speed + 1), "A unit teleported or exceeded its actual configured speed.");
            if (unit.State == NavigationState.Attacking)
            {
                long reach = 1000L + order.Radius + order.Range;
                Require(DistanceSquared(new Position2(unit.X, unit.Y), Center(challenge.Geometry.Target)) <= reach * reach,
                    "A unit attacked outside its own radius/range.");
                GeometryChecks.VerifyAttackLine(challenge.Geometry, unit);
            }

            previous[unit.Id] = unit;
        }
    }

    private static UnitObservation[] Observe(NavigationCrowd crowd)
    {
        return crowd.Agents.Select(agent => new UnitObservation(
            agent.Id,
            new Cell((int)Math.Floor(agent.Position.X / 1000d), (int)Math.Floor(agent.Position.Y / 1000d)),
            agent.Status == NavigationAgentStatus.Attacking ? NavigationState.Attacking
                : agent.Status == NavigationAgentStatus.Waiting ? NavigationState.Waiting : NavigationState.Moving,
            agent.GoalCell,
            1,
            agent.Position.X,
            agent.Position.Y,
            agent.Radius)).ToArray();
    }

    private static bool TryBirth(NavigationCrowd crowd, AgentOrder order)
    {
        long tick = crowd.CurrentTick;
        var before = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
        bool accepted = crowd.TrySpawn(order.Id, Center(order.Spawn), order.Speed, order.Radius, order.Range);
        Require(crowd.CurrentTick == tick, "An admission command advanced simulation time.");
        VerifyCommandPositions(crowd, before);
        if (accepted)
        {
            var born = crowd.Agents.Single(agent => agent.Id == order.Id);
            var source = Center(order.Spawn);
            Require(born.Position.X == source.X && born.Position.Y == source.Y, "A newborn skipped its source position before a simulation tick.");
        }
        else
        {
            Require(!crowd.Agents.Any(agent => agent.Id == order.Id), "A rejected birth secretly created a live agent.");
        }

        return accepted;
    }

    private static bool Remove(NavigationCrowd crowd, int id)
    {
        long tick = crowd.CurrentTick;
        var before = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
        bool removed = crowd.Remove(id);
        Require(crowd.CurrentTick == tick, "A death command advanced simulation time.");
        VerifyCommandPositions(crowd, before);
        Require(!removed || !crowd.Agents.Any(agent => agent.Id == id), "A reported successful death left its body alive.");
        return removed;
    }

    private static void VerifyCommandPositions(NavigationCrowd crowd, Dictionary<int, Position2> before)
    {
        foreach (var agent in crowd.Agents)
        {
            if (before.TryGetValue(agent.Id, out var prior))
            {
                Require(prior.X == agent.Position.X && prior.Y == agent.Position.Y, "A spawn/death command moved an existing body between simulation ticks.");
            }
        }
    }

    private static void Update(
        ChallengeResult result,
        NavigationCrowd crowd,
        int requested,
        int admitted,
        int deaths,
        int pending,
        int recovery,
        NavigationAgent[] regular,
        Challenge challenge)
    {
        result.Requested = requested;
        result.Admitted = admitted;
        result.Active = regular.Length;
        result.Pending = pending;
        result.ExplicitDeaths = deaths;
        result.Arrived = regular.Count(agent => Arrived(challenge, crowd, agent));
        result.Attacking = regular.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
        result.Waiting = regular.Count(agent => agent.Status == NavigationAgentStatus.Waiting);
        result.Ticks = (int)crowd.CurrentTick;
        result.CompletedRecoveryPhases = recovery;
        result.ExpandedNodes = crowd.TotalExpandedNodes;
        result.Replans = crowd.ReplanCount;
    }

    private static void Record(ChallengeResult result, NavigationCrowd crowd, AgentOrder order, string kind, string role = "regular")
    {
        result.Commands.Add(new ChallengeCommand
        {
            Tick = (int)crowd.CurrentTick,
            Kind = kind,
            Role = role,
            Id = order.Id,
            Spawn = order.Spawn,
            Radius = order.Radius,
            Range = order.Range,
            Speed = order.Speed
        });
    }

    private static long DistanceSquared(Position2 first, Position2 second)
    {
        long dx = (long)first.X - second.X;
        long dy = (long)first.Y - second.Y;
        return dx * dx + dy * dy;
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
