using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;
using Cached = NavigationLab.DAoe4;
using Original = NavigationLab.DAoe4Original;

long comparisons = 0;
int passes = 0;
int[] radii = { 0, 180, 250, 490, 650, 1000 };
var edgeField = typeof(Cached.GridGeometry).GetField("edgeClearance", BindingFlags.Instance | BindingFlags.NonPublic)!;
var checks = new List<object>();
foreach (var size in new[] { (width: 9, height: 9), (width: 17, height: 13) })
{
    int width = size.width;
    int height = size.height;
    var walkable = Enumerable.Range(0, width * height).Select(cell =>
        cell % width > 0 && cell % width < width - 1 && cell / width > 0 && cell / width < height - 1).ToArray();
    walkable[width * 3 + 3] = false;
    walkable[width * 3 + 4] = false;
    walkable[width * 5 + 5] = false;
    var holding = Enumerable.Repeat(true, width * height).ToArray();
    var world = new WorldDefinition(
        width,
        height,
        1000,
        new Point2(-137, 211),
        walkable,
        holding,
        new[] { width + 1 },
        new[] { new TargetDefinition(1, new Point2(width * 650, height * 550), 350) },
        10000);
    var bodies = new[]
    {
        new Cached.PlanningBody(1, world.Center(width * 4 + 4), 250),
        new Cached.PlanningBody(2, world.Center(width * 5 + 3), 650),
        new Cached.PlanningBody(3, world.Center(width * 2 + 5) + new Point2(250, -125), 180)
    };
    var originalBodies = bodies.Select(body => new Original.PlanningBody(body.Id, body.Position, body.Radius)).ToArray();
    var candidate = new Cached.GridGeometry(world, bodies);
    var reference = new Original.GridGeometry(world, originalBodies);
    var independent = new Cached.GridGeometry(world);
    var independentReference = new Original.GridGeometry(world);
    // Input arrays and body structs are copied by both implementations.
    walkable[width + 2] = false;
    holding[width + 2] = false;
    bodies[0] = new Cached.PlanningBody(1, world.Center(width * 2 + 2), 1000);
    originalBodies[0] = new Original.PlanningBody(1, world.Center(width * 2 + 2), 1000);
    Compare(candidate, reference, world, "cold");
    long entriesBefore = CachedEntries(candidate);
    Compare(candidate, reference, world, "warm");
    Require(CachedEntries(candidate) == entriesBefore && entriesBefore > 0, "Warm queries changed populated cache count");
    Compare(independent, independentReference, world, "independent");
    long independentEntries = CachedEntries(independent);
    var block = new WorldChange { BlockCells = new[] { width * 2 + 2, width * 2 + 3 } };
    candidate.Apply(block);
    reference.Apply(block);
    Require(CachedEntries(candidate) == 0, "Apply did not clear cached edges");
    Compare(candidate, reference, candidate.World, "after-block");
    var openAndTarget = new WorldChange
    {
        OpenCells = new[] { width * 2 + 2, width * 2 + 3, width * 3 + 3 },
        ReplaceTarget = new TargetDefinition(1, world.Center(width * 2 + 2), 500)
    };
    candidate.Apply(openAndTarget);
    reference.Apply(openAndTarget);
    Require(CachedEntries(candidate) == 0, "Target/open change did not clear cached edges");
    Compare(candidate, reference, candidate.World, "after-open-and-target");
    Compare(candidate, reference, candidate.World, "after-open-and-target-warm");
    Require(CachedEntries(independent) == independentEntries, "Other-instance Apply touched independent cache");
    Compare(independent, independentReference, world, "independent-after-other-apply");
    Require(candidate.Revision == reference.Revision && candidate.Revision == 2, "Revision mismatch");
    checks.Add(new { width, height, cachedEntries = CachedEntries(candidate), independentEntries });
}
var output = new
{
    success = true,
    scope = "Directed integer-cell edge memoization equivalence against preserved pre-cache source. No navigation simulation or universal proof.",
    comparisons,
    passes,
    radii,
    checks,
    sourceSha256 = new Dictionary<string, string>
    {
        ["Program.cs"] = Hash(Path.Combine(AppContext.BaseDirectory, "../../../Program.cs")),
        ["OriginalGridGeometry.cs"] = Hash(Path.Combine(AppContext.BaseDirectory, "../../../OriginalGridGeometry.cs")),
        ["CandidateGridGeometry.cs"] = Hash(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../Experiments~/NavigationLab/Candidates/D-Aoe4/GridGeometry.cs"))
    }
};
string json = JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
if (args.Length == 1)
{
    File.WriteAllText(args[0], json + "\n");
}
Console.WriteLine(json);

void Compare(Cached.GridGeometry candidate, Original.GridGeometry reference, WorldDefinition world, string label)
{
    foreach (int radius in radii)
    {
        for (int from = -1; from <= world.CellCount; from++)
        {
            foreach (int to in Queries(from, world))
            {
                bool expected = reference.CanEdge(from, to, radius);
                bool actual = candidate.CanEdge(from, to, radius);
                Require(actual == expected, $"{label}: from={from},to={to},radius={radius},actual={actual},expected={expected}");
                comparisons++;
            }
        }
        Require(candidate.CanEdge(int.MaxValue, int.MinValue, radius) == reference.CanEdge(int.MaxValue, int.MinValue, radius),
            "Extreme invalid-index mismatch");
    }
    passes++;
}

static IEnumerable<int> Queries(int from, WorldDefinition world)
{
    if (world.CellCount <= 100)
    {
        return Enumerable.Range(-1, world.CellCount + 2);
    }
    var queries = new HashSet<int> { -1, world.CellCount, from, 0, world.CellCount - 1, world.CellCount / 2, from + 1 };
    if (from >= 0 && from < world.CellCount)
    {
        int x = from % world.Width;
        int y = from / world.Width;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (x + dx >= 0 && x + dx < world.Width && y + dy >= 0 && y + dy < world.Height)
                {
                    queries.Add((y + dy) * world.Width + x + dx);
                }
            }
        }
    }
    return queries;
}

long CachedEntries(Cached.GridGeometry geometry)
{
    return ((Dictionary<int, byte[]>)edgeField.GetValue(geometry)!).Values.Sum(entries => entries.LongCount(value => value != 0));
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string Hash(string path)
{
    return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.GetFullPath(path)))).ToLowerInvariant();
}
