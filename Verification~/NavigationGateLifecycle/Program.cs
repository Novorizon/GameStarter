using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--quota") >= 0)
        {
            return QuotaGateLifecycle.Run(args);
        }

        if (Array.IndexOf(args, "--legacy") < 0)
        {
            return ForestGateLifecycle.Run(args);
        }

        string output = Argument(args, "--output") ?? "Artifacts/NavigationGateLifecycle";
        string? seedText = Argument(args, "--seed");
        int[] seeds = seedText == null ? new[] { 1, 17, 733, 20261110 } : new[] { int.Parse(seedText) };
        string? roleFilter = Argument(args, "--role");
        string[] roles = roleFilter == null ? new[] { "gate", "requester" } : new[] { roleFilter };
        var assembly = Assembly.GetExecutingAssembly();
        string domain = Path.GetFullPath(Metadata("NavigationDomainRoot"));
        string project = Path.GetFullPath(Metadata("AuditProjectRoot"));
        var sourceHashes = DomainHashes(domain);
        var testHashes = TestHashes(project);
        GeometryChecks.VerifyOracle();
        Directory.CreateDirectory(output);
        var results = new List<AuditResult>();
        foreach (string role in roles)
        {
            if (role != "gate" && role != "requester")
            {
                throw new ArgumentException("--role must be gate or requester.");
            }
            foreach (int seed in seeds)
            {
                var result = GateLifecycleAudit.Execute(role, seed);
                File.WriteAllText(Path.Combine(output, role + "-" + seed + ".json"), JsonSerializer.Serialize(result, Json));
                results.Add(result);
                Console.WriteLine($"{(result.Success ? "PASS" : "FAIL")} {role} seed={seed} requested={result.Requested} admitted={result.Admitted}"
                    + $" active={result.Active} pending={result.Pending} deaths={result.ExplicitDeaths} attack={result.Attacking}"
                    + $" gateMidflightInjected={result.Injection != null} ticks={result.Ticks} {result.Error}");
            }
        }
        bool sourceUnchanged = SameHashes(sourceHashes, DomainHashes(domain));
        bool testUnchanged = SameHashes(testHashes, TestHashes(project));
        var summary = new
        {
            utc = DateTime.UtcNow.ToString("O"),
            scope = "Four request orders; gate owner and passage actor die at a real non-cell-centre point while the actual GateActors job is active."
                + " Remove is followed by same-profile birth at the original source, then another all-short-range death/refill and 200-tick settled hold."
                + " This is a .NET model audit; it does not establish Unity rendering, IL2CPP or device behavior.",
            domain,
            sourceHashes,
            testHashes,
            testBinarySha256 = FileDigest(assembly.Location),
            sourceFilesUnchanged = sourceUnchanged,
            testSourceFilesUnchanged = testUnchanged,
            count = results.Count,
            passed = results.Count(result => result.Success),
            success = results.Count > 0 && results.All(result => result.Success) && sourceUnchanged && testUnchanged,
            results = results.Select(result => new
            {
                result.Role, result.Seed, result.Success, result.Error, result.Injection, result.Requested,
                result.Admitted, result.Active, result.Pending, result.ExplicitDeaths, result.Attacking, result.Ticks,
                result.ElapsedMilliseconds, result.QueueConserved, result.ContinuousOracleVerified, result.ApiPositionsUnchanged,
                result.RequestPlanSha256, result.InputCommandsSha256, result.PhysicalCommandsSha256
            })
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Json));
        return summary.success ? 0 : 1;

        string Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == key).Value!;
    }

    public static string Digest<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value, Json))).ToLowerInvariant();
    private static string FileDigest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Dictionary<string, string> DomainHashes(string domain)
    {
        return Directory.GetFiles(Path.Combine(domain, "Navigation"), "*.cs", SearchOption.AllDirectories)
            .Concat(new[] { Path.Combine(domain, "Configuration.cs") }).OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, FileDigest);
    }

    private static Dictionary<string, string> TestHashes(string project)
    {
        return Directory.GetFiles(project, "*.cs").Concat(new[]
        {
            Path.GetFullPath(Path.Combine(project, "../TowerDefenseNavigationChallenges/Challenges.cs")),
            Path.GetFullPath(Path.Combine(project, "../TowerDefenseNavigationChallenges/GeometryTypes.cs")),
            Path.GetFullPath(Path.Combine(project, "../TowerDefenseNavigation/GeometryChecks.cs")),
            Path.GetFullPath(Path.Combine(project, "../TowerDefenseNavigation/Observations.cs"))
        }).OrderBy(path => path, StringComparer.Ordinal).ToDictionary(path => path, FileDigest);
    }

    private static bool SameHashes(Dictionary<string, string> first, Dictionary<string, string> second)
    {
        return first.Count == second.Count && first.All(entry => second.TryGetValue(entry.Key, out string? hash) && hash == entry.Value);
    }
}
