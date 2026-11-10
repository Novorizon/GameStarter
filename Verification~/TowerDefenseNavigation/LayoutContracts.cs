using System;
using Game.HotUpdate.TowerDefense.Data;

internal static class LayoutContracts
{
    public static void Verify()
    {
        const string valid = "NAVGRID 1\nsize 5 5\ncell 500\norigin 0 0\ntarget 2250 2250 400\nrows north-to-south\naaaaB\naaaaa\naaaaa\naaaaa\nSaaaa\n";
        var grid = NavigationLayout.Parse("\uFEFF; authored test\r\n" + valid.Replace("\n", "\r\n"));
        if (grid.Width != 5 || grid.Height != 5 || grid.SpawnCells.Count != 1 || grid.SpawnCells[0] != 0
            || grid.Center(grid.SpawnCells[0]).X != 250 || grid.TargetCenter.Y != 2250)
        {
            throw new InvalidOperationException("BOM/CRLF navigation input changed geometry or north-to-south row orientation.");
        }

        Reject(valid.Replace("size 5 5", "size 6 5"));
        Reject(valid.Replace("size 5 5", "size 5 6"));
        Reject(valid.Replace("NAVGRID 1", "NAVGRID 2"));
        Reject(valid.Replace("aaaaB", "aaa?B"));
        Reject(valid.Replace("target 2250 2250 400", "target 1750 2250 400"));
        Reject(valid.Replace("target 2250 2250 400", "target 2250 2250 4000"));
        Reject(valid.Replace("Saaaa", "aaaaa"));
        Reject(valid.Replace("\naaaaB", "\n....B").Replace("\naaaaa", "\n.....").Replace("\nSaaaa", "\nS...."));
    }

    private static void Reject(string text)
    {
        try
        {
            NavigationLayout.Parse(text);
        }
        catch (FormatException)
        {
            return;
        }

        throw new InvalidOperationException("Malformed navigation layout was accepted: " + text);
    }
}
