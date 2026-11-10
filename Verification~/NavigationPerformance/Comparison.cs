using System.Security.Cryptography;

internal static class Comparison
{
    internal static object Verify(string baseline, string candidate)
    {
        foreach (string file in new[] { "trajectory.csv", "commands.csv" })
        {
            Require(
                File.ReadAllBytes(Path.Combine(baseline, file)).SequenceEqual(File.ReadAllBytes(Path.Combine(candidate, file))),
                "Deterministic " + file + " differs from baseline.");
        }

        string[] oldLines = File.ReadAllLines(Path.Combine(baseline, "counters.csv"));
        string[] newLines = File.ReadAllLines(Path.Combine(candidate, "counters.csv"));
        Require(oldLines.Length == newLines.Length, "Counter trace length differs.");
        string[] oldHeader = oldLines[0].Split(',');
        string[] newHeader = newLines[0].Split(',');
        var indices = oldHeader.Select(name => Array.IndexOf(newHeader, name)).ToArray();
        Require(indices.All(index => index >= 0), "An existing public counter disappeared.");
        for (int row = 1; row < oldLines.Length; row++)
        {
            var oldValues = oldLines[row].Split(',');
            var newValues = newLines[row].Split(',');
            for (int column = 0; column < indices.Length; column++)
            {
                Require(oldValues[column] == newValues[indices[column]], "Counter " + oldHeader[column] + " differs at row " + row);
            }
        }

        return new
        {
            success = true,
            everyTickTrajectoryEqual = true,
            allBirthResultsEqual = true,
            allExistingPublicCountersEqual = true,
            comparedCounterColumns = oldHeader,
            extraCandidateCounterColumns = newHeader.Except(oldHeader).ToArray(),
            baselineTrajectorySha256 = Digest(Path.Combine(baseline, "trajectory.csv")),
            candidateTrajectorySha256 = Digest(Path.Combine(candidate, "trajectory.csv"))
        };
    }

    internal static object VerifyNative(string native, string trajectory)
    {
        string[] nativeLines = File.ReadAllLines(native);
        string[] ownLines = File.ReadAllLines(trajectory);
        string[] columns = { "tick", "id", "xMm", "yMm", "radiusMm", "state", "goalCell" };
        string[] nativeHeader = nativeLines[0].Split(',');
        string[] ownHeader = ownLines[0].Split(',');
        int[] nativeColumns = columns.Select(name => Array.IndexOf(nativeHeader, name)).ToArray();
        int[] ownColumns = columns.Select(name => Array.IndexOf(ownHeader, name)).ToArray();
        Require(nativeColumns.All(index => index >= 0) && ownColumns.All(index => index >= 0), "Native comparison columns are missing.");
        var states = ownLines.Skip(1).Select(line => line.Split(','))
            .ToDictionary(values => (values[ownColumns[0]], values[ownColumns[1]]), values => values);
        int compared = 0;
        foreach (string line in nativeLines.Skip(1))
        {
            var values = line.Split(',');
            Require(states.TryGetValue((values[nativeColumns[0]], values[nativeColumns[1]]), out var actual), "Native tick/ID is absent.");
            for (int column = 0; column < columns.Length; column++)
            {
                Require(values[nativeColumns[column]] == actual![ownColumns[column]], "Native " + columns[column] + " differs at sample " + compared);
            }

            compared++;
        }

        return new
        {
            success = true,
            nativeCsv = Path.GetFullPath(native),
            nativeSha256 = Digest(native),
            comparedRows = compared,
            columns
        };
    }

    private static string Digest(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
