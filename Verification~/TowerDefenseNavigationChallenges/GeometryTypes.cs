using System;

internal readonly record struct Cell(int X, int Y);

internal sealed class NavigationScenario
{
    public int Width { get; init; }
    public int Height { get; init; }
    public Cell Target { get; init; }
    public bool[] Walkable { get; init; } = Array.Empty<bool>();
    public bool[]? HoldingAllowed { get; init; }
    public Cell[] SpawnCells { get; init; } = Array.Empty<Cell>();
    public int GatheringDistance { get; init; }

    public bool Open(Cell cell)
    {
        return cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height && Walkable[cell.Y * Width + cell.X];
    }
}
