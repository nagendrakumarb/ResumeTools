namespace ProfessionalHub.ResumeTools.Models;

public sealed class JobTailoringOptions
{
    public ResumeFixOptions AtsFixes { get; set; } = new();
    public IReadOnlyCollection<string> SelectedTerms { get; set; } = Array.Empty<string>();
}
