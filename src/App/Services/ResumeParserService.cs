using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ProfessionalHub.ResumeTools.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

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
        var layoutLines = pages.SelectMany((page, index) => ExtractLayoutLines(page, index + 1)).ToArray();
        var firstPage = pages.FirstOrDefault();
        var split = firstPage is null ? null : DetectColumnSplit(layoutLines.Where(line => line.Page == 1).ToArray(), firstPage.Width);
        var layout = firstPage is null
            ? null
            : new PdfLayoutProfile(firstPage.Width, firstPage.Height, split, layoutLines);
        var semanticText = layout is null
            ? string.Join(Environment.NewLine, pages.Select(page => ContentOrderTextExtractor.GetText(page)))
            : BuildSemanticText(layout);
        return new ParsedResume(semanticText, "PDF", fileName, stream.ToArray(), size, pages.Length, Math.Round(averageFont, 1), Math.Round(bold, 1), layout);
    }

    private static IReadOnlyList<PdfLayoutLine> ExtractLayoutLines(Page page, int pageNumber)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .ToArray();
        if (words.Length == 0) return [];

        var rows = words
            .GroupBy(word => Math.Round(word.BoundingBox.Bottom / 2.5) * 2.5)
            .OrderByDescending(group => group.Key);
        var result = new List<PdfLayoutLine>();
        foreach (var row in rows)
        {
            var ordered = row.OrderBy(word => word.BoundingBox.Left).ToArray();
            if (ordered.Length == 0) continue;
            var fragments = new List<List<Word>> { new() { ordered[0] } };
            for (var index = 1; index < ordered.Length; index++)
            {
                var previous = ordered[index - 1];
                var current = ordered[index];
                var gap = current.BoundingBox.Left - previous.BoundingBox.Right;
                if (gap > Math.Max(18, page.Width * 0.025)) fragments.Add([]);
                fragments[^1].Add(current);
            }

            foreach (var fragment in fragments)
            {
                var text = string.Join(" ", fragment.Select(word => word.Text)).Trim();
                if (string.IsNullOrWhiteSpace(text)) continue;
                var left = fragment.Min(word => word.BoundingBox.Left);
                var right = fragment.Max(word => word.BoundingBox.Right);
                var bottom = fragment.Min(word => word.BoundingBox.Bottom);
                var top = fragment.Max(word => word.BoundingBox.Top);
                var fragmentLetters = fragment.SelectMany(word => word.Letters).ToArray();
                var fontSize = fragmentLetters.Length == 0 ? 10d : fragmentLetters.Average(letter => letter.PointSize);
                var bold = fragmentLetters.Length > 0 && fragmentLetters.Count(letter => (letter.FontName ?? "").Contains("bold", StringComparison.OrdinalIgnoreCase)) >= fragmentLetters.Length / 2d;
                result.Add(new PdfLayoutLine(pageNumber, left, bottom, right - left, top - bottom, fontSize, bold, text));
            }
        }
        return result;
    }

    private static double? DetectColumnSplit(IReadOnlyList<PdfLayoutLine> lines, double pageWidth)
    {
        if (lines.Count < 8) return null;
        var starts = lines.Select(line => line.Left).OrderBy(x => x).ToArray();
        var leftCenter = starts[starts.Length / 4];
        var rightCenter = starts[(starts.Length * 3) / 4];
        for (var iteration = 0; iteration < 20; iteration++)
        {
            var left = starts.Where(x => Math.Abs(x - leftCenter) <= Math.Abs(x - rightCenter)).ToArray();
            var right = starts.Where(x => Math.Abs(x - leftCenter) > Math.Abs(x - rightCenter)).ToArray();
            if (left.Length == 0 || right.Length == 0) return null;
            var nextLeft = left.Average();
            var nextRight = right.Average();
            if (Math.Abs(nextLeft - leftCenter) < 0.1 && Math.Abs(nextRight - rightCenter) < 0.1) break;
            leftCenter = nextLeft;
            rightCenter = nextRight;
        }
        if (rightCenter - leftCenter < pageWidth * 0.16) return null;
        var split = (leftCenter + rightCenter) / 2d;
        var leftCount = lines.Count(line => line.Left + line.Width / 2d < split);
        var rightCount = lines.Count - leftCount;
        return leftCount >= 3 && rightCount >= 3 ? split : null;
    }

    private static string BuildSemanticText(PdfLayoutProfile layout)
    {
        var ordered = layout.ColumnSplitX is not double split
            ? layout.Lines.OrderBy(line => line.Page).ThenByDescending(line => line.Bottom).ThenBy(line => line.Left)
            : layout.Lines.OrderBy(line => line.Page)
                .ThenBy(line => line.Left + line.Width / 2d < split ? 0 : 1)
                .ThenByDescending(line => line.Bottom);
        return string.Join(Environment.NewLine, ordered.Select(line => line.Text));
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
