using Microsoft.JSInterop;
using ProfessionalHub.ResumeTools.Models;

namespace ProfessionalHub.ResumeTools.Services;

public sealed class AnalysisHistoryService(IJSRuntime jsRuntime)
{
    public ValueTask SaveAsync(AnalysisRecord record) => jsRuntime.InvokeVoidAsync("professionalHub.history.save", record);
    public ValueTask<AnalysisRecord[]> GetAllAsync() => jsRuntime.InvokeAsync<AnalysisRecord[]>("professionalHub.history.getAll");
}
