namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public enum NavigationAgentStatus
    {
        Navigating,
        Waiting,
        Attacking
    }

    public enum NavigationFailure
    {
        None,
        InvalidSpawn,
        NoStaticPath,
        NoAttackPositions,
        SpawnOccupied,
        /// <summary>Current spawn time has no route through fixed finite reservations; retry outside the map.</summary>
        ReservationBlocked,
        /// <summary>No admissible target under current committed goals and access policy, not a physical-capacity proof.</summary>
        CapacityExceeded,
        GoalAccessBlocked,
        PlanningHorizonExceeded,
        SearchLimitExceeded
    }

    public sealed class NavigationAgent
    {
        public int Id { get; internal set; }
        public Position2 Position { get; internal set; }
        public NavigationAgentStatus Status { get; internal set; }
        public int GoalCell { get; internal set; }
        public int Radius { get; internal set; }
        public int AttackRange { get; internal set; }
        public int Speed { get; internal set; }
        public long ArrivalTick { get; internal set; }
        public int DistanceTravelled { get; internal set; }
        public int RemainingDistance { get; internal set; }
    }
}
