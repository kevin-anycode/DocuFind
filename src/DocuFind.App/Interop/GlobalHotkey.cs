namespace DocuFind.App.Interop;

/// <summary>全局热键：在窗口上挂子类过程接收 WM_HOTKEY。</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int HotkeyId = 0x0D0C;

    private readonly IntPtr _hwnd;
    private readonly Action _callback;
    private readonly NativeMethods.SubclassProc _proc;
    private bool _registered;

    public GlobalHotkey(IntPtr hwnd, uint modifiers, uint virtualKey, Action callback)
    {
        _hwnd = hwnd;
        _callback = callback;
        _proc = OnMessage;

        var ok = NativeMethods.SetWindowSubclass(hwnd, _proc, 1, IntPtr.Zero);
        _registered = NativeMethods.RegisterHotKey(hwnd, HotkeyId, modifiers | NativeMethods.ModNoRepeat, virtualKey);
        Services.Log.Write($"hotkey subclass={ok} registered={_registered}");
    }

    public bool IsRegistered => _registered;

    private IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == NativeMethods.WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Services.Log.Write("wm_hotkey");
            _callback();
            return IntPtr.Zero;
        }

        return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_registered) return;

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyId);
        _registered = false;
    }
}
