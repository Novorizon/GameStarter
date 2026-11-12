using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace NavigationLab.Candidates.ASipp
{
    public sealed class SippFactory : INavigationFactory
    {
        public string Id => "A-Sipp-SiegePositions";
        public string Description => "SIPP with earliest reachable attack positions, stable attackers, nearby waiting and dynamic terrain.";
        public NavigationFeatures Features => NavigationFeatures.DeathAndReinforcement | NavigationFeatures.DynamicTerrain;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new SippNavigator(world, profiles);
    }

    internal sealed class SippNavigator : INavigator
    {
        private readonly NavigationCrowd crowd;
        private readonly int targetId;
        private readonly Dictionary<int, Point2> starts = new Dictionary<int, Point2>();
        private int phase;
        public string LastSpawnFailure => crowd.LastFailure.ToString();
        public IReadOnlyDictionary<string, long> Metrics => new Dictionary<string, long>
        {
            ["ExpandedNodes"] = crowd.TotalExpandedNodes,
            ["Replans"] = crowd.ReplanCount,
            ["RejectedSpawns"] = crowd.RejectedSpawns,
            ["CachedAdmissionRejections"] = crowd.CachedAdmissionRejections,
            ["Reservations"] = crowd.ReservationCount
        };

        internal SippNavigator(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            var target = world.Targets.Single();
            targetId = target.Id;
            if (world.Origin.X != (int)world.Origin.X || world.Origin.Y != (int)world.Origin.Y
                || target.Center.X != (int)target.Center.X || target.Center.Y != (int)target.Center.Y)
            {
                throw new ArgumentException("Frozen A requires integer millimetre world coordinates.");
            }
            var grid = new NavigationGrid(
                world.Width,
                world.Height,
                world.CellSizeMm,
                new Position2((int)world.Origin.X, (int)world.Origin.Y),
                world.CopyWalkable(),
                new Position2((int)target.Center.X, (int)target.Center.Y),
                target.RadiusMm,
                world.CopyHolding(),
                world.SpawnCells.ToArray());
            crowd = new NavigationCrowd(grid, gatheringDistance: world.GatheringDistanceMm, stableSiegePositions: true);
            foreach (var profile in profiles)
            {
                crowd.RegisterProfile(profile.RadiusMm, profile.AttackRangeMm);
            }
        }

        public bool TrySpawn(SpawnRequest request)
        {
            if (phase != 0)
            {
                throw new InvalidOperationException("A supports spawning at its original 50ms boundary.");
            }
            if (request.TargetId != targetId)
            {
                throw new ArgumentException("Requested target does not match the single frozen A target.");
            }
            var point = crowd.Grid.Center(request.SpawnCell);
            return crowd.TrySpawn(request.Id, point, request.Profile.SpeedMmPerTick, request.Profile.RadiusMm, request.Profile.AttackRangeMm);
        }

        public void Step()
        {
            if (phase == 0)
            {
                starts.Clear();
                foreach (var agent in crowd.Agents)
                {
                    starts[agent.Id] = new Point2(agent.Position.X, agent.Position.Y);
                }
                crowd.Step(crowd.CurrentTick + 1);
            }
            phase++;
            if (phase == SimulationProtocol.Substeps)
            {
                phase = 0;
            }
        }

        public IReadOnlyList<UnitSnapshot> Snapshot()
        {
            return crowd.Agents.Select(agent =>
            {
                var position = new Point2(agent.Position.X, agent.Position.Y);
                var state = agent.Status == NavigationAgentStatus.Attacking ? MotionState.Attacking
                    : agent.Status == NavigationAgentStatus.Waiting ? MotionState.Waiting : MotionState.Moving;
                if (phase != 0 && starts.TryGetValue(agent.Id, out var start))
                {
                    position = start + (position - start) * (phase / (double)SimulationProtocol.Substeps);
                    if (position != new Point2(agent.Position.X, agent.Position.Y))
                    {
                        state = MotionState.Moving;
                    }
                }
                return new UnitSnapshot(agent.Id, position, state, agent.GoalCell, targetId);
            }).OrderBy(unit => unit.Id).ToArray();
        }

        public bool Remove(int id)
        {
            starts.Remove(id);
            return crowd.Remove(id);
        }
        public bool ApplyWorldChange(WorldChange change)
        {
            return phase == 0 && change != null && !change.ReplaceTarget.HasValue
                && crowd.TryApplyTerrainChange(change.BlockCells, change.OpenCells);
        }
        public void Dispose()
        {
            starts.Clear();
        }
    }
}
