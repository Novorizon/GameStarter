using System;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.UnityPlayground
{
    // JsonUtility reads these DTOs on the editor thread. A simulation owns a private copy.
    [Serializable]
    public sealed class PlaygroundInput
    {
        public string modelInputSha256;
        public string expectation;
        public string requiredFeatures;
        public WorldInput world;
        public RequestInput[] requests = Array.Empty<RequestInput>();
        public TerrainInput[] worldEvents = Array.Empty<TerrainInput>();
        public DeathInput[] deaths = Array.Empty<DeathInput>();
        public MinimumAttackersInput[] minimumAttackers = Array.Empty<MinimumAttackersInput>();
        public int expectedLive;
        public int expectedAttackers = -1;
        public int deadlineTicks;
        public int holdTicks = 200;
        public int initialSettledCheckTick = -1;
        public int minimumOldWaitingPromotions;

        internal PlaygroundInput Copy()
        {
            var copy = (PlaygroundInput)MemberwiseClone();
            copy.world = world.Copy();
            copy.requests = requests.Select(item => item.Copy()).ToArray();
            copy.worldEvents = worldEvents.Select(item => item.Copy()).ToArray();
            copy.deaths = deaths.Select(item => item.Copy()).ToArray();
            copy.minimumAttackers = minimumAttackers.Select(item => item.Copy()).ToArray();
            return copy;
        }
    }

    [Serializable]
    public sealed class WorldInput
    {
        public int width;
        public int height;
        public int cellSizeMm;
        public int gatheringDistanceMm;
        public OriginInput origin;
        public string walkable;
        public string holding;
        public int[] spawnCells;
        public TargetInput[] targets;

        public WorldDefinition ToWorld()
        {
            return new WorldDefinition(
                width,
                height,
                cellSizeMm,
                new Point2(origin.x, origin.y),
                walkable.Select(value => value == '1').ToArray(),
                holding.Select(value => value == '1').ToArray(),
                spawnCells,
                targets.Select(target => target.Definition()).ToArray(),
                gatheringDistanceMm);
        }

        internal WorldInput Copy()
        {
            var copy = (WorldInput)MemberwiseClone();
            copy.origin = new OriginInput { x = origin.x, y = origin.y };
            copy.spawnCells = (int[])spawnCells.Clone();
            copy.targets = targets.Select(item => item.Copy()).ToArray();
            return copy;
        }
    }

    [Serializable]
    public sealed class OriginInput
    {
        public double x;
        public double y;
    }

    [Serializable]
    public sealed class TargetInput
    {
        public int id;
        public int radiusMm;
        public double x;
        public double y;

        public TargetDefinition Definition() => new TargetDefinition(id, new Point2(x, y), radiusMm);
        internal TargetInput Copy() => (TargetInput)MemberwiseClone();
    }

    [Serializable]
    public sealed class RequestInput
    {
        public int readyTick;
        public int id;
        public int radiusMm;
        public int speedMmPerTick;
        public int attackRangeMm;
        public int spawnCell;
        public int targetId;
        public int assignedGoalCell;

        public UnitProfile Profile() => new UnitProfile(radiusMm, speedMmPerTick, attackRangeMm);
        public SpawnRequest Command() => new SpawnRequest(id, Profile(), spawnCell, targetId, assignedGoalCell);
        internal RequestInput Copy() => (RequestInput)MemberwiseClone();

        internal static RequestInput Replacement(int id, int tick, SpawnRequest original)
        {
            return new RequestInput
            {
                id = id,
                readyTick = tick,
                radiusMm = original.Profile.RadiusMm,
                speedMmPerTick = original.Profile.SpeedMmPerTick,
                attackRangeMm = original.Profile.AttackRangeMm,
                spawnCell = original.SpawnCell,
                targetId = original.TargetId,
                assignedGoalCell = original.AssignedGoalCell
            };
        }
    }

    [Serializable]
    public sealed class TerrainInput
    {
        public int tick;
        public int[] blockCells = Array.Empty<int>();
        public int[] openCells = Array.Empty<int>();
        public bool afterSettlement;
        public bool hasReplaceTarget;
        public TargetInput replaceTarget;

        public WorldChange Change()
        {
            // JsonUtility can deserialize a null inline class as an instance with all fields at their defaults.
            bool replace = replaceTarget != null && (hasReplaceTarget || replaceTarget.id != 0 || replaceTarget.radiusMm != 0
                || replaceTarget.x != 0 || replaceTarget.y != 0);
            return new WorldChange
            {
                BlockCells = (int[])blockCells.Clone(),
                OpenCells = (int[])openCells.Clone(),
                ReplaceTarget = replace ? replaceTarget.Definition() : (TargetDefinition?)null
            };
        }

        internal TerrainInput Copy()
        {
            var copy = (TerrainInput)MemberwiseClone();
            copy.blockCells = (int[])blockCells.Clone();
            copy.openCells = (int[])openCells.Clone();
            copy.replaceTarget = replaceTarget?.Copy();
            return copy;
        }
    }

    [Serializable]
    public sealed class DeathInput
    {
        public int tick;
        public int count;
        public int range;
        public bool afterSettlement;
        public bool replace;
        public bool requireMoving;
        public bool attackersOnly;

        internal DeathInput Copy() => (DeathInput)MemberwiseClone();
    }

    [Serializable]
    public sealed class MinimumAttackersInput
    {
        public int range;
        public int count;

        internal MinimumAttackersInput Copy() => (MinimumAttackersInput)MemberwiseClone();
    }
}
