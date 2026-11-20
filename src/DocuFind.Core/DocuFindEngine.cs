using DocuFind.Core.Embedding;
using DocuFind.Core.Index;
using DocuFind.Core.Ingestion;
using DocuFind.Core.Search;

namespace DocuFind.Core;

/// <summary>
/// 应用层门面：把索引库、向量模型、检索、入库串起来。
/// 模型缺失时自动退化为关键词检索，不会阻断使用。
/// </summary>
public sealed class DocuFindEngine : IDisposable
{
    private readonly IndexDatabase _database;

    public IEmbedder Embedder { get; }
    public VectorIndex Vectors { get; }
    public SearchService Search { get; }
    public IngestionService Ingestion { get; }
    public string? EmbedderError { get; }
    public bool NeedsReindex { get; private set; }

    private DocuFindEngine(IndexDatabase database, IEmbedder embedder, VectorIndex vectors, string? embedderError, bool needsReindex)
    {
        _database = database;
        Embedder = embedder;
        Vectors = vectors;
        EmbedderError = embedderError;
        NeedsReindex = needsReindex;

        Search = new SearchService(database, embedder, vectors);
        Ingestion = new IngestionService(database, embedder, vectors);
    }

    public static DocuFindEngine Create(string? databasePath = null)
    {
        DocuFindPaths.EnsureCreated();

        var database = new IndexDatabase(databasePath ?? DocuFindPaths.DatabasePath);
        var embedder = EmbedderFactory.Create(out var embedderError);
        var vectors = new VectorIndex();

        var (ids, data, count, dimensions) = database.LoadVectors();
        var needsReindex = false;

        if (count > 0)
        {
            if (embedder.IsAvailable && dimensions != embedder.Dimensions)
            {
                needsReindex = true;
            }
            else
            {
                vectors.Load(ids, data, dimensions);
            }
        }

        database.SetMeta("model", embedder.Description);
        return new DocuFindEngine(database, embedder, vectors, embedderError, needsReindex);
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int take, CancellationToken cancellationToken = default) =>
        Search.SearchAsync(query, take, cancellationToken);

    public (int Documents, int Chunks) GetCounts() => _database.GetCounts();

    public IReadOnlyList<DocumentRecord> GetDocuments() => _database.GetDocuments();

    public void RemoveDocument(long documentId) => Ingestion.RemoveDocument(documentId);

    public void ClearAll()
    {
        _database.Clear();
        Vectors.Reset();
    }

    /// <summary>用现有切片重新生成向量（模型更换或索引损坏时使用）。</summary>
    public async Task ReindexAsync(IProgress<IngestProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Embedder.IsAvailable) throw new InvalidOperationException("向量模型未加载，无法重建索引");

        var documents = _database.GetDocuments();
        var total = documents.Sum(d => d.ChunkCount);
        var done = 0;

        Vectors.Reset();

        foreach (var document in documents)
        {
            var chunkIds = _database.GetChunkIds(document.Id);
            var chunks = _database.GetChunks(chunkIds);

            foreach (var id in chunkIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!chunks.TryGetValue(id, out var chunk)) continue;

                var vector = await Task.Run(() => Embedder.Embed(chunk.Text, EmbedKind.Document), cancellationToken)
                    .ConfigureAwait(false);

                Vectors.Add(id, vector);
                done++;

                if (done % 32 == 0) progress?.Report(new IngestProgress(done, total, document.FileName, "重建索引"));
            }
        }

        NeedsReindex = false;
        progress?.Report(new IngestProgress(total, total, string.Empty, "重建索引完成"));
    }

    public void Dispose()
    {
        Embedder.Dispose();
        _database.Dispose();
    }
}
