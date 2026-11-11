using System;
using System.IO;
using System.Linq;

namespace GameStarter.Verification.Navigation
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool repeat = args.Contains("--repeat", StringComparer.Ordinal);
            bool time = args.Contains("--time", StringComparer.Ordinal);
            args = args.Where(arg => arg != "--repeat" && arg != "--time").ToArray();
            if (args.Length > 0 && args[0] == "--list")
            {
                Console.WriteLine(string.Join(Environment.NewLine, Fixtures.Names));
                return 0;
            }

            string project = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
            string selection = args.Length > 0 ? args[0] : "all";
            string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(project, "Artifacts", "NavigationScenarios");
            uint seed = args.Length > 2 ? uint.Parse(args[2]) : 12345;
            var names = selection == "all" ? Fixtures.Names : selection.Split(',', StringSplitOptions.RemoveEmptyEntries);
            Directory.CreateDirectory(output);
            var maps = new MapLoader(project);
            int failed = 0;
            foreach (string name in names)
            {
                try
                {
                    var scenario = Fixtures.Create(maps, name);
                    var report = new ScenarioRun(scenario, seed).Run(output, time);
                    if (repeat)
                    {
                        var repeated = new ScenarioRun(Fixtures.Create(maps, name), seed).Run();
                        if (report.Hash != repeated.Hash)
                        {
                            throw new InvalidOperationException($"Replay changed: {report.Hash:X16} != {repeated.Hash:X16}");
                        }
                    }

                    Console.WriteLine($"{(report.Passed ? "PASS" : "FAIL")} {name}: {report.Summary}");
                    if (!report.Passed)
                    {
                        failed++;
                    }
                }
                catch (Exception error)
                {
                    Console.Error.WriteLine($"FAIL {name}: {error}");
                    failed++;
                }
            }

            return failed == 0 ? 0 : 1;
        }
    }
}
