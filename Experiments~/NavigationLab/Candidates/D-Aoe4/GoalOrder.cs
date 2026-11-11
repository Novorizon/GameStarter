using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal sealed partial class AoeNavigator
    {
        private readonly Dictionary<string, double[]> goalDepths = new Dictionary<string, double[]>();

        private double[] GoalDepths(int radius, int start)
        {
            string key = radius + ":" + start;
            if (goalDepths.TryGetValue(key, out var cached))
            {
                Add("StaticGoalDepthCacheHits");
                return cached;
            }
            var distance = Enumerable.Repeat(-1.0, World.CellCount).ToArray();
            goalDepths.Add(key, distance);
            Add("StaticGoalDepthBuilds");
            if (start < 0 || start >= World.CellCount || !geometry.Clearance(radius)[start])
            {
                return distance;
            }
            var open = new MinHeap();
            distance[start] = 0;
            open.Push(start, 0);
            while (open.TryPop(out int cell, out double cost))
            {
                if (cost > distance[cell] + 1e-7)
                {
                    continue;
                }
                Add("StaticGoalDepthCellsExpanded");
                foreach (int next in geometry.Neighbours(cell))
                {
                    if (!geometry.CanEdge(cell, next, radius))
                    {
                        continue;
                    }
                    double candidate = cost + Point2.Distance(World.Center(cell), World.Center(next));
                    if (distance[next] < 0 || candidate < distance[next] - 1e-7)
                    {
                        distance[next] = candidate;
                        open.Push(next, candidate);
                    }
                }
            }
            return distance;
        }
    }
}
