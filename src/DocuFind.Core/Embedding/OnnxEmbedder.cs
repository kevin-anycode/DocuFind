using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace DocuFind.Core.Embedding;

public enum EmbedKind
{
    Document,
    Query
}

public interface IEmbedder : IDisposable
{
    bool IsAvailable { get; }
    int Dimensions { get; }
    string Description { get; }
    float[] Embed(string text, EmbedKind kind);
}

/// <summary>模型缺失时的占位实现：检索自动退化为纯关键词模式。</summary>
public sealed class NullEmbedder : IEmbedder
{
    public static readonly NullEmbedder Instance = new();

    public bool IsAvailable => false;
    public int Dimensions => 0;
    public string Description => "未加载向量模型（当前为关键词检索）";

    public float[] Embed(string text, EmbedKind kind) => Array.Empty<float>();
    public void Dispose() { }
}

/// <summary>
/// bge-small-zh-v1.5 的 ONNX CPU 推理。CLS 池化 + L2 归一化，
/// 查询侧加官方中文指令前缀，文档侧不加。
/// </summary>
public sealed class OnnxEmbedder : IEmbedder
{
    /// <summary>bge v1.5 官方建议：只给「短查询」加指令，文档侧不加。</summary>
    public const string QueryInstruction = "为这个句子生成表示以用于检索相关文章：";

    private const int MaxTokens = 512;

    private readonly InferenceSession _session;
    private readonly BertTokenizer _tokenizer;
    private readonly string _outputName;
    private readonly bool _hasTokenTypeIds;
    private readonly object _gate = new();

    public bool IsAvailable => true;
    public int Dimensions { get; }
    public string Description => "bge-small-zh-v1.5 (int8, ONNX CPU)";

    private OnnxEmbedder(InferenceSession session, BertTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;

        var inputNames = session.InputMetadata.Keys.ToList();
        _hasTokenTypeIds = inputNames.Contains("token_type_ids");
        _outputName = session.OutputMetadata.Keys.First();

        var dims = session.OutputMetadata[_outputName].Dimensions;
        Dimensions = dims.Length > 0 && dims[^1] > 0 ? dims[^1] : 512;
    }

    public static OnnxEmbedder Create(string modelPath, string vocabPath)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
            InterOpNumThreads = 1,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };

        var session = new InferenceSession(modelPath, options);
        var tokenizer = BertTokenizer.Load(vocabPath);
        return new OnnxEmbedder(session, tokenizer);
    }

    public float[] Embed(string text, EmbedKind kind)
    {
        var input = kind == EmbedKind.Query && !string.IsNullOrEmpty(text)
            ? QueryInstruction + text
            : text;

        var ids = _tokenizer.Encode(input, MaxTokens);
        var length = ids.Count;

        var idsArray = ids.ToArray();
        var mask = new long[length];
        Array.Fill(mask, 1L);

        var inputs = new List<NamedOnnxValue>(3)
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(idsArray, new[] { 1, length })),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, new[] { 1, length }))
        };

        if (_hasTokenTypeIds)
        {
            inputs.Add(NamedOnnxValue.CreateFromTensor("token_type_ids",
                new DenseTensor<long>(new long[length], new[] { 1, length })));
        }

        var vector = new float[Dimensions];

        lock (_gate)
        {
            using var results = _session.Run(inputs);
            var tensor = results.First().AsTensor<float>();

            if (tensor is DenseTensor<float> dense)
            {
                var span = dense.Buffer.Span;
                var copy = Math.Min(span.Length, Dimensions);
                span[..copy].CopyTo(vector);
            }
            else
            {
                var flat = tensor.ToArray();
                Array.Copy(flat, vector, Math.Min(flat.Length, Dimensions));
            }
        }

        Normalize(vector);
        return vector;
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0f;
        foreach (var v in vector) sum += v * v;

        var norm = MathF.Sqrt(sum);
        if (norm <= 1e-8f) return;

        for (var i = 0; i < vector.Length; i++) vector[i] /= norm;
    }

    public void Dispose() => _session.Dispose();
}
