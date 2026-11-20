using DocuFind.Core;

namespace DocuFind.App.Services;

internal static class Log
{
    public static void Write(string message)
    {
        try
        {
            DocuFindPaths.EnsureCreated();
            File.AppendAllText(
                Path.Combine(DocuFindPaths.LogDirectory, "layout.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch
        {
            // 日志写不进去不影响功能
        }
    }
}
