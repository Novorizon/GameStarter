using System;
using System.Collections.Generic;

internal sealed class ChallengeResult
{
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public bool Success { get; set; }
    public string Outcome { get; set; } = "failed";
    public string Error { get; set; } = "";
    public string Stage { get; set; } = "initial-gather";
    public int Requested { get; set; }
    public int Admitted { get; set; }
    public int Active { get; set; }
    public int Pending { get; set; }
    public int ExplicitDeaths { get; set; }
    public int Arrived { get; set; }
    public int EverArrived { get; set; }
    public int Attacking { get; set; }
    public int Waiting { get; set; }
    public int CompletedRecoveryPhases { get; set; }
    public int PlannedRecoveryPhases { get; set; }
    public int Ticks { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public long ExpandedNodes { get; set; }
    public int Replans { get; set; }
    public bool QueueConserved { get; set; }
    public bool ContinuousOracleVerified { get; set; }
    public bool SourceFilesUnchanged { get; set; }
    public CapacityEvidence? CapacityEvidence { get; set; }
    public string RequestPlanSha256 { get; set; } = "";
    public string OfferedFlowSha256 { get; set; } = "";
    public string InputCommandsSha256 { get; set; } = "";
    public string PhysicalCommandsSha256 { get; set; } = "";
    public string PhysicalGeometrySha256 { get; set; } = "";
    public string HoldingMaskSha256 { get; set; } = "";
    public string PrescribedEndpointSetSha256 { get; set; } = "";
    public int[] PrescribedEndpoints { get; set; } = Array.Empty<int>();
    public int FinalPrescribedEndpointCount { get; set; }
    public bool InitialAttackingGoalsMustReturn { get; set; }
    public bool RecoveryActiveAtEnd { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int CellSize { get; set; } = 1000;
    public int TargetRadius { get; set; } = 1000;
    public Cell Target { get; set; }
    public int GatheringDistance { get; set; }
    public bool[] Walkable { get; set; } = Array.Empty<bool>();
    public bool[] HoldingAllowed { get; set; } = Array.Empty<bool>();
    public Dictionary<string, int> AdmissionFailures { get; } = new Dictionary<string, int>();
    public List<TrajectoryFrame> Trajectory { get; } = new List<TrajectoryFrame>();
    public List<ChallengeCommand> Commands { get; } = new List<ChallengeCommand>();
    public List<AgentLifecycle> Agents { get; } = new List<AgentLifecycle>();
    internal Dictionary<int, AgentLifecycle> AgentIndex { get; } = new Dictionary<int, AgentLifecycle>();
    public List<int> InitialInputPriority { get; } = new List<int>();
    public Dictionary<int, int> InitialGroupGoals { get; } = new Dictionary<int, int>();
}

internal sealed class AgentLifecycle
{
    public int Id { get; set; }
    public int Radius { get; set; }
    public int Range { get; set; }
    public int Speed { get; set; }
    public int CreatedTick { get; set; }
    public int? BornTick { get; set; }
    public int? FirstAssignedGoalArrivalTick { get; set; }
    public int? FirstAttackTick { get; set; }
    public int? ExplicitDeathTick { get; set; }
    public bool DiedWhileNavigating { get; set; }
    public bool DiedBeforeAnyGoal => ExplicitDeathTick.HasValue && !FirstAssignedGoalArrivalTick.HasValue;
    public string FinalStatus { get; set; } = "queued";
    public int? FinalX { get; set; }
    public int? FinalY { get; set; }
    public int? FinalGoalCell { get; set; }
}

internal sealed class ChallengeCommand
{
    public int Tick { get; set; }
    public string Kind { get; set; } = "";
    public string Role { get; set; } = "regular";
    public int Id { get; set; }
    public Cell Spawn { get; set; }
    public int Radius { get; set; }
    public int Range { get; set; }
    public int Speed { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}
