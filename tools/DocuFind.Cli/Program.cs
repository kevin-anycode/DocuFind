using System.Diagnostics;
using System.Text;
using DocuFind.Core;

Console.OutputEncoding = Encoding.UTF8;

var options = Parse(args);

if (options.SelfTest) return RunSelfTest();

using var engine = DocuFindEngine.Create(options.DatabasePath ?? DocuFindPaths.DatabasePath);

Console.WriteLine($"数据库: {options.DatabasePath ?? DocuFindPaths.DatabasePath}");
Console.WriteLine($"模型:   {engine.Embedder.Description}");
if (!string.IsNullOrEmpty(engine.EmbedderError)) Console.WriteLine($"警告:   {engine.EmbedderError}");

if (options.IngestPath is { Length: > 0 } ingestPath)
{
    var watch = Stopwatch.StartNew();
    var results = await engine.Ingestion.IngestAsync(new[] { ingestPath });
    watch.Stop();

    foreach (var result in results)
        Console.WriteLine($"  {result.Status,-10} {Path.GetFileName(result.Path),-24} 切片={result.Chunks,-4} {result.Message}");

    Console.WriteLine($"入库 {results.Count} 个文件，耗时 {watch.ElapsedMilliseconds} ms");
}

if (options.Query is { Length: > 0 } query)
{
    var watch = Stopwatch.StartNew();
    var hits = await engine.SearchAsync(query, 5);
    watch.Stop();

    Console.WriteLine();
    Console.WriteLine($"查询: {query}  ({watch.ElapsedMilliseconds} ms)");

    foreach (var hit in hits)
    {
        var page = hit.Page > 0 ? $" 第{hit.Page}页" : string.Empty;
        Console.WriteLine($"  {hit.Score:F4} [{hit.Source,-7}] {hit.FileName}{page}");
        Console.WriteLine($"      {hit.Snippet}");
    }

    if (hits.Count == 0) Console.WriteLine("  (无结果)");
}

// 调参用：直接看查询向量和各切片余弦的分布
if (options.Cosines is { Length: > 0 } probe)
{
    var vector = engine.Embedder.Embed(probe, DocuFind.Core.Embedding.EmbedKind.Query);
    var top = engine.Vectors.TopK(vector, 12);

    Console.WriteLine();
    Console.WriteLine($"余弦分布: {probe}");
    foreach (var (id, score) in top) Console.WriteLine($"  {score:F4}  chunk={id}");

    if (top.Count == 0) Console.WriteLine("  (没有向量)");
}

var counts = engine.GetCounts();
Console.WriteLine();
Console.WriteLine($"共 {counts.Documents} 篇文档 / {counts.Chunks} 个切片");
return 0;

static int RunSelfTest()
{
    var root = Path.Combine(Path.GetTempPath(), "docufind-selftest");
    if (Directory.Exists(root)) Directory.Delete(root, true);
    Directory.CreateDirectory(root);

    var samplesDirectory = Path.Combine(AppContext.BaseDirectory, "samples");
    var files = Directory.Exists(samplesDirectory)
        ? Directory.GetFiles(samplesDirectory)
        : Array.Empty<string>();

    if (files.Length == 0)
    {
        Console.WriteLine($"找不到样例文件：{samplesDirectory}");
        return 1;
    }

    using var engine = DocuFindEngine.Create(Path.Combine(root, "index.db"));
    Console.WriteLine($"模型: {engine.Embedder.Description} (可用={engine.Embedder.IsAvailable}, 维度={engine.Embedder.Dimensions})");

    var ingest = engine.Ingestion.IngestAsync(files).GetAwaiter().GetResult();
    foreach (var result in ingest)
        Console.WriteLine($"  入库 {result.Status,-9} {Path.GetFileName(result.Path),-20} 切片={result.Chunks} {result.Message}");

    var duplicate = engine.Ingestion.IngestAsync(new[] { files[0] }).GetAwaiter().GetResult();
    Console.WriteLine($"  重复入库 -> {duplicate[0].Status}");

    foreach (var query in new[] { "年假可以休几天", "出差住宿标准是多少", "查询延迟目标", "发票什么时候到账" })
    {
        var watch = Stopwatch.StartNew();
        var hits = engine.SearchAsync(query, 2).GetAwaiter().GetResult();
        watch.Stop();

        var top = hits.Count > 0 ? $"{hits[0].FileName} ({hits[0].Source})" : "(无结果)";
        Console.WriteLine($"  Q: {query,-22} -> {top}   {watch.ElapsedMilliseconds} ms");
    }

    return 0;
}

static Options Parse(string[] args)
{
    var options = new Options();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--self-test":
                options.SelfTest = true;
                break;
            case "--db" when i + 1 < args.Length:
                options.DatabasePath = args[++i];
                break;
            case "--ingest" when i + 1 < args.Length:
                options.IngestPath = args[++i];
                break;
            case "--query" when i + 1 < args.Length:
                options.Query = args[++i];
                break;
            case "--cosines" when i + 1 < args.Length:
                options.Cosines = args[++i];
                break;
            case "--help":
                Console.WriteLine("""
                    DocuFind CLI

                      --self-test              用内置样例跑一遍端到端自检
                      --db <path>              指定索引库（默认 %LOCALAPPDATA%\DocuFind\index.db）
                      --ingest <file|dir>      导入文件或目录
                      --query <text>           检索
                      --cosines <text>         打印该查询的余弦分布（调阈值用）
                    """);
                break;
        }
    }

    return options;
}

internal sealed class Options
{
    public bool SelfTest { get; set; }
    public string? DatabasePath { get; set; }
    public string? IngestPath { get; set; }
    public string? Query { get; set; }
    public string? Cosines { get; set; }
}
