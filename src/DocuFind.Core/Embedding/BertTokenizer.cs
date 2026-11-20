using System.Text;

namespace DocuFind.Core.Embedding;

/// <summary>
/// 精简版 BERT WordPiece 分词器，专门给 bge-* / m3e 这类中文 BERT 模型用。
/// 中文按单字切分（与官方 tokenizer 行为一致），英文走 wordpiece 贪心最长匹配。
/// </summary>
public sealed class BertTokenizer
{
    private const int MaxCharsPerWord = 100;

    private readonly Dictionary<string, int> _vocab;

    public int ClsId { get; }
    public int SepId { get; }
    public int UnkId { get; }
    public int PadId { get; }

    private BertTokenizer(Dictionary<string, int> vocab)
    {
        _vocab = vocab;
        ClsId = Lookup("[CLS]", 101);
        SepId = Lookup("[SEP]", 102);
        UnkId = Lookup("[UNK]", 100);
        PadId = Lookup("[PAD]", 0);
    }

    public static BertTokenizer Load(string vocabPath)
    {
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        var index = 0;

        foreach (var line in File.ReadLines(vocabPath, Encoding.UTF8))
        {
            vocab[line.TrimEnd('\r')] = index++;
        }

        if (vocab.Count < 1000)
            throw new InvalidDataException($"词表文件看起来不完整：{vocabPath}");

        return new BertTokenizer(vocab);
    }

    public int VocabSize => _vocab.Count;

    private int Lookup(string token, int fallback) =>
        _vocab.TryGetValue(token, out var id) ? id : fallback;

    public List<long> Encode(string? text, int maxLength)
    {
        var ids = new List<long>(Math.Min(maxLength, 512)) { ClsId };

        if (!string.IsNullOrEmpty(text))
        {
            foreach (var word in BasicTokenize(text))
            {
                AddWordPiece(word, ids);
                if (ids.Count >= maxLength - 1) break;
            }
        }

        ids.Add(SepId);

        if (ids.Count > maxLength) ids.RemoveRange(maxLength, ids.Count - maxLength);
        return ids;
    }

    private void AddWordPiece(string word, List<long> ids)
    {
        if (word.Length > MaxCharsPerWord)
        {
            ids.Add(UnkId);
            return;
        }

        var start = 0;
        var pieces = new List<long>(4);

        while (start < word.Length)
        {
            var end = word.Length;
            var found = -1;

            while (end > start)
            {
                var piece = start > 0 ? "##" + word[start..end] : word[start..end];
                if (_vocab.TryGetValue(piece, out var id))
                {
                    found = id;
                    break;
                }

                end--;
            }

            if (found < 0)
            {
                // BERT 规则：整词无法切分时整体记为 [UNK]
                ids.Add(UnkId);
                return;
            }

            pieces.Add(found);
            start = end;
        }

        ids.AddRange(pieces);
    }

    private static IEnumerable<string> BasicTokenize(string text)
    {
        var current = new StringBuilder(32);

        foreach (var ch in text)
        {
            if (ch == '\uFFFD' || char.IsControl(ch)) continue;

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                continue;
            }

            if (IsCjk(ch) || IsPunctuation(ch))
            {
                if (current.Length > 0)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                yield return ch.ToString();
                continue;
            }

            current.Append(char.ToLowerInvariant(ch));
        }

        if (current.Length > 0) yield return current.ToString();
    }

    private static bool IsCjk(char c) =>
        (c >= 0x4E00 && c <= 0x9FFF) ||
        (c >= 0x3400 && c <= 0x4DBF) ||
        (c >= 0xF900 && c <= 0xFAFF) ||
        (c >= 0x3040 && c <= 0x30FF) ||
        (c >= 0xAC00 && c <= 0xD7AF) ||
        (c >= 0x3000 && c <= 0x303F);

    private static bool IsPunctuation(char c) =>
        char.IsPunctuation(c) || char.IsSymbol(c);
}
