using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProfessionalHub.ResumeTools.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace ProfessionalHub.ResumeTools.Services;

public sealed class ResumeParserService
{
    public async Task<ParsedResume> ParseAsync(Stream source, string fileName)
    {
        await using var buffer = new MemoryStream();
        await source.CopyToAsync(buffer); var size = buffer.Length; buffer.Position = 0;
        var result = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => ReadPdf(buffer, fileName, size),
            ".docx" => ReadDocx(buffer, fileName, size),
            _ => throw new NotSupportedException("Only PDF and DOCX resumes are supported.")
        };
        if (string.IsNullOrWhiteSpace(result.Text)) throw new InvalidDataException("No selectable text was found. Scanned PDFs require OCR and are not supported offline yet.");
        return result;
    }

    private static ParsedResume ReadPdf(MemoryStream stream, string fileName, long size)
    {
        using var document = PdfDocument.Open(stream);
        var pages = document.GetPages().ToArray();
        var letters = pages.SelectMany(p => p.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToArray();
        var averageFont = letters.Length == 0 ? 0 : letters.Average(l => l.PointSize);
        var bold = letters.Length == 0 ? 0 : letters.Count(l => (l.FontName ?? "").Contains("bold", StringComparison.OrdinalIgnoreCase)) * 100d / letters.Length;
        return new ParsedResume(string.Join(Environment.NewLine, pages.Select(page => ContentOrderTextExtractor.GetText(page))), "PDF", fileName, stream.ToArray(), size, pages.Length, Math.Round(averageFont, 1), Math.Round(bold, 1));
    }

    private static ParsedResume ReadDocx(MemoryStream stream, string fileName, long size)
    {
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        var runs = body?.Descendants<Run>().ToArray() ?? [];
        var characters = runs.Sum(r => r.InnerText.Length);
        var boldCharacters = runs.Where(r => r.RunProperties?.Bold is not null).Sum(r => r.InnerText.Length);
        var sizes = runs.Select(r => r.RunProperties?.FontSize?.Val?.Value).Where(v => double.TryParse(v, out _)).Select(v => double.Parse(v!) / 2d).ToArray();
        var pagesText = document.ExtendedFilePropertiesPart?.Properties?.Pages?.Text;
        var pages = int.TryParse(pagesText, out var pageCount) ? pageCount : Math.Max(1, (int)Math.Ceiling((body?.InnerText.Length ?? 0) / 3500d));
        var text = body is null ? "" : string.Join(Environment.NewLine,
            body.Descendants<Paragraph>()
                .Select(p => p.InnerText.Trim())
                .Where(t => !string.IsNullOrWhiteSpace(t)));
        return new ParsedResume(text, "DOCX", fileName, stream.ToArray(), size, pages, sizes.Length == 0 ? 0 : Math.Round(sizes.Average(), 1), characters == 0 ? 0 : Math.Round(boldCharacters * 100d / characters, 1));
    }
}
