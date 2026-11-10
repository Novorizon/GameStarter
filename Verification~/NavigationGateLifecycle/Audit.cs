using System.Diagnostics;
using System.Reflection;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed record Command(long Tick, string Kind, int Id, Cell Spawn, int Radius, int Range, int Speed, int X, int Y);
internal sealed record Injection(long Tick, int Id, int X, int Y, int Range, bool JobActive, bool NonCenter, int[] Participants);

internal sealed class AuditResult
{
    public string Role { get; init; } = "";
    public int Seed { get; init; }
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public Injection? Injection { get; set; }
    public int Requested { get; set; }
    public int Admitted { get; set; }
    public int Active { get; set; }
    public int ExplicitDeaths { get; set; }
    public int Pending { get; set; }
    public int Attacking { get; set; }
    public long Ticks { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public bool QueueConserved { get; set; }
    public bool ContinuousOracleVerified { get; set; }
    public bool ApiPositionsUnchanged { get; set; }
    public string RequestPlanSha256 { get; set; } = "";
    public string InputCommandsSha256 { get; set; } = "";
    public string PhysicalCommandsSha256 { get; set; } = "";
    public List<int> InitialPriority { get; } = new List<int>();
    public List<Command> Commands { get; } = new List<Command>();
}

internal static class GateLifecycleAudit
{
    public static AuditResult Execute(string role, int seed)
    {
        var result = new AuditResult { Role = role, Seed = seed };
        var timer = Stopwatch.StartNew();
        var fixture = ChallengeCatalog.Create(seed).Single(value => value.Name == "mixed-range-full-outer-ring-inner-death");
        var requests = fixture.Orders.Select(order => new AgentOrder
        {
            Id = order.Id,
            Spawn = order.Spawn,
            Radius = order.Radius,
            Range = order.Range,
            Speed = order.Id % 2 == 0 ? 250 : 500,
            Group = order.Group
        }).ToDictionary(order => order.Id);
        var pending = requests.Values.ToList();
        if (seed != 1)
        {
            pending.Sort((first, second) => Priority(first.Id, seed).CompareTo(Priority(second.Id, seed)));
        }
        result.InitialPriority.AddRange(pending.AsEnumerable().Reverse().Select(order => order.Id));
        result.RequestPlanSha256 = Program.Digest(new { requests = requests.Values, priority = result.InitialPriority });
        var admitted = new HashSet<int>();
        var deaths = new HashSet<int>();
        var previous = new Dictionary<int, UnitObservation>();
        var crowd = CreateCrowd(fixture);
        int group = 0;
        int phase = 0;
        int nextId = requests.Count + 1;
        int stable = 0;
        try
        {
            for (int tick = 0; tick <= fixture.MaxTicks; tick++)
            {
                Validate(fixture, crowd, requests, previous);
                if (phase == 1 && result.Injection == null && crowd.RecoveryActive)
                {
                    var participants = GateParticipants(crowd);
                    var victim = crowd.Agents.Where(agent => participants.Contains(agent.Id) && NonCenter(agent))
                        .FirstOrDefault(agent => role == "gate" ? agent.AttackRange == 4000 : agent.AttackRange == 1000);
                    if (victim != null)
                    {
                        result.Injection = new Injection(
                            crowd.CurrentTick,
                            victim.Id,
                            victim.Position.X,
                            victim.Position.Y,
                            victim.AttackRange,
                            crowd.RecoveryActive,
                            NonCenter(victim),
                            participants.OrderBy(id => id).ToArray());
                        Replace(new[] { victim });
                    }
                }

                var blocked = new HashSet<Cell>();
                for (int index = pending.Count - 1; index >= 0; index--)
                {
                    var request = pending[index];
                    if (request.Group > group || blocked.Contains(request.Spawn))
                    {
                        continue;
                    }
                    var before = Positions(crowd);
                    bool born = crowd.TrySpawn(request.Id, Center(request.Spawn), request.Speed, request.Radius, request.Range);
                    VerifyApi(crowd, before, tick);
                    Record(request, born ? "spawn" : "spawn-rejected", Center(request.Spawn));
                    if (born)
                    {
                        Require(admitted.Add(request.Id), "A request was admitted twice.");
                        pending.RemoveAt(index);
                        Require(Same(crowd.Agents.Single(agent => agent.Id == request.Id).Position, Center(request.Spawn)), "A birth skipped its source.");
                    }
                    else
                    {
                        Require(!crowd.Agents.Any(agent => agent.Id == request.Id), "A rejected birth created a hidden body.");
                        blocked.Add(request.Spawn);
                    }
                }

                Validate(fixture, crowd, requests, previous);
                Require(requests.Count == crowd.Agents.Count + pending.Count + deaths.Count, "Request conservation failed.");
                Require(crowd.Agents.Select(agent => agent.Id).ToHashSet().SetEquals(admitted.Except(deaths)), "Active IDs changed without births/deaths.");
                Require(pending.All(request => !admitted.Contains(request.Id) && !deaths.Contains(request.Id)), "A pending request was consumed.");
                bool allArrived = crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating);
                if (group == 0 && pending.All(request => request.Group > 0) && allArrived)
                {
                    group = 1;
                }
                bool ready = pending.Count == 0 && allArrived && crowd.Agents.Count == 40
                    && crowd.Agents.All(agent => agent.Status == NavigationAgentStatus.Attacking);
                if (ready && phase == 0)
                {
                    Replace(crowd.Agents.Where(agent => agent.AttackRange == 1000).ToArray());
                    phase = 1;
                }
                else if (ready && phase == 1 && result.Injection != null)
                {
                    Replace(crowd.Agents.Where(agent => agent.AttackRange == 1000).ToArray());
                    phase = 2;
                }
                else if (ready && phase == 2)
                {
                    stable++;
                    if (stable >= 200)
                    {
                        Require(result.Injection is { JobActive: true, NonCenter: true }, "The gate-midflight injection did not execute.");
                        Require(deaths.Count == 25 && requests.Count == 65 && admitted.Count == 65, "The intended lifecycle did not complete.");
                        result.Success = true;
                        break;
                    }
                }
                else
                {
                    stable = 0;
                }

                crowd.Step(crowd.CurrentTick + 1);
            }
            Require(result.Success, "Deadline reached; an unexecuted gate injection is a fixture failure, not a passing test.");
            result.QueueConserved = true;
            result.ContinuousOracleVerified = true;
            result.ApiPositionsUnchanged = true;
        }
        catch (Exception error)
        {
            result.Success = false;
            result.Error = error.ToString();
        }
        result.Requested = requests.Count;
        result.Admitted = admitted.Count;
        result.Active = crowd.Agents.Count;
        result.ExplicitDeaths = deaths.Count;
        result.Pending = pending.Count;
        result.Attacking = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
        result.Ticks = crowd.CurrentTick;
        result.ElapsedMilliseconds = timer.ElapsedMilliseconds;
        result.InputCommandsSha256 = Program.Digest(result.Commands);
        result.PhysicalCommandsSha256 = Program.Digest(result.Commands.Select(command => new
        {
            command.Tick, command.Kind, command.Spawn, command.Radius, command.Range, command.Speed, command.X, command.Y
        }));
        return result;

        void Record(AgentOrder request, string kind, Position2 position)
        {
            result.Commands.Add(new Command(
                crowd.CurrentTick,
                kind,
                request.Id,
                request.Spawn,
                request.Radius,
                request.Range,
                request.Speed,
                position.X,
                position.Y));
        }

        void Replace(IEnumerable<NavigationAgent> victims)
        {
            foreach (var victim in victims)
            {
                var before = Positions(crowd);
                var request = requests[victim.Id];
                Require(crowd.Remove(victim.Id) && deaths.Add(victim.Id), "A death was applied twice or failed.");
                VerifyApi(crowd, before, crowd.CurrentTick);
                Record(request, "remove", victim.Position);
                var replacement = new AgentOrder
                {
                    Id = nextId++, Spawn = request.Spawn, Radius = request.Radius,
                    Range = request.Range, Speed = request.Speed, Group = group
                };
                requests.Add(replacement.Id, replacement);
                pending.Add(replacement);
            }
        }
    }

    private static NavigationCrowd CreateCrowd(Challenge fixture)
    {
        var geometry = fixture.Geometry;
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
        var crowd = new NavigationCrowd(grid, planningHorizon: fixture.PlanningHorizon, gatheringDistance: geometry.GatheringDistance);
        foreach (var profile in fixture.Orders.Select(order => (order.Radius, order.Range)).Distinct())
        {
            crowd.RegisterProfile(profile.Radius, profile.Range);
        }
        return crowd;
    }

    private static HashSet<int> GateParticipants(NavigationCrowd crowd)
    {
        var job = typeof(NavigationCrowd).GetField("vacancyJob", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(crowd);
        return job?.GetType().GetField("GateActors", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(job) as HashSet<int> ?? new HashSet<int>();
    }

    private static void Validate(Challenge fixture, NavigationCrowd crowd, Dictionary<int, AgentOrder> requests, Dictionary<int, UnitObservation> previous)
    {
        var units = crowd.Agents.Select(agent => new UnitObservation(
            agent.Id,
            new Cell(agent.Position.X / 1000, agent.Position.Y / 1000),
            agent.Status == NavigationAgentStatus.Attacking ? NavigationState.Attacking : NavigationState.Moving,
            agent.GoalCell,
            1,
            agent.Position.X,
            agent.Position.Y,
            agent.Radius)).ToArray();
        GeometryChecks.VerifyUnitClearance(units, previous);
        foreach (var unit in units)
        {
            var before = previous.TryGetValue(unit.Id, out var prior) ? prior : unit;
            var request = requests[unit.Id];
            GeometryChecks.VerifyObstacleClearance(fixture.Geometry, before, unit);
            GeometryChecks.VerifyTargetClearance(fixture.Geometry, before, unit);
            long dx = (long)unit.X - before.X;
            long dy = (long)unit.Y - before.Y;
            Require(dx * dx + dy * dy <= (long)(request.Speed + 1) * (request.Speed + 1), "A body exceeded its requested speed.");
            var actor = crowd.Agents.Single(agent => agent.Id == unit.Id);
            Require(
                actor.Radius == request.Radius && actor.AttackRange == request.Range && actor.Speed == request.Speed,
                "A body's requested profile changed.");
            if (actor.Status == NavigationAgentStatus.Attacking)
            {
                Require(Same(actor.Position, crowd.Grid.Center(actor.GoalCell)), "An attacker did not physically reach its assigned centre.");
                Require(crowd.Grid.HoldingAllowed(actor.GoalCell), "An attacker stopped on a circulation-only cell.");
                long targetX = fixture.Geometry.Target.X * 1000L + 500;
                long targetY = fixture.Geometry.Target.Y * 1000L + 500;
                long targetDx = actor.Position.X - targetX;
                long targetDy = actor.Position.Y - targetY;
                long reach = 1000L + request.Radius + request.Range;
                Require(targetDx * targetDx + targetDy * targetDy <= reach * reach, "An actor attacked outside its own requested reach.");
                Require(crowd.Grid.HasAttackLine(actor.GoalCell, actor.Radius, actor.AttackRange), "An invalid attacker was counted.");
                GeometryChecks.VerifyAttackLine(fixture.Geometry, unit);
            }
            previous[unit.Id] = unit;
        }
    }

    private static Dictionary<int, Position2> Positions(NavigationCrowd crowd) => crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
    private static bool NonCenter(NavigationAgent actor) => actor.Position.X % 1000 != 500 || actor.Position.Y % 1000 != 500;
    private static bool Same(Position2 first, Position2 second) => first.X == second.X && first.Y == second.Y;
    private static Position2 Center(Cell cell) => new Position2(cell.X * 1000 + 500, cell.Y * 1000 + 500);

    private static void VerifyApi(NavigationCrowd crowd, Dictionary<int, Position2> before, long tick)
    {
        Require(crowd.CurrentTick == tick, "An API call advanced simulation time.");
        Require(crowd.Agents.All(agent => !before.TryGetValue(agent.Id, out var prior) || Same(prior, agent.Position)), "An API call moved an existing body.");
    }

    private static uint Priority(int id, int seed)
    {
        unchecked
        {
            uint value = (uint)id ^ (uint)seed * 0x9e3779b9U;
            value = (value ^ value >> 16) * 0x85ebca6bU;
            value = (value ^ value >> 13) * 0xc2b2ae35U;
            return value ^ value >> 16;
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
