namespace ProfessionalHub.ResumeTools.Models;

public sealed record AtsCheck(string Group, string Name, double Score, string Evidence, string Assessment, string Improvement, double Weight = 1)
{
    public bool IsComplete => Score >= 99.5;
}
public sealed record SkillSummary(IReadOnlyList<string> HardSkills, IReadOnlyList<string> SoftSkills);
public sealed record AtsResult(double Score, string Grade, IReadOnlyList<AtsCheck> Checks, SkillSummary Skills, string Summary);
