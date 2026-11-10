using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private sealed class SafeInterval
        {
            internal long Start;
            internal long End;
        }

        private sealed class SearchNode
        {
            internal int Cell;
            internal int Interval;
            internal long Arrival;
            internal long Departure;
            internal long Estimate;
            internal SearchNode Parent;
        }

        private sealed class MinHeap
        {
            private readonly List<SearchNode> items = new List<SearchNode>();
            internal int Count => items.Count;

            internal void Push(SearchNode node)
            {
                int index = items.Count;
                items.Add(node);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (Compare(items[parent], node) <= 0)
                    {
                        break;
                    }

                    items[index] = items[parent];
                    index = parent;
                }

                items[index] = node;
            }

            internal SearchNode Pop()
            {
                var first = items[0];
                var last = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0)
                {
                    return first;
                }

                int index = 0;
                while (index * 2 + 1 < items.Count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < items.Count && Compare(items[child + 1], items[child]) < 0)
                    {
                        child++;
                    }

                    if (Compare(last, items[child]) <= 0)
                    {
                        break;
                    }

                    items[index] = items[child];
                    index = child;
                }

                items[index] = last;
                return first;
            }

            private static int Compare(SearchNode first, SearchNode second)
            {
                int value = first.Estimate.CompareTo(second.Estimate);
                if (value == 0)
                {
                    value = first.Arrival.CompareTo(second.Arrival);
                }

                if (value == 0)
                {
                    value = first.Cell.CompareTo(second.Cell);
                }

                return value == 0 ? first.Interval.CompareTo(second.Interval) : value;
            }
        }

        private List<TimedCell> FindPlan(Occupant state, int start, bool allowGathering)
        {
            LastExpandedNodes = 0;
            if (!parkingPrepared)
            {
                PrepareParking();
            }
            var attacks = new bool[grid.CellCount];
            var preferredAttacks = new bool[grid.CellCount];
            var gathering = new bool[grid.CellCount];
            bool anyAttack = false;
            bool anyPreferredAttack = false;
            bool anyGather = false;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                if (entries.Contains(cell) || (goalOwners.TryGetValue(cell, out int owner) && owner != state.Agent.Id))
                {
                    continue;
                }

                attacks[cell] = state.Profile.Attack[cell];
                preferredAttacks[cell] = attacks[cell] && state.Profile.PreferredAttack[cell];
                anyPreferredAttack |= preferredAttacks[cell];
                gathering[cell] = allowGathering && state.Profile.Gather[cell];
                anyAttack |= attacks[cell];
                anyGather |= gathering[cell];
            }

            if (!anyAttack && !anyGather)
            {
                LastFailure = NavigationFailure.CapacityExceeded;
                return null;
            }

            var preferredGathering = new bool[grid.CellCount];
            bool anyPreferred = false;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                preferredGathering[cell] = gathering[cell] && state.Profile.Preferred[cell];
                anyPreferred |= preferredGathering[cell];
            }

            var regions = AccessRegions(state, start);
            var safety = new sbyte[grid.CellCount];
            var safeIntervals = new Dictionary<int, List<SafeInterval>>();
            NavigationFailure attackFailure = NavigationFailure.GoalAccessBlocked;
            if (anyPreferredAttack)
            {
                var path = Search(state, start, preferredAttacks, regions, safety, safeIntervals);
                if (path != null)
                {
                    return path;
                }

                attackFailure = LastFailure;
            }

            if (anyAttack)
            {
                var path = Search(state, start, attacks, regions, safety, safeIntervals);
                if (path != null)
                {
                    return path;
                }

                attackFailure = MergeFailure(attackFailure, LastFailure);
            }

            if (anyPreferred)
            {
                var path = Search(state, start, preferredGathering, regions, safety, safeIntervals);
                if (path != null)
                {
                    return path;
                }

                attackFailure = MergeFailure(attackFailure, LastFailure);
            }

            if (anyGather)
            {
                var path = Search(state, start, gathering, regions, safety, safeIntervals);
                if (path != null)
                {
                    return path;
                }

                LastFailure = MergeFailure(attackFailure, LastFailure);
            }

            if (allowGathering && LastFailure == NavigationFailure.GoalAccessBlocked)
            {
                // No target is admissible under current committed goals and preserved access. This is not a proof
                // that the map has no solution after a different joint rearrangement of existing occupants.
                LastFailure = NavigationFailure.CapacityExceeded;
            }

            return null;
        }

        private static NavigationFailure MergeFailure(NavigationFailure first, NavigationFailure second)
        {
            if (first == NavigationFailure.SearchLimitExceeded || second == NavigationFailure.SearchLimitExceeded)
            {
                return NavigationFailure.SearchLimitExceeded;
            }

            if (first == NavigationFailure.PlanningHorizonExceeded || second == NavigationFailure.PlanningHorizonExceeded)
            {
                return NavigationFailure.PlanningHorizonExceeded;
            }

            if (first == NavigationFailure.SpawnOccupied || second == NavigationFailure.SpawnOccupied)
            {
                return NavigationFailure.SpawnOccupied;
            }

            return first == NavigationFailure.ReservationBlocked || second == NavigationFailure.ReservationBlocked
                ? NavigationFailure.ReservationBlocked : second;
        }

        private List<TimedCell> Search(
            Occupant state,
            int start,
            bool[] targets,
            List<AccessRegion> regions,
            sbyte[] safety,
            Dictionary<int, List<SafeInterval>> safeIntervals,
            long planningStart = -1)
        {
            if (planningStart < 0)
            {
                planningStart = CurrentTick;
            }

            if (planningStart < CurrentTick)
            {
                throw new ArgumentOutOfRangeException(nameof(planningStart));
            }

            var distances = GoalDistances(state.Profile, targets);
            if (distances[start] < 0)
            {
                LastFailure = NavigationFailure.GoalAccessBlocked;
                return null;
            }

            int moveTicks = (grid.CellSize + state.Agent.Speed - 1) / state.Agent.Speed;
            var open = new MinHeap();
            var best = new Dictionary<long, SearchNode>();
            var startIntervals = Intervals(start, state.Agent.Id, safeIntervals);
            int initialInterval = startIntervals.FindIndex(value => value.Start <= planningStart && planningStart < value.End);
            if (initialInterval < 0)
            {
                LastFailure = NavigationFailure.SpawnOccupied;
                return null;
            }

            var initial = new SearchNode
            {
                Cell = start,
                Interval = initialInterval,
                Arrival = planningStart,
                Departure = planningStart,
                Estimate = planningStart + (long)distances[start] * moveTicks
            };
            open.Push(initial);
            best.Add(NodeKey(start, initialInterval), initial);
            long deadline = planningStart + planningHorizon;
            bool exceededHorizon = false;
            while (open.Count > 0)
            {
                var node = open.Pop();
                if (!ReferenceEquals(best[NodeKey(node.Cell, node.Interval)], node))
                {
                    continue;
                }

                if (!AdmissionBudgetAvailable())
                {
                    return null;
                }

                LastExpandedNodes++;
                TotalExpandedNodes++;
                if (LastExpandedNodes > searchLimit)
                {
                    LastFailure = NavigationFailure.SearchLimitExceeded;
                    return null;
                }

                var sourceInterval = Intervals(node.Cell, state.Agent.Id, safeIntervals)[node.Interval];
                if (targets[node.Cell] && sourceInterval.End == Forever)
                {
                    if (safety[node.Cell] == 0)
                    {
                        safety[node.Cell] = GoalPreservesAccess(regions, node.Cell, state.Agent.Id) ? (sbyte)1 : (sbyte)-1;
                    }

                    if (safety[node.Cell] > 0)
                    {
                        LastFailure = NavigationFailure.None;
                        return Reconstruct(node);
                    }
                }

                foreach (int next in grid.Neighbours(node.Cell))
                {
                    if (distances[next] < 0 || !grid.CanTraverse(node.Cell, next, state.Profile.Radius))
                    {
                        continue;
                    }

                    var intervals = Intervals(next, state.Agent.Id, safeIntervals);
                    for (int i = 0; i < intervals.Count; i++)
                    {
                        var interval = intervals[i];
                        long departure = Math.Max(node.Arrival, interval.Start);
                        long arrival = departure + moveTicks;
                        if (arrival > deadline)
                        {
                            exceededHorizon = true;
                            continue;
                        }

                        // Both endpoint cells remain exclusively owned for the entire move, including slow transit.
                        if (arrival > sourceInterval.End || arrival >= interval.End)
                        {
                            continue;
                        }

                        long key = NodeKey(next, i);
                        if (best.TryGetValue(key, out var earlier) && earlier.Arrival <= arrival)
                        {
                            continue;
                        }

                        var candidate = new SearchNode
                        {
                            Cell = next,
                            Interval = i,
                            Arrival = arrival,
                            Departure = departure,
                            Parent = node,
                            Estimate = arrival + (long)distances[next] * moveTicks
                        };
                        best[key] = candidate;
                        open.Push(candidate);
                    }
                }
            }

            var component = regions.FirstOrDefault(value => ReferenceEquals(value.Profile, state.Profile) && value.Before[start]);
            bool allGoalsRejected = component != null;
            for (int cell = 0; cell < grid.CellCount && allGoalsRejected; cell++)
            {
                if (targets[cell] && component.Before[cell] && safety[cell] != -1)
                {
                    allGoalsRejected = false;
                }
            }

            LastFailure = allGoalsRejected ? NavigationFailure.GoalAccessBlocked
                : exceededHorizon ? NavigationFailure.PlanningHorizonExceeded : NavigationFailure.ReservationBlocked;
            return null;
        }

        private int[] GoalDistances(Profile profile, bool[] targets)
        {
            var distances = Enumerable.Repeat(-1, grid.CellCount).ToArray();
            var queue = new Queue<int>();
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                if (targets[cell])
                {
                    distances[cell] = 0;
                    queue.Enqueue(cell);
                }
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (int next in grid.Neighbours(cell))
                {
                    // This is an optimistic terrain heuristic. A future goal claim is not a wall before its arrival;
                    // the safe intervals and swept reservations decide when a cell can actually be traversed.
                    if (distances[next] < 0 && profile.Stand[next] && grid.CanTraverse(cell, next, profile.Radius))
                    {
                        distances[next] = distances[cell] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            return distances;
        }

        private List<SafeInterval> Intervals(int cell, int owner, Dictionary<int, List<SafeInterval>> cache)
        {
            if (cache.TryGetValue(cell, out var existing))
            {
                return existing;
            }

            var result = new List<SafeInterval>();
            long cursor = CurrentTick;
            if (reservations.TryGetValue(cell, out var list))
            {
                var ordered = list.Where(value => value.Owner != owner && value.End > CurrentTick).OrderBy(value => value.Start).ThenBy(value => value.End);
                foreach (var value in ordered)
                {
                    if (value.Start > cursor)
                    {
                        result.Add(new SafeInterval { Start = cursor, End = value.Start });
                    }

                    cursor = Math.Max(cursor, value.End);
                    if (cursor == Forever)
                    {
                        break;
                    }
                }
            }

            if (cursor < Forever)
            {
                result.Add(new SafeInterval { Start = cursor, End = Forever });
            }

            cache.Add(cell, result);
            return result;
        }

        private static List<TimedCell> Reconstruct(SearchNode end)
        {
            var result = new List<TimedCell>();
            for (var node = end; node != null; node = node.Parent)
            {
                result.Add(new TimedCell { Cell = node.Cell, Arrival = node.Arrival, Departure = node.Departure });
            }

            result.Reverse();
            return result;
        }

        private static long NodeKey(int cell, int interval) => ((long)cell << 32) | (uint)interval;
    }
}
