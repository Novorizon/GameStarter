using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;

var source = "/workspace/navigation-comparison/results/d-v2-stress";
using var input = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source, "inputs/open-arena.json")));
using var result = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(source, "open-arena.json")));
var w = input.RootElement.GetProperty("world");
var world = new WorldDefinition(
    w.GetProperty("width").GetInt32(),
    w.GetProperty("height").GetInt32(),
    w.GetProperty("cellSizeMm").GetInt32(),
    new Point2(w.GetProperty("origin").GetProperty("x").GetDouble(), w.GetProperty("origin").GetProperty("y").GetDouble()),
    w.GetProperty("walkable").GetString()!.Select(value => value == '1').ToArray(),
    w.GetProperty("holding").GetString()!.Select(value => value == '1').ToArray(),
    w.GetProperty("spawnCells").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
    w.GetProperty("targets").EnumerateArray().Select(t => new TargetDefinition(t.GetProperty("id").GetInt32(), new Point2(t.GetProperty("x").GetDouble(), t.GetProperty("y").GetDouble()), t.GetProperty("radiusMm").GetInt32())).ToArray(),
    w.GetProperty("gatheringDistanceMm").GetInt32());
var units = result.RootElement.GetProperty("FinalState").GetString()!.Split('|').Select(text =>
{
    var fields = text.Split(':');
    var xy = fields[1].Split(',');
    return new { Id = int.Parse(fields[0]), X = double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture), Y = double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture), State = int.Parse(fields[2]), Goal = int.Parse(fields[3]) };
}).ToArray();
var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
var assembly = typeof(NavigationLab.DAoe4.Aoe4Factory).Assembly;
var bodyType = assembly.GetType("NavigationLab.DAoe4.PlanningBody", true)!;
var fixedBodies = units.Where(unit => unit.State != 0).ToArray();
var bodies = Array.CreateInstance(bodyType, fixedBodies.Length);
for (int i = 0; i < fixedBodies.Length; i++)
{
    var unit = fixedBodies[i];
    bodies.SetValue(Activator.CreateInstance(bodyType, flags, null, new object[] { unit.Id, new Point2(unit.X, unit.Y), 250 }, null), i);
}
var geoType = assembly.GetType("NavigationLab.DAoe4.GridGeometry", true)!;
var plannerType = assembly.GetType("NavigationLab.DAoe4.HierarchicalFlow", true)!;
var geometry = Activator.CreateInstance(geoType, flags, null, new object[] { world, bodies }, null)!;
var metrics = new Dictionary<string, long>();
var planner = Activator.CreateInstance(plannerType, flags, null, new object[] { geometry, metrics, 250 }, null)!;
var subject = units.Single(unit => unit.Id == 3);
var position = new Point2(subject.X, subject.Y);
int start = world.CellAt(position);
var clearance = (bool[])geoType.GetMethod("Clearance", flags)!.Invoke(geometry, new object[] { 250 })!;
bool Segment(Point2 from, Point2 to) => (bool)geoType.GetMethod("CanSegment", flags)!.Invoke(geometry, new object[] { from, to, 250d, -1 })!;
int anchor = clearance[start] ? start : Enumerable.Range(0, world.CellCount)
    .Where(cell => clearance[cell] && Segment(position, world.Center(cell)))
    .OrderBy(cell => Point2.Distance(position, world.Center(cell))).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
bool reconnecting = anchor >= 0 && anchor != start;
var route = plannerType.GetMethod("FindRoute", flags)!.Invoke(planner, new object[] { anchor, subject.Goal, 250 });
object? Get(object instance, string name) => instance.GetType().GetField(name, flags)!.GetValue(instance);
var argsForDirection = new object?[] { position, route, null };
Point2 direction = route == null ? default : (Point2)plannerType.GetMethod("Direction", flags)!.Invoke(planner, argsForDirection)!;
var field = argsForDirection[2];
bool directSegment = (bool)geoType.GetMethod("CanSegment", flags)!.Invoke(
    geometry,
    new object[] { position, world.Center(subject.Goal), 250d, -1 })!;
var output = new
{
    scope = "Pure planner diagnostic reconstructed from the reported R8 final state. No candidate pass, replay, full internal-state restore or trajectory assertion.",
    candidateAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))).ToLowerInvariant(),
    coreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(Point2).Assembly.Location))).ToLowerInvariant(),
    actorId = 3, start, anchor, reconnecting, physicalCellCenterValid = clearance[start], goal = subject.Goal,
    directSegmentAcceptedByPlanningGeometry = directSegment,
    oldRecoveryGuardWouldRun = !reconnecting && direction.LengthSquared < 1e-12 && directSegment,
    withoutReconnectGuardWouldRun = direction.LengthSquared < 1e-12 && directSegment, fixedBodies = fixedBodies.Select(unit => unit.Id).ToArray(),
    route = route == null ? null : new { tiles = Get(route, "Tiles"), components = Get(route, "Components"), exits = Get(route, "Exits"), currentIndex = Get(route, "CurrentIndex") },
    direction = new[] { direction.X, direction.Y },
    field = field == null ? null : new
    {
        exit = Get(field, "Exit"),
        sourceIncluded = ((bool[])Get(field, "Included")!)[start],
        sourceDistance = ((double[])Get(field, "Distance")!)[start].ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        goalIncluded = ((bool[])Get(field, "Included")!)[subject.Goal],
        goalDistance = ((double[])Get(field, "Distance")!)[subject.Goal].ToString("R", System.Globalization.CultureInfo.InvariantCulture)
    },
    metrics
};
Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
