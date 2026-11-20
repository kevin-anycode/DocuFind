using System.Text;

namespace DocuFind.Core.Text;

/// <summary>切片结果。Page 从 1 开始，0 表示该格式没有页码概念。</summary>
public readonly record struct TextChunk(int Ordinal, int Page, int CharStart, int Length, string Text);

/// <summary>
/// 中文友好的切片器：先按段落/句末标点断句，再聚合成约 450 字的块，块之间保留 60 字重叠，
/// 避免答案正好被切在边界上。
/// </summary>
public static class Chunker
{
    public const int TargetSize = 450;
    public const int OverlapSize = 60;
    public const int HardLimit = 900;
    private const int MinChunkLength = 8;

    public static List<TextChunk> Split(string? text, int page, int startOrdinal = 0)
    {
        var chunks = new List<TextChunk>();
        if (string.IsNullOrWhiteSpace(text)) return chunks;

        var pieces = ToPieces(Normalize(text));
        var buffer = new StringBuilder(TargetSize * 2);
        var ordinal = startOrdinal;
        var bufferStart = -1;

        void Flush(bool final)
        {
            var raw = buffer.ToString();
            var content = raw.Trim();
            if (content.Length >= MinChunkLength)
            {
                chunks.Add(new TextChunk(ordinal++, page, Math.Max(bufferStart, 0), content.Length, content));
            }

            if (final)
            {
                buffer.Clear();
                bufferStart = -1;
                return;
            }

            var keep = Math.Min(OverlapSize, raw.Length);
            var tail = raw[^keep..];
            buffer.Clear().Append(tail);
            bufferStart = bufferStart + raw.Length - keep;
        }

        foreach (var (start, piece) in pieces)
        {
            if (bufferStart < 0) bufferStart = start;
            buffer.Append(piece);
            if (buffer.Length >= TargetSize) Flush(false);
        }

        Flush(true);
        return chunks;
    }

    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSpace = false;

        foreach (var c in text)
        {
            switch (c)
            {
                case '\r':
                    continue;
                case '\n':
                    sb.Append('\n');
                    lastWasSpace = false;
                    continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;
        }

        return sb.ToString();
    }

    private static List<(int Start, string Text)> ToPieces(string text)
    {
        var pieces = new List<(int, string)>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!IsBoundary(text[i])) continue;

            var end = i + 1;
            while (end < text.Length && IsTrailing(text[end])) end++;

            AddPiece(pieces, text, start, end);
            start = end;
            i = end - 1;
        }

        if (start < text.Length) AddPiece(pieces, text, start, text.Length);
        return pieces;
    }

    private static void AddPiece(List<(int, string)> pieces, string text, int start, int end)
    {
        while (end - start > HardLimit)
        {
            var cut = start + HardLimit;
            var searchFrom = start + HardLimit / 2;
            var space = text.LastIndexOf(' ', cut - 1, cut - searchFrom);
            if (space > searchFrom) cut = space + 1;

            pieces.Add((start, text[start..cut]));
            start = cut;
        }

        if (end > start) pieces.Add((start, text[start..end]));
    }

    private static bool IsBoundary(char c) =>
        c is '。' or '！' or '？' or '；' or '!' or '?' or ';' or '\n';

    private static bool IsTrailing(char c) =>
        "”’」』】）》)]\"'".IndexOf(c) >= 0;
}
