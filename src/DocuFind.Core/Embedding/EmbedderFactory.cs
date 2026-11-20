namespace DocuFind.Core.Embedding;

public static class EmbedderFactory
{
    /// <summary>加载向量模型；模型文件缺失时返回 NullEmbedder，检索退化为关键词模式。</summary>
    public static IEmbedder Create(out string? error)
    {
        error = null;

        var resolved = DocuFindPaths.ResolveModel();
        if (resolved is null)
        {
            error = $"未找到模型文件（{DocuFindPaths.ModelFileName}）。请运行 tools\\fetch-model.ps1 下载，或把模型放到 {DocuFindPaths.UserModelDirectory}。";
            return NullEmbedder.Instance;
        }

        try
        {
            return OnnxEmbedder.Create(resolved.Value.Model, resolved.Value.Vocab);
        }
        catch (Exception ex)
        {
            error = $"向量模型加载失败：{ex.Message}";
            return NullEmbedder.Instance;
        }
    }
}
