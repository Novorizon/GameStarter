using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace NavigationLab.Core
{
    public readonly struct Point2 : IEquatable<Point2>
    {
        public double X { get; }
        public double Y { get; }
        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }
        public double Length => Math.Sqrt(X * X + Y * Y);
        public double LengthSquared => X * X + Y * Y;
        public Point2 Normalized => Length > 1e-12 ? this / Length : default;
        public static Point2 operator +(Point2 a, Point2 b) => new Point2(a.X + b.X, a.Y + b.Y);
        public static Point2 operator -(Point2 a, Point2 b) => new Point2(a.X - b.X, a.Y - b.Y);
        public static Point2 operator *(Point2 a, double scale) => new Point2(a.X * scale, a.Y * scale);
        public static Point2 operator /(Point2 a, double scale) => new Point2(a.X / scale, a.Y / scale);
        public static double Dot(Point2 a, Point2 b) => a.X * b.X + a.Y * b.Y;
        public static double Distance(Point2 a, Point2 b) => (a - b).Length;
        public bool Equals(Point2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Point2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(Point2 a, Point2 b) => a.Equals(b);
        public static bool operator !=(Point2 a, Point2 b) => !a.Equals(b);
    }

    public readonly struct UnitProfile : IEquatable<UnitProfile>
    {
        public int RadiusMm { get; }
        public int SpeedMmPerTick { get; }
        public int AttackRangeMm { get; }
        public UnitProfile(int radiusMm, int speedMmPerTick, int attackRangeMm)
        {
            RadiusMm = radiusMm;
            SpeedMmPerTick = speedMmPerTick;
            AttackRangeMm = attackRangeMm;
        }
        public bool Equals(UnitProfile other) => RadiusMm == other.RadiusMm && SpeedMmPerTick == other.SpeedMmPerTick && AttackRangeMm == other.AttackRangeMm;
        public override bool Equals(object? obj) => obj is UnitProfile other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(RadiusMm, SpeedMmPerTick, AttackRangeMm);
    }

    public readonly struct TargetDefinition
    {
        public int Id { get; }
        public Point2 Center { get; }
        public int RadiusMm { get; }
        public TargetDefinition(int id, Point2 center, int radiusMm)
        {
            Id = id;
            Center = center;
            RadiusMm = radiusMm;
        }
    }

    public readonly struct SpawnRequest
    {
        public int Id { get; }
        public UnitProfile Profile { get; }
        public int SpawnCell { get; }
        public int TargetId { get; }
        public int AssignedGoalCell { get; }
        public SpawnRequest(int id, UnitProfile profile, int spawnCell, int targetId = 1, int assignedGoalCell = -1)
        {
            Id = id;
            Profile = profile;
            SpawnCell = spawnCell;
            TargetId = targetId;
            AssignedGoalCell = assignedGoalCell;
        }
    }

    public enum MotionState
    {
        Moving,
        Attacking,
        Waiting
    }

    public readonly struct UnitSnapshot
    {
        public int Id { get; }
        public Point2 Position { get; }
        public MotionState State { get; }
        public int GoalCell { get; }
        public int TargetId { get; }
        public UnitSnapshot(int id, Point2 position, MotionState state, int goalCell, int targetId = 1)
        {
            Id = id;
            Position = position;
            State = state;
            GoalCell = goalCell;
            TargetId = targetId;
        }
    }

    [Flags]
    public enum NavigationFeatures
    {
        None = 0,
        DeathAndReinforcement = 1,
        DynamicTerrain = 2,
        MovingTarget = 4,
        MultipleTargets = 8,
        AssignedGoals = 16,
        LargeBodies = 32,
        FastPerTick = 64
    }

    /// <summary>Every Step is one 5ms physical observation interval. Profiles use mm per 50ms logical tick.</summary>
    public interface INavigator : IDisposable
    {
        bool TrySpawn(SpawnRequest request);
        void Step();
        bool Remove(int id);
        bool ApplyWorldChange(WorldChange change);
        IReadOnlyList<UnitSnapshot> Snapshot();
        IReadOnlyDictionary<string, long> Metrics { get; }
        string LastSpawnFailure { get; }
    }

    public interface INavigationFactory
    {
        string Id { get; }
        string Description { get; }
        NavigationFeatures Features { get; }
        INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles);
    }

    public static class SimulationProtocol
    {
        public const int MicrostepMilliseconds = 5;
        public const int LogicalTickMilliseconds = 50;
        public const int Substeps = LogicalTickMilliseconds / MicrostepMilliseconds;
        public const double GoalToleranceMm = 5;
        public const double GeometryToleranceMm = 0.1;
    }

    public sealed class WorldChange
    {
        public int[] BlockCells { get; set; } = Array.Empty<int>();
        public int[] OpenCells { get; set; } = Array.Empty<int>();
        public TargetDefinition? ReplaceTarget { get; set; }
        public WorldChange DeepClone() => new WorldChange
        {
            BlockCells = (int[])BlockCells.Clone(),
            OpenCells = (int[])OpenCells.Clone(),
            ReplaceTarget = ReplaceTarget
        };
    }

    /// <summary>Immutable input. Every navigator receives an independent clone; the oracle retains its own world.</summary>
    public sealed class WorldDefinition
    {
        private readonly bool[] walkable;
        private readonly bool[] holding;
        private readonly int[] spawnCells;
        private readonly TargetDefinition[] targets;
        private readonly ReadOnlyCollection<int> spawnView;
        private readonly ReadOnlyCollection<TargetDefinition> targetView;
        public int Width { get; }
        public int Height { get; }
        public int CellSizeMm { get; }
        public Point2 Origin { get; }
        public int GatheringDistanceMm { get; }
        public int CellCount => Width * Height;
        public IReadOnlyList<int> SpawnCells => spawnView;
        public IReadOnlyList<TargetDefinition> Targets => targetView;

        public WorldDefinition(
            int width,
            int height,
            int cellSizeMm,
            Point2 origin,
            bool[] walkable,
            bool[] holding,
            int[] spawnCells,
            TargetDefinition[] targets,
            int gatheringDistanceMm = 4000)
        {
            if (width < 2 || height < 2 || cellSizeMm <= 0 || walkable.Length != width * height || holding.Length != width * height)
            {
                throw new ArgumentException("Invalid world geometry.");
            }
            Width = width;
            Height = height;
            CellSizeMm = cellSizeMm;
            Origin = origin;
            GatheringDistanceMm = gatheringDistanceMm;
            this.walkable = (bool[])walkable.Clone();
            this.holding = (bool[])holding.Clone();
            this.spawnCells = (int[])spawnCells.Clone();
            this.targets = (TargetDefinition[])targets.Clone();
            spawnView = Array.AsReadOnly(this.spawnCells);
            targetView = Array.AsReadOnly(this.targets);
            if (targets.Length == 0 || targets.Select(target => target.Id).Distinct().Count() != targets.Length)
            {
                throw new ArgumentException("Expected unique targets.");
            }
        }

        public bool IsWalkable(int cell) => cell >= 0 && cell < CellCount && walkable[cell];
        public bool HoldingAllowed(int cell) => IsWalkable(cell) && holding[cell] && !spawnCells.Contains(cell);
        public TargetDefinition Target(int id) => targets.Single(target => target.Id == id);
        public Point2 Center(int cell) => new Point2(Origin.X + (cell % Width + .5) * CellSizeMm, Origin.Y + (cell / Width + .5) * CellSizeMm);
        public int CellAt(Point2 point)
        {
            int x = (int)Math.Floor((point.X - Origin.X) / CellSizeMm);
            int y = (int)Math.Floor((point.Y - Origin.Y) / CellSizeMm);
            return x >= 0 && y >= 0 && x < Width && y < Height ? y * Width + x : -1;
        }
        public IEnumerable<int> Neighbours(int cell)
        {
            int x = cell % Width;
            int y = cell / Width;
            if (x > 0)
            {
                yield return cell - 1;
            }
            if (x + 1 < Width)
            {
                yield return cell + 1;
            }
            if (y > 0)
            {
                yield return cell - Width;
            }
            if (y + 1 < Height)
            {
                yield return cell + Width;
            }
        }
        public WorldDefinition Clone() => new WorldDefinition(Width, Height, CellSizeMm, Origin, walkable, holding, spawnCells, targets, GatheringDistanceMm);
        public WorldDefinition WithChange(WorldChange change)
        {
            var next = (bool[])walkable.Clone();
            foreach (int cell in change.BlockCells)
            {
                next[cell] = false;
            }
            foreach (int cell in change.OpenCells)
            {
                next[cell] = true;
            }
            var nextTargets = targets.Select(target => change.ReplaceTarget?.Id == target.Id ? change.ReplaceTarget.Value : target).ToArray();
            return new WorldDefinition(Width, Height, CellSizeMm, Origin, next, holding, spawnCells, nextTargets, GatheringDistanceMm);
        }
        public bool[] CopyWalkable() => (bool[])walkable.Clone();
        public bool[] CopyHolding() => (bool[])holding.Clone();
    }
}
