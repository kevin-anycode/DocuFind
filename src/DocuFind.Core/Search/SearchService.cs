using System.Text;
using System.Text.RegularExpressions;
using DocuFind.Core.Embedding;
using DocuFind.Core.Index;

namespace DocuFind.Core.Search;

public readonly record struct HighlightRange(int Start, int Length);

public sealed record SearchHit(
    long ChunkId,
    long DocId,
    string FileName,
    string DocPath,
    int Page,
    string Snippet,
    IReadOnlyList<HighlightRange> Highlights,
    double Score,
    string Source);

/// <summary>
/// 混合检索：向量召回 + FTS5 关键词召回，用 RRF 融合。
/// 只靠向量会漏掉编号、人名、精确词；只靠关键词又搜不到同义表达，所以两路都要。
/// </summary>
public sealed class SearchService
{
    private const int ChannelLimit = 80;
    private const int RrfK = 60;

    /// <summary>
    /// 向量通道的绝对门槛。bge-small-zh 对任何中文都会给出 0.30~0.40 的"底噪"余弦，
    /// 不比一下绝对值，随便打一串「啊啊啊」也会把库里所有片段都排出来。
    /// 实测：真正相关的查询 top1 都在 0.46 以上（0.47~0.70），无关内容在 0.39 以下。
    /// </summary>
    private const double MinVectorScore = 0.42;

    /// <summary>
    /// 关键词已经命中时，向量通道补充上下文的门槛。
    /// 这时答案基本已经有了，多出来的只应该是"同一件事的另一段"，
    /// 所以把绝对值抬到 0.50、相对值抬到最好分×0.95，并且最多补 2 条。
    /// </summary>
    private const double KeywordAugmentScore = 0.50;

    private static readonly Regex TermSplitter = new(
        @"[\s,，。;；、|/\\()\[\]{}<>""'“”‘’《》【】!?！？:：]+",
        RegexOptions.Compiled);

    private readonly IndexDatabase _database;
    private readonly IEmbedder _embedder;
    private readonly VectorIndex _vectors;

    public SearchService(IndexDatabase database, IEmbedder embedder, VectorIndex vectors)
    {
        _database = database;
        _embedder = embedder;
        _vectors = vectors;
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int take, CancellationToken cancellationToken = default) =>
        Task.Run(() => Search(query, take, cancellationToken), cancellationToken);

    public IReadOnlyList<SearchHit> Search(string query, int take, CancellationToken cancellationToken = default)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0 || take <= 0) return Array.Empty<SearchHit>();

        var terms = SplitTerms(query);

        // 关键词这一路先跑：它命中的东西基本可以确信是对的，
        // 后面用它的有无来决定向量通道该"松"还是"紧"。
        var keywordIds = _database.SearchKeyword(terms, ChannelLimit);
        var hasKeyword = keywordIds.Count > 0;

        var vectorIds = new List<long>();
        if (_embedder.IsAvailable && _vectors.Count > 0)
        {
            try
            {
                var queryVector = _embedder.Embed(query, EmbedKind.Query);
                var vectorRanked = _vectors.TopK(queryVector, ChannelLimit);

                // 关键词已经命中了：向量这一路只当"补充上下文"，
                // 门槛抬到 0.50 / 最好分×0.95，而且最多补 2 条——
                // 否则「考勤时间」这种词会把只沾一点边的方案文档也带出来。
                // 关键词没命中：向量是唯一线索，用 0.42 / ×0.86 的常规门槛。
                var absolute = hasKeyword ? KeywordAugmentScore : MinVectorScore;
                var relative = hasKeyword ? 0.95 : 0.86;
                var limit = hasKeyword ? 2 : int.MaxValue;

                var cutoff = absolute;
                if (vectorRanked.Count > 0) cutoff = Math.Max(absolute, vectorRanked[0].Score * relative);

                foreach (var (id, score) in vectorRanked)
                {
                    if (score < cutoff) break; // TopK 已按分数降序
                    vectorIds.Add(id);
                    if (vectorIds.Count >= limit) break;
                }
            }
            catch
            {
                // 向量通道失败不影响关键词检索
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        var scores = new Dictionary<long, double>();
        var sources = new Dictionary<long, int>();

        Accumulate(vectorIds, 1.0, 1);
        Accumulate(keywordIds, 1.0, 2);

        if (scores.Count == 0) return Array.Empty<SearchHit>();

        var ranked = scores
            .OrderByDescending(kv => kv.Value)
            .Take(Math.Max(take * 4, 20))
            .ToList();

        var chunkMap = _database.GetChunks(ranked.Select(x => x.Key).ToList());
        var docMap = _database.GetDocumentsByIds(chunkMap.Values.Select(c => c.DocId).Distinct().ToList());

        var hits = new List<SearchHit>(take);
        var perDocument = new Dictionary<long, int>();

        foreach (var (chunkId, score) in ranked)
        {
            if (!chunkMap.TryGetValue(chunkId, out var chunk)) continue;
            if (!docMap.TryGetValue(chunk.DocId, out var document)) continue;

            perDocument.TryGetValue(document.Id, out var seen);
            if (seen >= 2) continue;

            perDocument[document.Id] = seen + 1;

            var (snippet, highlights) = BuildSnippet(chunk.Text, terms);
            hits.Add(new SearchHit(
                chunkId,
                document.Id,
                document.FileName,
                document.Path,
                chunk.Page,
                snippet,
                highlights,
                score,
                sources.TryGetValue(chunkId, out var source) ? (source == 3 ? "both" : source == 1 ? "vector" : "keyword") : "keyword"));

            if (hits.Count >= take) break;
        }

        return hits;

        void Accumulate(IReadOnlyList<long> ids, double weight, int sourceFlag)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                var contribution = weight / (RrfK + i + 1);
                scores[id] = scores.GetValueOrDefault(id) + contribution;
                sources[id] = sources.GetValueOrDefault(id) | sourceFlag;
            }
        }
    }

    public static List<string> SplitTerms(string query) =>
        TermSplitter.Split(query)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static (string Snippet, IReadOnlyList<HighlightRange> Highlights) BuildSnippet(string text, IReadOnlyList<string> terms)
    {
        const int window = 200;
        const int lead = 60;

        var anchor = -1;
        foreach (var term in terms)
        {
            var index = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (anchor < 0 || index < anchor)) anchor = index;
        }

        var start = anchor < 0 ? 0 : Math.Max(0, anchor - lead);
        var end = Math.Min(text.Length, start + window);
        if (end - start < window) start = Math.Max(0, end - window);

        var body = text[start..end].Replace('\n', ' ').Replace('\r', ' ');
        var prefix = start > 0 ? "…" : string.Empty;
        var suffix = end < text.Length ? "…" : string.Empty;
        var snippet = prefix + body + suffix;

        var ranges = new List<HighlightRange>();
        foreach (var term in terms)
        {
            if (term.Length == 0) continue;

            var position = 0;
            while (position < snippet.Length)
            {
                var index = snippet.IndexOf(term, position, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                ranges.Add(new HighlightRange(index, term.Length));
                position = index + term.Length;
            }
        }

        return (snippet.Trim(), Merge(ranges, snippet));
    }

    private static List<HighlightRange> Merge(List<HighlightRange> ranges, string snippet)
    {
        if (ranges.Count == 0) return ranges;

        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<HighlightRange> { ranges[0] };

        for (var i = 1; i < ranges.Count; i++)
        {
            var last = merged[^1];
            var current = ranges[i];

            if (current.Start <= last.Start + last.Length)
            {
                var end = Math.Max(last.Start + last.Length, current.Start + current.Length);
                merged[^1] = last with { Length = end - last.Start };
            }
            else
            {
                merged.Add(current);
            }
        }

        var trimmed = snippet.TrimStart();
        var shift = snippet.Length - trimmed.Length;
        if (shift > 0)
        {
            merged = merged
                .Select(r => r with { Start = r.Start - shift })
                .Where(r => r.Start + r.Length > 0)
                .Select(r => r.Start < 0 ? r with { Start = 0, Length = r.Length + r.Start } : r)
                .ToList();
        }

        return merged;
    }
}
