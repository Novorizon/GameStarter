using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Single-player fixed-tick rules. No Unity objects, services, wall clock or global random.</summary>
    public sealed partial class FightSession
    {
        public const int TickMilliseconds = 50;
        public const int TowerFoundationRadiusMillimetres = 420;
        private const int RoadLaneOffsetMillimetres = 360;
        private const int EnemyMoveStepMillimetres = 180;

        private sealed class Tower
        {
            internal int Id;
            internal TowerDefinition Definition;
            internal BuildSlotDefinition Slot;
            internal int PaidBuildCost;
            internal int Level = 1;
            internal int Cooldown;
            internal int LastTargetId;
            internal int ShotSequence;
        }

        private sealed class Enemy
        {
            internal int Id;
            internal EnemyDefinition Definition;
            internal Route Route;
            internal int BatchIndex;
            internal int Health;
            internal int Distance;
            internal int Lane;
            internal Position2 Position;
            internal BaseAttackOrder BaseAttack;
            internal AssaultMotion AssaultOrder;
            internal long NextBaseAttackTick;
            internal int RoadSegmentIndex;
            internal int RoadSegmentStart;
            internal NavigationAgent NavigationAgent;
        }

        private sealed class Route
        {
            internal RouteDefinition Definition;
            internal int[] SegmentLengths;
            internal int Length;
            internal int RelayDistance;
            internal int EndInset;
            internal int LargestBodyRadius;
            internal Position2[] LaneOffsets;

            internal Position2 Position(int distance, int lane = 0)
            {
                for (int i = 0; i < SegmentLengths.Length; i++)
                {
                    int length = SegmentLengths[i];
                    if (distance <= length)
                    {
                        var first = Definition.Points[i];
                        var second = Definition.Points[i + 1];
                        int x = Interpolate(first.X, second.X, distance, length);
                        int y = Interpolate(first.Y, second.Y, distance, length);
                        if (lane != 0)
                        {
                            var firstOffset = LaneOffsets[i];
                            var secondOffset = LaneOffsets[i + 1];
                            int laneLength = i == SegmentLengths.Length - 1 ? length - EndInset : length;
                            x += lane * Interpolate(firstOffset.X, secondOffset.X, distance, laneLength);
                            y += lane * Interpolate(firstOffset.Y, secondOffset.Y, distance, laneLength);
                        }

                        return new Position2(x, y);
                    }

                    distance -= length;
                }

                return Definition.Points[Definition.Points.Length - 1];
            }

            private static int Interpolate(int start, int end, int distance, int length)
            {
                return start + (int)((long)(end - start) * distance / length);
            }
        }

        private readonly Dictionary<int, TowerDefinition> towerDefinitions;
        private readonly Dictionary<int, EnemyDefinition> enemyDefinitions;
        private readonly Dictionary<int, BuildSlotDefinition> slots;
        private readonly Dictionary<int, Route> routes;
        private readonly List<Tower> towers = new List<Tower>();
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<Enemy> movementOrder = new List<Enemy>();
        private readonly List<TowerSnapshot> towerSnapshots = new List<TowerSnapshot>();
        private readonly List<EnemySnapshot> enemySnapshots = new List<EnemySnapshot>();
        private readonly List<CardDefinition> offers = new List<CardDefinition>();
        private readonly Dictionary<int, int> cardStacks = new Dictionary<int, int>();
        private readonly AssaultRules assault;
        private readonly bool useRoadTraffic;
        private readonly bool useAuthoredNavigation;
        private readonly int largestAssaultBodyRadius;
        private readonly int[] remainingByBatch;
        private int[] emitted = Array.Empty<int>();
        private int waveTick;
        private bool batchSpawned;
        private long nextBatchAt;
        private int spentDraftPoints;
        private int nextTowerId = 1;
        private int nextEnemyId = 1;
        private int damageBonus;
        private int speedBonus;
        private int rangeBonus;
        private bool usedOpeningDiscount;
        private uint random;

        /// <summary>Configuration is read-only for the duration of a run; feed a fresh DTO from table adapters.</summary>
        public FightConfiguration Configuration { get; }
        public uint Seed { get; }
        public BattlePhase Phase { get; private set; } = BattlePhase.Planning;
        public int WaveIndex { get; private set; }
        public int Gold { get; private set; }
        public int BaseHp { get; private set; }
        public int BaseMaxHp { get; private set; }
        public int BaseLevel { get; private set; }
        public long Tick { get; private set; }
        public bool IsPaused { get; private set; }
        public int Kills { get; private set; }
        public int Leaks { get; private set; }
        public int DraftPoints => Kills - spentDraftPoints;
        public int DraftPointsRequired { get; private set; }
        public bool AllEnemiesSpawned => batchSpawned && WaveIndex == Configuration.Waves.Length;
        public int ReinforcementTicksRemaining => assault != null && batchSpawned && !AllEnemiesSpawned
            ? (int)Math.Max(0, nextBatchAt - Tick) : 0;
        public bool RepairUsed { get; private set; }
        public bool RelayHeld { get; private set; }
        public bool CanRepair => Configuration.Objective.HasBase && Configuration.PlanningRepair > 0 && !RepairUsed
            && !IsPaused && (Phase == BattlePhase.Planning || assault != null && Phase == BattlePhase.Fighting) && BaseHp < BaseMaxHp;
        public BaseUpgrade? NextBaseUpgrade => BaseLevel > 0 && BaseLevel <= Configuration.Objective.BaseUpgrades.Count
            ? Configuration.Objective.BaseUpgrades[BaseLevel - 1] : (BaseUpgrade?)null;
        public bool CanUpgradeBase => CanBuild() && NextBaseUpgrade is BaseUpgrade upgrade && Gold >= upgrade.GoldCost;
        public int TowersBuilt => nextTowerId - 1;
        public int RemainingEnemiesInWave
        {
            get
            {
                if (Phase != BattlePhase.Fighting)
                {
                    return 0;
                }

                if (remainingByBatch != null)
                {
                    return remainingByBatch[WaveIndex - 1];
                }

                var groups = Configuration.Waves[WaveIndex - 1].Groups;
                int remaining = enemies.Count;
                for (int index = 0; index < groups.Length; index++)
                {
                    remaining += groups[index].Count - emitted[index];
                }

                return remaining;
            }
        }

        public IReadOnlyList<TowerSnapshot> Towers { get; }
        public IReadOnlyList<EnemySnapshot> Enemies { get; }
        public IReadOnlyList<CardDefinition> CurrentCardOffers { get; }
        public IReadOnlyDictionary<int, int> ChosenCardStacks { get; }

        public FightSession(FightConfiguration configuration, uint seed = 1)
            : this(configuration, seed, null)
        {
        }

        internal FightSession(FightConfiguration configuration, uint seed, AssaultRules defaultAssault)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Validate(configuration);
            useRoadTraffic = configuration.Objective.Type == BattleObjectiveType.DefendBase || configuration.Terrain != null;
            useAuthoredNavigation = configuration.Terrain != null || configuration.Objective.BaseAttack?.LocalArea != null;
            assault = configuration.Objective.Assault ?? defaultAssault;
            if (assault != null)
            {
                remainingByBatch = configuration.Waves.Select(batch => batch.Groups.Sum(group => group.Count)).ToArray();
                DraftPointsRequired = assault.FirstDraftPoints;
            }

            Seed = seed;
            random = seed == 0 ? 0x9e3779b9u : seed;
            towerDefinitions = configuration.Towers.ToDictionary(value => value.Id);
            enemyDefinitions = configuration.Enemies.ToDictionary(value => value.Id);
            slots = configuration.BuildSlots.ToDictionary(value => value.Id);
            int attackInset = configuration.Objective.BaseAttack?.ApproachInset ?? 0;
            routes = configuration.Routes.ToDictionary(value => value.Id, value => CreateRoute(value, configuration.Objective.HasBase, attackInset));
            if (useRoadTraffic)
            {
                foreach (var route in routes.Values)
                {
                    route.LaneOffsets = CreateLaneOffsets(route.Definition.Points);
                }
            }

            if (useAuthoredNavigation)
            {
                foreach (var group in configuration.Waves.SelectMany(wave => wave.Groups))
                {
                    var route = routes[group.RouteId];
                    route.LargestBodyRadius = Math.Max(route.LargestBodyRadius, enemyDefinitions[group.EnemyId].BodyRadius);
                }

                largestAssaultBodyRadius = routes.Values.Max(route => route.LargestBodyRadius);
            }

            InitializeANavigation();
            ReserveBaseAttackCorridors();
            Gold = configuration.InitialGold;
            BaseLevel = configuration.Objective.HasBase ? 1 : 0;
            BaseMaxHp = BaseHp = configuration.Objective.HasBase ? configuration.BaseHealth : 0;
            RelayHeld = configuration.RelayRouteId != 0;
            if (RelayHeld)
            {
                var relayRoute = routes[configuration.RelayRouteId];
                relayRoute.RelayDistance = relayRoute.SegmentLengths.Take(configuration.RelayPointIndex).Sum();
            }
            Towers = towerSnapshots.AsReadOnly();
            Enemies = enemySnapshots.AsReadOnly();
            CurrentCardOffers = offers.AsReadOnly();
            ChosenCardStacks = new ReadOnlyDictionary<int, int>(cardStacks);
        }

        public bool CanBuildTower(int slotId, int towerTypeId)
        {
            if (!CanBuild() || !slots.TryGetValue(slotId, out var slot) || !towerDefinitions.TryGetValue(towerTypeId, out var definition)
                || Gold < BuildCost(definition))
            {
                return false;
            }

            int clearance = Configuration.BuildGridStep > 0 ? 2 * TowerFoundationRadiusMillimetres : 0;
            foreach (var tower in towers)
            {
                long dx = (long)tower.Slot.Position.X - slot.Position.X;
                long dy = (long)tower.Slot.Position.Y - slot.Position.Y;
                if (tower.Slot.Id == slotId || dx * dx + dy * dy < clearance * clearance)
                {
                    return false;
                }
            }

            return !useAuthoredNavigation || CanBuildAmongTerrainTraffic(slot.Position);
        }

        public bool BuildTower(int slotId, int towerTypeId)
        {
            if (!CanBuildTower(slotId, towerTypeId))
            {
                return false;
            }

            var slot = slots[slotId];
            if (aCrowd != null && !ChangeATowerTerrain(slot.Position, building: true))
            {
                return false;
            }

            var definition = towerDefinitions[towerTypeId];
            int paid = BuildCost(definition);
            Gold -= paid;
            usedOpeningDiscount |= definition.Id == Configuration.OpeningDiscountTowerId;
            towers.Add(new Tower { Id = nextTowerId++, Slot = slot, Definition = definition, PaidBuildCost = paid });
            RefreshSnapshots();
            return true;
        }

        public int BuildCost(TowerDefinition definition)
        {
            return !usedOpeningDiscount && definition.Id == Configuration.OpeningDiscountTowerId
                ? Math.Max(0, definition.BuildCost - Configuration.OpeningDiscountGold)
                : definition.BuildCost;
        }

        public bool RepairBase()
        {
            if (!CanRepair)
            {
                return false;
            }

            BaseHp = Math.Min(BaseMaxHp, SaturatingAdd(BaseHp, Configuration.PlanningRepair));
            RepairUsed = true;
            return true;
        }

        public bool UpgradeBase()
        {
            if (!CanUpgradeBase)
            {
                return false;
            }

            var upgrade = NextBaseUpgrade.Value;
            Gold -= upgrade.GoldCost;
            BaseLevel++;
            BaseMaxHp += upgrade.DurabilityIncrease;
            BaseHp += upgrade.DurabilityIncrease;
            return true;
        }

        public bool UpgradeTower(int instanceId)
        {
            var tower = towers.Find(value => value.Id == instanceId);
            if (!CanBuild() || tower == null || tower.Level >= tower.Definition.MaxLevel)
            {
                return false;
            }

            int cost = tower.Definition.UpgradeCost * tower.Level;
            if (Gold < cost)
            {
                return false;
            }

            Gold -= cost;
            tower.Level++;
            RefreshSnapshots();
            return true;
        }

        internal (int Damage, int Range) PreviewTowerUpgrade(TowerSnapshot tower)
        {
            var definition = towerDefinitions[tower.DefinitionId];
            int nextLevel = Math.Min(tower.Level + 1, definition.MaxLevel);
            return (Damage(definition, nextLevel), Range(definition, nextLevel));
        }

        public bool SellTower(int instanceId)
        {
            int index = towers.FindIndex(tower => tower.Id == instanceId);
            if (!CanBuild() || index < 0)
            {
                return false;
            }

            var tower = towers[index];
            if (aCrowd != null && !ChangeATowerTerrain(tower.Slot.Position, building: false, tower))
            {
                return false;
            }

            Gold = SaturatingAdd(Gold, SellValue(tower));
            towers.RemoveAt(index);
            RefreshSnapshots();
            return true;
        }

        private bool CanBuild()
        {
            return !IsPaused && (Phase == BattlePhase.Planning || Phase == BattlePhase.Fighting);
        }

        public bool StartNextWave()
        {
            if (Phase != BattlePhase.Planning || IsPaused || WaveIndex >= Configuration.Waves.Length)
            {
                return false;
            }

            BeginBatch();
            foreach (var tower in towers)
            {
                tower.Cooldown = 0;
            }

            Phase = BattlePhase.Fighting;
            RefreshSnapshots();
            return true;
        }

        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        /// <summary>Advance exact simulation ticks; callers accumulate real elapsed time outside this class.</summary>
        public void Step(int ticks = 1)
        {
            if (ticks < 0 || ticks > 100000)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }

            for (int i = 0; i < ticks; i++)
            {
                if (Phase != BattlePhase.Fighting || IsPaused)
                {
                    break;
                }

                StepOne();
            }

            RefreshSnapshots();
        }

        private void StepOne()
        {
            Tick++;
            SpawnScheduledEnemies();

            foreach (var tower in towers)
            {
                if (tower.Cooldown > 0)
                {
                    tower.Cooldown--;
                }

                if (tower.Cooldown != 0)
                {
                    continue;
                }

                var target = Target(tower);
                if (target == null)
                {
                    continue;
                }

                tower.Cooldown = AttackInterval(tower);
                tower.LastTargetId = target.Id;
                tower.ShotSequence++;
                target.Health -= Damage(tower.Definition, tower.Level);
                if (target.Health <= 0)
                {
                    Gold = SaturatingAdd(Gold, target.Definition.GoldReward);
                    Kills++;
                    aCrowd?.Remove(target.Id);
                    enemies.Remove(target);
                    RewardResolvedEnemy(target);
                }
            }

            if (aCrowd != null)
            {
                AdvanceANavigation();
            }
            else if (useRoadTraffic)
            {
                AdvanceTraffic();
            }

            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var enemy = enemies[i];
                if (!useRoadTraffic)
                {
                    enemy.Distance = Math.Min(enemy.Distance + enemy.Definition.SpeedPerTick, enemy.Route.Length);
                    enemy.Position = enemy.Route.Position(enemy.Distance);
                }

                if (RelayHeld && enemy.Route.RelayDistance > 0 && enemy.Distance >= enemy.Route.RelayDistance)
                {
                    RelayHeld = false;
                }

                if (!ReachedObjective(enemy))
                {
                    continue;
                }

                if (ResolveArrival(enemy, i))
                {
                    Phase = BattlePhase.Defeat;
                    enemies.Clear();
                    return;
                }
            }

            if (assault != null)
            {
                if (AllEnemiesSpawned && enemies.Count == 0)
                {
                    Phase = BattlePhase.Victory;
                }
                else
                {
                    TryOpenAssaultDraft();
                }
            }
            else if (batchSpawned && enemies.Count == 0)
            {
                var wave = Configuration.Waves[WaveIndex - 1];
                Gold = SaturatingAdd(Gold, wave.RewardGold);
                if (WaveIndex == Configuration.Waves.Length)
                {
                    Phase = BattlePhase.Victory;
                }
                else if (wave.GiveCard)
                {
                    DrawCards();
                    Phase = BattlePhase.ChoosingCard;
                }
                else
                {
                    Phase = BattlePhase.Planning;
                }
            }
        }

        private void BeginBatch()
        {
            var groups = Configuration.Waves[WaveIndex++].Groups;
            emitted = new int[groups.Length];
            waveTick = assault == null ? 0 : groups.Min(group => group.StartTick);
            batchSpawned = false;
        }

        private void SpawnScheduledEnemies()
        {
            if (batchSpawned)
            {
                if (assault == null || AllEnemiesSpawned || Tick < nextBatchAt)
                {
                    return;
                }

                BeginBatch();
            }

            int batchIndex = WaveIndex - 1;
            var groups = Configuration.Waves[batchIndex].Groups;
            batchSpawned = true;
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                int age = waveTick - group.StartTick;
                int due = age < 0 ? 0 : Math.Min(group.Count, 1 + age / group.IntervalTicks);
                if (emitted[i] < due && TrySpawnEnemy(group, batchIndex))
                {
                    emitted[i]++;
                }

                batchSpawned &= emitted[i] == group.Count;
            }

            waveTick++;
            if (batchSpawned && assault != null)
            {
                nextBatchAt = Tick + assault.ReinforcementGapTicks;
            }
        }

        private bool TrySpawnEnemy(SpawnGroup group, int batchIndex)
        {
            var definition = enemyDefinitions[group.EnemyId];
            var route = routes[group.RouteId];
            int lane = 0;
            var position = route.Definition.Points[0];
            NavigationAgent agent = null;
            if (aCrowd != null)
            {
                agent = TrySpawnAEnemy(definition);
                if (agent == null)
                {
                    return false;
                }

                position = agent.Position;
            }
            else if (useAuthoredNavigation)
            {
                if (!CanSpawnOnTerrain(position, definition.BodyRadius))
                {
                    return false;
                }
            }
            else if (useRoadTraffic)
            {
                if (definition.BodyRadius <= RoadLaneOffsetMillimetres)
                {
                    lane = (nextEnemyId & 1) == 0 ? -1 : 1;
                    position = route.Position(0, lane);
                }

                if (!HasRoadSpace(position, definition.BodyRadius))
                {
                    if (lane == 0)
                    {
                        return false;
                    }

                    lane = -lane;
                    position = route.Position(0, lane);
                    if (!HasRoadSpace(position, definition.BodyRadius))
                    {
                        return false;
                    }
                }
            }

            enemies.Add(new Enemy
            {
                Id = nextEnemyId++,
                Definition = definition,
                Route = route,
                Position = position,
                NavigationAgent = agent,
                Lane = lane,
                BatchIndex = batchIndex,
                Health = definition.Health
            });
            return true;
        }

        private void AdvanceTraffic()
        {
            FillBaseAttackGaps();
            var baseAttackLayout = Configuration.Objective.BaseAttack;
            movementOrder.AddRange(enemies);
            movementOrder.Sort(CompareRoadPriority);
            if (baseAttackLayout?.LocalArea != null)
            {
                ScheduleAssaultPositions(baseAttackLayout);
            }

            ScheduleRoadConflicts();
            foreach (var enemy in movementOrder)
            {
                AdvanceTrafficEnemy(enemy, baseAttackLayout);
            }

            movementOrder.Clear();
        }

        private void AdvanceTrafficEnemy(Enemy enemy, BaseAttackLayout baseAttackLayout)
        {
            if (enemy.AssaultOrder is AssaultMotion motion)
            {
                AdvanceAssaultMotion(enemy, motion, baseAttackLayout);
                return;
            }

            var attack = enemy.BaseAttack;
            if (attack != null)
            {
                attack.Distance = AdvancePath(enemy, attack.Path, attack.Distance, attack.Stop, 0);
                return;
            }

            if (useAuthoredNavigation)
            {
                AdvanceCorridorRoad(enemy);
            }
            else
            {
                enemy.Distance = AdvancePath(enemy, enemy.Route, enemy.Distance, enemy.Route.Length, enemy.Lane);
            }

            if (enemy.Distance == enemy.Route.Length && baseAttackLayout?.LocalArea == null)
            {
                AssignBaseAttack(enemy);
            }
        }

        private int AdvancePath(Enemy enemy, Route route, int progress, int stop, int lane)
        {
            int target = Math.Min(progress + enemy.Definition.SpeedPerTick, stop);
            while (progress < target)
            {
                int distance = Math.Min(target, progress + EnemyMoveStepMillimetres);
                var position = route.Position(distance, lane);
                if (!CanAdvancePathTo(enemy, position))
                {
                    // Close up to the available gap instead of dropping the whole movement step.
                    int blocked = distance;
                    while (blocked - progress > 1)
                    {
                        int middle = progress + (blocked - progress) / 2;
                        var probe = route.Position(middle, lane);
                        if (CanAdvancePathTo(enemy, probe))
                        {
                            progress = middle;
                            enemy.Position = probe;
                        }
                        else
                        {
                            blocked = middle;
                        }
                    }

                    break;
                }

                progress = distance;
                enemy.Position = position;
            }

            return progress;
        }

        private bool CanAdvancePathTo(Enemy enemy, Position2 position)
        {
            int radius = enemy.Definition.BodyRadius;
            return useAuthoredNavigation
                ? IsClearBattleGround(enemy.Position, position, radius) && IsClearOfEnemyBodies(enemy.Position, position, enemy, radius)
                : HasRoadSpace(position, radius, enemy);
        }

        private static int CompareRoadPriority(Enemy first, Enemy second)
        {
            int order = (first.Route.Length - first.Distance).CompareTo(second.Route.Length - second.Distance);
            return order != 0 ? order : first.Id.CompareTo(second.Id);
        }

        private bool HasRoadSpace(Position2 position, int radius, Enemy mover = null)
        {
            if (mover?.BaseAttack is { Layout: null } && !IsClearOfTowers(mover.Position, position, radius))
            {
                return false;
            }

            var layout = Configuration.Objective.BaseAttack;
            bool enteringBaseApproach = mover != null && mover.BaseAttack == null && layout?.Engagement != null
                && (layout.LocalArea == null || mover.Route.Length - mover.Distance <= 2500);
            foreach (var other in enemies)
            {
                if (other == mover)
                {
                    continue;
                }

                int clearance = radius + other.Definition.BodyRadius;
                long required = (long)clearance * clearance;
                long gap = DistanceSquared(position, other.Position);
                if (gap < required)
                {
                    return false;
                }

                // Let an enemy already committed to crossing the road clear its approach.
                if (enteringBaseApproach && other.BaseAttack != null && BlocksReservedAttackPath(position, other, required))
                {
                    return false;
                }

                if (mover == null || mover.BaseAttack != null || mover.Route == other.Route || other.Distance == other.Route.Length
                    || gap >= 9 * required || CompareRoadPriority(other, mover) >= 0)
                {
                    continue;
                }

                // Yield before entering the lead enemy's path at a junction.
                int lookAhead = Math.Min(other.Route.Length, other.Distance + 2 * clearance);
                var ahead = other.Route.Position(lookAhead, other.Lane);
                double reservedGap = SegmentDistanceSquared(position, other.Position, ahead);
                if (reservedGap < required && reservedGap <= SegmentDistanceSquared(mover.Position, other.Position, ahead))
                {
                    return false;
                }
            }

            return true;
        }

        private static double SegmentDistanceSquared(Position2 point, Position2 start, Position2 end)
        {
            long x = (long)end.X - start.X;
            long y = (long)end.Y - start.Y;
            long dx = (long)point.X - start.X;
            long dy = (long)point.Y - start.Y;
            long projection = dx * x + dy * y;
            if (projection <= 0)
            {
                return DistanceSquared(point, start);
            }

            long lengthSquared = x * x + y * y;
            if (projection >= lengthSquared)
            {
                return DistanceSquared(point, end);
            }

            long cross = dx * y - dy * x;
            return (double)cross * cross / lengthSquared;
        }

        private static long DistanceSquared(Position2 first, Position2 second)
        {
            long x = (long)first.X - second.X;
            long y = (long)first.Y - second.Y;
            return x * x + y * y;
        }

        private void RewardResolvedEnemy(Enemy enemy)
        {
            if (remainingByBatch != null && --remainingByBatch[enemy.BatchIndex] == 0)
            {
                Gold = SaturatingAdd(Gold, Configuration.Waves[enemy.BatchIndex].RewardGold);
            }
        }

        private void RecordLeak(Enemy enemy, int index)
        {
            enemies.RemoveAt(index);
            Leaks++;
            RewardResolvedEnemy(enemy);
        }

        private bool ResolveArrival(Enemy enemy, int index)
        {
            var objective = Configuration.Objective;
            switch (objective.Type)
            {
                case BattleObjectiveType.InterceptEscapes:
                    RecordLeak(enemy, index);
                    return Leaks >= objective.EscapesToDefeat;
                case BattleObjectiveType.DefendBase:
                    if (enemy.NextBaseAttackTick == 0 && objective.BaseAttack?.Engagement is BaseAttackEngagement engagement)
                    {
                        enemy.NextBaseAttackTick = Tick + engagement.WindupTicks;
                        return false;
                    }

                    if (Tick < enemy.NextBaseAttackTick)
                    {
                        return false;
                    }

                    enemy.NextBaseAttackTick = Tick + objective.BaseAttackIntervalTicks;
                    break;
                case BattleObjectiveType.LegacyBaseLeak:
                    RecordLeak(enemy, index);
                    break;
            }

            BaseHp = Math.Max(0, BaseHp - enemy.Definition.BaseDamage);
            return BaseHp == 0;
        }

        private Enemy Target(Tower tower)
        {
            Enemy best = null;
            int remaining = int.MaxValue;
            long range = Range(tower.Definition, tower.Level);
            foreach (var enemy in enemies)
            {
                int distanceToGoal = enemy.Route.Length - enemy.Distance;
                if (DistanceSquared(enemy.Position, tower.Slot.Position) <= range * range && distanceToGoal < remaining)
                {
                    best = enemy;
                    remaining = distanceToGoal;
                }
            }

            return best;
        }

        private int Damage(TowerDefinition definition, int level)
        {
            long value = definition.Damage + (level - 1) * definition.UpgradeDamage;
            return (int)Math.Min(int.MaxValue, value * (100 + damageBonus) / 100);
        }

        private int AttackInterval(Tower tower)
        {
            return Math.Max(1, (tower.Definition.AttackIntervalTicks * 100 + 99 + speedBonus) / (100 + speedBonus));
        }

        private static int SellValue(Tower tower)
        {
            long paid = tower.PaidBuildCost + (long)tower.Definition.UpgradeCost * tower.Level * (tower.Level - 1) / 2;
            return (int)Math.Min(int.MaxValue, paid * 70 / 100);
        }

        private int Range(TowerDefinition definition, int level)
        {
            long value = definition.Range + (level - 1) * definition.UpgradeRange;
            return (int)Math.Min(int.MaxValue, value * (100 + rangeBonus) / 100);
        }

        private void DrawCards()
        {
            offers.Clear();
            var pool = Configuration.Cards.Where(card => IsApplicable(Configuration.Objective, card) && (card.MaxStacks == 0
                || !cardStacks.TryGetValue(card.Id, out int stacks) || stacks < card.MaxStacks)).ToList();
            for (int i = 0; i < 3; i++)
            {
                uint total = (uint)pool.Sum(card => card.Weight);
                uint chosen = NextRandom() % total;
                int index = 0;
                while (chosen >= pool[index].Weight)
                {
                    chosen -= (uint)pool[index++].Weight;
                }

                offers.Add(pool[index]);
                pool.RemoveAt(index);
            }
        }

        private void TryOpenAssaultDraft()
        {
            if (DraftPoints < DraftPointsRequired)
            {
                return;
            }

            spentDraftPoints += DraftPointsRequired;
            DraftPointsRequired = assault.RepeatDraftPoints;
            DrawCards();
            Phase = BattlePhase.ChoosingCard;
        }

        public bool ChooseCard(int offerIndex)
        {
            if (Phase != BattlePhase.ChoosingCard || IsPaused || offerIndex < 0 || offerIndex >= offers.Count)
            {
                return false;
            }

            var card = offers[offerIndex];
            cardStacks.TryGetValue(card.Id, out int count);
            cardStacks[card.Id] = count + 1;
            switch (card.Effect)
            {
                case CardEffect.DamagePercent:
                    damageBonus = Math.Min(10000, damageBonus + card.Value);
                    break;
                case CardEffect.AttackSpeedPercent:
                    speedBonus = Math.Min(10000, speedBonus + card.Value);
                    break;
                case CardEffect.RangePercent:
                    rangeBonus = Math.Min(10000, rangeBonus + card.Value);
                    break;
                case CardEffect.Gold:
                    Gold = SaturatingAdd(Gold, card.Value);
                    break;
                case CardEffect.HealBase:
                    BaseHp = Math.Min(BaseMaxHp, SaturatingAdd(BaseHp, card.Value));
                    break;
            }

            offers.Clear();
            Phase = assault == null ? BattlePhase.Planning : BattlePhase.Fighting;
            if (assault != null)
            {
                TryOpenAssaultDraft();
            }

            RefreshSnapshots();
            return true;
        }

        private uint NextRandom()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }

        private static int SaturatingAdd(int first, int second)
        {
            return (int)Math.Min(int.MaxValue, (long)first + second);
        }

        private void RefreshSnapshots()
        {
            towerSnapshots.Clear();
            foreach (var tower in towers)
            {
                towerSnapshots.Add(new TowerSnapshot(
                    tower.Id,
                    tower.Definition.Id,
                    tower.Slot.Id,
                    tower.Slot.Position,
                    tower.Level,
                    Range(tower.Definition, tower.Level),
                    Damage(tower.Definition, tower.Level),
                    AttackInterval(tower),
                    SellValue(tower),
                    tower.Cooldown,
                    tower.LastTargetId,
                    tower.ShotSequence));
            }

            enemySnapshots.Clear();
            foreach (var enemy in enemies)
            {
                enemySnapshots.Add(new EnemySnapshot(
                    enemy.Id,
                    enemy.Definition.Id,
                    enemy.Route.Definition.Id,
                    enemy.Position,
                    enemy.Health,
                    enemy.Definition.Health,
                    enemy.Distance,
                    useRoadTraffic && ReachedObjective(enemy),
                    enemy.NextBaseAttackTick));
            }
        }

        private static Route CreateRoute(RouteDefinition definition, bool hasBase, int attackInset = 0)
        {
            var route = new Route { Definition = definition, SegmentLengths = new int[definition.Points.Length - 1] };
            for (int i = 0; i < route.SegmentLengths.Length; i++)
            {
                long dx = definition.Points[i + 1].X - definition.Points[i].X;
                long dy = definition.Points[i + 1].Y - definition.Points[i].Y;
                long square = dx * dx + dy * dy;
                int length = (int)Math.Sqrt(square);
                while ((long)(length + 1) * (length + 1) <= square)
                {
                    length++;
                }

                while ((long)length * length > square)
                {
                    length--;
                }

                Require(length > 0, "Route segments must have positive length.");
                route.SegmentLengths[i] = length;
                route.Length = checked(route.Length + length);
                Require(route.Length <= 10000000, "Route length exceeds supported range.");
            }

            if (hasBase)
            {
                int finalSegmentLength = route.SegmentLengths[route.SegmentLengths.Length - 1];
                Require(definition.BaseEntranceInset >= 0 && definition.BaseEntranceInset < finalSegmentLength, "Invalid base entrance inset.");
                route.EndInset = attackInset > 0 ? attackInset : definition.BaseEntranceInset;
                Require(route.EndInset < route.Length, "The base attack approach must start after the route entrance.");
                route.Length -= route.EndInset;
            }

            return route;
        }

        private static Position2[] CreateLaneOffsets(Position2[] points)
        {
            var offsets = new Position2[points.Length];
            var incoming = LaneNormal(points[0], points[1]);
            for (int i = 0; i < points.Length; i++)
            {
                var outgoing = i > 0 && i + 1 < points.Length ? LaneNormal(points[i], points[i + 1]) : incoming;
                // Use the corner shared by both offset segments so the lanes stay separated through a bend.
                double alignment = 1 + incoming.X * outgoing.X + incoming.Y * outgoing.Y;
                if (alignment < .5)
                {
                    incoming = outgoing;
                    alignment = 2;
                }

                double scale = RoadLaneOffsetMillimetres / alignment;
                offsets[i] = new Position2(
                    (int)Math.Round((incoming.X + outgoing.X) * scale),
                    (int)Math.Round((incoming.Y + outgoing.Y) * scale));
                incoming = outgoing;
            }

            return offsets;
        }

        private static (double X, double Y) LaneNormal(Position2 start, Position2 end)
        {
            double length = Math.Sqrt(DistanceSquared(start, end));
            return ((start.Y - end.Y) / length, (end.X - start.X) / length);
        }

        private static void Validate(FightConfiguration value)
        {
            Require(value.Objective != null, "A battle objective is required.");
            Require(value.InitialGold >= 0 && value.BaseHealth >= (value.Objective.HasBase ? 1 : 0), "Invalid initial currency/base health.");
            long maxBaseHp = value.BaseHealth + value.Objective.BaseUpgrades.Sum(upgrade => (long)upgrade.DurabilityIncrease);
            Require(maxBaseHp <= int.MaxValue, "Base durability exceeds the supported range.");
            Require(value.BuildGridStep >= 0, "Build grid step cannot be negative.");
            Require(value.Routes != null && value.Routes.Length > 0, "At least one route is required.");
            Require(value.BuildSlots != null && value.Towers != null && value.Enemies != null && value.Cards != null, "Configuration arrays cannot be null.");
            Require(
                value.Routes.Length <= 64 && value.BuildSlots.Length <= 512 && value.Towers.Length <= 256
                    && value.Enemies.Length <= 256 && value.Cards.Length <= 1000,
                "Definition count exceeds supported limits.");
            Require(value.Waves != null && value.Waves.Length > 0 && value.Waves.Length <= 1000, "Expected 1–1000 waves.");
            Require(
                value.Routes.All(item => item != null) && value.BuildSlots.All(item => item != null)
                    && value.Towers.All(item => item != null) && value.Enemies.All(item => item != null)
                    && value.Waves.All(item => item != null) && value.Cards.All(item => item != null),
                "Definition rows cannot be null.");
            Unique(value.Routes.Select(item => item.Id));
            Unique(value.BuildSlots.Select(item => item.Id));
            Unique(value.Towers.Select(item => item.Id));
            Unique(value.Enemies.Select(item => item.Id));
            Unique(value.Waves.Select(item => item.Id));
            Unique(value.Cards.Select(item => item.Id));
            Require(value.OpeningDiscountGold >= 0 && value.PlanningRepair >= 0, "Invalid expedition supplies.");
            Require(value.OpeningDiscountGold == 0 || value.Towers.Any(tower => tower.Id == value.OpeningDiscountTowerId), "Discount tower is missing.");
            if (value.RelayRouteId != 0)
            {
                var relay = value.Routes.FirstOrDefault(route => route.Id == value.RelayRouteId);
                Require(relay?.Points != null && value.RelayPointIndex > 0 && value.RelayPointIndex < relay.Points.Length - 1, "Invalid relay checkpoint.");
            }

            var baseAttack = value.Objective.BaseAttack;
            if (baseAttack != null)
            {
                if (baseAttack.Engagement != null)
                {
                    ValidatePoint(baseAttack.Engagement.Min);
                    ValidatePoint(baseAttack.Engagement.Max);
                }

                if (baseAttack.LocalArea is BaseApproachArea area)
                {
                    ValidatePoint(area.Min);
                    ValidatePoint(area.Max);
                    foreach (var obstacle in area.Obstacles)
                    {
                        ValidatePoint(obstacle.Center);
                    }
                }

                foreach (var path in baseAttack.Paths)
                {
                    Require(baseAttack.Engagement == null || path.FirstAttackPoint == path.Points.Count - 1,
                        "A local attack path must end at its only attack position.");
                    foreach (var point in path.Points)
                    {
                        ValidatePoint(point);
                    }
                }
            }

            foreach (var route in value.Routes)
            {
                Require(route.Points != null && route.Points.Length >= 2 && route.Points.Length <= 1000, "Invalid route point count.");
                foreach (var point in route.Points)
                {
                    ValidatePoint(point);
                }

                CreateRoute(route, value.Objective.HasBase, baseAttack?.ApproachInset ?? 0);
            }

            if (value.Objective.Type == BattleObjectiveType.InterceptEscapes)
            {
                var exit = value.Routes[0].Points[^1];
                Require(value.Routes.All(route => route.Points[^1].Equals(exit)), "Escape routes must end at the same portal.");
            }

            var gridPositions = value.BuildGridStep > 0 ? new HashSet<Position2>() : null;
            foreach (var slot in value.BuildSlots)
            {
                ValidatePoint(slot.Position);
                if (gridPositions != null)
                {
                    Require(slot.Position.X % value.BuildGridStep == 0 && slot.Position.Y % value.BuildGridStep == 0
                        && gridPositions.Add(slot.Position), "Build grid cells must align to the grid and have unique positions.");
                }
            }

            foreach (var tower in value.Towers)
            {
                Require(tower.BuildCost > 0 && tower.UpgradeCost > 0 && tower.UpgradeCost <= 1000000, "Tower costs must be positive and bounded.");
                Require(tower.Damage > 0 && tower.Damage <= 1000000 && tower.Range > 0 && tower.Range <= 1000000, "Invalid tower damage/range.");
                Require(
                    tower.MaxLevel >= 1 && tower.MaxLevel <= 20 && tower.AttackIntervalTicks > 0 && tower.AttackIntervalTicks <= 1000000,
                    "Invalid tower level/cooldown.");
                Require(
                    tower.UpgradeDamage >= 0 && tower.UpgradeDamage <= 1000000 && tower.UpgradeRange >= 0 && tower.UpgradeRange <= 1000000,
                    "Invalid tower upgrade modifiers.");
            }

            foreach (var enemy in value.Enemies)
            {
                Require(enemy.Health > 0 && enemy.SpeedPerTick > 0 && enemy.SpeedPerTick <= 1000000, "Invalid enemy health/speed.");
                Require(enemy.BodyRadius > 0 && enemy.BodyRadius <= 2000, "Invalid enemy body radius.");
                Require(enemy.GoldReward >= 0 && enemy.BaseDamage > 0, "Invalid enemy rewards/base damage.");
            }

            foreach (var wave in value.Waves)
            {
                Require(wave.RewardGold >= 0 && wave.Groups != null && wave.Groups.Length > 0 && wave.Groups.Length <= 1000, "Invalid wave.");
                Require(
                    wave.Groups.All(item => item != null) && wave.Groups.Sum(item => (long)item.Count) <= 10000,
                    "Wave spawn rows/count exceed supported limits.");
                foreach (var group in wave.Groups)
                {
                    var enemy = value.Enemies.FirstOrDefault(definition => definition.Id == group.EnemyId);
                    Require(enemy != null && value.Routes.Any(route => route.Id == group.RouteId), "Spawn references missing enemy/route.");
                    Require(baseAttack == null || baseAttack.LocalArea != null || baseAttack.Paths.Any(path => enemy.BodyRadius <= path.MaxBodyRadius
                        && (baseAttack.Engagement == null || baseAttack.Engagement.CanAttackFrom(path.Points[path.FirstAttackPoint], enemy.BodyRadius))),
                        "No base attack path fits the enemy.");
                    Require(
                        group.Count > 0 && group.Count <= 10000 && group.StartTick >= 0 && group.IntervalTicks > 0
                            && (long)group.StartTick + (long)group.Count * group.IntervalTicks <= 10000000,
                        "Invalid spawn timing/count.");
                }
            }

            Require(value.Cards.Count(card => card.MaxStacks == 0 && IsApplicable(value.Objective, card)) >= 3,
                "Three applicable repeatable cards are required for a full draft.");
            foreach (var card in value.Cards)
            {
                Require(Enum.IsDefined(typeof(CardEffect), card.Effect) && card.Value > 0 && card.Value <= 1000000, "Invalid card effect/value.");
                Require(card.Weight > 0 && card.Weight <= 1000000 && card.MaxStacks >= 0, "Invalid card weight/stack limit.");
            }

            Require(value.Cards.Sum(card => (long)card.Weight) <= int.MaxValue, "Card weight total exceeds supported range.");
        }

        private static bool IsApplicable(BattleObjective objective, CardDefinition card) => objective.HasBase || card.Effect != CardEffect.HealBase;

        private static void ValidatePoint(Position2 point)
        {
            Require(Math.Abs((long)point.X) <= 1000000 && Math.Abs((long)point.Y) <= 1000000, "Map coordinates exceed supported range.");
        }

        private static void Unique(IEnumerable<int> values)
        {
            var seen = new HashSet<int>();
            foreach (int value in values)
            {
                Require(value > 0 && seen.Add(value), "Definition IDs must be positive and unique within their table.");
            }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, "configuration");
            }
        }
    }
}
