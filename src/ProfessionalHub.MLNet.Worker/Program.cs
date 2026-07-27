using ProfessionalHub.AI.Contracts.Abstractions;
using ProfessionalHub.AI.Contracts.Services;
using ProfessionalHub.MLNet.Worker;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGrpc();
builder.Services.AddSingleton<IAiCapabilityProvider, MlNetWorkerCapabilities>();

var app = builder.Build();
app.MapGrpcService<PlaceholderArtifactWorkerService>();
app.MapGet("/", () => Results.Ok(new
{
    success = true,
    worker = "professionalhub-mlnet",
    message = "Use a gRPC client to access the local ML.NET worker."
}));
app.Run();
