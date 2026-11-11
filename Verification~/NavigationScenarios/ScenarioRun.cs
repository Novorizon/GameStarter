using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;

namespace GameStarter.Verification.Navigation
{
    internal sealed record ScenarioReport(bool Passed, string Summary, ulong Hash);

    internal sealed class ScenarioRun
    {
        private const int SampleInterval = 10;
        private const int IdleThreshold = 40;
        private const int RefillAttempts = 25;
        private const int RefillBudget = 180;
        private const int DrainInterval = 40;
        private const int CoordinateTolerance = 2;
        private const int MaxInterpolatedOverlap = 16;
        private readonly Scenario scenario;
        private readonly FightSession game;
        private readonly Dictionary<int, EnemyDefinition> definitions;
        private readonly Dictionary<int, Motion> motion = new();
        private readonly HashSet<int> hitIds = new();
        private readonly HashSet<int> hitRoutes = new();
        private readonly HashSet<int> crossedRoutes = new();
        private readonly HashSet<string> hitFaces = new();
        private int westernmostHit = int.MaxValue;
        private readonly Dictionary<string, (int Count, string First)> problems = new();
        private Dictionary<int, NavigationSample> previous = new();
        private TextWriter trace;
        private TextWriter events;
        private ulong hash = 14695981039346656037;
        private long firstHit = -1;
        private long lastHit = -1;
        private int peakRing;
        private int peakWaiting;
        private long warmupEnded = -1;
        private int lastRingCount;
        private long lastRingChange;
        private long emptySince = -1;
        private long longestEmpty;
        private long nextVacancy;
        private Vacancy vacancy;
        private int attempts;
        private int refilled;
        private int reachableAttempts;
        private int missedReachable;
        private int endpointOverlaps;
        private int sweptOverlaps;
        private double largestSweepOverlap;
        private string firstSweepOverlap;
        private int blockedSpawns;

        internal ScenarioRun(Scenario scenario, uint seed)
        {
            this.scenario = scenario;
            game = new FightSession(scenario.Config, seed);
            definitions = scenario.Config.Enemies.ToDictionary(enemy => enemy.Id);
        }

        internal ScenarioReport Run(string output = null, bool time = false)
        {
            using var traceOutput = output == null ? null : File.CreateText(Path.Combine(output, scenario.Name + "-trace.csv"));
            using var eventOutput = output == null ? null : File.CreateText(Path.Combine(output, scenario.Name + "-events.txt"));
            trace = traceOutput ?? TextWriter.Null;
            events = eventOutput ?? TextWriter.Null;
            trace.WriteLine("tick,id,route,type,radius,x,y,distance,segment,phase,goalX,goalY,idleTicks,waitLabel,hit");
            events.WriteLine($"stage={scenario.Config.StageId}; seed={game.Seed}; tickMilliseconds={FightSession.TickMilliseconds}");
            VerifyAuthoredRoad();
            VerifySizeLimitedOpening();
            if (scenario.BuildObstacle && !game.BuildTower(1, 1))
            {
                throw new InvalidOperationException("The flank obstruction must use a legal tower position.");
            }

            if (!game.StartNextWave())
            {
                throw new InvalidOperationException("Scenario could not enter combat.");
            }

            int authored = Fixtures.SpawnCount(scenario.Config);
            List<long> stepTimes = time ? new List<long>(scenario.Limit) : null;
            for (int i = 0; i < scenario.Limit && game.Phase == BattlePhase.Fighting; i++)
            {
                int beforeHp = game.BaseHp;
                long stepStart = time ? Stopwatch.GetTimestamp() : 0;
                game.Step();
                if (time)
                {
                    stepTimes.Add(Stopwatch.GetTimestamp() - stepStart);
                }

                var samples = game.ReadNavigationSamples().OrderBy(item => item.Id).ToArray();
                var attacking = samples.Where(item => item.Phase == NavigationPhase.Attacking).ToArray();
                InspectSamples(samples, beforeHp);
                ObserveRing(attacking.Length, samples);
                previous = samples.ToDictionary(item => item.Id);
                if (scenario.Kind is ScenarioKind.Saturation or ScenarioKind.Surround && (game.Tick == 500 || game.Tick == 1700))
                {
                    ReportParking(samples);
                }

                if (scenario.Kind == ScenarioKind.Saturation && game.Tick == 1700)
                {
                    foreach (var capacity in game.MeasureAttackCapacity())
                    {
                        events.WriteLine($"tick={game.Tick} capacity id={capacity.Id} radius={capacity.Radius} position={Point(capacity.Position)} "
                            + $"staged={capacity.Staged} staticCells={capacity.Static} freeCells=[{string.Join(",", capacity.Free.Select(Point))}] "
                            + $"reachableCells={capacity.Reachable} reachableWithoutOtherStaged={capacity.ReachableWithoutOtherStaged} "
                            + $"reachableWithAttackersOnly={capacity.ReachableWithAttackersOnly}");
                    }
                }

                if (warmupEnded >= 0 && scenario.Kind == ScenarioKind.Drain && (game.Tick - warmupEnded) % DrainInterval == 0)
                {
                    RemoveResolvedAttackers(attacking);
                    if (hitIds.Count == authored)
                    {
                        break;
                    }
                }

                if (warmupEnded >= 0 && scenario.Kind == ScenarioKind.Vacancies && ObserveVacancies(samples, attacking))
                {
                    break;
                }
            }

            ValidateResult(authored);
            ValidateNoRemovalGathering(authored);
            ValidateLogicalWitnesses();
            foreach (var problem in problems)
            {
                events.WriteLine($"failure {problem.Key} ({problem.Value.Count}): {problem.Value.First}");
            }

            if (firstSweepOverlap != null)
            {
                events.WriteLine($"diagnostic interpolated body overlap ({sweptOverlaps}): {firstSweepOverlap}");
            }

            string summary = $"tick={game.Tick}, phase={game.Phase}, spawned={motion.Count}/{authored}, exits={game.Leaks}, hitIds={hitIds.Count}, "
                + $"firstHit={firstHit}, peakRing={peakRing}, peakWaiting={peakWaiting}, saturatedAt={warmupEnded}, longestEmptyTicks={longestEmpty}, "
                + $"refill={refilled}/{attempts}, reachableVacancies={reachableAttempts}, missedReachable={missedReachable}, "
                + $"blockedSpawns={blockedSpawns}, overlaps={endpointOverlaps}, sweptOverlaps={sweptOverlaps}, "
                + $"largestSweepOverlapMm={largestSweepOverlap:F1}, replay={hash:X16}";
            if (problems.Count != 0)
            {
                summary += $", failed={string.Join(",", problems.Keys)}";
            }

            if (stepTimes?.Count > 0)
            {
                stepTimes.Sort();
                double p50 = Percentile(0.50);
                double p95 = Percentile(0.95);
                double p99 = Percentile(0.99);
                double max = Percentile(1.00);
                summary += FormattableString.Invariant($", stepMs[p50={p50:F3},p95={p95:F3},p99={p99:F3},max={max:F3}]");

                double Percentile(double fraction)
                {
                    int index = (int)Math.Ceiling(fraction * stepTimes.Count) - 1;
                    return 1000d * stepTimes[index] / Stopwatch.Frequency;
                }
            }

            events.WriteLine(summary);
            return new ScenarioReport(problems.Count == 0, summary, hash);
        }

        private void InspectSamples(NavigationSample[] samples, int beforeHp)
        {
            Mix(game.Tick);
            Mix(game.BaseHp);
            Mix(game.Leaks);
            Mix((int)game.Phase);
            int expectedDamage = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = samples[i];
                bool existed = previous.TryGetValue(sample.Id, out var old);
                var origin = existed ? old.Position : scenario.Config.Routes.Single(route => route.Id == sample.RouteId).Points[0];
                if (!motion.TryGetValue(sample.Id, out var track))
                {
                    track = new Motion(sample, game.Tick);
                    motion.Add(sample.Id, track);
                }

                if (track.Current.Phase != sample.Phase || DistanceSquared(track.Anchor, sample.Position) >= 80 * 80 || track.Current.Segment != sample.Segment)
                {
                    track.Anchor = sample.Position;
                    track.LastProgress = game.Tick;
                }

                InspectTerrain(sample, origin);
                InspectProgress(sample, track, origin);
                for (int j = 0; scenario.Config.Terrain != null && j < i; j++)
                {
                    InspectPair(sample, origin, samples[j], existed);
                }

                if (sample.Hit)
                {
                    expectedDamage += definitions[sample.Type].BaseDamage;
                    hitIds.Add(sample.Id);
                    hitRoutes.Add(sample.RouteId);
                    var engagement = scenario.Config.Objective.BaseAttack?.Engagement;
                    if (engagement != null)
                    {
                        hitFaces.Add(ParkingFace(sample.Position, engagement));
                    }

                    westernmostHit = Math.Min(westernmostHit, sample.Position.X);
                    lastHit = game.Tick;
                    if (firstHit < 0)
                    {
                        firstHit = game.Tick;
                    }
                }

                if (scenario.Name == "crossing-entries" && DistanceSquared(sample.Position, new Position2(-2100, 0)) <= 150L * 150)
                {
                    crossedRoutes.Add(sample.RouteId);
                }

                WriteTrace(sample, track, samples);
                track.Current = sample;
                Mix(sample.Id);
                Mix(sample.Position.X);
                Mix(sample.Position.Y);
                Mix(sample.Distance);
                Mix(sample.Segment);
                Mix((int)sample.Phase);
                Mix(sample.Goal.X);
                Mix(sample.Goal.Y);
                Mix(sample.Hit ? 1 : 0);
            }

            if (scenario.Config.Objective.Type == BattleObjectiveType.DefendBase && game.Phase != BattlePhase.Defeat && beforeHp - game.BaseHp != expectedDamage)
            {
                Problem("damage-does-not-match-attacks", $"tick={game.Tick} expected={expectedDamage} actual={beforeHp - game.BaseHp}");
            }
        }

        private void InspectTerrain(NavigationSample sample, Position2 origin)
        {
            var terrain = scenario.Config.Terrain;
            if (terrain == null)
            {
                return;
            }

            if (!terrain.CanMove(origin, sample.Position, sample.Radius))
            {
                Problem("terrain-sweep", $"{Describe(sample)} from={Point(origin)}");
            }

            var engagement = scenario.Config.Objective.BaseAttack?.Engagement;
            if (engagement != null)
            {
                if (engagement.DistanceToFootprintSquared(sample.Position) < (long)sample.Radius * sample.Radius)
                {
                    Problem("base-footprint", Describe(sample));
                }

                if (sample.Hit && !engagement.CanAttackFrom(sample.Position, sample.Radius))
                {
                    Problem("hit-outside-reach", Describe(sample));
                }
            }

            foreach (var tower in game.Towers)
            {
                int clearance = sample.Radius + FightSession.TowerFoundationRadiusMillimetres;
                if (SweepDistanceSquared(origin, sample.Position, tower.Position, tower.Position) < (long)clearance * clearance)
                {
                    Problem("tower-sweep", $"{Describe(sample)} tower={tower.Id}");
                }
            }
        }

        private void InspectProgress(NavigationSample sample, Motion track, Position2 origin)
        {
            if (sample.Distance < track.Current.Distance || sample.Segment < track.Current.Segment)
            {
                Problem("road-reversed", Describe(sample));
            }

            if (scenario.Config.Terrain != null && DistanceSquared(origin, sample.Position) > (long)(sample.Speed + CoordinateTolerance) * (sample.Speed + CoordinateTolerance))
            {
                Problem("faster-than-walk", $"{Describe(sample)} from={Point(origin)} speed={sample.Speed}");
            }

            if (sample.Phase != NavigationPhase.Road && sample.Phase != NavigationPhase.Gate && scenario.Config.Terrain != null)
            {
                int beforeFinal = scenario.Config.Routes.Single(route => route.Id == sample.RouteId).Points.Length - 2;
                if (sample.Segment < beforeFinal)
                {
                    Problem("approach-before-final-road", $"{Describe(sample)} finalSegment={beforeFinal}");
                }
            }

            if (sample.Phase == NavigationPhase.Attacking && track.AttackSince < 0)
            {
                track.AttackSince = game.Tick;
            }
            else if (sample.Phase != NavigationPhase.Attacking)
            {
                track.AttackSince = -1;
                track.LastHit = -1;
            }

            if (sample.Hit)
            {
                int windup = scenario.Config.Objective.BaseAttack?.Engagement?.WindupTicks ?? 0;
                long since = track.LastHit < 0 ? track.AttackSince : track.LastHit;
                int interval = track.LastHit < 0 ? windup : scenario.Config.Objective.BaseAttackIntervalTicks;
                if (game.Tick - since != interval)
                {
                    Problem("attack-timing", $"{Describe(sample)} since={since} expectedInterval={interval}");
                }

                track.LastHit = game.Tick;
            }
        }

        private void InspectPair(NavigationSample first, Position2 firstOrigin, NavigationSample second, bool existed)
        {
            int distance = first.Radius + second.Radius;
            long required = (long)distance * distance;
            if (DistanceSquared(first.Position, second.Position) < required)
            {
                endpointOverlaps++;
                Problem("body-overlap", $"{Describe(first)} and {Describe(second)}");
            }
            else if (existed && previous.TryGetValue(second.Id, out var oldSecond))
            {
                double sweep = SweepDistanceSquared(firstOrigin, first.Position, oldSecond.Position, second.Position);
                if (sweep < (long)(distance - CoordinateTolerance) * (distance - CoordinateTolerance))
                {
                    sweptOverlaps++;
                    double depth = distance - Math.Sqrt(sweep);
                    largestSweepOverlap = Math.Max(largestSweepOverlap, depth);
                    string detail = $"{Describe(first)} from={Point(firstOrigin)} and {Describe(second)} from={Point(oldSecond.Position)} depth={depth:F1}";
                    firstSweepOverlap ??= detail;
                    if (depth > MaxInterpolatedOverlap)
                    {
                        Problem("deep-interpolated-body-overlap", detail);
                    }
                }
            }
        }

        private void ObserveRing(int attackers, NavigationSample[] samples)
        {
            peakRing = Math.Max(peakRing, attackers);
            int waiting = samples.Count(item => item.Phase == NavigationPhase.Gate || item.Phase == NavigationPhase.Staged);
            peakWaiting = Math.Max(peakWaiting, waiting);
            if (attackers != lastRingCount)
            {
                lastRingChange = game.Tick;
                lastRingCount = attackers;
            }

            if (warmupEnded < 0 && scenario.MinimumRing > 0 && attackers >= scenario.MinimumRing && waiting > 0 && game.Tick - lastRingChange >= 100)
            {
                warmupEnded = game.Tick;
                events.WriteLine($"tick={game.Tick} stable ring={attackers} stagedOrGate={waiting} active={samples.Length}");
            }

            if (warmupEnded >= 0 && attackers == 0 && samples.Length > 0)
            {
                emptySince = emptySince < 0 ? game.Tick : emptySince;
                longestEmpty = Math.Max(longestEmpty, game.Tick - emptySince + 1);
            }
            else
            {
                emptySince = -1;
            }
        }

        private void RemoveResolvedAttackers(NavigationSample[] attacking)
        {
            var cleared = attacking.Where(item => hitIds.Contains(item.Id)).ToArray();
            foreach (var attacker in cleared)
            {
                game.RemoveFixtureEnemy(attacker.Id);
            }

            if (cleared.Length > 0)
            {
                events.WriteLine($"tick={game.Tick} cleared={string.Join(",", cleared.Select(item => item.Id))} hitUnique={hitIds.Count}");
            }
        }

        private bool ObserveVacancies(NavigationSample[] samples, NavigationSample[] attacking)
        {
            if (vacancy != null)
            {
                var newAttacker = attacking.FirstOrDefault(item => item.Hit && !vacancy.OriginalAttackers.Contains(item.Id));
                if (newAttacker.Id != 0)
                {
                    refilled++;
                    events.WriteLine($"tick={game.Tick} refill removed={vacancy.Removed.Id} joined={newAttacker.Id} delay={game.Tick - vacancy.Tick}");
                }
                else if (game.Tick - vacancy.Tick < RefillBudget)
                {
                    return false;
                }
                else
                {
                    if (vacancy.Reachable)
                    {
                        missedReachable++;
                    }

                    events.WriteLine($"tick={game.Tick} timeout removed={vacancy.Removed.Id} reachable={vacancy.Reachable} point={Point(vacancy.Removed.Position)}");
                }

                vacancy = null;
                nextVacancy = game.Tick + 35;
            }

            if (attempts >= RefillAttempts)
            {
                return true;
            }

            if (game.Tick < nextVacancy || attacking.Length < scenario.MinimumRing)
            {
                return false;
            }

            var candidates = attacking.Where(item => hitIds.Contains(item.Id)).ToArray();
            if (candidates.Length == 0)
            {
                return false;
            }

            var removed = candidates[attempts % candidates.Length];
            bool reachable = HasDirectReplacement(removed, samples);
            reachableAttempts += reachable ? 1 : 0;
            attempts++;
            vacancy = new Vacancy(removed, game.Tick, reachable, attacking.Select(item => item.Id).ToHashSet());
            game.RemoveFixtureEnemy(removed.Id);
            events.WriteLine($"tick={game.Tick} vacancy removed={removed.Id} reachable={reachable} point={Point(removed.Position)}");
            return false;
        }

        private bool HasDirectReplacement(NavigationSample removed, NavigationSample[] samples)
        {
            var terrain = scenario.Config.Terrain;
            var engagement = scenario.Config.Objective.BaseAttack.Engagement;
            foreach (var candidate in samples)
            {
                if (candidate.Phase != NavigationPhase.Staged && candidate.Phase != NavigationPhase.Gate)
                {
                    continue;
                }

                var from = candidate.Position;
                var to = removed.Position;
                int radius = candidate.Radius;
                if (DistanceSquared(from, to) > 2500 * 2500 || !engagement.CanAttackFrom(to, radius) || !terrain.CanMove(from, to, radius))
                {
                    continue;
                }

                if (samples.All(other => other.Id == candidate.Id || other.Id == removed.Id
                    || SweepDistanceSquared(from, to, other.Position, other.Position) >= (long)(radius + other.Radius) * (radius + other.Radius)))
                {
                    return true;
                }
            }

            return false;
        }

        private void WriteTrace(NavigationSample sample, Motion track, NavigationSample[] samples)
        {
            long idle = game.Tick - track.LastProgress;
            if (trace == TextWriter.Null || (game.Tick % SampleInterval != 0 && !sample.Hit && idle != IdleThreshold))
            {
                return;
            }

            string label = WaitLabel(sample, idle, samples);
            trace.WriteLine($"{game.Tick},{sample.Id},{sample.RouteId},{sample.Type},{sample.Radius},"
                + $"{sample.Position.X},{sample.Position.Y},{sample.Distance},{sample.Segment},{sample.Phase},{sample.Goal.X},{sample.Goal.Y},{idle},{label},{(sample.Hit ? 1 : 0)}");
        }

        private string WaitLabel(NavigationSample sample, long idle, NavigationSample[] samples)
        {
            if (sample.Phase == NavigationPhase.Staged || sample.Phase == NavigationPhase.Gate)
            {
                return "awaiting-assault-position";
            }

            if (sample.Phase == NavigationPhase.Attacking || idle < IdleThreshold)
            {
                return "";
            }

            var nearest = samples.Where(item => item.Id != sample.Id)
                .OrderBy(item => DistanceSquared(sample.Position, item.Position)).FirstOrDefault();
            int gap = (int)Math.Sqrt(DistanceSquared(sample.Position, nearest.Position)) - sample.Radius - nearest.Radius;
            return nearest.Id != 0 && gap <= sample.Speed + 80 ? $"near-unit-{nearest.Id}" : "no-nearby-body";
        }

        private void ValidateResult(int authored)
        {
            switch (scenario.Kind)
            {
                case ScenarioKind.Exit:
                    int cap = scenario.Config.Objective.EscapesToDefeat;
                    if (game.Leaks != Math.Min(cap, authored) || game.Phase != (cap <= authored ? BattlePhase.Defeat : BattlePhase.Victory))
                    {
                        Problem("road-did-not-clear", $"leaks={game.Leaks}, authored={authored}, cap={cap}, phase={game.Phase}");
                    }

                    break;

                case ScenarioKind.SealedRoute:
                    if (game.Leaks != 0 || motion.Count == 0 || previous.Values.Any(unit => unit.Position.X >= -5200))
                    {
                        Problem("sealed-route-bypassed", $"leaks={game.Leaks}, admitted={motion.Count}");
                    }

                    break;

                case ScenarioKind.SealedApproach:
                    var blockedAtGate = game.MeasureAttackCapacity();
                    if (firstHit >= 0 || blockedAtGate.Length == 0 || blockedAtGate.Any(unit => unit.Static != 0) || game.BaseHp != game.BaseMaxHp)
                    {
                        Problem("sealed-local-approach-invalid", $"firstHit={firstHit}, atGate={blockedAtGate.Length}, hp={game.BaseHp}");
                    }

                    events.WriteLine($"tick={game.Tick} sealed local approach: localWaiters={blockedAtGate.Length}, geometryReachableAttackCells=0 expected");
                    break;
                case ScenarioKind.Defeat:
                    if (game.Phase != BattlePhase.Defeat || game.BaseHp != 0 || firstHit < 0)
                    {
                        Problem("base-did-not-fall", $"phase={game.Phase}, hp={game.BaseHp}, firstHit={firstHit}");
                    }

                    break;
                case ScenarioKind.Drain:
                    if (hitIds.Count != authored || hitRoutes.Count != scenario.Config.Routes.Length || longestEmpty > 220)
                    {
                        Problem("drain-stalled", $"actual hits={hitIds.Count}/{authored}, routes={hitRoutes.Count}, idle={longestEmpty}");
                    }

                    break;
                case ScenarioKind.Vacancies:
                    if (attempts != RefillAttempts || refilled < RefillAttempts / 2 || missedReachable > 0)
                    {
                        Problem("vacancy-refill-stalled", $"refilled={refilled}/{attempts}, confirmed reachable missed={missedReachable}");
                    }

                    break;
                case ScenarioKind.Saturation:
                case ScenarioKind.Surround:
                    var blocked = game.ReadPendingEntries().Where(entry => previous.Values.Any(unit =>
                        DistanceSquared(unit.Position, entry.Position) < (long)(unit.Radius + entry.Radius) * (unit.Radius + entry.Radius))).ToArray();
                    blockedSpawns = blocked.Sum(entry => entry.Due);
                    if ((!game.AllEnemiesSpawned && blockedSpawns == 0) || lastRingCount < scenario.MinimumRing
                        || lastHit < game.Tick - scenario.Config.Objective.BaseAttackIntervalTicks)
                    {
                        Problem("saturation-incomplete", $"attackers={lastRingCount}, admitted={motion.Count}/{authored}, blockedDue={blockedSpawns}, lastHit={lastHit}");
                    }

                    if (scenario.Kind == ScenarioKind.Surround)
                    {
                        ValidateSurround();
                    }

                    break;
            }

            if (scenario.MinimumRing > 0 && warmupEnded < 0)
            {
                Problem("stable-ring-not-observed", $"peak={peakRing}, waiting={peakWaiting}");
            }
        }

        private void Problem(string name, string detail)
        {
            problems.TryGetValue(name, out var existing);
            problems[name] = (existing.Count + 1, existing.First ?? detail);
        }

        private void Mix(long value)
        {
            hash = unchecked((hash ^ (ulong)value) * 1099511628211);
        }

        private string Describe(NavigationSample sample) => $"tick={game.Tick} id={sample.Id} phase={sample.Phase} p={Point(sample.Position)} goal={Point(sample.Goal)}";

        private void VerifySizeLimitedOpening()
        {
            if (scenario.Name != "size-limited-flank")
            {
                return;
            }

            var terrain = scenario.Config.Terrain;
            var west = new Position2(500, 0);
            var east = new Position2(2000, 0);
            bool lightCanCross = terrain.CanMove(west, east, definitions[1].BodyRadius);
            bool heavyCanCross = terrain.CanMove(west, east, definitions[2].BodyRadius);
            events.WriteLine($"opening: clear span 820mm, light sweep={lightCanCross}, heavy sweep={heavyCanCross}");
            if (!lightCanCross || heavyCanCross)
            {
                Problem("mixed-width-opening-invalid", $"lightCanCross={lightCanCross}, heavyCanCross={heavyCanCross}");
            }
        }

        private void ValidateLogicalWitnesses()
        {
            if (scenario.Name == "compound-detour" && (firstHit < 0 || westernmostHit <= scenario.Config.Objective.BaseAttack.Engagement.Max.X))
            {
                Problem("compound-attack-did-not-use-open-side", $"westernmostHit={westernmostHit}, faces={string.Join(",", hitFaces)}");
            }

            if (scenario.Name == "crossing-entries" && crossedRoutes.Count != 2)
            {
                Problem("routes-did-not-cross", $"routes within 150mm of crossing={crossedRoutes.Count}");
            }

            if (scenario.Name == "wide-three-vacancies" && reachableAttempts == 0)
            {
                Problem("no-directly-reachable-vacancy", $"refilled={refilled}/{attempts}");
            }

            string westernmost = westernmostHit == int.MaxValue ? "none" : westernmostHit.ToString();
            events.WriteLine($"attack faces={string.Join(",", hitFaces.OrderBy(side => side))}; westernmostHit={westernmost}; crossing routes={crossedRoutes.Count}");
        }

        private void ValidateNoRemovalGathering(int authored)
        {
            if (scenario.Kind != ScenarioKind.Saturation)
            {
                return;
            }

            var positions = game.ReadNavigationSamples().ToArray();
            int attacking = positions.Count(unit => unit.Phase == NavigationPhase.Attacking);
            int parked = positions.Count(unit => unit.Phase == NavigationPhase.Staged);
            var capacities = game.MeasureIntakeCapacity().ToArray();
            events.WriteLine($"tick={game.Tick} no-removal active={positions.Length} attacking={attacking} parked={parked} "
                + $"entrance={string.Join(';', capacities.Select(slot => $"{slot.Id}/r{slot.Radius}:live={slot.LiveSites},yield={slot.SitesWithClearance}"))}");
            if (scenario.Name.StartsWith("forest-", StringComparison.Ordinal))
            {
                if (parked < 40 || capacities.Any(slot => slot.SitesWithClearance > 0))
                {
                    Problem("forest-premature-saturation", $"parked={parked}, intake=[{string.Join(',', capacities)}]");
                }
            }
            else if (attacking + parked != authored)
            {
                Problem("incomplete-no-removal-gathering", $"stationary={attacking + parked}/{authored}");
            }
        }

        private void ReportParking(NavigationSample[] samples)
        {
            var bounds = scenario.Config.Objective.BaseAttack.Engagement;
            var staged = samples.Where(item => item.Phase == NavigationPhase.Staged).ToArray();
            events.WriteLine($"tick={game.Tick} parking occupied=[{SideCounts(staged.Select(item => item.Position))}] "
                + $"positions=[{string.Join(",", staged.Select(item => Point(item.Position)))}]");
            foreach (var capacity in game.MeasureParkingCapacity())
            {
                events.WriteLine($"tick={game.Tick} parking radius={capacity.Radius} gap={capacity.MinimumGap} "
                    + $"static=[{SideCounts(capacity.Static)}] live=[{SideCounts(capacity.Live)}] attackOnly=[{SideCounts(capacity.AttackersOnly)}] "
                    + $"attackOnlyPack=[{SideCounts(Pack(capacity.AttackersOnly, capacity.Radius))}]");
            }

            string SideCounts(IEnumerable<Position2> points) => string.Join(",", points.GroupBy(point => ParkingFace(point, bounds)).OrderBy(group => group.Key)
                .Select(group => $"{group.Key}={group.Count()}"));

            IEnumerable<Position2> Pack(Position2[] points, int radius)
            {
                var packed = new List<Position2>();
                int spacing = 2 * radius + 2;
                foreach (var point in points.OrderBy(point => bounds.DistanceToFootprintSquared(point)))
                {
                    if (packed.All(other => DistanceSquared(other, point) >= (long)spacing * spacing))
                    {
                        packed.Add(point);
                    }
                }

                return packed;
            }
        }

        private void ValidateSurround()
        {
            var bounds = scenario.Config.Objective.BaseAttack.Engagement;
            var parked = previous.Values.Where(unit => unit.Phase == NavigationPhase.Staged).ToArray();
            var sides = parked.GroupBy(unit => ParkingFace(unit.Position, bounds)).OrderBy(group => group.Key).ToArray();
            string perSide = string.Join(",", sides.Select(group => $"{group.Key}={group.Count()}"));
            events.WriteLine($"tick={game.Tick} surround staged={parked.Length} faces={sides.Length} [{perSide}]");
            if (parked.Length < 6 || sides.Length < 3)
            {
                Problem("insufficient-surround", $"staged={parked.Length}/6 faces={sides.Length}/3 [{perSide}]");
            }
        }

        private static string ParkingFace(Position2 point, BaseAttackEngagement bounds)
        {
            int dx = point.X - Math.Clamp(point.X, bounds.Min.X, bounds.Max.X);
            int dy = point.Y - Math.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y);
            return Math.Abs(dx) >= Math.Abs(dy) ? (dx < 0 ? "W" : "E") : (dy < 0 ? "S" : "N");
        }

        private void VerifyAuthoredRoad()
        {
            var config = scenario.Config;
            var terrain = config.Terrain;
            if (terrain == null)
            {
                return;
            }

            bool obstructed = false;
            foreach (var route in config.Routes)
            {
                int radius = config.Waves.SelectMany(wave => wave.Groups).Where(group => group.RouteId == route.Id)
                    .Max(group => definitions[group.EnemyId].BodyRadius);
                int inset = config.Objective.HasBase ? config.Objective.BaseAttack?.ApproachInset ?? route.BaseEntranceInset : 0;
                var points = route.Points;
                for (int i = 1; i < points.Length; i++)
                {
                    var from = points[i - 1];
                    var to = points[i];
                    if (i == points.Length - 1 && inset > 0)
                    {
                        int length = (int)Math.Sqrt(DistanceSquared(from, to));
                        if (inset >= length)
                        {
                            Problem("handoff-before-final-segment", $"route={route.Id}, finalLength={length}, inset={inset}");
                            continue;
                        }

                        to = new Position2(from.X + (to.X - from.X) * (length - inset) / length, from.Y + (to.Y - from.Y) * (length - inset) / length);
                        var engagement = config.Objective.BaseAttack?.Engagement;
                        if (engagement != null && engagement.DistanceToFootprintSquared(to) < (long)radius * radius)
                        {
                            Problem("invalid-handoff", $"route={route.Id}, position={Point(to)}, radius={radius}");
                        }
                    }

                    if (!terrain.CanMove(from, to, radius))
                    {
                        obstructed = true;
                        events.WriteLine($"authored-road-obstruction route={route.Id} segment={i - 1} from={Point(from)} to={Point(to)} radius={radius}");
                    }
                }
            }

            if (obstructed != (scenario.Kind == ScenarioKind.SealedRoute))
            {
                Problem("unexpected-authored-road-clearance", $"obstructed={obstructed}");
            }
        }

        private static string Point(Position2 point) => $"({point.X},{point.Y})";

        private static long DistanceSquared(Position2 first, Position2 second)
        {
            long x = first.X - second.X;
            long y = first.Y - second.Y;
            return x * x + y * y;
        }

        private static double SweepDistanceSquared(Position2 firstFrom, Position2 firstTo, Position2 secondFrom, Position2 secondTo)
        {
            double x = firstFrom.X - secondFrom.X;
            double y = firstFrom.Y - secondFrom.Y;
            double dx = firstTo.X - secondTo.X - x;
            double dy = firstTo.Y - secondTo.Y - y;
            double length = dx * dx + dy * dy;
            double fraction = length == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / length, 0, 1);
            double atX = x + fraction * dx;
            double atY = y + fraction * dy;
            return atX * atX + atY * atY;
        }

        private sealed record Vacancy(NavigationSample Removed, long Tick, bool Reachable, HashSet<int> OriginalAttackers);

        private sealed class Motion
        {
            internal NavigationSample Current;
            internal Position2 Anchor;
            internal long LastProgress;
            internal long AttackSince = -1;
            internal long LastHit = -1;

            internal Motion(NavigationSample sample, long tick)
            {
                Current = sample;
                Anchor = sample.Position;
                LastProgress = tick;
            }
        }
    }
}
