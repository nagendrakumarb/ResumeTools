namespace ProfessionalHub.ResumeTools.Models;

public sealed class ResumeFixOptions
{
    public bool AddProfessionalSummaryHeading { get; set; } = true;
    public bool ForceProfessionalSummary { get; set; }
    public bool KeepOnePrimaryPhone { get; set; } = true;
    public bool CompactPageLayout { get; set; } = true;
    public bool ForceCompactPageLayout { get; set; }
    public bool BalanceBoldUsage { get; set; } = true;
    public bool ForceBalanceBoldUsage { get; set; }
    public bool RemoveRepeatedContent { get; set; } = true;
    public bool ImproveReadingClarity { get; set; } = true;
    public bool FixGrammarAndSyntax { get; set; } = true; // Added property
    public bool AddEvidenceBackedStrengths { get; set; } = true;
    public bool ForceEvidenceBackedStrengths { get; set; }
}