using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.DAoe4;

internal static class Program
{
    private const int Width = 24;
    private const int Height = 16;
    private const int Start = 2 + 4 * Width;
    private const int Goal = 20 + 10 * Width;
    private static readonly List<string> Passed = new List<string>();

    private static void Main()
    {
        CheckPositiveIsolation();
        CheckNegativeAndDirectedInputs();
        CheckRadiusAndInstances();
        CheckInvalidationAndWorldRevision();
        CheckRevisionGuard();
        CheckExceptionAndCapacity();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scope = "Independent cache contracts; no common49 navigation simulation or performance acceptance.",
            passed = Passed.Count,
            cases = Passed,
            candidateAssemblySha256 = Hash(typeof(Aoe4ProgressExchangeFactory).Assembly.Location),
            coreAssemblySha256 = Hash(typeof(Point2).Assembly.Location)
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CheckPositiveIsolation()
    {
        var world = MakeWorld(false);
        var metrics = new Dictionary<string, long>();
        var planner = NewPlanner(new GridGeometry(world), metrics);
        var control = NewPlanner(new GridGeometry(world));
        var first = planner.FindRoute(Start, Goal, 250) ?? throw new InvalidOperationException("Positive route required.");
        string expected = Plan(control.FindRoute(Start, Goal, 250));
        Require(Plan(first) == expected, "First positive result differs from fresh planner.");
        var second = planner.FindRoute(Start, Goal, 250)!;
        Require(!ReferenceEquals(first, second), "Positive hit shared the Route instance.");
        IndependentArrays(first, second);
        FreshState(second);
        Require(Plan(second) == expected, "Positive hit changed route plan.");
        planner.Direction(world.Center(Start), first, out _);
        Require(first.LastTile >= 0 && first.LastFlow != null, "Direction did not mutate the first runtime cache.");
        var third = planner.FindRoute(Start, Goal, 250)!;
        IndependentArrays(second, third);
        Require(Plan(third) == expected, "Direction mutation leaked into next route.");
        FreshState(third);
        first.Tiles[0] = -123;
        first.Components[0] = -123;
        first.Exits[0] = -123;
        first.RemainingAfterExits[0] = -123;
        second.Goal = -99;
        second.StartCell = -99;
        second.TotalCost = -99;
        second.ProgressKey = "modified";
        second.Radius = -99;
        second.Revision = -99;
        second.CurrentIndex = 999;
        second.LastTile = 999;
        second.LastSegmentIndex = 999;
        second.Tiles[0] = -456;
        second.Components[0] = -456;
        second.Exits[0] = -456;
        second.RemainingAfterExits[0] = -456;
        var fourth = planner.FindRoute(Start, Goal, 250)!;
        Require(Plan(fourth) == expected, "Returned fields/arrays mutated the cache template.");
        FreshState(fourth);
        Require(metrics["HpaSearches"] == 4 && metrics["RouteCacheHits"] == 3, "Per-call/cache metrics incorrect.");
        Passed.Add("Positive plan equals fresh control; returned objects/four arrays/runtime state are independent.");
    }

    private static void CheckNegativeAndDirectedInputs()
    {
        var world = MakeWorld(true);
        var metrics = new Dictionary<string, long>();
        var planner = NewPlanner(new GridGeometry(world), metrics);
        Require(planner.FindRoute(Start, Goal, 250) == null, "Closed wall should have no route.");
        Require(planner.FindRoute(Start, Goal, 250) == null, "Negative hit should stay null.");
        Require(metrics["HpaSearches"] == 2 && metrics["RouteCacheHits"] == 1, "Negative result was not cached.");
        var open = MakeWorld(false);
        var directed = NewPlanner(new GridGeometry(open));
        var forward = directed.FindRoute(Start, Goal, 250)!;
        var reverse = directed.FindRoute(Goal, Start, 250)!;
        Require(forward.StartCell == Start && reverse.StartCell == Goal, "Directed start was conflated.");
        Require(forward.Goal == Goal && reverse.Goal == Start, "Directed goal was conflated.");
        Require(Plan(reverse) == Plan(NewPlanner(new GridGeometry(open)).FindRoute(Goal, Start, 250)), "Reverse control differs.");
        Passed.Add("Repeated normal null is cached; directed queries preserve distinct exact routes.");
    }

    private static void CheckRadiusAndInstances()
    {
        var world = MakeWorld(false);
        var planner = NewPlanner(new GridGeometry(world));
        foreach (int radius in new[] { 250, 350, 600 })
        {
            var actual = planner.FindRoute(Start, Goal, radius);
            var expected = NewPlanner(new GridGeometry(world)).FindRoute(Start, Goal, radius);
            Require(Plan(actual) == Plan(expected), "Radius query differs from fresh geometry.");
            if (actual != null)
            {
                Require(actual.Radius == radius, "Radius cache keys were conflated.");
            }
        }
        var firstMetrics = new Dictionary<string, long>();
        var secondMetrics = new Dictionary<string, long>();
        var first = NewPlanner(new GridGeometry(world), firstMetrics);
        var second = NewPlanner(new GridGeometry(world), secondMetrics);
        first.FindRoute(Start, Goal, 250);
        first.FindRoute(Start, Goal, 250);
        second.FindRoute(Start, Goal, 250);
        Require(firstMetrics.GetValueOrDefault("RouteCacheHits") == 1, "First instance cache missing.");
        Require(secondMetrics.GetValueOrDefault("RouteCacheHits") == 0, "Cache state crossed instances.");
        Passed.Add("Radius inputs and independent planner caches remain distinct.");
    }

    private static void CheckInvalidationAndWorldRevision()
    {
        var world = MakeWorld(true);
        var geometry = new GridGeometry(world);
        var metrics = new Dictionary<string, long>();
        var planner = NewPlanner(geometry, metrics);
        Require(planner.FindRoute(Start, Goal, 250) == null, "Initial closed-wall route should be null.");
        planner.Invalidate();
        Require(CacheCount(planner) == 0, "Invalidate did not clear memo entries.");
        planner.FindRoute(Start, Goal, 250);
        Require(metrics.GetValueOrDefault("RouteCacheHits") == 0, "Invalidate reused an old null.");
        var open = new WorldChange { OpenCells = new[] { 10 + 7 * Width, 10 + 8 * Width } };
        geometry.Apply(open);
        planner.Invalidate();
        var after = planner.FindRoute(Start, Goal, 250);
        Require(after != null, "World opening retained stale negative result.");
        var controlGeometry = new GridGeometry(world);
        controlGeometry.Apply(open);
        Require(Plan(after) == Plan(NewPlanner(controlGeometry).FindRoute(Start, Goal, 250)), "Opened world control differs.");
        var close = new WorldChange { BlockCells = open.OpenCells };
        geometry.Apply(close);
        planner.Invalidate();
        Require(planner.FindRoute(Start, Goal, 250) == null, "World closure retained stale positive result.");
        var bodyGeometry = new GridGeometry(MakeWorld(false), new[] { new PlanningBody(1, MakeWorld(false).Center(Goal), 250) });
        var replacement = NewPlanner(bodyGeometry);
        Require(replacement.FindRoute(Start, Goal, 250) == null, "New planner reused a route through its fixed body.");
        Passed.Add("Invalidate, world opening/closure and replacement body geometry do not reuse stale routes.");
    }

    private static void CheckRevisionGuard()
    {
        var geometry = new GridGeometry(MakeWorld(false));
        var metrics = new Dictionary<string, long>();
        var planner = NewPlanner(geometry, metrics);
        Require(planner.FindRoute(Start, Goal, 250) != null, "Initial revision route required.");
        geometry.Apply(new WorldChange { BlockCells = new[] { Goal } });
        // This guard-only case never uses stale graph data. General world changes
        // still follow the existing Apply + planner.Invalidate lifecycle above.
        Require(planner.FindRoute(Start, Goal, 250) == null, "Revision change hit a stale positive route.");
        Require(metrics.GetValueOrDefault("RouteCacheHits") == 0 && CacheCount(planner) == 2, "Revision was absent from the memo key.");
        planner.Invalidate();
        Require(CacheCount(planner) == 0, "Invalidate did not clear both revisions.");
        Passed.Add("Revision key independently prevents a stale positive hit in the blocked-goal clearance guard.");
    }

    private static void CheckExceptionAndCapacity()
    {
        var planner = NewPlanner(new GridGeometry(MakeWorld(false)));
        int count = CacheCount(planner);
        for (int repeat = 0; repeat < 2; repeat++)
        {
            bool threw = false;
            try
            {
                planner.FindRoute(Width * Height + 1, Goal, 250);
            }
            catch (IndexOutOfRangeException)
            {
                threw = true;
            }
            Require(threw && CacheCount(planner) == count, "Exceptional search result was memoized.");
        }
        for (int start = -1; start >= -4100; start--)
        {
            Require(planner.FindRoute(start, Goal, 250) == null, "Normal invalid-start null changed.");
        }
        Require(CacheCount(planner) == 4096, "Memo storage is not capped at 4096.");
        Passed.Add("Exceptions leave no entries; normal-result memo storage is bounded at4096.");
    }

    private static HierarchicalFlow NewPlanner(GridGeometry geometry, Dictionary<string, long>? metrics = null)
    {
        return new HierarchicalFlow(geometry, metrics ?? new Dictionary<string, long>(), 600);
    }

    private static WorldDefinition MakeWorld(bool closed)
    {
        var cells = Enumerable.Range(0, Width * Height).Select(cell =>
        {
            int x = cell % Width;
            int y = cell / Width;
            return x > 0 && x < Width - 1 && y > 0 && y < Height - 1 && (x != 10 || !closed && (y == 7 || y == 8));
        }).ToArray();
        return new WorldDefinition(
            Width,
            Height,
            1000,
            default,
            cells,
            cells,
            new[] { Start },
            new[] { new TargetDefinition(1, new Point2(22500, 14500), 300) });
    }

    private static string Plan(HierarchicalFlow.Route? route)
    {
        return route == null ? "null" : JsonSerializer.Serialize(new
        {
            route.Goal,
            route.StartCell,
            route.TotalCost,
            route.ProgressKey,
            route.Radius,
            route.Revision,
            route.Tiles,
            route.Components,
            route.Exits,
            route.RemainingAfterExits
        });
    }

    private static void FreshState(HierarchicalFlow.Route route)
    {
        Require(route.CurrentIndex == 0 && route.LastTile == -1 && route.LastSegmentIndex == -1 && route.LastFlow == null, "Runtime state leaked.");
    }

    private static void IndependentArrays(HierarchicalFlow.Route first, HierarchicalFlow.Route second)
    {
        Require(!ReferenceEquals(first.Tiles, second.Tiles) && !ReferenceEquals(first.Components, second.Components), "Integer route arrays are shared.");
        Require(
            !ReferenceEquals(first.Exits, second.Exits) && !ReferenceEquals(first.RemainingAfterExits, second.RemainingAfterExits),
            "Exit arrays are shared.");
    }

    private static int CacheCount(HierarchicalFlow planner)
    {
        return ((IDictionary)typeof(HierarchicalFlow).GetField("routes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(planner)!).Count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static string Hash(string path)
    {
        return Convert.ToHexString(SHA256.HashData(System.IO.File.ReadAllBytes(path))).ToLowerInvariant();
    }
}
