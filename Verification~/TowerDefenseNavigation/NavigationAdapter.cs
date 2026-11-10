using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed class NavigationAdapter : INavigationUnderTest
{
    private readonly NavigationCrowd crowd;
    private readonly NavigationScenario scenario;
    private readonly Dictionary<int, int> movement = new Dictionary<int, int>();

    public int Tick { get; private set; }
    public int AttackSlotCapacity => crowd.CountAttackSlots(scenario.UnitRadius, 1000);
    public int GatherSlotCapacity => crowd.CountGatherSlots(scenario.UnitRadius, 1000);
    public string LastFailure => crowd.LastFailure.ToString();
    public long ExpandedNodes => crowd.TotalExpandedNodes;
    public int Replans => crowd.ReplanCount;
    public int ReservationCount => crowd.ReservationCount;

    public IReadOnlyList<UnitObservation> Units => crowd.Agents.Select(agent => new UnitObservation(
        agent.Id,
        new Cell((int)Math.Floor(agent.Position.X / 1000d), (int)Math.Floor(agent.Position.Y / 1000d)),
        agent.Status == NavigationAgentStatus.Attacking ? NavigationState.Attacking
            : agent.Status == NavigationAgentStatus.Waiting ? NavigationState.Waiting : NavigationState.Moving,
        agent.GoalCell,
        movement[agent.Id],
        agent.Position.X,
        agent.Position.Y,
        agent.Radius)).ToArray();

    public NavigationAdapter(NavigationScenario scenario)
    {
        this.scenario = scenario;
        var grid = new NavigationGrid(
            scenario.Width,
            scenario.Height,
            1000,
            new Position2(0, 0),
            scenario.Walkable,
            Center(scenario.Target),
            1000,
            scenario.HoldingAllowed,
            spawnCells: scenario.SpawnCells.Select(cell => cell.Y * scenario.Width + cell.X).ToArray());
        crowd = new NavigationCrowd(grid, gatheringDistance: scenario.GatheringDistance);
        crowd.RegisterProfile(scenario.UnitRadius, 1000);
    }

    public bool TrySpawn(int id, Cell cell, int moveTicks)
    {
        if (!crowd.TrySpawn(id, Center(cell), 1000 / moveTicks, scenario.UnitRadius, 1000))
        {
            return false;
        }

        movement.Add(id, moveTicks);
        return true;
    }

    public void Step(int ticks)
    {
        for (int index = 0; index < ticks; index++)
        {
            crowd.Step(++Tick);
        }
    }

    public void Remove(int id)
    {
        if (!crowd.Remove(id))
        {
            throw new InvalidOperationException("Cannot remove absent unit " + id + " from " + scenario.Name);
        }

        movement.Remove(id);
    }

    private static Position2 Center(Cell cell) => new Position2(cell.X * 1000 + 500, cell.Y * 1000 + 500);
}
