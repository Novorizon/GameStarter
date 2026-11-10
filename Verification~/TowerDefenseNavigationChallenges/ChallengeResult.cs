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
}
