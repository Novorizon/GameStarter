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
        int? timeout = int.TryParse(Argument(args, "--max-ticks"), out int parsedTicks) ? parsedTicks : null;
        int[] seeds = Array.IndexOf(args, "--stress") >= 0 ? new[] { 1, 17, 733, 20261110 } : new[] { 1 };
        if (int.TryParse(Argument(args, "--seed"), out int seed))
        {
            seeds = new[] { seed };
        }

        string domain = Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(value => value.Key == "NavigationDomainRoot").Value!);
        var hashes = Hashes(domain);
        var results = new List<ChallengeResult>();
        var timer = Stopwatch.StartNew();
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { success = false, state = "running", domain }, JsonOptions));
        GeometryChecks.VerifyOracle();
        foreach (int currentSeed in seeds)
        {
            foreach (var challenge in ChallengeCatalog.Create(currentSeed))
            {
                if (filter != null && !challenge.Name.Contains(filter, StringComparison.Ordinal))
                {
                    continue;
                }

                var result = ChallengeRunner.Execute(challenge, currentSeed, timeout);
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

        bool unchanged = Matches(domain, hashes);
        var summary = new
        {
            utc = DateTime.UtcNow.ToString("O"),
            domain,
            scope = "Strict queue-preserving challenges; an admission rejection never completes a feasible request.",
            sourceHashes = hashes,
            sourceFilesUnchanged = unchanged,
            elapsedMilliseconds = timer.ElapsedMilliseconds,
            count = results.Count,
            passed = results.Count(value => value.Success),
            failed = results.Count(value => !value.Success),
            success = results.Count > 0 && results.All(value => value.Success) && unchanged,
            results = results.Select(value => new
            {
                value.Name, value.Seed, value.Success, value.Outcome, value.Error, value.Stage, value.Requested,
                value.Admitted, value.Active, value.Pending, value.ExplicitDeaths, value.Arrived, value.Attacking,
                value.Waiting, value.CompletedRecoveryPhases, value.PlannedRecoveryPhases, value.Ticks,
                value.ElapsedMilliseconds, value.ExpandedNodes, value.Replans, value.QueueConserved, value.ContinuousOracleVerified
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

    private static Dictionary<string, string> Hashes(string domain)
    {
        return Directory.GetFiles(Path.Combine(domain, "Navigation"), "*.cs", SearchOption.AllDirectories)
            .Concat(new[] { Path.Combine(domain, "Configuration.cs") }).OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
    }

    private static bool Matches(string domain, Dictionary<string, string> original)
    {
        var current = Hashes(domain);
        return current.Count == original.Count && original.All(file => current.TryGetValue(file.Key, out string? value) && value == file.Value);
    }
}
