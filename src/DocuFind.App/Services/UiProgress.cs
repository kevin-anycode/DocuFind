using Microsoft.UI.Dispatching;

namespace DocuFind.App.Services;

/// <summary>把后台线程的进度回调安全地送回 UI 线程。</summary>
internal sealed class UiProgress<T> : IProgress<T>
{
    private readonly DispatcherQueue _dispatcher;
    private readonly Action<T> _handler;

    public UiProgress(DispatcherQueue dispatcher, Action<T> handler)
    {
        _dispatcher = dispatcher;
        _handler = handler;
    }

    public void Report(T value) => _dispatcher.TryEnqueue(() => _handler(value));
}
