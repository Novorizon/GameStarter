using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed record TickMeasurement(
    int Tick,
    int Active,
    int Pending,
    long Expanded,
    long ApiAndStepAllocatedBytes,
    long StepAllocatedBytes,
    long ApiAndStepStopwatchTicks,
    long StepStopwatchTicks,
    int GenerationZeroCollections,
    int GenerationOneCollections,
    int GenerationTwoCollections);

internal sealed class PerformanceResult
{
    public bool Success { get; set; }
    public int FinalTick { get; set; }
    public int AllArrivedTick { get; set; }
    public int BigSettledTick { get; set; }
    public long TotalExpandedNodes { get; set; }
    public long StopwatchFrequency { get; set; } = Stopwatch.Frequency;
    public bool ApiPositionsUnchanged { get; set; }
    public bool ContinuousOracleVerified { get; set; }
    public long CheckedBodySegments { get; set; }
    public long CheckedPairs { get; set; }
    public string TrajectorySha256 { get; set; } = "";
    public string CommandsSha256 { get; set; } = "";
    public string CountersSha256 { get; set; } = "";
    public List<TickMeasurement> Measurements { get; set; } = new List<TickMeasurement>();
    public object? Comparison { get; set; }
    public object? NativeComparison { get; set; }
    public object? FocusedMetrics { get; set; }
    public object? OptionalInstrumentation { get; set; }
}

internal static class PerformanceRunner
{
    internal static PerformanceResult Execute(string map, string output)
    {
        Directory.CreateDirectory(output);
        var grid = NavigationLayout.Parse(File.ReadAllText(map));
        var crowd = new NavigationCrowd(grid);
        // Match the real table's enemy-ID order: Small(2), then Big(4).
        crowd.RegisterProfile(180, 450);
        crowd.RegisterProfile(250, 550);
        var expected = new Dictionary<int, Request>();
        var previous = new Dictionary<int, Position2>();
        var oldPositions = new Dictionary<int, Position2>();
        var bigClaims = new Dictionary<int, int>();
        var oracle = new GeometryOracle(grid);
        var properties = typeof(NavigationCrowd).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(int) || property.PropertyType == typeof(long)
                || property.PropertyType == typeof(bool) || property.PropertyType.IsEnum)
            .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
        var result = new PerformanceResult();
        using var trajectory = Writer(Path.Combine(output, "trajectory.csv"));
        using var commands = Writer(Path.Combine(output, "commands.csv"));
        using var counters = Writer(Path.Combine(output, "counters.csv"));
        trajectory.WriteLine("tick,id,xMm,yMm,radiusMm,state,goalCell,speedMm,attackRangeMm,arrivalTick,remainingDistance,distanceTravelled");
        commands.WriteLine("tick,group,id,speedMm,radiusMm,attackRangeMm,accepted,failure");
        counters.WriteLine("tick," + string.Join(",", properties.Select(property => property.Name)));
        int big = 0;
        int small = 0;
        int next = 1;
        long previousExpanded = 0;
        for (int tick = 1; tick <= 5428; tick++)
        {
            long allAllocated = 0;
            long allTime = 0;
            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            if (big < 44 && tick - 1 >= big)
            {
                if (Birth("big", new Request(next, 55, 250, 550), tick))
                {
                    big++;
                    next++;
                }
            }

            if (small < 2 && tick - 1 >= 4000 + small)
            {
                Require(result.BigSettledTick > 0 && result.BigSettledTick < 4000, "44 Big were not truly settled before the late input.");
                if (Birth("small", new Request(next, 165, 180, 450), tick))
                {
                    small++;
                    next++;
                }
            }

            previous.Clear();
            foreach (var actor in crowd.Agents)
            {
                previous.Add(actor.Id, actor.Position);
            }

            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            long timeStart = Stopwatch.GetTimestamp();
            crowd.Step(tick);
            long stepTime = Stopwatch.GetTimestamp() - timeStart;
            long stepAllocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            allAllocated += stepAllocated;
            allTime += stepTime;
            var measurement = new TickMeasurement(
                tick,
                crowd.Agents.Count,
                46 - big - small,
                crowd.TotalExpandedNodes - previousExpanded,
                allAllocated,
                stepAllocated,
                allTime,
                stepTime,
                GC.CollectionCount(0) - gc0,
                GC.CollectionCount(1) - gc1,
                GC.CollectionCount(2) - gc2);
            previousExpanded = crowd.TotalExpandedNodes;
            result.Measurements.Add(measurement);
            oracle.Verify(crowd, expected, previous, true);
            foreach (var actor in crowd.Agents)
            {
                trajectory.WriteLine(string.Join(
                    ",",
                    new object[]
                    {
                        tick, actor.Id, actor.Position.X, actor.Position.Y, actor.Radius, actor.Status, actor.GoalCell,
                        actor.Speed, actor.AttackRange, actor.ArrivalTick, actor.RemainingDistance, actor.DistanceTravelled
                    }));
            }

            counters.Write(tick.ToString(CultureInfo.InvariantCulture));
            foreach (var property in properties)
            {
                counters.Write(',');
                counters.Write(Convert.ToString(property.GetValue(crowd), CultureInfo.InvariantCulture));
            }

            counters.WriteLine();
            bool allSettled = crowd.Agents.All(actor => actor.Status != NavigationAgentStatus.Navigating);
            if (big == 44 && small == 0 && allSettled && result.BigSettledTick == 0)
            {
                Require(crowd.Agents.Count(actor => actor.Status == NavigationAgentStatus.Attacking) == 18, "Initial Big attack share was not 18.");
                result.BigSettledTick = tick;
                foreach (var actor in crowd.Agents)
                {
                    bigClaims.Add(actor.Id, actor.GoalCell);
                }
            }

            if (big == 44 && small == 2 && allSettled && result.AllArrivedTick == 0)
            {
                result.AllArrivedTick = tick;
            }

            bool Birth(string group, Request request, int current)
            {
                oldPositions.Clear();
                foreach (var actor in crowd.Agents)
                {
                    oldPositions.Add(actor.Id, actor.Position);
                }

                long oldTick = crowd.CurrentTick;
                long beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
                long beforeTime = Stopwatch.GetTimestamp();
                bool admitted = crowd.TrySpawn(request.Id, grid.Center(grid.SpawnCells[0]), request.Speed, request.Radius, request.Range);
                allTime += Stopwatch.GetTimestamp() - beforeTime;
                allAllocated += GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
                Require(crowd.CurrentTick == oldTick, "TrySpawn changed the clock.");
                foreach (var actor in crowd.Agents)
                {
                    if (oldPositions.TryGetValue(actor.Id, out var before))
                    {
                        Require(Same(before, actor.Position), "TrySpawn moved an existing body.");
                    }
                }

                if (admitted)
                {
                    Require(expected.TryAdd(request.Id, request), "An ID was admitted twice.");
                    Require(Same(crowd.Agents.Single(actor => actor.Id == request.Id).Position, grid.Center(grid.SpawnCells[0])), "Birth skipped the source.");
                }
                else
                {
                    Require(crowd.Agents.All(actor => actor.Id != request.Id), "A rejected request created a hidden actor.");
                }

                oracle.Verify(crowd, expected, oldPositions, false);
                commands.WriteLine(string.Join(",", current, group, request.Id, request.Speed, request.Radius, request.Range, admitted, crowd.LastFailure));
                return admitted;
            }
        }

        Require(big == 44 && small == 2 && crowd.Agents.Count == 46, "The original 46 requests were not all admitted.");
        Require(result.AllArrivedTick == 5228, "The exact B7 full-arrival tick changed.");
        Require(crowd.Agents.Count(actor => actor.Status == NavigationAgentStatus.Attacking) == 20, "Final attack share changed.");
        Require(crowd.Agents.Count(actor => actor.Status == NavigationAgentStatus.Waiting) == 26, "Final waiting share changed.");
        foreach (var claim in bigClaims)
        {
            Require(crowd.Agents.Single(actor => actor.Id == claim.Key).GoalCell == claim.Value, "A Big did not return to its original final goal.");
        }

        Require(
            crowd.Agents.Where(actor => expected[actor.Id].Radius == 180).Select(actor => actor.GoalCell)
                .OrderBy(cell => cell).SequenceEqual(new[] { 256, 257 }),
            "Small bodies did not occupy the original two Small-only goals.");
        trajectory.Flush();
        commands.Flush();
        counters.Flush();
        result.Success = true;
        result.FinalTick = (int)crowd.CurrentTick;
        result.TotalExpandedNodes = crowd.TotalExpandedNodes;
        result.ApiPositionsUnchanged = true;
        result.ContinuousOracleVerified = true;
        result.CheckedBodySegments = oracle.CheckedBodySegments;
        result.CheckedPairs = oracle.CheckedPairs;
        result.TrajectorySha256 = Digest(Path.Combine(output, "trajectory.csv"));
        result.CommandsSha256 = Digest(Path.Combine(output, "commands.csv"));
        result.CountersSha256 = Digest(Path.Combine(output, "counters.csv"));
        result.FocusedMetrics = Summarize(result.Measurements.Where(row => row.Tick >= 1400 && row.Tick < 2100).ToArray());
        File.WriteAllText(Path.Combine(output, "measurements.csv"), MeasurementsCsv(result.Measurements));
        result.OptionalInstrumentation = ReadOptionalInstrumentation();
        return result;
    }

    private static object Summarize(TickMeasurement[] rows)
    {
        var allocations = rows.Select(row => row.ApiAndStepAllocatedBytes).OrderBy(value => value).ToArray();
        var times = rows.Select(row => row.ApiAndStepStopwatchTicks * 1000d / Stopwatch.Frequency).OrderBy(value => value).ToArray();
        return new
        {
            firstTick = rows[0].Tick,
            lastTick = rows[^1].Tick,
            ticks = rows.Length,
            zeroExpandedTicks = rows.Count(row => row.Expanded == 0),
            allocatedBytes = allocations.Sum(),
            meanAllocatedBytes = allocations.Average(),
            maximumAllocatedBytes = allocations[^1],
            medianMilliseconds = times[times.Length / 2],
            p95Milliseconds = times[(int)((times.Length - 1) * .95)],
            maximumMilliseconds = times[^1]
        };
    }

    private static object? ReadOptionalInstrumentation()
    {
        var type = typeof(NavigationCrowd).Assembly.GetType("Game.HotUpdate.TowerDefense.Domain.Navigation.NavAuditMetrics");
        if (type == null)
        {
            return null;
        }

        return type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(long[]))
            .ToDictionary(field => field.Name, field => field.GetValue(null));
    }

    private static string MeasurementsCsv(IEnumerable<TickMeasurement> rows)
    {
        const string header = "tick,active,pending,expanded,apiAndStepAllocatedBytes,stepAllocatedBytes,"
            + "apiAndStepStopwatchTicks,stepStopwatchTicks,gc0,gc1,gc2";
        var csv = new StringBuilder(header + "\n");
        foreach (var row in rows)
        {
            csv.AppendLine(string.Join(
                ",",
                new object[]
                {
                    row.Tick, row.Active, row.Pending, row.Expanded, row.ApiAndStepAllocatedBytes, row.StepAllocatedBytes,
                    row.ApiAndStepStopwatchTicks, row.StepStopwatchTicks, row.GenerationZeroCollections,
                    row.GenerationOneCollections, row.GenerationTwoCollections
                }));
        }

        return csv.ToString();
    }

    private static StreamWriter Writer(string path) => new StreamWriter(path, false, new UTF8Encoding(false));
    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool Same(Position2 first, Position2 second) => first.X == second.X && first.Y == second.Y;

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal sealed record Request(int Id, int Speed, int Radius, int Range);
