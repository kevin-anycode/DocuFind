using System.Runtime.InteropServices;
using System.Threading;
using Interop.UIAutomationClient;

namespace DocuFind.App.Interop;

/// <summary>
/// 探测任务栏“图标行”的右缘。
///
/// Win11 任务栏上的应用图标不是窗口、也没有 HWND——它们是任务栏自己的 XAML 元素，
/// 位置只有通过 UI Automation 才读得到（实测 UIA 里的按钮坐标和屏幕上像素一致）。
/// 这里用一个后台线程每秒读一次，主线程只读缓存值，不卡输入。
/// </summary>
internal static class TaskbarProbe
{
    /// <summary>任务栏应用按钮的 UIA ClassName，和别的按钮（开始、搜索、托盘）区分开。</summary>
    private const string AppButtonClass = "Taskbar.TaskListButtonAutomationPeer";

    private static int _stripRight;
    private static int _pendingRight;
    private static bool _started;

    /// <summary>图标行右缘（物理像素），0 表示还没探测到。</summary>
    public static int IconStripRight => Volatile.Read(ref _stripRight);

    /// <summary>图标行右缘变了（开了新窗口、取消固定等），在后台线程上触发。</summary>
    public static event Action? Changed;

    public static void Start()
    {
        if (_started) return;
        _started = true;

        // 先同步读一次，启动时第一次计算位置就能贴着图标行，不会先在右端闪一下
        RefreshNow();

        var thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "TaskbarProbe"
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    private static void Loop()
    {
        IUIAutomation? automation = null;

        try
        {
            automation = (IUIAutomation)new CUIAutomation8();
        }
        catch
        {
        }

        while (true)
        {
            try
            {
                if (automation is not null) Refresh(automation);
            }
            catch
            {
            }

            Thread.Sleep(1000);
        }
    }

    private static void RefreshNow()
    {
        try
        {
            Refresh((IUIAutomation)new CUIAutomation8());
        }
        catch
        {
        }
    }

    private static void Refresh(IUIAutomation automation)
    {
        var taskbar = TaskbarLocator.FindTaskbar();
        if (taskbar == IntPtr.Zero) return;

        var root = automation.ElementFromHandle(taskbar);
        if (root is null) return;

        var condition = automation.CreatePropertyCondition(
            UIA_PropertyIds.UIA_ClassNamePropertyId,
            AppButtonClass);

        var elements = root.FindAll(TreeScope.TreeScope_Descendants, condition);
        if (elements is null) return;

        var right = 0;
        var count = 0;

        try
        {
            count = elements.Length;

            for (var i = 0; i < count; i++)
            {
                var element = elements.GetElement(i);
                if (element is null) continue;

                var bounds = element.CurrentBoundingRectangle;
                if (bounds.right > right) right = bounds.right;
            }
        }
        finally
        {
            Release(elements);
            Release(condition);
            Release(root);
        }

        // 任务栏上一个应用都没有时保持上次的值，别把胶囊丢到左端去
        if (right <= 0) return;

        var previous = Volatile.Read(ref _stripRight);
        if (right == previous)
        {
            _pendingRight = 0;
            return;
        }

        // 图标行变宽（新开窗口、取消固定）要立刻跟，否则会压住图标；
        // 变窄则要连着读到两次才认——UIA 刚启动时偶尔少读一个按钮，
        // 跟着它缩回去会露出一块空白，过一秒又跳一次。
        if (right < previous)
        {
            if (_pendingRight != right)
            {
                _pendingRight = right;
                return;
            }
        }

        _pendingRight = 0;
        Volatile.Write(ref _stripRight, right);
        Services.Log.Write($"strip right={right} buttons={count} prev={previous}");

        try
        {
            Changed?.Invoke();
        }
        catch
        {
        }
    }

    private static void Release(object? comObject)
    {
        if (comObject is null) return;

        try
        {
            if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
        }
        catch
        {
        }
    }
}
