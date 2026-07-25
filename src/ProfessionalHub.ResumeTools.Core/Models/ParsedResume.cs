namespace ProfessionalHub.ResumeTools.Models;

public sealed record ParsedResume(string Text, string FileType, string OriginalFileName, byte[] OriginalBytes, long FileSizeBytes, int PageCount, double AverageFontSize, double BoldPercentage);
