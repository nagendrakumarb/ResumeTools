using ProfessionalHub.AI.Contracts.Abstractions;

namespace ProfessionalHub.AI.Orchestrator;

public interface IArtifactOrchestrator
{
    ValueTask<AiTaskResult> BuildAsync(
        AiTaskContext context,
        CancellationToken cancellationToken = default);

    ValueTask<AiTaskResult> ValidateAsync(
        AiTaskContext context,
        CancellationToken cancellationToken = default);

    ValueTask<AiTaskResult> PromoteAsync(
        AiTaskContext context,
        CancellationToken cancellationToken = default);
}

public sealed class PlaceholderArtifactOrchestrator : IArtifactOrchestrator
{
    public ValueTask<AiTaskResult> BuildAsync(AiTaskContext context, CancellationToken cancellationToken = default) =>
        Success(context, "Build request accepted; worker selection and artifact generation are pending.");

    public ValueTask<AiTaskResult> ValidateAsync(AiTaskContext context, CancellationToken cancellationToken = default) =>
        Success(context, "Validation request accepted; parity and browser validation are pending.");

    public ValueTask<AiTaskResult> PromoteAsync(AiTaskContext context, CancellationToken cancellationToken = default) =>
        Success(context, "Promotion request accepted; no production files were changed.");

    private static ValueTask<AiTaskResult> Success(AiTaskContext context, string message) =>
        ValueTask.FromResult(AiTaskResult.Completed(context.TaskId, message, context.OutputPath));
}
