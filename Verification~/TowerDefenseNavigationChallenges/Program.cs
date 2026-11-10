using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int Main(string[] args)
    {
        string output = Argument(args, "--output") ?? "Artifacts/NavigationChallenges";
        string? filter = Argument(args, "--scenario");
        bool allowCompatibleGoalReassignment = Array.IndexOf(args, "--allow-compatible-goal-reassignment") >= 0;
        int? timeout = int.TryParse(Argument(args, "--max-ticks"), out int parsedTicks) ? parsedTicks : null;
        int[] seeds = Array.IndexOf(args, "--stress") >= 0 ? new[] { 1, 17, 733, 20261110 } : new[] { 1 };
        if (int.TryParse(Argument(args, "--seed"), out int seed))
        {
            seeds = new[] { seed };
        }

        string domain = Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(value => value.Key == "NavigationDomainRoot").Value!);
        var hashes = Hashes(domain);
        var testHashes = TestHashes();
        var results = new List<ChallengeResult>();
        var timer = Stopwatch.StartNew();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { success = false, state = "running", domain }, JsonOptions));
        GeometryChecks.VerifyOracle();
        foreach (int currentSeed in seeds)
        {
            foreach (var challenge in ChallengeCatalog.Create(currentSeed, allowCompatibleGoalReassignment))
            {
                if (filter != null && !challenge.Name.Contains(filter, StringComparison.Ordinal))
                {
                    continue;
                }

                var result = ChallengeRunner.Execute(challenge, currentSeed, timeout);
                HashInputs(challenge, result);
                result.SourceFilesUnchanged = Matches(domain, hashes);
                if (!result.SourceFilesUnchanged)
                {
                    result.Success = false;
                    result.Error += " Candidate source changed during verification.";
                }

                results.Add(result);
                File.WriteAllText(Path.Combine(output, challenge.Name + "-" + currentSeed + ".json"), JsonSerializer.Serialize(result, JsonOptions));
                Console.WriteLine((result.Success ? "PASS " : "FAIL ") + challenge.Name + " seed=" + currentSeed + " requested=" + result.Requested
                    + " admitted=" + result.Admitted + " active=" + result.Active + " pending=" + result.Pending + " deaths=" + result.ExplicitDeaths
                    + " arrived=" + result.Arrived + " ticks=" + result.Ticks + (result.Success ? "" : " " + result.Error));
            }
        }

        bool testsUnchanged = DictionariesMatch(testHashes, TestHashes());
        bool unchanged = Matches(domain, hashes) && testsUnchanged;
        var summary = new
        {
            utc = DateTime.UtcNow.ToString("O"),
            domain,
            allowCompatibleGoalReassignment,
            scope = "Strict queue-preserving challenges; an admission rejection never completes a feasible request.",
            sourceHashes = hashes,
            testSourceHashes = testHashes,
            testBinarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))).ToLowerInvariant(),
            testSourceFilesUnchanged = testsUnchanged,
            sourceFilesUnchanged = unchanged,
            elapsedMilliseconds = timer.ElapsedMilliseconds,
            count = results.Count,
            passed = results.Count(value => value.Success),
            failed = results.Count(value => !value.Success),
            success = results.Count > 0 && results.All(value => value.Success) && unchanged,
            results = results.Select(value => new
            {
                value.Name, value.Seed, value.Success, value.Outcome, value.Error, value.Stage, value.Requested,
                value.Admitted, value.Active, value.Pending, value.ExplicitDeaths, value.Arrived, value.EverArrived, value.Attacking,
                value.Waiting, value.CompletedRecoveryPhases, value.PlannedRecoveryPhases, value.Ticks,
                value.ElapsedMilliseconds, value.ExpandedNodes, value.Replans, value.QueueConserved, value.ContinuousOracleVerified,
                value.RequestPlanSha256, value.OfferedFlowSha256, value.InputCommandsSha256, value.PhysicalCommandsSha256,
                value.PhysicalGeometrySha256, value.HoldingMaskSha256, value.PrescribedEndpointSetSha256, value.FinalPrescribedEndpointCount,
                value.InitialAttackingGoalsMustReturn
            })
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, JsonOptions));
        return summary.success ? 0 : 1;
    }

    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void HashInputs(Challenge challenge, ChallengeResult result)
    {
        var geometry = challenge.Geometry;
        result.PhysicalGeometrySha256 = Digest(new
        {
            geometry.Width, geometry.Height, geometry.Target, geometry.Walkable, geometry.SpawnCells, geometry.GatheringDistance
        });
        result.HoldingMaskSha256 = Digest(geometry.HoldingAllowed ?? geometry.Walkable);
        result.PrescribedEndpointSetSha256 = Digest(result.PrescribedEndpoints.Select(cell => new
        {
            x = cell % geometry.Width * 1000 + 500,
            y = cell / geometry.Width * 1000 + 500
        }));
        result.RequestPlanSha256 = Digest(new { requests = challenge.Orders, priority = result.InitialInputPriority });
        var offered = challenge.Orders.GroupBy(order => (order.Group, order.ReleaseTick, order.Spawn.X, order.Spawn.Y, order.Radius, order.Range, order.Speed))
            .OrderBy(group => group.Key.Group).ThenBy(group => group.Key.ReleaseTick).ThenBy(group => group.Key.X).ThenBy(group => group.Key.Y)
            .ThenBy(group => group.Key.Radius).ThenBy(group => group.Key.Range).ThenBy(group => group.Key.Speed)
            .Select(group => new
            {
                group.Key.Group, group.Key.ReleaseTick, group.Key.X, group.Key.Y,
                group.Key.Radius, group.Key.Range, group.Key.Speed, count = group.Count()
            });
        result.OfferedFlowSha256 = Digest(offered);
        result.InputCommandsSha256 = Digest(result.Commands);
        result.PhysicalCommandsSha256 = Digest(result.Commands.Select(command => new
        {
            command.Tick, command.Kind, command.Role, command.Spawn,
            command.Radius, command.Range, command.Speed, command.X, command.Y
        }));
    }

    private static string Digest<T>(T value)
    {
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions))).ToLowerInvariant();
    }

    private static Dictionary<string, string> Hashes(string domain)
    {
        return Directory.GetFiles(Path.Combine(domain, "Navigation"), "*.cs", SearchOption.AllDirectories)
            .Concat(new[] { Path.Combine(domain, "Configuration.cs") }).OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    private static bool Matches(string domain, Dictionary<string, string> original)
    {
        return DictionariesMatch(original, Hashes(domain));
    }

    private static Dictionary<string, string> TestHashes()
    {
        string root = Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(value => value.Key == "ChallengeProjectRoot").Value!);
        return Directory.GetFiles(root, "*.cs").Concat(new[]
        {
            Path.GetFullPath(Path.Combine(root, "../TowerDefenseNavigation/GeometryChecks.cs")),
            Path.GetFullPath(Path.Combine(root, "../TowerDefenseNavigation/Observations.cs"))
        }).OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    private static bool DictionariesMatch(Dictionary<string, string> original, Dictionary<string, string> current)
    {
        return current.Count == original.Count && original.All(file => current.TryGetValue(file.Key, out string? value) && value == file.Value);
    }
}
