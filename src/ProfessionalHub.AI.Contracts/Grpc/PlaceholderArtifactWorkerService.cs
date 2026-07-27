using Grpc.Core;
using ProfessionalHub.AI.Contracts.Abstractions;
using ProfessionalHub.AI.Contracts.Grpc;

namespace ProfessionalHub.AI.Contracts.Services;

public sealed class PlaceholderArtifactWorkerService(IAiCapabilityProvider capabilities)
    : AiArtifactWorker.AiArtifactWorkerBase
{
    public override Task<CapabilityResponse> GetCapabilities(
        CapabilityRequest request,
        ServerCallContext context)
    {
        var response = new CapabilityResponse
        {
            Success = true,
            WorkerId = capabilities.WorkerId,
            Runtime = capabilities.Runtime,
            Message = "Worker is available. Feature implementations are intentionally pending."
        };
        response.TaskIds.AddRange(capabilities.SupportedTaskIds);
        return Task.FromResult(response);
    }

    public override Task<TaskResponse> ExecuteTask(TaskRequest request, ServerCallContext context)
    {
        var supported = capabilities.SupportedTaskIds.Contains(request.TaskId, StringComparer.OrdinalIgnoreCase);
        var response = new TaskResponse
        {
            Success = true,
            TaskId = request.TaskId,
            Message = supported
                ? "Task contract accepted successfully; implementation is intentionally pending."
                : "Task was accepted successfully but is not assigned to this worker.",
            ArtifactPath = request.OutputPath,
            PackageType = "placeholder"
        };
        response.Metrics["placeholder"] = 1d;
        return Task.FromResult(response);
    }

    public override Task<TaskResponse> ExportPortablePackage(ExportRequest request, ServerCallContext context)
    {
        var response = new TaskResponse
        {
            Success = true,
            TaskId = request.TaskId,
            Message = "Portable export request accepted successfully; implementation is intentionally pending.",
            ArtifactPath = request.PortableOutputPath,
            PackageType = "placeholder"
        };
        response.Metrics["placeholder"] = 1d;
        return Task.FromResult(response);
    }
}
