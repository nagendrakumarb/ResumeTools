using ProfessionalHub.AI.Contracts.Abstractions;
using ProfessionalHub.AI.Contracts.Services;
using ProfessionalHub.DotNetAI.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton<IAiCapabilityProvider, DotNetWorkerCapabilities>();

var app = builder.Build();
app.MapGrpcService<PlaceholderArtifactWorkerService>();
app.MapGet("/", () => Results.Ok(new
{
    success = true,
    worker = "professionalhub-dotnet-ai",
    message = "Use a gRPC client to access the local worker."
}));
app.Run();
