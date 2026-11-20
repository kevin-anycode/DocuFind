namespace DocuFind.Core;

/// <summary>
/// 所有本地数据的位置。默认放在 %LOCALAPPDATA%\DocuFind，
/// 模型优先从安装目录下的 models\ 读取，便于随安装包一起分发。
/// </summary>
public static class DocuFindPaths
{
    public const string AppFolderName = "DocuFind";
    public const string ModelFileName = "bge-small-zh-v1.5-int8.onnx";
    public const string VocabFileName = "vocab.txt";

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName);

    public static string DatabasePath => Path.Combine(Root, "index.db");
    public static string UserModelDirectory => Path.Combine(Root, "models");
    public static string LogDirectory => Path.Combine(Root, "logs");
    public static string SettingsPath => Path.Combine(Root, "settings.json");

    public static string UserModelPath => Path.Combine(UserModelDirectory, ModelFileName);
    public static string UserVocabPath => Path.Combine(UserModelDirectory, VocabFileName);

    /// <summary>安装目录下的模型目录（随安装包分发）。</summary>
    public static string BundledModelDirectory => Path.Combine(AppContext.BaseDirectory, "models");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(UserModelDirectory);
        Directory.CreateDirectory(LogDirectory);
    }

    /// <summary>按优先级找到模型文件，找不到返回 null。</summary>
    public static (string Model, string Vocab)? ResolveModel()
    {
        var candidates = new[]
        {
            (Path.Combine(UserModelDirectory, ModelFileName), Path.Combine(UserModelDirectory, VocabFileName)),
            (Path.Combine(BundledModelDirectory, ModelFileName), Path.Combine(BundledModelDirectory, VocabFileName)),
        };

        foreach (var (model, vocab) in candidates)
        {
            if (File.Exists(model) && File.Exists(vocab))
                return (model, vocab);
        }

        return null;
    }
}
