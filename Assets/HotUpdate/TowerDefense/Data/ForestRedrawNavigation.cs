using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace Game.HotUpdate.TowerDefense.Data
{
    internal sealed class ForestRedrawNavigation
    {
        private const int Columns = 44;
        private const int Rows = 30;
        private const int Tile = 500;
        private const int Step = 125;
        private readonly Position2 origin;
        private readonly string[] symbols;
        private readonly BattleTerrain movementTerrain;

        internal Position2[] Route { get; }
        internal Position2 Target { get; }
        internal int TargetRadius { get; }
        internal Position2 HoldingMin { get; }
        internal Position2 HoldingMax { get; }
        internal Position2 SpawnMarker { get; }
        internal BattleTerrain BuildTerrain { get; }

        private ForestRedrawNavigation(Position2 origin, Position2 target, int targetRadius, string[] symbols)
        {
            this.origin = origin;
            this.symbols = symbols;
            Target = target;
            TargetRadius = targetRadius;

            var indices = Enumerable.Range(0, Columns * Rows);
            var spawns = indices.Where(cell => Symbol(cell % Columns, cell / Columns) == 'S').ToArray();
            var holding = indices.Where(cell => Symbol(cell % Columns, cell / Columns) == 'a').ToArray();
            if (spawns.Length != 1 || holding.Length == 0 || SymbolAt(target) != 'B')
            {
                throw new FormatException("The forest redraw requires one spawn, holding ground, and a target centered on B terrain.");
            }

            int spawn = spawns[0];
            var spawnCorner = Corner(spawn % Columns, spawn / Columns);
            SpawnMarker = new Position2(spawnCorner.X + Tile / 2, spawnCorner.Y + Tile / 2);
            HoldingMin = Corner(holding.Min(cell => cell % Columns), holding.Min(cell => cell / Columns));
            HoldingMax = Corner(holding.Max(cell => cell % Columns) + 1, holding.Max(cell => cell / Columns) + 1);

            // Turning points use the centers between the two authored road rows or columns.
            Route = new[]
            {
                new Position2(SpawnMarker.X, spawnCorner.Y + Tile),
                Corner(4, 26), Corner(4, 23), Corner(27, 23), Corner(27, 14),
                Corner(10, 14), Corner(10, 6), Target
            };
            var maximum = Corner(Columns, Rows);
            movementTerrain = new BattleTerrain(origin, maximum, Route, Tile, Obstacles((x, y) => !IsMovementGround(Symbol(x, y))));
            BuildTerrain = new BattleTerrain(origin, maximum, Route, Tile, Obstacles(BlocksConstruction));
            for (int index = 1; index < Route.Length; index++)
            {
                if (!movementTerrain.CanMove(Route[index - 1], Route[index], 0))
                {
                    throw new FormatException($"The forest redraw centerline crosses blocked ground at segment {index}.");
                }
            }
        }

        internal static ForestRedrawNavigation Parse(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new FormatException("The forest redraw is empty.");
            }

            var lines = source.TrimStart('\uFEFF').Split('\n').Select(line => line.Trim())
                .Where(line => line.Length != 0 && !line.StartsWith(";", StringComparison.Ordinal)).ToArray();
            if (lines.Length != 6 + Rows || lines[0] != "NAVGRID 1" || lines[5] != "rows north-to-south")
            {
                throw new FormatException("Expected forest redraw NAVGRID 1 and north-to-south rows.");
            }

            var size = ReadFields(lines[1], "size", 2);
            if (size[0] != Columns || size[1] != Rows || ReadFields(lines[2], "cell", 1)[0] != Tile)
            {
                throw new FormatException("The forest redraw uses a 44 × 30 grid of 500 mm cells.");
            }

            var origin = ReadFields(lines[3], "origin", 2);
            var target = ReadFields(lines[4], "target", 3);
            var symbols = lines.Skip(6).Reverse().ToArray();
            if (target[2] <= 0 || target[2] > 3000 || symbols.Any(row => row.Length != Columns || row.Any(symbol => ".+aBS#~^*T".IndexOf(symbol) < 0)))
            {
                throw new FormatException("Invalid forest redraw cell symbols or target radius.");
            }

            return new ForestRedrawNavigation(new Position2(origin[0], origin[1]), new Position2(target[0], target[1]), target[2], symbols);
        }

        internal NavigationGrid CreateGrid()
        {
            int width = Columns * (Tile / Step) + 1;
            int height = Rows * (Tile / Step) + 1;
            var fineOrigin = new Position2(origin.X - Step / 2, origin.Y - Step / 2);
            var walkable = new bool[width * height];
            var holding = new bool[walkable.Length];
            for (int cell = 0; cell < walkable.Length; cell++)
            {
                var point = new Position2(origin.X + cell % width * Step, origin.Y + cell / width * Step);
                walkable[cell] = ClearsMovement(point, point, 0);
                holding[cell] = walkable[cell] && SymbolAt(point) == 'a';
            }

            var spawn = Route[0];
            int spawnCell = (spawn.Y - origin.Y) / Step * width + (spawn.X - origin.X) / Step;
            return new NavigationGrid(width, height, Step, fineOrigin, walkable, Target, TargetRadius, holding, new[] { spawnCell },
                new[] { Route }, ClearsMovement, AllowsAttack);
        }

        private bool ClearsMovement(Position2 from, Position2 to, int radius)
        {
            long clearance = (long)TargetRadius + radius;
            return SegmentDistanceSquared(Target, from, to) >= clearance * clearance && movementTerrain.CanMove(from, to, radius);
        }

        private bool AllowsAttack(Position2 position, int radius, int range)
        {
            long reach = (long)TargetRadius + radius + range;
            return SymbolAt(position) == 'a' && NavigationGrid.DistanceSquared(position, Target) <= reach * reach
                && movementTerrain.CanMove(position, Target, 0);
        }

        private static double SegmentDistanceSquared(Position2 point, Position2 from, Position2 to)
        {
            long x = (long)point.X - from.X;
            long y = (long)point.Y - from.Y;
            long dx = (long)to.X - from.X;
            long dy = (long)to.Y - from.Y;
            long length = dx * dx + dy * dy;
            double fraction = length == 0 ? 0 : Math.Clamp((x * dx + y * dy) / (double)length, 0, 1);
            return (x - dx * fraction) * (x - dx * fraction) + (y - dy * fraction) * (y - dy * fraction);
        }

        private bool BlocksConstruction(int x, int y)
        {
            char symbol = Symbol(x, y);
            return "~^*BT".IndexOf(symbol) >= 0 || symbol == '#' && (x == 0 || x == Columns - 1 || y == 0 || y == Rows - 1);
        }

        private Position2[][] Obstacles(Func<int, int, bool> blocked)
        {
            var obstacles = new List<Position2[]>();
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    if (!blocked(x, y))
                    {
                        continue;
                    }

                    int first = x;
                    while (x + 1 < Columns && blocked(x + 1, y))
                    {
                        x++;
                    }

                    obstacles.Add(new[] { Corner(first, y), Corner(x + 1, y), Corner(x + 1, y + 1), Corner(first, y + 1) });
                }
            }

            return obstacles.ToArray();
        }

        private Position2 Corner(int x, int y) => new Position2(origin.X + x * Tile, origin.Y + y * Tile);

        private char Symbol(int x, int y) => x >= 0 && x < Columns && y >= 0 && y < Rows ? symbols[y][x] : '#';

        private char SymbolAt(Position2 point)
        {
            return point.X < origin.X || point.Y < origin.Y ? '#' : Symbol((point.X - origin.X) / Tile, (point.Y - origin.Y) / Tile);
        }

        private static int[] ReadFields(string line, string name, int count)
        {
            var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var values = new int[count];
            if (fields.Length != count + 1 || fields[0] != name)
            {
                throw new FormatException($"Invalid forest redraw {name} line.");
            }

            for (int index = 0; index < count; index++)
            {
                if (!int.TryParse(fields[index + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[index]))
                {
                    throw new FormatException($"Invalid number in forest redraw {name} line.");
                }
            }

            return values;
        }

        private static bool IsMovementGround(char symbol) => ".+aBS".IndexOf(symbol) >= 0;
    }
}
