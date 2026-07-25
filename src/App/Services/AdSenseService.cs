using Microsoft.JSInterop;

namespace ProfessionalHub.ResumeTools.Services;

public sealed class AdSenseService(IJSRuntime jsRuntime)
{
    public async ValueTask<bool> InitializeAsync(string elementId, string publisherId)
    {
        try { return await jsRuntime.InvokeAsync<bool>("professionalHub.ads.initialize", elementId, publisherId); }
        catch (JSException) { return false; }
    }
}
