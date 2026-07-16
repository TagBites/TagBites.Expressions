using System.Text;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace TagBites.Expressions.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "feature-comparer")
        {
            LibraryFeatureComparer.Run();
            return;
        }

        var logger = new ConsoleLogger();
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddLogger(logger)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .WithOption(ConfigOptions.LogBuildOutput, false);

        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        PrintPivotTable(summaries, logger);
    }

    private static void PrintPivotTable(IEnumerable<Summary> summaries, ILogger logger)
    {
        var summaryList = summaries.ToList();
        var rows = summaryList
            .SelectMany(s => s.Reports)
            .Select(r =>
            {
                var method = r.BenchmarkCase.Descriptor.WorkloadMethod.Name;
                var parts = method.Split('_', 2);
                var library = parts[0];
                var testCase = parts.Length > 1 ? parts[1] : "Default";
                double? meanNs = r.ResultStatistics?.Mean;
                double? allocatedBytes = r.GcStats.GetBytesAllocatedPerOperation(r.BenchmarkCase);
                return (Library: library, TestCase: testCase, MeanNs: meanNs, AllocatedBytes: allocatedBytes);
            })
            .ToList();

        var libraries = rows.Select(x => x.Library).Distinct().OrderByDescending(x => string.Equals(x, "TagBites")).ThenBy(x => x).ToList();
        var testCases = rows.Select(x => x.TestCase).Distinct().OrderBy(x => x).ToList();

        static string FormatTime(double ns) => ns switch
        {
            < 1_000 => $"{ns:F1} ns",
            _ => $"{ns / 1_000:F2} us",
        };

        static string FormatBytes(double b) => b < 1024 ? $"{b:F0} B" : $"{b / 1024:F2} KB";

        static string FormatRatio(double ratio) => ratio >= 100 ? $"{ratio:F1}x" : $"{ratio:F2}x";

        var sb = new StringBuilder();

        sb.Append("| TestCase |");
        foreach (var lib in libraries) sb.Append($" {lib} |");
        sb.AppendLine();

        sb.Append("|---|");
        foreach (var _ in libraries) sb.Append("---:|");
        sb.AppendLine();

        foreach (var tc in testCases)
        {
            var bestTime = rows.Where(x => x.TestCase == tc).Min(x => x.MeanNs);
            var bestAlloc = rows.Where(x => x.TestCase == tc).Min(x => x.AllocatedBytes);

            sb.Append($"| {tc} |");
            foreach (var lib in libraries)
            {
                var r = rows.FirstOrDefault(x => x.Library == lib && x.TestCase == tc);
                if (r.MeanNs is null)
                {
                    sb.Append(" NA |");
                    continue;
                }
                var time = FormatTime(r.MeanNs.Value);
                var alloc = r.AllocatedBytes.HasValue ? FormatBytes(r.AllocatedBytes.Value) : "-";

                time = $"`{time}`";

                if (bestTime >= r.MeanNs)
                    time = $"**{time}**";

                if (bestAlloc >= r.AllocatedBytes)
                    alloc = $"**{alloc}**";

                var timeRatio = $" ({FormatRatio(r.MeanNs.Value / bestTime!.Value)})";
                var allocRatio = r.AllocatedBytes.HasValue && bestAlloc.HasValue ? $" ({FormatRatio(r.AllocatedBytes.Value / bestAlloc.Value)})" : "";

                sb.Append($" {time}{timeRatio}<br>{alloc}{allocRatio} |");
            }
            sb.AppendLine();
        }

        var text = sb.ToString();

        logger.WriteLine();
        logger.WriteLine(text);

        var resultsDirectory = summaryList.FirstOrDefault()?.ResultsDirectoryPath;
        if (resultsDirectory != null)
        {
            Directory.CreateDirectory(resultsDirectory);
            var path = Path.Combine(resultsDirectory, "Pivot.md");
            File.WriteAllText(path, text);
            logger.WriteLine($"Pivot table saved to {path}");
        }
    }
}
