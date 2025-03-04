using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;

namespace TagBites.Expressions.Benchmarks;

public class Program
{
    public static void Main(string[] args)
    {
        var logger = new ConsoleLogger();
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .AddLogger(logger)
            .WithOptions(ConfigOptions.DisableOptimizationsValidator)
            .WithOption(ConfigOptions.LogBuildOutput, false);

        BenchmarkRunner.Run(typeof(Program).Assembly, config);
    }
}
