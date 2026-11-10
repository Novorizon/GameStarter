using System;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal static class GridContracts
{
    public static void Verify()
    {
        bool[] open = Enumerable.Repeat(true, 25).ToArray();
        var offset = new NavigationGrid(5, 5, 500, new Position2(0, 0), open, new Position2(1500, 1350), 200);
        int left = 2 * 5 + 2;
        int right = left + 1;
        Require(offset.CanOccupy(left, 50) && offset.CanOccupy(right, 50), "Offset-base counterexample endpoints must be legal.");
        Require(!offset.CanTraverse(left, right, 50), "A legal endpoint pair must not permit a segment through an offset circular base.");
        Require(!offset.CanTraverse(left, left + 6, 50), "Diagonal traversal cuts a four-neighbor grid corner.");
        Require(offset.CellAt(new Position2(-1, 0)) == -1, "Negative map coordinates must remain outside the grid.");
        Require(offset.CellAt(new Position2(2500, 0)) == -1, "The upper map boundary is exclusive.");

        bool[] blocked = (bool[])open.Clone();
        blocked[1] = false;
        var occluded = new NavigationGrid(5, 5, 500, new Position2(0, 0), blocked, new Position2(2000, 2000), 100);
        Require(!occluded.HasLine(new Position2(250, 250), new Position2(1250, 1250)), "A line crossing a blocked corner must be occluded.");
        blocked[1] = true;
        Require(!occluded.IsWalkable(1), "Caller mutation must not change an accepted geometry mask.");

        var crowd = new NavigationCrowd(offset);
        bool rejectedTick = false;
        try
        {
            crowd.Step(2);
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectedTick = true;
        }

        Require(rejectedTick && crowd.CurrentTick == 0, "Skipped ticks must be rejected without mutating simulation time.");
        Require(!crowd.Remove(404) && crowd.Agents.Count == 0, "Removing an absent agent must preserve the crowd.");

        var declared = new NavigationGrid(
            31,
            31,
            1000,
            new Position2(0, 0),
            Enumerable.Repeat(true, 31 * 31).ToArray(),
            new Position2(23500, 15500),
            1000,
            spawnCells: new[] { 15 * 31 + 1 });
        var configured = new NavigationCrowd(declared);
        configured.RegisterProfile(250, 1000);
        Require(configured.TrySpawn(1, new Position2(1500, 15500), 1000, 250, 1000), "The declared profile and entrance must admit an initial agent.");
        RejectLateConfiguration(() => configured.RegisterProfile(250, 4000), "A new profile was silently registered after admission.");
        RejectLateConfiguration(() => configured.TrySpawn(2, new Position2(1500, 15500), 1000, 250, 4000), "An unregistered profile bypassed protection.");
        RejectLateConfiguration(() => configured.TrySpawn(2, new Position2(1500, 16500), 1000, 250, 1000), "An undeclared entrance bypassed protection.");
        Require(configured.Agents.Count == 1 && configured.CurrentTick == 0, "Rejected configuration mutated admitted agents or simulation time.");
    }

    private static void RejectLateConfiguration(Action action, string failure)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(failure);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
