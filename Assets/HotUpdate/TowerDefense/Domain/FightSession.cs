using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Single-player fixed-tick rules. No Unity objects, services, wall clock or global random.</summary>
    public sealed class FightSession
    {
        public const int TickMilliseconds = 50;
        private sealed class Tower
        {
            internal int Id;
            internal TowerDefinition Definition;
            internal BuildSlotDefinition Slot;
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
            internal int Health;
            internal int Distance;
        }

        private sealed class Route
        {
            internal RouteDefinition Definition;
            internal int[] SegmentLengths;
            internal int Length;

            internal Position2 Position(int distance)
            {
                for (int i = 0; i < SegmentLengths.Length; i++)
                {
                    int length = SegmentLengths[i];
                    if (distance <= length)
                    {
                        var first = Definition.Points[i];
                        var second = Definition.Points[i + 1];
                        return new Position2(
                            first.X + (int)((long)(second.X - first.X) * distance / length),
                            first.Y + (int)((long)(second.Y - first.Y) * distance / length));
                    }

                    distance -= length;
                }

                return Definition.Points[Definition.Points.Length - 1];
            }
        }

        private readonly Dictionary<int, TowerDefinition> towerDefinitions;
        private readonly Dictionary<int, EnemyDefinition> enemyDefinitions;
        private readonly Dictionary<int, BuildSlotDefinition> slots;
        private readonly Dictionary<int, Route> routes;
        private readonly List<Tower> towers = new List<Tower>();
        private readonly List<Enemy> enemies = new List<Enemy>();
        private readonly List<TowerSnapshot> towerSnapshots = new List<TowerSnapshot>();
        private readonly List<EnemySnapshot> enemySnapshots = new List<EnemySnapshot>();
        private readonly List<CardDefinition> offers = new List<CardDefinition>();
        private readonly Dictionary<int, int> cardStacks = new Dictionary<int, int>();
        private int[] emitted = Array.Empty<int>();
        private int waveTick;
        private int nextTowerId = 1;
        private int nextEnemyId = 1;
        private int damageBonus;
        private int speedBonus;
        private int rangeBonus;
        private uint random;

        /// <summary>Configuration is read-only for the duration of a run; feed a fresh DTO from table adapters.</summary>
        public FightConfiguration Configuration { get; }
        public uint Seed { get; }
        public BattlePhase Phase { get; private set; } = BattlePhase.Planning;
        public int WaveIndex { get; private set; }
        public int Gold { get; private set; }
        public int BaseHp { get; private set; }
        public long Tick { get; private set; }
        public bool IsPaused { get; private set; }
        public int Kills { get; private set; }
        public int Leaks { get; private set; }
        public IReadOnlyList<TowerSnapshot> Towers { get; }
        public IReadOnlyList<EnemySnapshot> Enemies { get; }
        public IReadOnlyList<CardDefinition> CurrentCardOffers { get; }
        public IReadOnlyDictionary<int, int> ChosenCardStacks { get; }

        public FightSession(FightConfiguration configuration, uint seed = 1)
        {
            Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Validate(configuration);
            Seed = seed;
            random = seed == 0 ? 0x9e3779b9u : seed;
            towerDefinitions = configuration.Towers.ToDictionary(value => value.Id);
            enemyDefinitions = configuration.Enemies.ToDictionary(value => value.Id);
            slots = configuration.BuildSlots.ToDictionary(value => value.Id);
            routes = configuration.Routes.ToDictionary(value => value.Id, CreateRoute);
            Gold = configuration.InitialGold;
            BaseHp = configuration.BaseHealth;
            Towers = towerSnapshots.AsReadOnly();
            Enemies = enemySnapshots.AsReadOnly();
            CurrentCardOffers = offers.AsReadOnly();
            ChosenCardStacks = new ReadOnlyDictionary<int, int>(cardStacks);
        }

        public bool BuildTower(int slotId, int towerTypeId)
        {
            if (!CanBuild() || !slots.TryGetValue(slotId, out var slot)
                || !towerDefinitions.TryGetValue(towerTypeId, out var definition)
                || Gold < definition.BuildCost || towers.Exists(value => value.Slot.Id == slotId))
            {
                return false;
            }

            Gold -= definition.BuildCost;
            towers.Add(new Tower { Id = nextTowerId++, Slot = slot, Definition = definition });
            RefreshSnapshots();
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

            var wave = Configuration.Waves[WaveIndex];
            WaveIndex++;
            emitted = new int[wave.Groups.Length];
            waveTick = 0;
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
            var wave = Configuration.Waves[WaveIndex - 1];
            bool allSpawned = true;
            for (int i = 0; i < wave.Groups.Length; i++)
            {
                var group = wave.Groups[i];
                if (emitted[i] < group.Count && waveTick >= group.StartTick
                    && (waveTick - group.StartTick) % group.IntervalTicks == 0)
                {
                    var definition = enemyDefinitions[group.EnemyId];
                    enemies.Add(new Enemy
                    {
                        Id = nextEnemyId++,
                        Definition = definition,
                        Route = routes[group.RouteId],
                        Health = definition.Health
                    });
                    emitted[i]++;
                }

                allSpawned &= emitted[i] == group.Count;
            }

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

                tower.Cooldown = Math.Max(1, (tower.Definition.AttackIntervalTicks * 100 + 99 + speedBonus) / (100 + speedBonus));
                tower.LastTargetId = target.Id;
                tower.ShotSequence++;
                target.Health -= Damage(tower);
                if (target.Health <= 0)
                {
                    Gold = SaturatingAdd(Gold, target.Definition.GoldReward);
                    Kills++;
                    enemies.Remove(target);
                }
            }

            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                var enemy = enemies[i];
                enemy.Distance += enemy.Definition.SpeedPerTick;
                if (enemy.Distance < enemy.Route.Length)
                {
                    continue;
                }

                BaseHp = Math.Max(0, BaseHp - enemy.Definition.BaseDamage);
                Leaks++;
                enemies.RemoveAt(i);
                if (BaseHp == 0)
                {
                    Phase = BattlePhase.Defeat;
                    enemies.Clear();
                    return;
                }
            }

            waveTick++;
            if (allSpawned && enemies.Count == 0)
            {
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

        private Enemy Target(Tower tower)
        {
            Enemy best = null;
            int remaining = int.MaxValue;
            long range = Range(tower);
            foreach (var enemy in enemies)
            {
                var point = enemy.Route.Position(enemy.Distance);
                long dx = point.X - tower.Slot.Position.X;
                long dy = point.Y - tower.Slot.Position.Y;
                int distanceToBase = enemy.Route.Length - enemy.Distance;
                if (dx * dx + dy * dy <= range * range && distanceToBase < remaining)
                {
                    best = enemy;
                    remaining = distanceToBase;
                }
            }

            return best;
        }

        private int Damage(Tower tower)
        {
            long value = tower.Definition.Damage + (tower.Level - 1) * tower.Definition.UpgradeDamage;
            return (int)Math.Min(int.MaxValue, value * (100 + damageBonus) / 100);
        }

        private int Range(Tower tower)
        {
            long value = tower.Definition.Range + (tower.Level - 1) * tower.Definition.UpgradeRange;
            return (int)Math.Min(int.MaxValue, value * (100 + rangeBonus) / 100);
        }

        private void DrawCards()
        {
            offers.Clear();
            var pool = Configuration.Cards.Where(card => card.MaxStacks == 0
                || !cardStacks.TryGetValue(card.Id, out int stacks) || stacks < card.MaxStacks).ToList();
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
                    BaseHp = Math.Min(Configuration.BaseHealth, SaturatingAdd(BaseHp, card.Value));
                    break;
            }

            offers.Clear();
            Phase = BattlePhase.Planning;
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
                    Range(tower),
                    Damage(tower),
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
                    enemy.Route.Position(enemy.Distance),
                    enemy.Health,
                    enemy.Definition.Health,
                    enemy.Distance));
            }
        }

        private static Route CreateRoute(RouteDefinition definition)
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

            return route;
        }

        private static void Validate(FightConfiguration value)
        {
            Require(value.InitialGold >= 0 && value.BaseHealth > 0, "Invalid initial currency/base health.");
            Require(value.Routes != null && value.Routes.Length > 0, "At least one route is required.");
            Require(value.BuildSlots != null && value.Towers != null && value.Enemies != null && value.Cards != null,
                "Configuration arrays cannot be null.");
            Require(value.Routes.Length <= 64 && value.BuildSlots.Length <= 512 && value.Towers.Length <= 256
                && value.Enemies.Length <= 256 && value.Cards.Length <= 1000, "Definition count exceeds supported limits.");
            Require(value.Waves != null && value.Waves.Length > 0 && value.Waves.Length <= 1000, "Expected 1–1000 waves.");
            Require(value.Routes.All(item => item != null) && value.BuildSlots.All(item => item != null)
                && value.Towers.All(item => item != null) && value.Enemies.All(item => item != null)
                && value.Waves.All(item => item != null) && value.Cards.All(item => item != null), "Definition rows cannot be null.");
            Unique(value.Routes.Select(item => item.Id));
            Unique(value.BuildSlots.Select(item => item.Id));
            Unique(value.Towers.Select(item => item.Id));
            Unique(value.Enemies.Select(item => item.Id));
            Unique(value.Waves.Select(item => item.Id));
            Unique(value.Cards.Select(item => item.Id));
            foreach (var route in value.Routes)
            {
                Require(route.Points != null && route.Points.Length >= 2 && route.Points.Length <= 1000, "Invalid route point count.");
                foreach (var point in route.Points)
                {
                    ValidatePoint(point);
                }

                CreateRoute(route);
            }

            foreach (var slot in value.BuildSlots)
            {
                ValidatePoint(slot.Position);
            }

            foreach (var tower in value.Towers)
            {
                Require(tower.BuildCost > 0 && tower.UpgradeCost > 0 && tower.UpgradeCost <= 1000000,
                    "Tower costs must be positive and bounded.");
                Require(tower.Damage > 0 && tower.Damage <= 1000000 && tower.Range > 0 && tower.Range <= 1000000,
                    "Invalid tower damage/range.");
                Require(tower.MaxLevel >= 1 && tower.MaxLevel <= 20 && tower.AttackIntervalTicks > 0 && tower.AttackIntervalTicks <= 1000000,
                    "Invalid tower level/cooldown.");
                Require(tower.UpgradeDamage >= 0 && tower.UpgradeDamage <= 1000000 && tower.UpgradeRange >= 0 && tower.UpgradeRange <= 1000000,
                    "Invalid tower upgrade modifiers.");
            }

            foreach (var enemy in value.Enemies)
            {
                Require(enemy.Health > 0 && enemy.SpeedPerTick > 0 && enemy.SpeedPerTick <= 1000000,
                    "Invalid enemy health/speed.");
                Require(enemy.GoldReward >= 0 && enemy.BaseDamage > 0, "Invalid enemy rewards/base damage.");
            }

            foreach (var wave in value.Waves)
            {
                Require(wave.RewardGold >= 0 && wave.Groups != null && wave.Groups.Length > 0 && wave.Groups.Length <= 1000,
                    "Invalid wave.");
                Require(wave.Groups.All(item => item != null) && wave.Groups.Sum(item => (long)item.Count) <= 10000,
                    "Wave spawn rows/count exceed supported limits.");
                foreach (var group in wave.Groups)
                {
                    Require(value.Enemies.Any(enemy => enemy.Id == group.EnemyId) && value.Routes.Any(route => route.Id == group.RouteId),
                        "Spawn references missing enemy/route.");
                    Require(group.Count > 0 && group.Count <= 10000 && group.StartTick >= 0 && group.IntervalTicks > 0
                        && (long)group.StartTick + (long)group.Count * group.IntervalTicks <= 10000000, "Invalid spawn timing/count.");
                }
            }

            Require(value.Cards.Count(card => card.MaxStacks == 0) >= 3, "Three repeatable cards are required for a full draft.");
            foreach (var card in value.Cards)
            {
                Require(Enum.IsDefined(typeof(CardEffect), card.Effect) && card.Value > 0 && card.Value <= 1000000,
                    "Invalid card effect/value.");
                Require(card.Weight > 0 && card.Weight <= 1000000 && card.MaxStacks >= 0, "Invalid card weight/stack limit.");
            }

            Require(value.Cards.Sum(card => (long)card.Weight) <= int.MaxValue, "Card weight total exceeds supported range.");
        }

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
