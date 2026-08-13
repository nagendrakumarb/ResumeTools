using ProfessionalHub.AI.Contracts.Abstractions;
using ProfessionalHub.AI.Orchestrator;

if (args.Length > 0 && args[0].Equals("package", StringComparison.OrdinalIgnoreCase))
{
    var options = CommandLineOptions.Parse(args.Skip(1));
    var pipeline = new PortablePackagePipeline();
    var result = await pipeline.ExecuteAsync(options.RequestPath, options.OutputRoot);
    Console.WriteLine(result.Message);
    Environment.ExitCode = result.Success ? 0 : 1;
    return;
}

var taskId = args.FirstOrDefault() ?? AiTaskIds.RequirementClassification;
IArtifactOrchestrator orchestrator = new PlaceholderArtifactOrchestrator();
var legacyResult = await orchestrator.BuildAsync(new AiTaskContext(taskId));
Console.WriteLine(legacyResult.Message);
Environment.ExitCode = legacyResult.Success ? 0 : 1;

internal sealed record CommandLineOptions(string RequestPath, string OutputRoot)
{
    public static CommandLineOptions Parse(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        string Required(string name)
        {
            var index = Array.FindIndex(values, value => value.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index + 1 >= values.Length || string.IsNullOrWhiteSpace(values[index + 1]))
                throw new ArgumentException($"Missing required option {name}.");
            return values[index + 1];
        }

        return new CommandLineOptions(Required("--request"), Required("--output"));
    }
}
