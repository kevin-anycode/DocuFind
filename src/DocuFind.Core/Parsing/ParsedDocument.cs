namespace DocuFind.Core.Parsing;

/// <summary>一段带页码的正文（一页 PDF / 一张工作表 / 一页幻灯片）。</summary>
public sealed record ParsedSegment(int Page, string Text);

/// <summary>解析后的文档：若干段正文 + 页码信息。</summary>
public sealed class ParsedDocument
{
    public required string Path { get; init; }
    public required string FileName { get; init; }
    public required IReadOnlyList<ParsedSegment> Segments { get; init; }
    public int PageCount { get; init; }
    public string? Warning { get; init; }

    public int TotalLength => Segments.Sum(s => s.Text.Length);
}

public interface IDocumentParser
{
    /// <summary>支持的扩展名，小写、带点。</summary>
    IReadOnlyCollection<string> Extensions { get; }

    ParsedDocument Parse(string path);
}
