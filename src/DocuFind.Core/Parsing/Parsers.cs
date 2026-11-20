using System.Text;
using System.Text.RegularExpressions;
using DocuFind.Core.Text;

namespace DocuFind.Core.Parsing;

/// <summary>纯文本类：txt / md / csv / json / xml / 代码 / html。</summary>
public sealed class PlainTextParser : IDocumentParser
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".csv", ".tsv", ".json", ".xml", ".yaml", ".yml",
        ".ini", ".cfg", ".conf", ".sql", ".cs", ".java", ".py", ".js", ".ts", ".html", ".htm", ".rtf"
    };

    private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Compiled);

    public IReadOnlyCollection<string> Extensions => TextExtensions;

    public ParsedDocument Parse(string path)
    {
        var text = TextFileReader.ReadAllText(path);

        var ext = Path.GetExtension(path);
        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            text = StripHtml(text);

        var fileName = Path.GetFileName(path);
        var segments = new List<ParsedSegment> { new(0, text) };

        return new ParsedDocument
        {
            Path = path,
            FileName = fileName,
            Segments = segments,
            PageCount = 0
        };
    }

    private static string StripHtml(string html)
    {
        var cleaned = Regex.Replace(html, "(?is)<(script|style)[^>]*>.*?</\\1>", " ");
        cleaned = Regex.Replace(cleaned, "(?i)<br\\s*/?>", "\n");
        cleaned = Regex.Replace(cleaned, "(?i)</(p|div|li|tr|h[1-6])>", "\n");
        cleaned = HtmlTag.Replace(cleaned, " ");
        return System.Net.WebUtility.HtmlDecode(cleaned);
    }
}

/// <summary>PDF：逐页提取文字（不包含扫描件 OCR）。</summary>
public sealed class PdfParser : IDocumentParser
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".pdf" };

    public ParsedDocument Parse(string path)
    {
        var segments = new List<ParsedSegment>();
        var pageCount = 0;

        using (var document = UglyToad.PdfPig.PdfDocument.Open(path))
        {
            foreach (var page in document.GetPages())
            {
                pageCount = page.Number;
                var text = page.Text;
                if (!string.IsNullOrWhiteSpace(text))
                    segments.Add(new ParsedSegment(page.Number, text));
            }
        }

        var warning = segments.Count == 0 ? "PDF 中没有可提取的文字层（可能是扫描件）" : null;

        return new ParsedDocument
        {
            Path = path,
            FileName = Path.GetFileName(path),
            Segments = segments,
            PageCount = pageCount,
            Warning = warning
        };
    }
}

/// <summary>Word：按正文顺序取段落与表格，表格按行拍平。</summary>
public sealed class DocxParser : IDocumentParser
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".docx" };

    public ParsedDocument Parse(string path)
    {
        var sb = new StringBuilder();

        using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(path, false))
        {
            var body = document.MainDocumentPart?.Document?.Body;
            if (body != null)
            {
                foreach (var element in body.ChildElements)
                {
                    switch (element)
                    {
                        case DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph:
                        {
                            var text = paragraph.InnerText;
                            if (!string.IsNullOrWhiteSpace(text)) sb.AppendLine(text);
                            break;
                        }
                        case DocumentFormat.OpenXml.Wordprocessing.Table table:
                        {
                            foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
                            {
                                var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>()
                                    .Select(c => c.InnerText.Trim())
                                    .Where(c => c.Length > 0);

                                var line = string.Join(" | ", cells);
                                if (line.Length > 0) sb.AppendLine(line);
                            }
                            break;
                        }
                    }
                }
            }
        }

        return new ParsedDocument
        {
            Path = path,
            FileName = Path.GetFileName(path),
            Segments = new[] { new ParsedSegment(0, sb.ToString()) },
            PageCount = 0
        };
    }
}

/// <summary>Excel：每个工作表一段，每行拼成一行文本，保留工作表名。</summary>
public sealed class XlsxParser : IDocumentParser
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".xlsx", ".xlsm" };

    public ParsedDocument Parse(string path)
    {
        var segments = new List<ParsedSegment>();

        using (var document = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false))
        {
            var workbookPart = document.WorkbookPart;
            var sheets = workbookPart?.Workbook?.Sheets?.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>().ToList();
            if (workbookPart == null || sheets == null) throw new InvalidDataException("工作簿结构无法识别");

            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable?
                .Elements<DocumentFormat.OpenXml.Spreadsheet.SharedStringItem>()
                .Select(x => x.InnerText)
                .ToList() ?? new List<string>();

            var index = 0;
            foreach (var sheet in sheets)
            {
                index++;
                if (sheet.Id?.Value is not { } relationshipId) continue;

                var worksheetPart = (DocumentFormat.OpenXml.Packaging.WorksheetPart)workbookPart.GetPartById(relationshipId);
                var sb = new StringBuilder();
                sb.Append("【工作表 ").Append(sheet.Name?.Value ?? index.ToString()).Append("】\n");

                foreach (var row in worksheetPart.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>())
                {
                    var values = new List<string>();
                    foreach (var cell in row.Elements<DocumentFormat.OpenXml.Spreadsheet.Cell>())
                    {
                        var raw = cell.CellValue?.InnerText;
                        if (string.IsNullOrEmpty(raw) && cell.InlineString != null)
                            raw = cell.InlineString.InnerText;

                        if (string.IsNullOrEmpty(raw)) continue;

                        if (cell.DataType?.Value == DocumentFormat.OpenXml.Spreadsheet.CellValues.SharedString
                            && int.TryParse(raw, out var sharedIndex)
                            && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                        {
                            raw = sharedStrings[sharedIndex];
                        }

                        values.Add(raw);
                    }

                    if (values.Count > 0) sb.AppendLine(string.Join(" | ", values));
                }

                var text = sb.ToString();
                if (text.Length > 20) segments.Add(new ParsedSegment(index, text));
            }
        }

        return new ParsedDocument
        {
            Path = path,
            FileName = Path.GetFileName(path),
            Segments = segments,
            PageCount = segments.Count
        };
    }
}

/// <summary>PowerPoint：每页幻灯片一段。</summary>
public sealed class PptxParser : IDocumentParser
{
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".pptx" };

    public ParsedDocument Parse(string path)
    {
        var segments = new List<ParsedSegment>();

        using (var document = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(path, false))
        {
            var presentationPart = document.PresentationPart;
            var slideIds = presentationPart?.Presentation?.SlideIdList?.Elements<DocumentFormat.OpenXml.Presentation.SlideId>().ToList();

            if (presentationPart != null && slideIds != null)
            {
                var index = 0;
                foreach (var slideId in slideIds)
                {
                    index++;
                    if (slideId.RelationshipId?.Value is not { } relationshipId) continue;

                    var slidePart = (DocumentFormat.OpenXml.Packaging.SlidePart)presentationPart.GetPartById(relationshipId);
                    var texts = slidePart.Slide.Descendants<DocumentFormat.OpenXml.Drawing.Text>()
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t));

                    var text = string.Join("\n", texts);
                    if (text.Length > 0) segments.Add(new ParsedSegment(index, text));
                }
            }
        }

        return new ParsedDocument
        {
            Path = path,
            FileName = Path.GetFileName(path),
            Segments = segments,
            PageCount = segments.Count
        };
    }
}
