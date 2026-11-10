using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

string output = Argument("--output") ?? "Artifacts/NavigationPerformance";
string? compare = Argument("--compare");
string? native = Argument("--native-csv");
int runs = int.TryParse(Argument("--runs"), out int parsedRuns) ? parsedRuns : 1;
if (runs < 1 || runs > 10)
{
    throw new ArgumentOutOfRangeException(nameof(runs), "Use 1..10 fresh crowd runs.");
}

string project = Path.GetFullPath(Metadata("VerificationProject"));
string domain = Path.GetFullPath(Metadata("CandidateDomain"));
string map = Path.GetFullPath(Argument("--map") ?? Path.Combine(project, "../../Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
var sourceHashes = HashFiles(Directory.GetFiles(domain, "*.cs", SearchOption.AllDirectories));
var testHashes = HashFiles(Directory.GetFiles(project, "*.cs").Concat(new[]
{
    Path.Combine(project, "NavigationPerformance.csproj"),
    Path.Combine(project, "../../Assets/HotUpdate/TowerDefense/Data/NavigationLayout.cs")
}));
string mapHash = Digest(map);
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "summary.json"), "{\"success\":false,\"state\":\"running\"}");
var geometryContracts = GeometryOracle.VerifyContracts();
bool contractsOnly = Array.IndexOf(args, "--contracts-only") >= 0;
var results = new List<PerformanceResult>();
for (int run = 0; run < (contractsOnly ? 0 : runs); run++)
{
    string directory = runs == 1 ? output : Path.Combine(output, "run-" + (run + 1));
    var result = PerformanceRunner.Execute(map, directory);
    if (compare != null)
    {
        string baseline = runs == 1 ? compare : Path.Combine(compare, "run-" + (run + 1));
        result.Comparison = Comparison.Verify(baseline, directory);
    }

    if (native != null)
    {
        result.NativeComparison = Comparison.VerifyNative(native, Path.Combine(directory, "trajectory.csv"));
    }

    results.Add(result);
    File.WriteAllText(Path.Combine(directory, "result.json"), JsonSerializer.Serialize(result, JsonOptions.Value));
    Console.WriteLine("PASS run=" + (run + 1) + " final=" + result.FinalTick + " allArrived=" + result.AllArrivedTick
        + " expanded=" + result.TotalExpandedNodes + " allocated=" + result.Measurements.Sum(row => row.ApiAndStepAllocatedBytes));
}

bool sourceUnchanged = SameHashes(sourceHashes, HashFiles(sourceHashes.Keys));
bool testUnchanged = SameHashes(testHashes, HashFiles(testHashes.Keys));
if (!sourceUnchanged || !testUnchanged || mapHash != Digest(map))
{
    throw new InvalidOperationException("Candidate source, test source or map changed during verification.");
}

File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    success = true,
    utc = DateTime.UtcNow.ToString("O"),
    scope = "Pure C# actual Forest input. Timed scopes exclude tracing and independent geometry. Dotnet timing is not Unity Mono/GPU timing.",
    domain,
    map,
    sourceHashes,
    testHashes,
    sourceFilesUnchanged = sourceUnchanged,
    testSourceFilesUnchanged = testUnchanged,
    mapSha256 = mapHash,
    binarySha256 = Digest(Assembly.GetExecutingAssembly().Location),
    runs = contractsOnly ? 0 : runs,
    geometryContracts,
    results
}, JsonOptions.Value));

string? Argument(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string Metadata(string name) => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == name).Value!;

static Dictionary<string, string> HashFiles(IEnumerable<string> files) => files.Select(Path.GetFullPath)
    .OrderBy(path => path, StringComparer.Ordinal).ToDictionary(path => path, Digest);

static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

static bool SameHashes(Dictionary<string, string> first, Dictionary<string, string> second) => first.Count == second.Count
    && first.All(pair => second.TryGetValue(pair.Key, out string? value) && value == pair.Value);

internal static class JsonOptions
{
    internal static readonly JsonSerializerOptions Value = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
}
