namespace DocuFind.App.Services;

/// <summary>
/// 单实例：第二次启动时只把已有窗口唤到前台，然后退出自己。
/// </summary>
internal static class SingleInstance
{
    private const string MutexName = @"Local\DocuFind.SingleInstance";
    private const string EventName = @"Local\DocuFind.Activate";

    private static Mutex? _mutex;
    private static EventWaitHandle? _signal;

    public static bool IsFirstInstance(Action onActivate)
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);

        if (!createdNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch
            {
                // 唤不醒就算了，直接退出
            }

            return false;
        }

        _signal = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, EventName);

        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (!_signal.WaitOne()) continue;
                }
                catch
                {
                    return;
                }

                onActivate();
            }
        })
        {
            IsBackground = true,
            Name = "DocuFind.SingleInstance"
        };

        thread.Start();
        return true;
    }
}
