using System.Security.Cryptography;
using DocuFind.Core.Embedding;
using DocuFind.Core.Index;
using DocuFind.Core.Parsing;
using DocuFind.Core.Text;

namespace DocuFind.Core.Ingestion;

public enum IngestStatus
{
    Added,
    Replaced,
    Skipped,
    Failed,
    Unsupported
}

public sealed record IngestItemResult(string Path, IngestStatus Status, int Chunks, string? Message);

public sealed record IngestProgress(int Completed, int Total, string FileName, string Stage);

public sealed record ConflictInfo(string Path, string FileName, long Size, DocumentRecord Existing);

public enum ConflictDecision
{
    Overwrite,
    KeepBoth,
    Skip
}

/// <summary>后台入库流水线：解析 → 切片 → 向量化 → 落库。全程在后台线程，UI 只收进度。</summary>
public sealed class IngestionService
{
    private const long MaxFileSize = 100L * 1024 * 1024;

    private readonly IndexDatabase _database;
    private readonly IEmbedder _embedder;
    private readonly VectorIndex _vectors;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public IngestionService(IndexDatabase database, IEmbedder embedder, VectorIndex vectors)
    {
        _database = database;
        _embedder = embedder;
        _vectors = vectors;
    }

    public IProgress<IngestProgress>? Progress { get; set; }

    public async Task<IReadOnlyList<IngestItemResult>> IngestAsync(
        IReadOnlyList<string> paths,
        Func<ConflictInfo, ConflictDecision>? conflictResolver = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => Ingest(paths, conflictResolver, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<IngestItemResult> Ingest(
        IReadOnlyList<string> paths,
        Func<ConflictInfo, ConflictDecision>? resolver,
        CancellationToken cancellationToken)
    {
        var files = Expand(paths);
        var results = new List<IngestItemResult>(files.Count);

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            Progress?.Report(new IngestProgress(i, files.Count, Path.GetFileName(file), "解析"));

            try
            {
                results.Add(IngestOne(file, resolver, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new IngestItemResult(file, IngestStatus.Failed, 0, FriendlyError(ex)));
            }

            Progress?.Report(new IngestProgress(i + 1, files.Count, Path.GetFileName(file), "完成"));
        }

        return results;
    }

    private IngestItemResult IngestOne(string path, Func<ConflictInfo, ConflictDecision>? resolver, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists) return new IngestItemResult(path, IngestStatus.Failed, 0, "文件不存在");
        if (!DocumentParserFactory.IsSupported(path)) return new IngestItemResult(path, IngestStatus.Unsupported, 0, "不支持的文件类型");
        if (info.Length > MaxFileSize) return new IngestItemResult(path, IngestStatus.Failed, 0, "文件超过 100 MB，已跳过");
        if (info.Length == 0) return new IngestItemResult(path, IngestStatus.Failed, 0, "空文件");

        var hash = ComputeHash(path);

        var existingByHash = _database.FindByHash(hash);
        if (existingByHash is not null)
            return new IngestItemResult(path, IngestStatus.Skipped, 0, $"内容已收录：{existingByHash.FileName}");

        var fileName = Path.GetFileName(path);
        var existingByName = _database.FindByName(fileName);
        var status = IngestStatus.Added;

        if (existingByName is not null)
        {
            var decision = resolver?.Invoke(new ConflictInfo(path, fileName, info.Length, existingByName))
                           ?? ConflictDecision.Overwrite;

            switch (decision)
            {
                case ConflictDecision.Skip:
                    return new IngestItemResult(path, IngestStatus.Skipped, 0, "已按你的选择跳过");
                case ConflictDecision.Overwrite:
                    RemoveDocument(existingByName.Id);
                    status = IngestStatus.Replaced;
                    break;
                case ConflictDecision.KeepBoth:
                    fileName = NextAvailableName(path, fileName);
                    break;
            }
        }

        var parsed = DocumentParserFactory.Parse(path);
        cancellationToken.ThrowIfCancellationRequested();

        if (parsed.Segments.Count == 0 || parsed.TotalLength < 4)
            return new IngestItemResult(path, IngestStatus.Failed, 0, parsed.Warning ?? "没有提取到文字内容");

        var chunks = new List<TextChunk>();
        foreach (var segment in parsed.Segments)
            chunks.AddRange(Chunker.Split(segment.Text, segment.Page, chunks.Count));

        if (chunks.Count == 0)
            return new IngestItemResult(path, IngestStatus.Failed, 0, "没有提取到文字内容");

        Progress?.Report(new IngestProgress(0, chunks.Count, fileName, "向量化"));

        var vectors = new List<float[]?>(chunks.Count);
        if (_embedder.IsAvailable)
        {
            for (var i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                vectors.Add(_embedder.Embed(chunks[i].Text, EmbedKind.Document));

                if (i % 16 == 0) Progress?.Report(new IngestProgress(i, chunks.Count, fileName, "向量化"));
            }
        }

        var documentId = _database.InsertDocument(path, fileName, hash, info.Length, info.LastWriteTimeUtc.Ticks, parsed.PageCount);
        var chunkIds = _database.InsertChunks(documentId, chunks, _embedder.IsAvailable ? vectors : null);

        for (var i = 0; i < chunkIds.Count && i < vectors.Count; i++)
        {
            if (vectors[i] is { } vector) _vectors.Add(chunkIds[i], vector);
        }

        var note = parsed.Warning;
        return new IngestItemResult(path, status, chunks.Count, note);
    }

    public void RemoveDocument(long documentId)
    {
        var chunkIds = _database.GetChunkIds(documentId);
        _database.DeleteDocument(documentId);
        _vectors.MarkDead(chunkIds);
    }

    private string NextAvailableName(string path, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{stem} ({i}){extension}";
            if (_database.FindByName(candidate) is null) return candidate;
        }

        return $"{stem} ({Guid.NewGuid():N}){extension}";
    }

    private static List<string> Expand(IReadOnlyList<string> paths)
    {
        var files = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    files.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(DocumentParserFactory.IsSupported));
                }
                else if (File.Exists(path))
                {
                    files.Add(path);
                }
            }
            catch
            {
                // 无权限的目录直接跳过
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ComputeHash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "没有权限读取该文件",
        IOException io => $"读取失败：{io.Message}",
        InvalidDataException => "文件结构损坏，无法解析",
        _ => ex.Message
    };
}
