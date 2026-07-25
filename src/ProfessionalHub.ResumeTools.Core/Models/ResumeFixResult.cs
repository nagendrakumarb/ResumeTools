namespace ProfessionalHub.ResumeTools.Models;

public sealed record ResumeFixOutcome(string Name, string Status, string Detail);
public sealed record ResumeFixResult(byte[] Bytes, IReadOnlyList<ResumeFixOutcome> Outcomes);
