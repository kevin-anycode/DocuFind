using System.Runtime.InteropServices;

namespace DocuFind.App.Interop;

internal static class WindowChrome
{
    private static readonly NativeMethods.SubclassProc MinSizeProc = OnMinSizeMessage;

    /// <summary>把窗口裁成圆角矩形（物理像素）。</summary>
    public static void Round(IntPtr hwnd, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0) return;

        var region = NativeMethods.CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
        NativeMethods.SetWindowRgn(hwnd, region, true);
    }

    /// <summary>
    /// 取消窗口自身的圆角裁切，让圆角交给 XAML 画——
    /// SetWindowRgn 是 1 位区域，边缘没有抗锯齿，胶囊两端会出现台阶状的锯齿。
    /// </summary>
    public static void NoRound(IntPtr hwnd)
    {
        const int DwmwaWindowCornerPreference = 33;
        const int NoRoundCorners = 1; // DWMWCP_DONOTROUND

        var preference = NoRoundCorners;
        NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, sizeof(int));
    }

    /// <summary>让窗口不抢焦点（点击不激活），用于结果面板。</summary>
    public static void MakeNoActivate(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        style |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(style));
    }

    /// <summary>把窗口钉在任务栏之上（任务栏在 TOPMOST 层，必须同样置顶才能盖住它）。</summary>
    public static void MakeTopmost(IntPtr hwnd)
    {
        var before = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        var wanted = before | NativeMethods.WsExTopmost | NativeMethods.WsExToolWindow;

        if (wanted != before)
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(wanted));

        // 已经是 TOPMOST 的窗口再用 HWND_TOPMOST 不会调整 z 序（系统直接当没变），
        // 被任务栏盖住后必须用 HWND_TOP 把它重新提到 TOPMOST 层的最上面。
        var insertAfter = (before & NativeMethods.WsExTopmost) != 0
            ? NativeMethods.HwndTop
            : NativeMethods.HwndTopmost;

        var ok = NativeMethods.SetWindowPos(
            hwnd,
            insertAfter,
            0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);

        var after = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();
        var signature = $"0x{after:X}";
        if (signature != _lastTopmostSignature)
        {
            _lastTopmostSignature = signature;
            Services.Log.Write($"topmost before=0x{before:X} wanted=0x{wanted:X} pos={ok} after=0x{after:X}");
        }
    }

    private static string? _lastTopmostSignature;

    /// <summary>
    /// 彻底去掉 WinUI 顶层窗口的边框：WS_DLGFRAME + WS_EX_WINDOWEDGE 会画出一圈
    /// 3px 的白色立体边框（非客户区），圆角胶囊看起来就有白边。
    /// 去掉后客户区正好等于窗口尺寸，再配合 window region 才能和任务栏无缝贴合。
    /// </summary>
    public static void StripFrame(IntPtr hwnd)
    {
        var style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlStyle).ToInt64();
        var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GwlExStyle).ToInt64();

        var newStyle = style & ~(NativeMethods.WsDlgFrame | NativeMethods.WsCaption | NativeMethods.WsThickFrame);
        var newEx = ex & ~(NativeMethods.WsExWindowEdge | NativeMethods.WsExClientEdge);

        var changed = false;

        if (newStyle != style)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlStyle, new IntPtr(newStyle));
            changed = true;
        }

        if (newEx != ex)
        {
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GwlExStyle, new IntPtr(newEx));
            changed = true;
        }

        if (!changed) return;

        NativeMethods.SetWindowPos(
            hwnd,
            0, 0, 0, 0, 0,
            NativeMethods.SwpFrameChanged | NativeMethods.SwpNoMove |
            NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
    }

    /// <summary>去掉 Windows 11 给窗口画的 1px 系统边框（否则圆角外会留白边）。</summary>
    public static void RemoveBorder(IntPtr hwnd)
    {
        const int DwmwaBorderColor = 34;
        const int NoBorder = unchecked((int)0xFFFFFFFE);

        var color = NoBorder;
        NativeMethods.DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref color, sizeof(int));
    }

    /// <summary>
    /// 顶层窗口默认有 39px 的最小尺寸（子窗口没有），
    /// 拦截 WM_GETMINMAXINFO 把它放开，胶囊才能压到任务栏的 32px 高度。
    /// </summary>
    public static void IgnoreMinimumSize(IntPtr hwnd)
    {
        NativeMethods.SetWindowSubclass(hwnd, MinSizeProc, 2, IntPtr.Zero);
    }

    private static IntPtr OnMinSizeMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr uIdSubclass,
        IntPtr dwRefData)
    {
        if (msg == NativeMethods.WmGetMinMaxInfo && lParam != IntPtr.Zero)
        {
            // MINMAXINFO.ptMinTrackSize 位于偏移 24 / 28
            Marshal.WriteInt32(lParam, 24, 1);
            Marshal.WriteInt32(lParam, 28, 1);
        }

        return NativeMethods.DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
