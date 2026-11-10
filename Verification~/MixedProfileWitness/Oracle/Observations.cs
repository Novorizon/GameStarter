using System.Collections.Generic;

internal enum NavigationState
{
    Moving,
    Waiting,
    Attacking,
    Unreachable
}

internal readonly record struct UnitObservation(int Id, Cell Cell, NavigationState State, int SlotId, int MoveTicks, int X, int Y, int Radius);

internal interface INavigationUnderTest
{
    int Tick { get; }
    IReadOnlyList<UnitObservation> Units { get; }
    int AttackSlotCapacity { get; }
    int GatherSlotCapacity { get; }
    string LastFailure { get; }
    long ExpandedNodes { get; }
    int Replans { get; }
    int ReservationCount { get; }
    bool TrySpawn(int id, Cell cell, int moveTicks);
    void Step(int ticks);
    void Remove(int id);
}

internal sealed class ScenarioResult
{
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public int Requested { get; set; }
    public int Spawned { get; set; }
    public int Removed { get; set; }
    public int Attacking { get; set; }
    public int Waiting { get; set; }
    public int Unreachable { get; set; }
    public int Rejected { get; set; }
    public int AttackSlotCapacity { get; set; }
    public int GatherSlotCapacity { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int CellSize { get; set; } = 1000;
    public int TargetRadius { get; set; } = 1000;
    public int UnitRadius { get; set; } = 250;
    public int AttackRange { get; set; } = 1000;
    public int GatheringDistance { get; set; }
    public Cell Target { get; set; }
    public bool[] Walkable { get; set; } = System.Array.Empty<bool>();
    public bool[] HoldingAllowed { get; set; } = System.Array.Empty<bool>();
    public int Ticks { get; set; }
    public int MaximumStationaryMovingTicks { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public long ReplayElapsedMilliseconds { get; set; }
    public long ExpandedNodes { get; set; }
    public int Replans { get; set; }
    public int MaximumReservations { get; set; }
    public int CertifiedParkingCapacity { get; set; }
    public bool CollisionFree { get; set; } = true;
    public bool ObstacleFree { get; set; } = true;
    public bool EdgeSwapFree { get; set; } = true;
    public bool AttackRangeVerified { get; set; } = true;
    public bool BatchEquivalent { get; set; }
    public bool ReplacementVerified { get; set; }
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string FinalState { get; set; } = "";
    public List<TrajectoryFrame> Trajectory { get; } = new List<TrajectoryFrame>();
    public List<NavigationCommand> Commands { get; } = new List<NavigationCommand>();
    public Dictionary<string, int> AdmissionFailures { get; } = new Dictionary<string, int>();
    public List<NavigationProfileResult> Profiles { get; } = new List<NavigationProfileResult>();
}

internal sealed class NavigationCommand
{
    public int Tick { get; set; }
    public string Kind { get; set; } = "";
    public int Id { get; set; }
    public Cell Cell { get; set; }
    public int MoveTicks { get; set; }
    public int Radius { get; set; } = 250;
    public int AttackRange { get; set; } = 1000;
}

internal sealed class NavigationProfileResult
{
    public int Radius { get; set; }
    public int AttackRange { get; set; }
    public int Requested { get; set; }
    public int Attacking { get; set; }
    public int Waiting { get; set; }
}

internal sealed class TrajectoryFrame
{
    public int Tick { get; set; }
    public UnitObservation[] Units { get; set; } = System.Array.Empty<UnitObservation>();
}
