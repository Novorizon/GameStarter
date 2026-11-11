using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;

const int width = 35;
const int height = 29;
var walkable = Enumerable.Range(0, width * height).Select(cell => cell % width > 0 && cell % width < width - 1
    && cell / width > 0 && cell / width < height - 1).ToArray();
var originalTarget = new TargetDefinition(1, new Point2(27500, 14500), 1000);
var relocatedTarget = new TargetDefinition(1, new Point2(16500, 14500), 1000);
var world = new WorldDefinition(width, height, 1000, default, walkable, walkable, new[] { 1 + 14 * width }, new[] { originalTarget }, 7000);
var next = world.WithChange(new WorldChange { ReplaceTarget = relocatedTarget });
var profile = new UnitProfile(250, 333, 1000);
double relocationDistance = Point2.Distance(originalTarget.Center, relocatedTarget.Center);
double oldCenterBound = originalTarget.RadiusMm + world.GatheringDistanceMm + SimulationProtocol.GoalToleranceMm;
double guaranteedNewBaseBodyClearance = relocationDistance - oldCenterBound - relocatedTarget.RadiusMm - profile.RadiusMm;
double maximumNewAttackDistance = relocatedTarget.RadiusMm + profile.RadiusMm + profile.AttackRangeMm;
double guaranteedNewAttackGoalBodyClearance = relocationDistance - maximumNewAttackDistance - oldCenterBound - 2 * profile.RadiusMm;
Require(relocationDistance == 11000 && guaranteedNewBaseBodyClearance > 0 && guaranteedNewAttackGoalBodyClearance > 0,
    "Relocation safety bound failed.");
var goals = Enumerable.Range(0, next.CellCount).Where(cell => next.HoldingAllowed(cell)
    && ContinuousOracle.CircleFits(next, next.Center(cell), profile.RadiusMm)
    && ContinuousOracle.CanAttack(next, next.Center(cell), profile, relocatedTarget)).ToArray();
Require(goals.Length == 16, "New target lacks exactly16 independently valid attack centres.");
var seen = new HashSet<int> { next.SpawnCells[0] };
var queue = new Queue<int>();
queue.Enqueue(next.SpawnCells[0]);
while (queue.Count > 0)
{
    int current = queue.Dequeue();
    foreach (int neighbour in next.Neighbours(current))
    {
        if (!seen.Contains(neighbour) && ContinuousOracle.SegmentFits(next, next.Center(current), next.Center(neighbour), profile.RadiusMm))
        {
            seen.Add(neighbour);
            queue.Enqueue(neighbour);
        }
    }
}
Require(goals.All(seen.Contains), "A new attack centre is statically disconnected from the original entry.");
double minimumGoalGap = goals.SelectMany((first, i) => goals.Skip(i + 1).Select(second =>
    Point2.Distance(next.Center(first), next.Center(second)) - 2 * profile.RadiusMm)).Min();
Require(minimumGoalGap >= 0, "New target goal bodies overlap.");
var sampleAttacker = originalTarget.Center + new Point2(-2000, 0);
Require(ContinuousOracle.CircleFits(world, sampleAttacker, profile.RadiusMm)
    && ContinuousOracle.CanAttack(world, sampleAttacker, profile, originalTarget), "Sweep-bound example is not an original legal attacker.");
bool continuousSweepHits = ContinuousOracle.DistanceToSegment(sampleAttacker, originalTarget.Center, relocatedTarget.Center)
    < originalTarget.RadiusMm + profile.RadiusMm;
Require(continuousSweepHits, "Expected relocation-vs-continuous-motion distinction disappeared.");
var report = new
{
    success = true,
    scope = "Static geometry and discrete target-relocation safety certificate; "
        + "not a candidate execution, continuous moving-solid test, or joint reconfiguration proof.",
    oldTarget = new { x = originalTarget.Center.X, y = originalTarget.Center.Y, radiusMm = originalTarget.RadiusMm },
    newTarget = new { x = relocatedTarget.Center.X, y = relocatedTarget.Center.Y, radiusMm = relocatedTarget.RadiusMm },
    world = new { width, height, cellMm = 1000, openInterior = new { left = 1000, right = 34000, bottom = 1000, top = 28000 } },
    originalLiveCount = 24,
    profile = new { radiusMm = profile.RadiusMm, speedMmPer50ms = profile.SpeedMmPerTick, rangeMm = profile.AttackRangeMm },
    oldLegalCentreDistanceBoundMm = oldCenterBound,
    relocationDistanceMm = relocationDistance,
    guaranteedNewBaseToOldBodyGapMm = guaranteedNewBaseBodyClearance,
    guaranteedNewGoalDiscToOldBodyGapMm = guaranteedNewAttackGoalBodyClearance,
    newAttackCentres = goals.Select(cell => new { cell, x = next.Center(cell).X, y = next.Center(cell).Y }),
    newAttackCentreCount = goals.Length,
    allNewGoalsReachableFromOriginalEntry = true,
    minimumPairwiseGoalBodyGapMm = minimumGoalGap,
    exampleOriginalAttacker = new { x = sampleAttacker.X, y = sampleAttacker.Y },
    targetContinuousSweepWouldHitExampleAttacker = continuousSweepHits,
    continuousTargetMotionWasNotValidated = true,
    sourceAndOracleHashes = new Dictionary<string, string>
    {
        ["Core"] = FileHash("../random-feasibility/Snapshot/Contracts.cs"),
        ["Oracle"] = FileHash("../random-feasibility/Snapshot/ContinuousOracle.cs"),
        ["Program.cs"] = FileHash("Program.cs")
    }
};
File.WriteAllText("report.json", JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine("PASS relocation11000mm; new16 goals reachable; new-base old-body clearance>=1745mm; new-goal old-body clearance>=245mm.");

static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
