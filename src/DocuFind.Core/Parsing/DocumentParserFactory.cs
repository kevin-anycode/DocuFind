namespace DocuFind.Core.Parsing;

/// <summary>按扩展名分发到具体解析器。</summary>
public static class DocumentParserFactory
{
    private static readonly List<IDocumentParser> Parsers = new()
    {
        new PdfParser(),
        new DocxParser(),
        new XlsxParser(),
        new PptxParser(),
        new PlainTextParser()
    };

    private static readonly Dictionary<string, IDocumentParser> ByExtension = BuildMap();

    private static Dictionary<string, IDocumentParser> BuildMap()
    {
        var map = new Dictionary<string, IDocumentParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var parser in Parsers)
            foreach (var ext in parser.Extensions)
                map[ext] = parser;
        return map;
    }

    /// <summary>支持的文件扩展名（小写、带点），用于文件对话框和拖放过滤。</summary>
    public static IReadOnlyCollection<string> SupportedExtensions => ByExtension.Keys;

    public static bool IsSupported(string path) =>
        ByExtension.ContainsKey(Path.GetExtension(path));

    public static ParsedDocument Parse(string path)
    {
        if (!ByExtension.TryGetValue(Path.GetExtension(path), out var parser))
            throw new NotSupportedException($"暂不支持的文件类型：{Path.GetExtension(path)}");

        return parser.Parse(path);
    }
}
