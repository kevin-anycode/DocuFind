using System.Runtime.InteropServices;

namespace DocuFind.App.Interop;

internal static class NativeMethods
{
    public const int WmHotkey = 0x0312;
    public const int WmDisplayChange = 0x007E;
    public const int WmSettingChange = 0x001A;
    public const int WmGetMinMaxInfo = 0x0024;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModNoRepeat = 0x4000;

    public const uint VkSpace = 0x20;

    public const int GwlExStyle = -20;
    public const int GwlStyle = -16;
    public const int GwlHwndParent = -8;
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTopmost = 0x00000008;
    public const int WsExWindowEdge = 0x00000100;
    public const int WsExClientEdge = 0x00000200;

    public const long WsChild = 0x40000000;
    public const long WsPopup = unchecked((long)0x80000000);
    public const long WsVisible = 0x10000000;
    public const long WsCaption = 0x00C00000;
    public const long WsThickFrame = 0x00040000;
    public const long WsSysMenu = 0x00080000;
    public const long WsMinimizeBox = 0x00020000;
    public const long WsMaximizeBox = 0x00010000;
    public const long WsDlgFrame = 0x00400000;

    public const int SwShowNoActivate = 4;

    public const uint SwpNoSize = 0x0001;
    public const uint SwpNoMove = 0x0002;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpNoZOrder = 0x0004;
    public const uint SwpFrameChanged = 0x0020;

    public static readonly IntPtr HwndTopmost = new(-1);
    public static readonly IntPtr HwndTop = IntPtr.Zero;

    public const uint MbYesNo = 0x00000004;
    public const uint MbIconWarning = 0x00000030;

    public delegate IntPtr SubclassProc(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        IntPtr uIdSubclass,
        IntPtr dwRefData);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, IntPtr uIdSubclass);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int width, int height);

    [DllImport("user32.dll")]
    public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int cmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr child, IntPtr parent);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int width, int height, [MarshalAs(UnmanagedType.Bool)] bool repaint);

    [DllImport("user32.dll")]
    public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Point points, int count);

    public const uint GaRoot = 2;

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);

    public const uint GwHwndPrev = 3;

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassNameInternal(IntPtr hWnd, System.Text.StringBuilder buffer, int maxCount);

    /// <summary>取窗口类名，失败时返回空串。</summary>
    public static string GetClassName(IntPtr hWnd)
    {
        var buffer = new System.Text.StringBuilder(256);
        return GetClassNameInternal(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    public static extern uint GetPixel(IntPtr hdc, int x, int y);

    /// <summary>读屏幕上某个点的颜色（用来取任务栏在当前壁纸下的实际颜色）。</summary>
    public static Windows.UI.Color? TrySampleScreen(int x, int y)
    {
        var dc = GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero) return null;

        try
        {
            var value = GetPixel(dc, x, y);
            if (value == 0xFFFFFFFF) return null; // CLR_INVALID

            return Windows.UI.Color.FromArgb(
                0xFF,
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF));
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }
    }
}

/// <summary>
/// 定位任务栏里的空位。
/// 胶囊放在托盘左侧的空白区域，绝不覆盖系统自带的搜索框。
/// </summary>
internal static class TaskbarLocator
{
    public const double PillWidth = 232;
    public const double PillHeight = 34;

    /// <summary>距任务栏边缘的留白。</summary>
    private const double EdgeMargin = 10;

    /// <summary>与托盘/天气挂件之间留的空隙。</summary>
    private const double TrayGap = 8;

    /// <summary>
    /// 天气/小组件挂件不是独立 HWND，只能按实测宽度给它让位：
    /// 托盘 HWND 左缘在 2251，天气挂件从 2055 开始（宽 152），留出 204 才不会压到它。
    /// </summary>
    private const double WeatherReserve = 204;

    /// <summary>与任务栏应用按钮区之间留的空隙。</summary>
    private const double AppGap = 8;

    public static IntPtr FindTaskbar()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        return taskbar != IntPtr.Zero && NativeMethods.IsWindow(taskbar) ? taskbar : IntPtr.Zero;
    }

    /// <summary>横向（底部或顶部）任务栏才能贴进去。</summary>
    public static bool IsHorizontal(IntPtr taskbar)
    {
        if (!NativeMethods.GetWindowRect(taskbar, out var bar)) return false;
        return bar.Height >= 24 && bar.Height <= bar.Width;
    }

    /// <summary>胶囊拖动时允许走的整条任务栏（物理像素）。</summary>
    public static bool TryGetBand(IntPtr taskbar, out NativeMethods.Rect band)
    {
        band = default;
        if (!NativeMethods.GetWindowRect(taskbar, out var bar)) return false;
        if (!IsHorizontal(taskbar)) return false;
        band = bar;
        return true;
    }

    /// <summary>
    /// 胶囊拖动时左端的最小值（物理像素）：不能压到任务栏图标。
    /// 探测不到图标行时返回 int.MinValue，表示没有额外限制。
    /// </summary>
    public static int GetDragFloor(double scale)
    {
        var gap = (int)Math.Round(TrayGap * scale);
        var strip = TaskbarProbe.IconStripRight;
        return strip > 0 ? strip + gap : int.MinValue;
    }

    /// <summary>
    /// 算出胶囊应该占的矩形（物理像素）。
    /// 默认贴着任务栏图标行的右缘（像图标行的最后一项），图标增减时跟着一起挪；
    /// 用户拖过就停在他放的位置，但绝不压住图标；
    /// 被媒体卡片之类的挂件挡住时，往左找最近的空档。
    /// <paramref name="preferredRightInset"/> 是用户拖动后记住的"右缘距屏幕右缘的像素"。
    /// </summary>
    public static bool TryGetPillRect(
        IntPtr taskbar,
        IntPtr self,
        double scale,
        int? preferredRightInset,
        out NativeMethods.Rect rect)
    {
        rect = default;

        if (!NativeMethods.GetWindowRect(taskbar, out var bar)) return false;
        if (!IsHorizontal(taskbar)) return false;

        var height = Math.Min(bar.Height - (int)Math.Round(16 * scale), (int)Math.Round(PillHeight * scale));
        if (height < 22) height = 22;

        var width = (int)Math.Round(PillWidth * scale);
        var margin = (int)Math.Round(EdgeMargin * scale);
        var gap = (int)Math.Round(TrayGap * scale);

        // 右端能到哪：托盘左边，再退一步给天气挂件让位
        var rightLimit = bar.Right - margin;
        var weatherReserve = (int)Math.Round(WeatherReserve * scale);

        var tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var trayRect) && trayRect.Width > 0)
            rightLimit = Math.Min(rightLimit, trayRect.Left - weatherReserve - gap);
        else
            rightLimit = Math.Min(rightLimit, bar.Right - weatherReserve - gap);

        // Win11 任务栏的图标不是窗口，右缘只能靠 UIA 读（TaskbarProbe 每秒刷新一次）。
        // 它的用处是给胶囊一个硬下限：绝不能盖住应用图标。
        var iconStripRight = TaskbarProbe.IconStripRight;

        var leftLimit = bar.Left + margin;
        if (iconStripRight > 0) leftLimit = Math.Max(leftLimit, iconStripRight + gap);

        // 右端被挤压时至少保证胶囊还放得下
        if (leftLimit > rightLimit - width) leftLimit = Math.Max(bar.Left + margin, rightLimit - width);

        // 默认槽位：任务栏最右边那块空档，也就是贴着最右侧那一簇（媒体卡片 / 天气挂件 / 托盘）。
        // 卡片在就停在卡片左边，卡片没了就往右贴近托盘。
        var preferred = rightLimit;

        if (preferredRightInset is { } inset)
            preferred = Math.Min(rightLimit, bar.Right - inset);

        if (iconStripRight > 0) preferred = Math.Max(preferred, iconStripRight + gap + width);
        preferred = Math.Min(preferred, rightLimit);

        var obstacles = CollectObstacles(taskbar, self, bar);

        NativeMethods.GetWindowRect(self, out var current);

        // 用户拖到的那个槽位如果还空着就别动它——媒体卡片跟着音乐滑动时，
        // 每 1.5 秒重新算一次位置会让胶囊自己抖来抖去。
        // 只认"用户拖到的槽位"：窗口初始化时的随机位置不算数。
        var currentRight = current.Left + width;
        var wantedRight = preferredRightInset is { } userInset ? bar.Right - userInset : int.MinValue;

        if (current.Width > 0 &&
            preferredRightInset is not null &&
            Math.Abs(currentRight - wantedRight) <= 24 &&
            Fits(current, width, leftLimit, rightLimit, gap, obstacles))
        {
            var keepTop = bar.Top + (bar.Height - height) / 2;
            rect = new NativeMethods.Rect
            {
                Left = current.Left,
                Top = keepTop,
                Right = current.Left + width,
                Bottom = keepTop + height
            };
            return true;
        }

        var right = FindFreeRight(preferred, width, leftLimit, rightLimit, gap, obstacles);
        if (right == int.MinValue)
            right = FindFreeRight(rightLimit, width, leftLimit, rightLimit, gap, obstacles);

        if (right == int.MinValue)
            right = Math.Max(leftLimit, preferred); // 实在没空位，宁可重叠也不消失

        var left = Math.Min(right, rightLimit) - width;
        if (left < leftLimit) left = leftLimit;

        var top = bar.Top + (bar.Height - height) / 2;

        rect = new NativeMethods.Rect { Left = left, Top = top, Right = left + width, Bottom = top + height };
        return true;
    }

    /// <summary>这个位置是不是还空着（没压到按钮区、没超出右端、没撞到别的挂件）。</summary>
    private static bool Fits(
        NativeMethods.Rect current,
        int width,
        int leftLimit,
        int rightLimit,
        int gap,
        List<NativeMethods.Rect> obstacles)
    {
        if (current.Left < leftLimit) return false;
        if (current.Left + width > rightLimit + gap) return false;

        return !obstacles.Any(o => o.Left < current.Left + width && o.Right > current.Left);
    }

    /// <summary>从右往左找第一个能容下胶囊的空档，返回空档的右边界。</summary>
    private static int FindFreeRight(
        int preferredRight,
        int width,
        int leftLimit,
        int rightLimit,
        int gap,
        List<NativeMethods.Rect> obstacles)
    {
        var right = Math.Min(preferredRight, rightLimit);
        var attempts = 0;

        while (attempts++ < 32)
        {
            if (right - width < leftLimit) return int.MinValue;

            var hit = obstacles.FirstOrDefault(o => o.Left < right && o.Right > right - width);
            if (hit.Right == 0 && hit.Left == 0) return right;

            right = hit.Left - gap;
        }

        return int.MinValue;
    }

    /// <summary>
    /// 收集压在任务栏这一条上、并且真的盖在任务栏上面的窗口（媒体卡片、浮动播放条、别人的挂件）。
    /// 只在 z 序上高于任务栏的才算——最大化的浏览器、桌面合成层虽然几何上叠着任务栏，
    /// 但视觉上在任务栏底下，不能因此把胶囊挤走。
    /// </summary>
    private static List<NativeMethods.Rect> CollectObstacles(IntPtr taskbar, IntPtr self, NativeMethods.Rect bar)
    {
        var result = new List<NativeMethods.Rect>();

        NativeMethods.GetWindowThreadProcessId(self, out var selfPid);

        var secondary = NativeMethods.FindWindow("Shell_SecondaryTrayWnd", null);
        var bandHeight = bar.Height;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == taskbar || hwnd == self) return true;
            if (secondary != IntPtr.Zero && hwnd == secondary) return true;
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) return true;

            // 和任务栏竖直方向有交集、并且落在任务栏水平范围内
            if (r.Bottom <= bar.Top || r.Top >= bar.Bottom) return true;
            if (r.Right <= bar.Left || r.Left >= bar.Right) return true;

            // 自己进程的窗口不算（比如结果面板）
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == selfPid) return true;

            // 只有“贴着任务栏这一条”的窗口才算挂件，整块屏幕那么大的窗口
            // （开始菜单、全屏应用）不该把胶囊赶到角落
            if (r.Height > bandHeight * 4) return true;

            if (!IsAboveWindow(taskbar, hwnd)) return true;

            result.Add(r);
            return true;
        }, IntPtr.Zero);

        return result;
    }

    /// <summary>在 z 序上 other 是否高于 anchor（从 other 往上走，先撞到 anchor 就说明在它下面）。</summary>
    private static bool IsAboveWindow(IntPtr anchor, IntPtr other)
    {
        var current = other;

        for (var i = 0; i < 20000; i++)
        {
            current = NativeMethods.GetWindow(current, NativeMethods.GwHwndPrev);
            if (current == anchor) return false;
            if (current == IntPtr.Zero) return true;
        }

        return false;
    }
}
