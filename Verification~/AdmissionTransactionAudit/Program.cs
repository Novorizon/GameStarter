using System.Text.Json;

namespace AdmissionTransactionAudit;

internal static class Program
{
    private static int Main(string[] arguments)
    {
        string output = Path.Combine(Environment.CurrentDirectory, "Artifacts", "AdmissionTransactionAudit", "results.json");
        for (int index = 0; index < arguments.Length; index++)
        {
            if (arguments[index] != "--output" || index + 1 >= arguments.Length)
            {
                Console.Error.WriteLine("Usage: AdmissionTransactionAudit [--output results.json]");
                return 2;
            }

            output = Path.GetFullPath(arguments[++index]);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Delete(output);
        var audit = new Audit();
        Exception? failure = null;
        try
        {
            CacheCases.Run(audit);
            ForestCases.Run(audit);
        }
        catch (Exception error)
        {
            failure = error;
            Console.Error.WriteLine(error);
        }

        var report = new
        {
            passed = failure == null,
            scope = "Linked production Domain in .NET 8; transaction semantics, not Unity/physical gameplay validation.",
            failure = failure?.ToString(),
            cases = audit.Rows
        };
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"ADMISSION_TRANSACTION_AUDIT {(failure == null ? "PASS" : "FAIL")} cases={audit.Rows.Count}");
        return failure == null ? 0 : 1;
    }
}
