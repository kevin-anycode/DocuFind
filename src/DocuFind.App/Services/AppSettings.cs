using System.Text.Json;
using DocuFind.Core;

namespace DocuFind.App.Services;

/// <summary>
/// 用户设置，存在 %LOCALAPPDATA%\DocuFind\settings.json。
/// 目前只有胶囊位置一项，后面热键、宽度、开机自启也放这里。
/// </summary>
internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>
    /// 胶囊拖到哪了：窗口右缘到任务栏右缘的物理像素。
    /// null 表示用默认槽位（托盘/天气挂件左边那块空白）。
    /// </summary>
    public int? PillRightInset { get; set; }

    public static AppSettings Load()
    {
        try
        {
            var path = DocuFindPaths.SettingsPath;
            if (!File.Exists(path)) return new AppSettings();

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var path = DocuFindPaths.SettingsPath;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            Services.Log.Write($"保存设置失败：{ex.Message}");
        }
    }
}
