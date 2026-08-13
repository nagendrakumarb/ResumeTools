namespace ProfessionalHub.ResumeTools.Models;

public sealed record PdfLayoutLine(
    int Page,
    double Left,
    double Bottom,
    double Width,
    double Height,
    double FontSize,
    bool Bold,
    string Text);

public sealed record PdfLayoutProfile(
    double PageWidth,
    double PageHeight,
    double? ColumnSplitX,
    IReadOnlyList<PdfLayoutLine> Lines);

public sealed record ParsedResume(
    string Text,
    string FileType,
    string OriginalFileName,
    byte[] OriginalBytes,
    long FileSizeBytes,
    int PageCount,
    double AverageFontSize,
    double BoldPercentage,
    PdfLayoutProfile? PdfLayout = null);
