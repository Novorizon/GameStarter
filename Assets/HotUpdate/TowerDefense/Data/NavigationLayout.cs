using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace Game.HotUpdate.TowerDefense.Data
{
    /// <summary>读取显式逻辑地形；图片不参与运行时障碍识别。行按北到南保存，网格索引按南到北排列。</summary>
    public static class NavigationLayout
    {
        public static NavigationGrid Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var lines = text.TrimStart('\uFEFF').Replace("\r", "").Split('\n').Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith(";", StringComparison.Ordinal)).ToArray();
            if (lines.Length < 8 || lines[0] != "NAVGRID 1" || lines[5] != "rows north-to-south")
            {
                throw new FormatException("Expected NAVGRID 1 with north-to-south terrain rows.");
            }

            var size = Numbers(lines[1], "size", 2);
            var cell = Numbers(lines[2], "cell", 1);
            var origin = Numbers(lines[3], "origin", 2);
            var target = Numbers(lines[4], "target", 3);
            int width = size[0];
            int height = size[1];
            if (width < 2 || width > 256 || height < 2 || height > 256 || lines.Length != height + 6)
            {
                throw new FormatException("Terrain dimensions must be 2–256 cells and exactly match the row count.");
            }

            var walkable = new bool[width * height];
            var holding = new bool[walkable.Length];
            var spawns = new List<int>();
            int targets = 0;
            for (int row = 0; row < height; row++)
            {
                string terrain = lines[row + 6];
                if (terrain.Length != width)
                {
                    throw new FormatException("Terrain row " + row + " does not match the declared width.");
                }

                for (int x = 0; x < width; x++)
                {
                    int index = (height - 1 - row) * width + x;
                    char symbol = terrain[x];
                    switch (symbol)
                    {
                        case 'a':
                            holding[index] = true;
                            walkable[index] = true;
                            break;
                        case 'S':
                            spawns.Add(index);
                            walkable[index] = true;
                            break;
                        case 'B':
                            // 目标自己的地面可用于视线检测；身体占用由 NavigationGrid 的目标圆盘单独排除。
                            targets++;
                            walkable[index] = true;
                            break;
                        case '.':
                        case '+':
                            walkable[index] = true;
                            break;
                        case '#':
                        case '~':
                        case '^':
                        case '*':
                        case 'T':
                            break;
                        default:
                            throw new FormatException("Unknown terrain symbol '" + symbol + "' at row " + row + ", column " + x + ".");
                    }
                }
            }

            if (spawns.Count == 0 || targets == 0 || !holding.Any(value => value))
            {
                throw new FormatException("Terrain requires a spawn gate, target ground and explicit holding area.");
            }

            var grid = new NavigationGrid(
                width,
                height,
                cell[0],
                new Position2(origin[0], origin[1]),
                walkable,
                new Position2(target[0], target[1]),
                target[2],
                holding,
                spawns.ToArray());
            int center = grid.CellAt(grid.TargetCenter);
            int centerRow = height - 1 - center / width;
            if (lines[centerRow + 6][center % width] != 'B')
            {
                throw new FormatException("The target centre must be marked as B target ground.");
            }

            if (grid.SpawnCells.Any(index => !grid.CanOccupy(index, grid.CellSize / 2)))
            {
                throw new FormatException("A spawn gate overlaps the target exclusion geometry.");
            }

            return grid;
        }

        private static int[] Numbers(string line, string name, int count)
        {
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length != count + 1 || tokens[0] != name)
            {
                throw new FormatException("Expected navigation field '" + name + "' with " + count + " integers.");
            }

            var values = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (!int.TryParse(tokens[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                {
                    throw new FormatException("Navigation field '" + name + "' contains an invalid integer.");
                }
            }

            return values;
        }
    }
}
