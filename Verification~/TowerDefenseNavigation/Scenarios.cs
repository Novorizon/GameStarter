using System;
using System.Collections.Generic;
using System.Linq;

internal readonly record struct Cell(int X, int Y);

internal sealed class NavigationScenario
{
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public bool[] Walkable { get; init; } = Array.Empty<bool>();
    public Cell Target { get; init; }
    public Cell[] SpawnCells { get; init; } = Array.Empty<Cell>();
    public int UnitCount { get; init; }
    public int[] MoveTicks { get; init; } = new[] { 1 };
    public int Deadline { get; init; } = 12000;
    public bool ExpectUnreachable { get; init; }
    public bool HasDeathReplacement { get; init; }
    public int Seed { get; init; }
    public int GatheringDistance { get; init; } = 5000;
    public bool ExpectCapacityRejection { get; init; }
    public int UnitRadius { get; init; } = 250;
    public bool[]? HoldingAllowed { get; init; }
    public bool RequireLaneCertificate { get; init; }

    public bool Open(Cell cell)
    {
        return cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height && Walkable[cell.Y * Width + cell.X];
    }
}

internal static class ScenarioCatalog
{
    public static IEnumerable<NavigationScenario> Create(int seed)
    {
        yield return Build("open-arena", 31, 31, new Cell(23, 15), new[] { new Cell(1, 4), new Cell(1, 15), new Cell(1, 26) }, 48, seed);
        yield return Build(
            "mixed-speed-approach",
            35,
            29,
            new Cell(27, 14),
            new[] { new Cell(1, 3), new Cell(1, 14), new Cell(1, 25) },
            64,
            seed,
            moveTicks: new[] { 1, 2, 4 });
        yield return Build(
            "large-radius-turning-clearance",
            35,
            29,
            new Cell(27, 14),
            new[] { new Cell(1, 3), new Cell(1, 14), new Cell(1, 25) },
            48,
            seed,
            moveTicks: new[] { 1, 2, 4 },
            unitRadius: 490);
        yield return Build(
            "single-cell-throat",
            39,
            27,
            new Cell(31, 13),
            new[] { new Cell(1, 5), new Cell(1, 13), new Cell(1, 21) },
            48,
            seed,
            blocked: cell => cell.X >= 15 && cell.X <= 18 && cell.Y != 13);
        yield return Build(
            "u-obstacle",
            37,
            31,
            new Cell(29, 15),
            new[] { new Cell(2, 4), new Cell(2, 15), new Cell(2, 26) },
            56,
            seed,
            blocked: cell => (cell.X == 17 && cell.Y >= 7 && cell.Y <= 23) || ((cell.Y == 7 || cell.Y == 23) && cell.X >= 10 && cell.X <= 17));
        yield return Build(
            "right-angle-corridor",
            35,
            29,
            new Cell(27, 21),
            new[] { new Cell(2, 4) },
            32,
            seed,
            blocked: cell => !(cell.Y >= 3 && cell.Y <= 5 && cell.X <= 20)
                && !(cell.X >= 18 && cell.X <= 20 && cell.Y <= 21) && !(cell.X >= 18 && cell.Y >= 14));
        yield return Build(
            "cross-junction-parking-lanes",
            45,
            45,
            new Cell(35, 22),
            new[] { new Cell(2, 22), new Cell(18, 2), new Cell(18, 42) },
            72,
            seed,
            blocked: cell => !(cell.Y >= 21 && cell.Y <= 23) && !(cell.X >= 17 && cell.X <= 19) && !(cell.X >= 24 && cell.Y >= 10 && cell.Y <= 34),
            gatheringDistance: 8000,
            parkingLanes: true,
            parkingAllowed: cell => cell.X >= 24 && cell.Y >= 10 && cell.Y <= 34);
        yield return Build(
            "cross-junction-original-4m",
            37,
            37,
            new Cell(29, 18),
            new[] { new Cell(2, 18), new Cell(18, 2), new Cell(18, 34) },
            72,
            seed,
            blocked: cell => !(cell.Y >= 17 && cell.Y <= 19) && !(cell.X >= 17 && cell.X <= 19) && !(cell.X >= 22 && cell.Y >= 10 && cell.Y <= 26),
            gatheringDistance: 4000);
        yield return Build(
            "cross-junction-original-5m",
            37,
            37,
            new Cell(29, 18),
            new[] { new Cell(2, 18), new Cell(18, 2), new Cell(18, 34) },
            72,
            seed,
            blocked: cell => !(cell.Y >= 17 && cell.Y <= 19) && !(cell.X >= 17 && cell.X <= 19) && !(cell.X >= 22 && cell.Y >= 10 && cell.Y <= 26),
            gatheringDistance: 5000);
        yield return Build(
            "shared-spawn-parking-lanes",
            39,
            39,
            new Cell(29, 19),
            new[] { new Cell(1, 19) },
            72,
            seed,
            gatheringDistance: 8000,
            parkingLanes: true);
        yield return Build(
            "shared-spawn-original-5m",
            35,
            29,
            new Cell(27, 14),
            new[] { new Cell(1, 14) },
            72,
            seed,
            gatheringDistance: 5000);
        yield return Build(
            "three-hundred-units-parking-lanes",
            69,
            69,
            new Cell(43, 34),
            new[] { new Cell(1, 10), new Cell(1, 34), new Cell(1, 58) },
            300,
            seed,
            moveTicks: new[] { 1, 2, 3, 5 },
            deadline: 30000,
            gatheringDistance: 16000,
            parkingLanes: true);
        yield return Build(
            "three-hundred-units-original-12m",
            61,
            61,
            new Cell(43, 30),
            new[] { new Cell(1, 10), new Cell(1, 30), new Cell(1, 50) },
            300,
            seed,
            moveTicks: new[] { 1, 2, 3, 5 },
            deadline: 30000,
            gatheringDistance: 12000);
        yield return Build(
            "unreachable-target",
            31,
            31,
            new Cell(23, 15),
            new[] { new Cell(2, 15) },
            12,
            seed,
            blocked: cell => cell.X == 15,
            unreachable: true,
            deadline: 1000);
        yield return Build(
            "full-slots-death-replacement",
            39,
            39,
            new Cell(29, 19),
            new[] { new Cell(1, 5), new Cell(1, 19), new Cell(1, 33) },
            120,
            seed,
            replacement: true,
            deadline: 18000,
            gatheringDistance: 8000);
        yield return Build(
            "explicit-capacity-exhaustion",
            19,
            19,
            new Cell(12, 9),
            new[] { new Cell(1, 9) },
            200,
            seed,
            capacityRejection: true,
            gatheringDistance: 4000,
            deadline: 3000);
    }

    private static NavigationScenario Build(
        string name,
        int width,
        int height,
        Cell target,
        Cell[] spawns,
        int count,
        int seed,
        Func<Cell, bool>? blocked = null,
        int[]? moveTicks = null,
        bool unreachable = false,
        bool replacement = false,
        int deadline = 12000,
        int gatheringDistance = 5000,
        bool capacityRejection = false,
        int unitRadius = 250,
        bool parkingLanes = false,
        Func<Cell, bool>? parkingAllowed = null)
    {
        var cells = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new Cell(x, y);
                cells[y * width + x] = x > 0 && y > 0 && x < width - 1 && y < height - 1 && !(blocked?.Invoke(cell) ?? false);
            }
        }

        bool[]? holding = null;
        if (parkingLanes)
        {
            holding = new bool[cells.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    holding[y * width + x] = cells[y * width + x] && y % 2 == 0 && x % 4 != 0
                        && (parkingAllowed?.Invoke(new Cell(x, y)) ?? true);
                }
            }

            foreach (var entry in spawns)
            {
                holding[entry.Y * width + entry.X] = false;
            }
        }

        if (name == "cross-junction-conservative-capacity-4m" || name == "cross-junction-original-4m")
        {
            // Lock the exact original 72 parking targets: the four inner diagonals remain transit-only here.
            holding = (bool[])cells.Clone();
            foreach (int dx in new[] { -1, 1 })
            {
                foreach (int dy in new[] { -1, 1 })
                {
                    holding[(target.Y + dy) * width + target.X + dx] = false;
                }
            }
        }

        var scenario = new NavigationScenario
        {
            Name = name,
            Width = width,
            Height = height,
            Target = target,
            SpawnCells = spawns,
            Walkable = cells,
            UnitCount = count,
            Seed = seed,
            MoveTicks = moveTicks ?? new[] { 1 },
            ExpectUnreachable = unreachable,
            HasDeathReplacement = replacement,
            Deadline = deadline,
            GatheringDistance = gatheringDistance,
            ExpectCapacityRejection = capacityRejection,
            UnitRadius = unitRadius,
            HoldingAllowed = holding,
            RequireLaneCertificate = parkingLanes
        };
        if (spawns.Any(spawn => !scenario.Open(spawn)) || !scenario.Open(target))
        {
            throw new InvalidOperationException("Invalid test fixture: " + name);
        }

        return scenario;
    }
}
