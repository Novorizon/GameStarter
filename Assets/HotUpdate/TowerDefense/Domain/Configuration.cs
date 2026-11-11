using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Map coordinates in millimetres. Presentation maps Y onto world Z.</summary>
    public readonly struct Position2
    {
        public int X { get; }
        public int Y { get; }

        public Position2(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    public enum BattleObjectiveType
    {
        LegacyBaseLeak = 0,
        DefendBase = 1,
        InterceptEscapes = 2
    }

    public readonly struct BaseUpgrade
    {
        public int GoldCost { get; }
        public int DurabilityIncrease { get; }

        public BaseUpgrade(int goldCost, int durabilityIncrease)
        {
            GoldCost = goldCost;
            DurabilityIncrease = durabilityIncrease;
        }
    }

    public sealed class AssaultRules
    {
        internal static readonly AssaultRules Default = new AssaultRules(100, 6, 12);

        public int ReinforcementGapTicks { get; }
        public int FirstDraftPoints { get; }
        public int RepeatDraftPoints { get; }

        public AssaultRules(int reinforcementGapTicks, int firstDraftPoints, int repeatDraftPoints)
        {
            if (reinforcementGapTicks <= 0 || firstDraftPoints <= 0 || repeatDraftPoints <= 0)
            {
                throw new ArgumentException("Assault gaps and draft thresholds must be positive.");
            }

            ReinforcementGapTicks = reinforcementGapTicks;
            FirstDraftPoints = firstDraftPoints;
            RepeatDraftPoints = repeatDraftPoints;
        }
    }

    public sealed class BattleObjective
    {
        internal static readonly BattleObjective Legacy = new BattleObjective(BattleObjectiveType.LegacyBaseLeak, "防守终点");

        public BattleObjectiveType Type { get; }
        public string TargetName { get; }
        public int BaseAttackIntervalTicks { get; }
        public int EscapesToDefeat { get; }
        public IReadOnlyList<BaseUpgrade> BaseUpgrades { get; }
        public AssaultRules Assault { get; }
        public int BaseAttackWindupTicks { get; }
        public bool HasBase => Type != BattleObjectiveType.InterceptEscapes;

        public BattleObjective(
            BattleObjectiveType type,
            string targetName,
            int baseAttackIntervalTicks = 0,
            int escapesToDefeat = 0,
            BaseUpgrade[] baseUpgrades = null,
            AssaultRules assault = null,
            int baseAttackWindupTicks = 0)
        {
            bool valid = type switch
            {
                BattleObjectiveType.LegacyBaseLeak => baseAttackIntervalTicks == 0 && escapesToDefeat == 0,
                BattleObjectiveType.DefendBase => baseAttackIntervalTicks > 0 && escapesToDefeat == 0,
                BattleObjectiveType.InterceptEscapes => baseAttackIntervalTicks == 0 && escapesToDefeat > 0,
                _ => false
            };
            if (!valid || string.IsNullOrWhiteSpace(targetName))
            {
                throw new ArgumentException("Invalid battle objective: " + type);
            }

            if (baseAttackWindupTicks < 0
                || (type == BattleObjectiveType.DefendBase ? baseAttackWindupTicks >= baseAttackIntervalTicks : baseAttackWindupTicks != 0))
            {
                throw new ArgumentException("Invalid base attack windup.");
            }

            var upgrades = baseUpgrades ?? Array.Empty<BaseUpgrade>();
            if ((type == BattleObjectiveType.InterceptEscapes && upgrades.Length != 0)
                || upgrades.Any(upgrade => upgrade.GoldCost <= 0 || upgrade.DurabilityIncrease <= 0))
            {
                throw new ArgumentException("Invalid base upgrade for " + type, nameof(baseUpgrades));
            }

            Type = type;
            TargetName = targetName;
            BaseAttackIntervalTicks = baseAttackIntervalTicks;
            EscapesToDefeat = escapesToDefeat;
            BaseUpgrades = Array.AsReadOnly((BaseUpgrade[])upgrades.Clone());
            Assault = assault;
            BaseAttackWindupTicks = baseAttackWindupTicks;
        }
    }

    public sealed class FightConfiguration
    {
        public int StageId { get; set; }
        public string StageName { get; set; } = "林间哨站";
        public BattleObjective Objective { get; set; } = BattleObjective.Legacy;
        public AssaultNavigationMap AssaultNavigation { get; set; }
        public int InitialGold { get; set; }
        public int BaseHealth { get; set; }
        public int OpeningDiscountTowerId { get; set; }
        public int OpeningDiscountGold { get; set; }
        public int PlanningRepair { get; set; }
        public int RelayRouteId { get; set; }
        public int RelayPointIndex { get; set; }
        public bool HidePlannedRoutes { get; set; }
        // Zero is used by scenarios with individually authored pads.
        public int BuildGridStep { get; set; }
        public RouteDefinition[] Routes { get; set; } = Array.Empty<RouteDefinition>();
        public BuildSlotDefinition[] BuildSlots { get; set; } = Array.Empty<BuildSlotDefinition>();
        public TowerDefinition[] Towers { get; set; } = Array.Empty<TowerDefinition>();
        public EnemyDefinition[] Enemies { get; set; } = Array.Empty<EnemyDefinition>();
        public WaveDefinition[] Waves { get; set; } = Array.Empty<WaveDefinition>();
        public CardDefinition[] Cards { get; set; } = Array.Empty<CardDefinition>();
    }

    public sealed class RouteDefinition
    {
        public int Id { get; set; }
        public string EntranceName { get; set; } = "敌人入口";
        public Position2[] Points { get; set; } = Array.Empty<Position2>();
    }

    public sealed class BuildSlotDefinition
    {
        public int Id { get; set; }
        public Position2 Position { get; set; }
    }

    public sealed class TowerDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int BuildCost { get; set; }
        public int UpgradeCost { get; set; }
        public int Damage { get; set; }
        public int Range { get; set; }
        public int AttackIntervalTicks { get; set; }
        public int MaxLevel { get; set; } = 3;
        public int UpgradeDamage { get; set; }
        public int UpgradeRange { get; set; }
    }

    public sealed class EnemyDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Health { get; set; }
        public int SpeedPerTick { get; set; }
        public int GoldReward { get; set; }
        public int BaseDamage { get; set; }
    }

    public sealed class SpawnGroup
    {
        public int EnemyId { get; set; }
        public int RouteId { get; set; }
        public int Count { get; set; }
        public int StartTick { get; set; }
        public int IntervalTicks { get; set; }
    }

    public sealed class WaveDefinition
    {
        public int Id { get; set; }
        public string PlanningCue { get; set; }
        public int RewardGold { get; set; }
        public bool GiveCard { get; set; } = true;
        public SpawnGroup[] Groups { get; set; } = Array.Empty<SpawnGroup>();
    }

    public enum CardEffect
    {
        DamagePercent = 1,
        AttackSpeedPercent = 2,
        RangePercent = 3,
        Gold = 4,
        HealBase = 5
    }

    public sealed class CardDefinition
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public CardEffect Effect { get; set; }
        public int Value { get; set; }
        public int Weight { get; set; } = 1;
        /// <summary>Zero means repeatable without a stack limit.</summary>
        public int MaxStacks { get; set; }
        public int Rarity { get; set; } = 1;
        public int IconIndex { get; set; } = -1;
    }

    public enum BattlePhase
    {
        Planning,
        Fighting,
        ChoosingCard,
        Victory,
        Defeat
    }

    public readonly struct TowerSnapshot
    {
        public int Id { get; }
        public int DefinitionId { get; }
        public int SlotId { get; }
        public Position2 Position { get; }
        public int Level { get; }
        public int Range { get; }
        public int Damage { get; }
        public int AttackIntervalTicks { get; }
        public int SellValue { get; }
        public int CooldownTicks { get; }
        public int LastTargetId { get; }
        public int ShotSequence { get; }

        internal TowerSnapshot(
            int id,
            int definitionId,
            int slotId,
            Position2 position,
            int level,
            int range,
            int damage,
            int attackIntervalTicks,
            int sellValue,
            int cooldownTicks,
            int lastTargetId,
            int shotSequence)
        {
            Id = id;
            DefinitionId = definitionId;
            SlotId = slotId;
            Position = position;
            Level = level;
            Range = range;
            Damage = damage;
            AttackIntervalTicks = attackIntervalTicks;
            SellValue = sellValue;
            CooldownTicks = cooldownTicks;
            LastTargetId = lastTargetId;
            ShotSequence = shotSequence;
        }
    }

    public readonly struct EnemySnapshot
    {
        public int Id { get; }
        public int DefinitionId { get; }
        public int RouteId { get; }
        public Position2 Position { get; }
        internal Position2 PreviousPosition { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public bool AttackingBase { get; }
        internal bool IsStaged { get; }
        public long NextBaseAttackTick { get; }

        internal EnemySnapshot(
            int id,
            int definitionId,
            int routeId,
            Position2 position,
            Position2 previousPosition,
            int health,
            int maxHealth,
            bool attackingBase,
            bool isStaged,
            long nextBaseAttackTick)
        {
            Id = id;
            DefinitionId = definitionId;
            RouteId = routeId;
            Position = position;
            PreviousPosition = previousPosition;
            Health = health;
            MaxHealth = maxHealth;
            AttackingBase = attackingBase;
            IsStaged = isStaged;
            NextBaseAttackTick = nextBaseAttackTick;
        }
    }
}
