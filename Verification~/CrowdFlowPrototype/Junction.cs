using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GameStarter.Verification.CrowdFlow;

internal sealed class Junction(Vector2 center, float halfLength, IReadOnlyDictionary<int, Vector2> headings)
{
    private readonly Queue<int> queue = new();
    private readonly HashSet<int> waiting = new();
    private int? owner;

    internal void Schedule(IReadOnlyList<CrowdAgent> agents)
    {
        if (owner is int id && !agents.Any(agent => agent.Id == id && Progress(id, agent.Position) <= halfLength))
        {
            owner = null;
        }

        foreach (var agent in agents)
        {
            float progress = Progress(agent.Id, agent.Position);
            if (progress >= -halfLength - 0.35f && progress <= -halfLength && owner != agent.Id && waiting.Add(agent.Id))
            {
                queue.Enqueue(agent.Id);
            }
        }

        if (owner == null && queue.TryPeek(out int first))
        {
            var next = agents.Single(agent => agent.Id == first);
            Vector2 exit = center + headings[first] * halfLength;
            if (!agents.Any(agent => agent.Id != first && Progress(first, agent.Position) > 0
                && Vector2.Distance(agent.Position, exit) < next.Radius + agent.Radius))
            {
                owner = queue.Dequeue();
                waiting.Remove(first);
            }
        }
    }

    internal bool CanAdvance(CrowdAgent agent, Vector2 to)
    {
        return owner == agent.Id || Progress(agent.Id, agent.Position) > halfLength || Progress(agent.Id, to) <= -halfLength;
    }

    private float Progress(int id, Vector2 point) => Vector2.Dot(headings[id], point - center);
}
