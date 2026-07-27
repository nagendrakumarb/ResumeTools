using ProfessionalHub.AI.Contracts.Abstractions;
using ProfessionalHub.AI.Orchestrator;

var taskId = args.FirstOrDefault() ?? AiTaskIds.RequirementClassification;
IArtifactOrchestrator orchestrator = new PlaceholderArtifactOrchestrator();
var result = await orchestrator.BuildAsync(new AiTaskContext(taskId));

Console.WriteLine(result.Message);
Environment.ExitCode = result.Success ? 0 : 1;
