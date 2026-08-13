using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ProfessionalHub.ResumeTools;
using ProfessionalHub.ResumeTools.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ResumeParserService>();
builder.Services.AddScoped<SimilarityService>();
builder.Services.AddScoped<AtsCompatibilityService>();
builder.Services.AddScoped<JobDescriptionService>();
builder.Services.AddScoped<JobSearchService>();
builder.Services.AddScoped<JobApplicationStore>();
builder.Services.AddScoped<ResumeImprovementService>();
builder.Services.AddScoped<AnalysisHistoryService>();

await builder.Build().RunAsync();
