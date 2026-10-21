using System.Reflection;
using BenchmarkDotNet.Running;
using Pixelbadger.Toolkit.Benchmarks.Results;

if (args.Length > 0)
{
    var rest = args.Skip(1).ToList();
    var cwd = Directory.GetCurrentDirectory();
    switch (args[0])
    {
        case "run":
            return RunnerCli.Run(rest, cwd, Console.Out, Console.Error, Assembly.GetExecutingAssembly());
        case "query":
            return RunnerCli.Query(rest, cwd, Console.Out, Console.Error);
        case "validate":
            return RunnerCli.Validate(rest, cwd, Console.Out, Console.Error);
        case "report":
            return RunnerCli.Report(rest, cwd, Console.Out, Console.Error);
    }
}

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
return 0;
