using DocuFind.App.Services;
using DocuFind.App.Views;
using DocuFind.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace DocuFind.App;

public partial class App : Application
{
    private static Task<DocuFindEngine>? _engineTask;

    private PillWindow? _mainWindow;
    private DispatcherQueueTimer? _watchdog;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    /// <summary>引擎全局只创建一次，窗口重建时复用（模型加载很贵）。</summary>
    public static Task<DocuFindEngine> GetEngineAsync() =>
        _engineTask ??= Task.Run(() => DocuFindEngine.Create());

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!SingleInstance.IsFirstInstance(ActivateExisting))
        {
            Exit();
            return;
        }

        _mainWindow = new PillWindow();
        _mainWindow.ShowWithoutActivation();

        // 胶囊是任务栏的子窗口，Explorer 重启会把它一起带走，这里负责重建
        _watchdog = _mainWindow.DispatcherQueue.CreateTimer();
        _watchdog.Interval = TimeSpan.FromSeconds(3);
        _watchdog.IsRepeating = true;
        _watchdog.Tick += (_, _) => EnsureWindowAlive();
        _watchdog.Start();
    }

    private void EnsureWindowAlive()
    {
        if (_mainWindow is not null && _mainWindow.IsAlive) return;

        Log.Write("胶囊窗口已失效（任务栏可能重启过），重建中");
        _mainWindow?.Detach();

        _mainWindow = new PillWindow();
        _mainWindow.ShowWithoutActivation();
    }

    private void ActivateExisting()
    {
        var window = _mainWindow;
        if (window is null) return;

        window.DispatcherQueue.TryEnqueue(() => window.ShowAndFocus());
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            DocuFindPaths.EnsureCreated();
            File.AppendAllText(
                Path.Combine(DocuFindPaths.LogDirectory, "crash.log"),
                $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}");
        }
        catch
        {
            // 日志写不进去就算了，不能让异常处理再抛
        }
    }
}
