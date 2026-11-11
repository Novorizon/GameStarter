using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;

var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
var assembly = typeof(NavigationLab.DAoe4.Aoe4Factory).Assembly;
var geometryType = assembly.GetType("NavigationLab.DAoe4.GridGeometry", true)!;
var plannerType = assembly.GetType("NavigationLab.DAoe4.HierarchicalFlow", true)!;
var bodyType = assembly.GetType("NavigationLab.DAoe4.PlanningBody", true)!;
var tests = new List<object>();
void Require(bool value, string error)
{
    if (!value)
    {
        throw new Exception(error);
    }
}
object? Get(object value, string field) => value.GetType().GetField(field, flags)!.GetValue(value);
WorldDefinition World(int width, int height, int[] closed, TargetDefinition? target = null)
{
    var walk = Enumerable.Repeat(true, width * height).ToArray();
    foreach (int cell in closed)
    {
        walk[cell] = false;
    }
    return new WorldDefinition(width, height, 1000, default, walk, walk, new[] { 0 }, new[]
    {
        target ?? new TargetDefinition(1, new Point2((width - .5) * 1000, (height - .5) * 1000), 250)
    });
}
object Geometry(WorldDefinition world, params (int Id, Point2 Point, int Radius)[] bodyData)
{
    var bodies = Array.CreateInstance(bodyType, bodyData.Length);
    for (int i = 0; i < bodyData.Length; i++)
    {
        bodies.SetValue(Activator.CreateInstance(bodyType, flags, null, new object[] { bodyData[i].Id, bodyData[i].Point, bodyData[i].Radius }, null), i);
    }
    return Activator.CreateInstance(geometryType, flags, null, new object[] { world, bodies }, null)!;
}
bool Edge(object geometry, int from, int to, int radius) => (bool)geometryType.GetMethod("CanEdge", flags)!.Invoke(geometry, new object[] { from, to, radius })!;

var cornerWorld = World(4, 4, new[] { 1 });
var cornerGeometry = Geometry(cornerWorld);
Require(!Edge(cornerGeometry, 0, 5, 250), "Diagonal cut through a closed rectangle corner was accepted.");
tests.Add(new { name = "closed-corner-diagonal-rejected", passed = true });
var baseWorld = World(4, 4, Array.Empty<int>(), new TargetDefinition(1, new Point2(1000, 1000), 300));
Require(!Edge(Geometry(baseWorld), 0, 5, 100), "Clear endpoints permitted a diagonal chord through the base.");
tests.Add(new { name = "base-chord-diagonal-rejected", passed = true });

var circleWorld = World(5, 5, Array.Empty<int>());
var circleGeometry = Geometry(circleWorld, (1, new Point2(2500, 1500), 250), (2, new Point2(1500, 2500), 250));
Require(Edge(circleGeometry, 6, 12, 250), "A genuinely clear diagonal between occupied orthogonal centres was rejected.");
Require(Math.Sqrt(500000) - 500 > 207, "Independent circle gap arithmetic failed.");
tests.Add(new { name = "clear-diagonal-between-real-circles-accepted", passed = true, analyticMinimumGapMm = Math.Sqrt(500000) - 500 });
var largeWorld = World(6, 6, new[] { 9 });
Require(Edge(Geometry(largeWorld), 7, 14, 700), "Large body clear diagonal with 7.1mm corner margin rejected.");
var largeBlockedWorld = World(6, 6, new[] { 8 });
Require(!Edge(Geometry(largeBlockedWorld), 7, 14, 700), "Large body cut through an obstacle corner.");
tests.Add(new { name = "large-radius-diagonal-margin-and-penetration", passed = true, radiusMm = 700 });

var wall = Enumerable.Range(0, 21).Where(y => y != 4).Select(y => y * 24 + 10).ToArray();
var routeWorld = World(24, 24, wall);
var routeGeometry = Geometry(routeWorld);
var metrics = new Dictionary<string, long>();
var planner = Activator.CreateInstance(plannerType, flags, null, new object[] { routeGeometry, metrics, 250 }, null)!;
int start = 12 * 24 + 1;
int goal = 12 * 24 + 20;
var route = plannerType.GetMethod("FindRoute", flags)!.Invoke(planner, new object[] { start, goal, 250 });
Require(route != null && ((int[])Get(route, "Tiles")!).Length >= 3, "Cross-tile HPA route missing.");
var graph = plannerType.GetMethod("Graph", flags)!.Invoke(planner, new object[] { 250 })!;
var cells = (int[])Get(graph, "Cells")!;
var edges = (Array)Get(graph, "Edges")!;
int checkedEdges = 0;
int nonintegerEdges = 0;
for (int from = 0; from < cells.Length; from++)
{
    foreach (object edge in (IEnumerable)edges.GetValue(from)!)
    {
        int to = (int)Get(edge, "Node")!;
        double cost = (double)Get(edge, "Cost")!;
        double straight = Point2.Distance(routeWorld.Center(cells[from]), routeWorld.Center(cells[to]));
        double firstHeuristic = Point2.Distance(routeWorld.Center(cells[from]), routeWorld.Center(goal));
        double secondHeuristic = Point2.Distance(routeWorld.Center(cells[to]), routeWorld.Center(goal));
        Require(cost + 1e-6 >= straight && cost > 0, "Abstract edge underprices the real metric.");
        Require(firstHeuristic <= cost + secondHeuristic + 1e-6, "Euclidean heuristic inconsistent with an abstract edge.");
        checkedEdges++;
        if (Math.Abs(cost / 1000 - Math.Round(cost / 1000)) > 1e-6)
        {
            nonintegerEdges++;
        }
    }
}
Require(checkedEdges > 0 && nonintegerEdges > 0, "No actual diagonal-weighted abstract edges observed.");
object?[] directionArguments = { routeWorld.Center(start), route, null };
var direction = (Point2)plannerType.GetMethod("Direction", flags)!.Invoke(planner, directionArguments)!;
var field = directionArguments[2]!;
Require(direction.LengthSquared > .5, "Reachable source got zero steering.");
Require(metrics.GetValueOrDefault("HpaSearches") > 0 && metrics.GetValueOrDefault("SegmentFlowBuilds") > 0
    && metrics.GetValueOrDefault("FmmAcceptedCells") > 0 && metrics.GetValueOrDefault("DiagonalFlowRelaxations") > 0,
    "The intended HPA/FMM/diagonal mechanisms were not executed.");
Require((bool)geometryType.GetMethod("CanSegment", flags)!.Invoke(routeGeometry, new object[]
{
    routeWorld.Center(start), routeWorld.Center(start) + direction * 200, 250d, -1
})!, "Returned local flow probe penetrated geometry.");
tests.Add(new { name = "actual-hpa-metric-and-heuristic-consistency", passed = true, checkedEdges, nonintegerEdges });
tests.Add(new { name = "actual-segment-flow-fmm-and-diagonal-relaxation-executed", passed = true, metrics });

var flowType = plannerType.GetNestedType("Flow", flags)!;
var eikonalFlow = Activator.CreateInstance(flowType)!;
var distances = Enumerable.Repeat(double.PositiveInfinity, routeWorld.CellCount).ToArray();
var accepted = new bool[routeWorld.CellCount];
int middle = 2 * 24 + 2;
foreach (int next in new[] { middle - 1, middle + 24 })
{
    distances[next] = 0;
    accepted[next] = true;
}
flowType.GetField("Distance", flags)!.SetValue(eikonalFlow, distances);
double estimate = (double)plannerType.GetMethod("Eikonal", flags)!.Invoke(planner, new object[] { eikonalFlow, accepted, middle, 250 })!;
Require(Math.Abs(estimate - 1000 / Math.Sqrt(2)) < 1e-6, "The four-neighbour Eikonal update was replaced or changed.");
tests.Add(new { name = "four-neighbour-eikonal-quadratic-retained", passed = true, expected = 1000 / Math.Sqrt(2), actual = estimate });
Console.WriteLine(JsonSerializer.Serialize(new
{
    scope = "Read-only development R10 geometry/route queries; no full benchmark, fresh replay or production acceptance claim.",
    candidateAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
    coreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Point2).Assembly.Location))).ToLowerInvariant(),
    tests
}, new JsonSerializerOptions { WriteIndented = true }));
