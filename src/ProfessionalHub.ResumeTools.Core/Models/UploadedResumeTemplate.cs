namespace ProfessionalHub.ResumeTools.Models;

public sealed record UploadedResumeTemplate(string FileName, string FileType, byte[] Bytes);
public sealed record TemplateConversionResult(byte[] Bytes, bool ExactLayout, string Message);
public sealed record CorrectedTemplateConversionResult(byte[] Bytes, bool ExactLayout, string Message, IReadOnlyList<ResumeFixOutcome> Outcomes);
public sealed record ImageTemplateAnalysis(
    string AccentHex,
    string Layout,
    string Sidebar,
    double AspectRatio,
    string Typography = "sans-serif",
    bool SectionRules = false,
    string HeaderAlignment = "left",
    double Density = 0);
