using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class Program
{
    private static void Main(string[] args)
    {
        string dll = Path.GetFullPath(args[0]);
        string summaryPath = Path.GetFullPath(args[1]);
        string witnessPath = Path.GetFullPath(args[2]);
        string output = Path.GetFullPath(args[3]);
        Directory.CreateDirectory(output);
        var assembly = Assembly.LoadFrom(dll);
        var catalog = assembly.GetType("ScenarioCatalog", true);
        var scenarios = (IEnumerable)catalog.GetMethod("Create", BindingFlags.Static | BindingFlags.Public).Invoke(null, new object[] { 1 });
        object actual = scenarios.Cast<object>().Single(value => (string)Get(value, "Name") == "cross-junction-conservative-capacity-4m");
        var grid = assembly.GetType("Program", true).GetMethod("GridFor", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new[] { actual });
        var geometry = new
        {
            Width = Get(grid, "Width"),
            Height = Get(grid, "Height"),
            CellSize = Get(grid, "CellSize"),
            Origin = Get(grid, "Origin"),
            TargetCenter = Get(grid, "TargetCenter"),
            TargetRadius = Get(grid, "TargetRadius"),
            Walkable = Get(actual, "Walkable"),
            HoldingAllowed = Get(actual, "HoldingAllowed") ?? Get(actual, "Walkable"),
            SpawnCells = Get(actual, "SpawnCells"),
            GatheringDistance = Get(actual, "GatheringDistance"),
            UnitRadius = Get(actual, "UnitRadius"),
            UnitCount = Get(actual, "UnitCount")
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        string geometryPath = Path.Combine(output, "actual-geometry.json");
        File.WriteAllText(geometryPath, JsonSerializer.Serialize(geometry, options));
        var source = JsonNode.Parse(File.ReadAllText(summaryPath));
        var original = JsonNode.Parse(File.ReadAllText(witnessPath))["paths"].AsArray();
        var proofs = new List<object>();
        foreach (var result in source["cases"].AsArray().Concat(source["separateControlResults"].AsArray()))
        {
            int birthSeed = (int)result["birthSeed"];
            int goalSeed = (int)result["goalSeed"];
            string name = (string)result["case"];
            int[] goals = original.Select(item => (int)item["Goal"]).ToArray();
            if (goalSeed != 0)
            {
                Shuffle(goals, goalSeed);
            }

            int[] queue = Enumerable.Range(1, 72).ToArray();
            string archive = Path.Combine(Path.GetDirectoryName(summaryPath), (string)result["archivedReport"]);
            using var file = File.OpenRead(archive);
            using var unpack = new GZipStream(file, CompressionMode.Decompress);
            var report = JsonNode.Parse(unpack);
            if (!(bool)report["SerialAdmission"] && birthSeed != 0)
            {
                Shuffle(queue, birthSeed);
            }

            var requests = original.Select((item, index) => new { Id = (int)item["Id"], Spawn = (int)item["Spawn"], Goal = goals[index] }).ToArray();
            var assignment = requests.ToDictionary(item => item.Id, item => item.Goal);
            foreach (var unit in report["Trajectory"].AsArray().Last()["Units"].AsArray())
            {
                if (assignment[(int)unit["Id"]] != (int)unit["SlotId"])
                {
                    throw new InvalidOperationException("Exported runtime goal assignment differs from the actual executed final trace.");
                }
            }

            string filename = name + "-birth-" + birthSeed + "-goal-" + goalSeed + ".json";
            string path = Path.Combine(output, filename);
            var input = new
            {
                SourceDllSha256 = Hash(dll),
                ActualGeometrySha256 = Hash(geometryPath),
                WitnessSha256 = Hash(witnessPath),
                Case = name,
                BirthSeed = birthSeed,
                GoalAssignmentSeed = goalSeed,
                InitialPendingOrder = queue,
                Requests = requests,
                ActualRadiusMm = report["ActualRadiusMm"].DeepClone(),
                MaximumSpeedMmPerLogicalTick = report["MaximumSpeedMmPerLogicalTick"].DeepClone(),
                LogicalTickSeconds = report["LogicalTickSeconds"].DeepClone(),
                LogicalSubsteps = report["LogicalSubsteps"].DeepClone(),
                TimeHorizon = report["TimeHorizon"].DeepClone(),
                TimeHorizonObstacle = report["TimeHorizonObstacle"].DeepClone(),
                NeighborDistance = report["NeighborDistance"].DeepClone(),
                AvoidanceGuardMm = report["AvoidanceGuardMm"].DeepClone(),
                SpawnClearanceMetres = report["SpawnClearanceMetres"].DeepClone()
            };
            File.WriteAllText(path, JsonSerializer.Serialize(input, options));
            proofs.Add(new { File = filename, ActualInputSha256 = Hash(path), AssignmentMatchedExecutedTrace = true });
        }

        File.WriteAllText(Path.Combine(output, "input-manifest.json"), JsonSerializer.Serialize(new
        {
            Source = "ScenarioCatalog.Create and Program.GridFor invoked from the exact tested DLL; goals and queue reproduced with the reported .NET seeds.",
            SourceDllSha256 = Hash(dll),
            ActualGeometrySha256 = Hash(geometryPath),
            Inputs = proofs
        }, options));
        Console.WriteLine("Exported actual compiled geometry and " + proofs.Count + " inputs; all goals matched executed traces.");
    }

    private static object Get(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void Shuffle(int[] values, int seed)
    {
        var random = new Random(seed);
        for (int index = values.Length - 1; index > 0; index--)
        {
            int other = random.Next(index + 1);
            (values[index], values[other]) = (values[other], values[index]);
        }
    }
}
