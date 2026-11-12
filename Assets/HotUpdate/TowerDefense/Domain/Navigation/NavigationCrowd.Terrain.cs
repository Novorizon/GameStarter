using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private sealed class TerrainHold
        {
            internal List<TimedCell> Path;
            internal Position2? MovementOrigin;
            internal int OtherCell = -1;
            internal TimedCell End => Path[Path.Count - 1];
            internal long ReservationStart;
        }

        private bool terrainRepairsPending;

        /// <summary>Applies a terrain change at the current tick without moving or removing any live body.</summary>
        public bool TryApplyTerrainChange(IReadOnlyCollection<int> blocked, IReadOnlyCollection<int> opened)
        {
            if (blocked == null || opened == null || blocked.Any(cell => cell < 0 || cell >= grid.CellCount || entries.Contains(cell))
                || opened.Any(cell => cell < 0 || cell >= grid.CellCount) || blocked.Intersect(opened).Any())
            {
                return false;
            }

            int[] closedCells = blocked.Where(grid.IsWalkable).Distinct().ToArray();
            int[] openedCells = opened.Where(cell => !grid.IsWalkable(cell)).Distinct().ToArray();
            if (closedCells.Length == 0 && openedCells.Length == 0)
            {
                return true;
            }

            var ordered = occupants.Values.OrderBy(state => state.Agent.Id).ToArray();
            if (ordered.Any(state => closedCells.Any(cell => grid.DiscOverlapsCell(state.Agent.Position, state.Agent.Radius, cell))))
            {
                return false;
            }

            var next = grid.WithTerrain(closedCells, openedCells);
            var holds = new Dictionary<int, TerrainHold>();
            var repairQueue = new Queue<int>();
            foreach (var state in ordered)
            {
                if (state.TerrainRepairPending || !CanRetainTerrainPath(state, next))
                {
                    int id = state.Agent.Id;
                    var hold = CreateTerrainHold(state, next);
                    if (hold == null)
                    {
                        return false;
                    }

                    holds.Add(id, hold);
                    repairQueue.Enqueue(id);
                }
            }

            while (repairQueue.Count > 0)
            {
                int id = repairQueue.Dequeue();
                var hold = holds[id];
                int radius = occupants[id].Agent.Radius;
                var claims = ConflictingReservations(hold.End.Cell, radius).Where(claim => claim.End > hold.ReservationStart);
                if (hold.OtherCell >= 0)
                {
                    claims = claims.Concat(ConflictingReservations(hold.OtherCell, radius)
                        .Where(claim => claim.End > CurrentTick && claim.Start < hold.End.Arrival));
                }

                // An affected unit temporarily keeps a cell it was expected to leave. Repair paths that depended on its departure.
                foreach (var claim in claims)
                {
                    if (!holds.ContainsKey(claim.Owner))
                    {
                        var dependent = CreateTerrainHold(occupants[claim.Owner], next);
                        if (dependent == null)
                        {
                            return false;
                        }

                        holds.Add(claim.Owner, dependent);
                        repairQueue.Enqueue(claim.Owner);
                    }
                }
            }

            var anchors = holds.Select(pair => (Center: next.Center(pair.Value.End.Cell), Radius: occupants[pair.Key].Agent.Radius)).ToArray();
            for (int first = 0; first < anchors.Length; first++)
            {
                for (int second = first + 1; second < anchors.Length; second++)
                {
                    long clearance = (long)anchors[first].Radius + anchors[second].Radius;
                    if (NavigationGrid.DistanceSquared(anchors[first].Center, anchors[second].Center) < clearance * clearance)
                    {
                        return false;
                    }
                }
            }

            grid = next;
            geometryComponents.Clear();
            profileFlexibility.Clear();
            parkingPrepared = false;
            quotaFailureRevision = -1;
            vacancyJob = null;
            vacancyAdmission = null;
            vacancyAdmissionFailures.Clear();
            vacancyAttackFailureRevision = -1;
            goalRevision++;
            trafficRevision++;
            foreach (var profile in profiles.Values)
            {
                profile.CapacityRevisions.Clear();
                RefreshProfileGeometry(profile);
                profile.HistoricalAttackQuota = 0;
            }

            foreach (var state in ordered)
            {
                if (holds.ContainsKey(state.Agent.Id))
                {
                    RemoveReservations(state.Agent.Id);
                    goalOwners.Remove(state.Agent.GoalCell);
                    state.Path = null;
                }
            }

            foreach (var state in ordered)
            {
                if (holds.TryGetValue(state.Agent.Id, out var hold))
                {
                    AcceptPlan(state, hold.Path);
                    state.TerrainMovementOrigin = hold.MovementOrigin;
                    state.TerrainMovementOtherCell = hold.OtherCell;
                    if (hold.OtherCell >= 0)
                    {
                        Reserve(hold.OtherCell, state.Agent.Id, state.Agent.Radius, CurrentTick, hold.End.Arrival);
                    }

                    state.TerrainRepairPending = true;
                    state.TerrainRepairRevision = -1;
                    state.Agent.Status = NavigationAgentStatus.Navigating;
                }
                else
                {
                    state.AttackGoal = state.Profile.Attack[state.Agent.GoalCell];
                    UpdatePosition(state, CurrentTick);
                }
            }

            foreach (var attackers in ordered.Where(state => state.Agent.Status == NavigationAgentStatus.Attacking).GroupBy(state => state.Profile))
            {
                attackers.Key.HistoricalAttackQuota = attackers.Count();
            }

            terrainRepairsPending = holds.Count > 0;
            if (terrainRepairsPending)
            {
                RepairTerrainRoutes();
            }

            return true;
        }

        private bool CanRetainTerrainPath(Occupant state, NavigationGrid next)
        {
            int goal = state.Agent.GoalCell;
            long gathering = (long)next.TargetRadius + GatheringDistance;
            if (!next.CanOccupy(goal, state.Profile.Radius) || !state.RoadQueue && (!next.HoldingAllowed(goal)
                || !next.HasAttackLine(goal, state.Profile.Radius, state.Profile.Range)
                && NavigationGrid.DistanceSquared(next.Center(goal), next.TargetCenter) > gathering * gathering))
            {
                return false;
            }

            for (int index = state.Segment + 1; index < state.Path.Count; index++)
            {
                if (!next.CanTraverse(state.Path[index - 1].Cell, state.Path[index].Cell, state.Profile.Radius))
                {
                    return false;
                }
            }

            return true;
        }

        private TerrainHold CreateTerrainHold(Occupant state, NavigationGrid nextGrid)
        {
            var origin = state.Agent.Position;
            int occupied = nextGrid.CellAt(origin);
            var start = new TimedCell { Cell = occupied, Departure = CurrentTick, Arrival = CurrentTick };
            var hold = new TerrainHold { Path = new List<TimedCell> { start }, ReservationStart = CurrentTick };
            if (Same(origin, nextGrid.Center(occupied)))
            {
                return hold;
            }

            var from = state.Path[state.Segment];
            var to = state.Path[state.Segment + 1];
            int radius = state.Agent.Radius;
            if (nextGrid.CanOccupy(to.Cell, radius) && nextGrid.HasClearanceOnAxis(origin, nextGrid.Center(to.Cell), radius))
            {
                hold.Path[0] = from;
                hold.Path.Add(to);
                hold.MovementOrigin = state.TerrainMovementOrigin;
                hold.OtherCell = state.TerrainMovementOtherCell;
                hold.ReservationStart = to.Arrival;
                return hold;
            }

            // The disc is still clear of the new obstacle. Back out along the part of the old segment already traversed.
            int destination = state.TerrainMovementOtherCell >= 0 ? state.TerrainMovementOtherCell : from.Cell;
            var center = nextGrid.Center(destination);
            if (!nextGrid.CanOccupy(destination, radius) || !nextGrid.HasClearanceOnAxis(origin, center, radius))
            {
                return null;
            }

            int distance = Math.Abs(center.X - origin.X) + Math.Abs(center.Y - origin.Y);
            long duration = (distance + state.Agent.Speed - 1) / state.Agent.Speed;
            hold.Path[0].Cell = destination;
            hold.Path.Add(new TimedCell { Cell = destination, Departure = CurrentTick, Arrival = CurrentTick + duration });
            hold.MovementOrigin = origin;
            hold.OtherCell = to.Cell;
            return hold;
        }

        private void RepairTerrainRoutes()
        {
            var priorFailure = LastFailure;
            foreach (var state in occupants.Values.Where(state => state.TerrainRepairPending).OrderBy(state => state.Agent.Id))
            {
                int cell = state.Agent.GoalCell;
                if (!Same(state.Agent.Position, grid.Center(cell)) || state.TerrainRepairRevision == goalRevision && CurrentTick < state.TerrainRepairRetryTick)
                {
                    continue;
                }

                state.TerrainRepairRevision = goalRevision;
                state.TerrainRepairRetryTick = CurrentTick + Math.Max(4, grid.CellSize / state.Agent.Speed);
                var plan = FindPlan(state, cell, true);
                if (plan == null && UseRoadQueue)
                {
                    plan = FindRoadQueuePlan(state, cell, grid.RoadProgress(cell));
                }

                if (plan == null)
                {
                    continue;
                }

                state.TerrainRepairPending = false;
                AcceptPlan(state, plan);
                UpdatePosition(state, CurrentTick);
                ReplanCount++;
            }

            terrainRepairsPending = occupants.Values.Any(state => state.TerrainRepairPending);
            LastFailure = priorFailure;
        }
    }
}
