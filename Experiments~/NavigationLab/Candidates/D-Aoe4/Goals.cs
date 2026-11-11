using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal sealed partial class AoeNavigator
    {
        private int[] Reachability(int start, int radius)
        {
            string key = radius + ":" + start;
            if (!reachable.TryGetValue(key, out var distances))
            {
                distances = geometry.Reachable(start, radius);
                reachable.Add(key, distances);
                Add("StaticReachabilityBuilds");
            }
            return distances;
        }

        private int[] Holding(UnitProfile profile, int target)
        {
            string key = ProfileKey(profile, target);
            if (!holding.TryGetValue(key, out var cells))
            {
                cells = Enumerable.Range(0, World.CellCount).Where(cell => geometry.CanHold(cell, profile, target)).ToArray();
                holding.Add(key, cells);
            }
            return cells;
        }

        private int PickGoal(UnitProfile profile, int target, int[] distances, Point2 from, bool attacksFirst)
        {
            return Holding(profile, target).Where(cell => distances[cell] >= 0 && !goalOwners.ContainsKey(cell) && GoalClear(cell, profile, -1))
                .OrderByDescending(cell => attacksFirst && geometry.CanAttack(World.Center(cell), profile, target))
                .ThenByDescending(cell => attacksFirst && PreferredAttack(cell, profile, target))
                .ThenByDescending(cell => distances[cell])
                .ThenBy(cell => Point2.Distance(from, World.Center(cell)))
                .ThenBy(cell => cell).DefaultIfEmpty(-1).First();
        }

        private bool PreferredAttack(int cell, UnitProfile profile, int target)
        {
            return geometry.CanAttack(World.Center(cell), profile, target)
                && geometry.Neighbours(cell).Any(next => geometry.CanEdge(cell, next, profile.RadiusMm)
                    && !geometry.CanAttack(World.Center(next), profile, target));
        }

        private int RelocateCompatibleOwner(SpawnRequest request, int[] incomingReachable)
        {
            foreach (var other in actors.Values.OrderBy(actor => actor.Request.Id))
            {
                if (other.ExchangePartner != 0 || other.Request.AssignedGoalCell >= 0 || other.Request.TargetId != request.TargetId
                    || incomingReachable[other.Goal] < 0
                    || !geometry.CanHold(other.Goal, request.Profile, request.TargetId))
                {
                    continue;
                }
                int start = World.CellAt(other.Position);
                var canReach = Reachability(start, other.Request.Profile.RadiusMm);
                int replacement = Holding(other.Request.Profile, other.Request.TargetId)
                    .Where(cell => !goalOwners.ContainsKey(cell) && canReach[cell] >= 0
                        && !geometry.CanHold(cell, request.Profile, request.TargetId) && GoalClear(cell, other.Request.Profile, other.Request.Id))
                    .OrderBy(cell => canReach[cell]).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
                if (replacement < 0)
                {
                    continue;
                }
                int released = other.Goal;
                SetGoal(other, replacement);
                Add("CompatibleGoalRelocations");
                return released;
            }
            return -1;
        }

        private void TrackAttackPeaks(Actor[] ordered)
        {
            foreach (var group in ordered.GroupBy(actor => ProfileKey(actor.Request.Profile, actor.Request.TargetId)))
            {
                int actual = group.Count(actor => State(actor) == MotionState.Attacking);
                attackPeaks.TryGetValue(group.Key, out int old);
                attackPeaks[group.Key] = Math.Max(old, actual);
                if (actual > old)
                {
                    goalRevision++;
                }
            }
        }

        private void Promote(Actor[] ordered)
        {
            foreach (var actor in ordered.OrderBy(actor => actor.Request.Profile.AttackRangeMm).ThenByDescending(actor => actor.Request.Profile.RadiusMm))
            {
                if (actor.ExchangePartner != 0 || actor.Request.AssignedGoalCell >= 0 || actor.LastPromotionRevision == goalRevision || actor.YieldCell >= 0
                    || geometry.CanAttack(World.Center(actor.Goal), actor.Request.Profile, actor.Request.TargetId))
                {
                    continue;
                }
                actor.LastPromotionRevision = goalRevision;
                int[] distances = Reachability(World.CellAt(actor.Position), actor.Request.Profile.RadiusMm);
                int attack = Holding(actor.Request.Profile, actor.Request.TargetId)
                    .Where(cell => !goalOwners.ContainsKey(cell) && distances[cell] >= 0 && GoalClear(cell, actor.Request.Profile, actor.Request.Id)
                        && geometry.CanAttack(World.Center(cell), actor.Request.Profile, actor.Request.TargetId))
                    .OrderBy(cell => distances[cell]).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
                if (attack >= 0)
                {
                    SetGoal(actor, attack);
                    Add("AttackPromotions");
                    continue;
                }
                string key = ProfileKey(actor.Request.Profile, actor.Request.TargetId);
                int live = ordered.Count(other => ProfileKey(other.Request.Profile, other.Request.TargetId) == key);
                int assigned = ordered.Count(other => ProfileKey(other.Request.Profile, other.Request.TargetId) == key
                    && geometry.CanAttack(World.Center(other.Goal), other.Request.Profile, other.Request.TargetId));
                attackPeaks.TryGetValue(key, out int peak);
                if (assigned >= Math.Min(live, Math.Max(1, peak)))
                {
                    continue;
                }
                var constrained = new HashSet<int>(Holding(actor.Request.Profile, actor.Request.TargetId)
                    .Where(cell => geometry.CanAttack(World.Center(cell), actor.Request.Profile, actor.Request.TargetId)));
                foreach (var donor in ordered.Where(other => other.Request.TargetId == actor.Request.TargetId
                    && other.ExchangePartner == 0 && other.Request.Id != actor.Request.Id && other.Request.AssignedGoalCell < 0
                    && constrained.Contains(other.Goal) && other.YieldCell < 0).OrderBy(other => other.Request.Id))
                {
                    var flexible = new HashSet<int>(Holding(donor.Request.Profile, donor.Request.TargetId)
                        .Where(cell => geometry.CanAttack(World.Center(cell), donor.Request.Profile, donor.Request.TargetId)));
                    if (flexible.Count <= constrained.Count || !constrained.All(flexible.Contains))
                    {
                        continue;
                    }
                    int[] donorReachable = Reachability(World.CellAt(donor.Position), donor.Request.Profile.RadiusMm);
                    int replacement = Holding(donor.Request.Profile, donor.Request.TargetId)
                        .Where(cell => !goalOwners.ContainsKey(cell) && donorReachable[cell] >= 0
                            && !geometry.CanAttack(World.Center(cell), donor.Request.Profile, donor.Request.TargetId)
                            && GoalClear(cell, donor.Request.Profile, donor.Request.Id))
                        .OrderBy(cell => donorReachable[cell]).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
                    if (replacement < 0 || distances[donor.Goal] < 0)
                    {
                        continue;
                    }
                    int released = donor.Goal;
                    SetGoal(donor, replacement);
                    SetGoal(actor, released);
                    Add("ConstrainedAttackTransfers");
                    break;
                }
            }
        }

        private bool GoalClear(int cell, UnitProfile profile, int ignoreId)
        {
            Point2 point = World.Center(cell);
            return goalOwners.All(pair => pair.Value == ignoreId || !actors.TryGetValue(pair.Value, out var owner)
                || Point2.Distance(point, World.Center(pair.Key)) >= profile.RadiusMm + owner.Request.Profile.RadiusMm - 1e-7);
        }

        private void SetGoal(Actor actor, int goal)
        {
            if (goalOwners.TryGetValue(actor.Goal, out int owner) && owner == actor.Request.Id)
            {
                goalOwners.Remove(actor.Goal);
            }
            actor.Goal = goal;
            goalRevision++;
            goalOwners[goal] = actor.Request.Id;
            actor.Route = null;
            actor.PreciseWaypoint = -1;
            actor.BestGoalDistance = double.MaxValue;
            actor.LastProgress = step;
        }
    }
}
