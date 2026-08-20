namespace ProfessionalHub.ResumeTools.Models;

public sealed class JobTailoringOptions
{
    public ResumeFixOptions AtsFixes { get; set; } = new();
    public IReadOnlyCollection<string> SelectedTerms { get; set; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> EvidenceStatements { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

