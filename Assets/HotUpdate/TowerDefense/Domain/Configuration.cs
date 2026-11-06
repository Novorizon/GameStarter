using System;

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

    public sealed class FightConfiguration
    {
        public int InitialGold { get; set; }
        public int BaseHealth { get; set; }
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
        public int Health { get; }
        public int MaxHealth { get; }
        public int Distance { get; }

        internal EnemySnapshot(int id, int definitionId, int routeId, Position2 position, int health, int maxHealth, int distance)
        {
            Id = id;
            DefinitionId = definitionId;
            RouteId = routeId;
            Position = position;
            Health = health;
            MaxHealth = maxHealth;
            Distance = distance;
        }
    }
}
