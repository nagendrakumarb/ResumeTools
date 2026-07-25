namespace ProfessionalHub.ResumeTools.Models;

public sealed class ResumeFixOptions
{
    public bool AddProfessionalSummaryHeading { get; set; } = true;
    public bool KeepOnePrimaryPhone { get; set; } = true;
    public bool CompactPageLayout { get; set; } = true;
    public bool BalanceBoldUsage { get; set; } = true;
    public bool RemoveRepeatedContent { get; set; } = true;
    public bool ImproveReadingClarity { get; set; } = true;
    public bool AddEvidenceBackedStrengths { get; set; } = true;
}
