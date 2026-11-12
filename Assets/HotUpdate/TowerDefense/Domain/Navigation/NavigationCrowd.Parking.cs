using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        // Grow a connected circulation network and prefer neighbouring endpoints outside it. This guides goal
        // ordering; it does not replace reservation/access checks or prove arbitrary multi-agent completeness.
        // A later fallback may use a backbone cell and therefore does not retain the endpoint certificate.
        private void PrepareParking()
        {
            var registered = profiles.Values.Where(value => value.Registered).ToArray();
            var candidate = new bool[grid.CellCount];
            var attack = new bool[grid.CellCount];
            var backbone = new bool[grid.CellCount];
            var covered = new bool[grid.CellCount];
            var queued = new bool[grid.CellCount];
            int radius = registered.Max(value => value.Radius);
            var adjacent = new int[grid.CellCount][];
            var serves = new int[grid.CellCount][];
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                candidate[cell] = !entries.Contains(cell) && registered.Any(value => value.Attack[cell] || value.Gather[cell]);
                attack[cell] = registered.Any(value => value.Attack[cell]);
                adjacent[cell] = grid.Neighbours(cell).Where(next => grid.CanTraverse(cell, next, radius)).ToArray();
            }

            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                serves[cell] = grid.Neighbours(cell).Where(next => candidate[next] && registered.Any(value => (value.Attack[next] || value.Gather[next])
                    && grid.CanTraverse(cell, next, value.Radius))).ToArray();
            }

            var frontier = new MinHeap();
            int Benefit(int cell) => serves[cell].Count(next => !covered[next]);
            long Score(int cell, int benefit) => (attack[cell] ? 8 : 0) - benefit;
            void Push(int cell, int benefit)
            {
                frontier.Push(new SearchNode
                {
                    Cell = cell,
                    Estimate = Score(cell, benefit),
                    Arrival = NavigationGrid.DistanceSquared(grid.Center(cell), grid.TargetCenter)
                });
            }

            void Add(int first)
            {
                var queue = new Queue<int>();
                backbone[first] = true;
                queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    int cell = queue.Dequeue();
                    covered[cell] = true;
                    foreach (int next in serves[cell])
                    {
                        covered[next] = true;
                    }

                    foreach (int next in adjacent[cell])
                    {
                        if (backbone[next])
                        {
                            continue;
                        }

                        if (!candidate[next])
                        {
                            backbone[next] = true;
                            queue.Enqueue(next);
                        }
                        else if (!queued[next])
                        {
                            queued[next] = true;
                            int benefit = Benefit(next);
                            if (benefit > 0)
                            {
                                Push(next, benefit);
                            }
                        }
                    }
                }
            }

            foreach (int entry in entries.OrderBy(value => value))
            {
                if (!backbone[entry] && grid.CanOccupy(entry, radius))
                {
                    Add(entry);
                }
            }

            while (frontier.Count > 0)
            {
                var node = frontier.Pop();
                if (backbone[node.Cell])
                {
                    continue;
                }

                int benefit = Benefit(node.Cell);
                if (benefit == 0)
                {
                    continue;
                }

                // Coverage only increases, so queued priorities are optimistic and can be refreshed lazily.
                if (node.Estimate != Score(node.Cell, benefit))
                {
                    Push(node.Cell, benefit);
                    continue;
                }

                Add(node.Cell);
            }

            foreach (var profile in registered)
            {
                profile.Preferred = new bool[grid.CellCount];
                for (int cell = 0; cell < grid.CellCount; cell++)
                {
                    profile.Preferred[cell] = !backbone[cell] && grid.Neighbours(cell)
                        .Any(next => backbone[next] && grid.CanTraverse(cell, next, profile.Radius));
                }
            }

            parkingPrepared = true;
        }
    }
}
