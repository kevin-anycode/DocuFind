using System.Diagnostics;
using DocuFind.App.Interop;
using DocuFind.App.Services;
using DocuFind.Core;
using DocuFind.Core.Ingestion;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace DocuFind.App.Views;

/// <summary>
/// 嵌在任务栏里的搜索胶囊：只负责输入，结果交给 ResultsWindow 弹在上方。
/// </summary>
public sealed partial class PillWindow : Window
{
    private const int MaxHits = 20;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly ResultsWindow _results;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _debounce;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _placement;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _topmostTick;
    private readonly GlobalHotkey? _hotkey;

    private DocuFindEngine? _engine;
    private CancellationTokenSource? _searchCts;
    private bool _active;
    private bool _ingesting;
    private bool _hiddenWithTaskbar;
    private string? _lastLayoutLog;

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly InputNonClientPointerSource _nonClient;
    private bool _dragging;

    public PillWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.PreferredMinimumWidth = 1;
            presenter.PreferredMinimumHeight = 1;
        }

        _appWindow.IsShownInSwitchers = false;

        WindowChrome.RemoveBorder(_hwnd);
        WindowChrome.IgnoreMinimumSize(_hwnd);

        _results = new ResultsWindow();

        _debounce = DispatcherQueue.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(120);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => _ = RunSearchAsync();

        _placement = DispatcherQueue.CreateTimer();
        _placement.Interval = TimeSpan.FromSeconds(1.5);
        _placement.IsRepeating = true;
        _placement.Tick += (_, _) => RepositionPill();
        _placement.Start();

        // 任务栏会时不时把自己提回 TOPMOST 最上层（点开始菜单、切窗口都会），
        // 用 250ms 的轻量重提把它压回去，避免胶囊被任务栏盖住半秒。
        _topmostTick = DispatcherQueue.CreateTimer();
        _topmostTick.Interval = TimeSpan.FromMilliseconds(250);
        _topmostTick.IsRepeating = true;
        _topmostTick.Tick += (_, _) => EnsureOnTop();
        _topmostTick.Start();

        _hotkey = new GlobalHotkey(
            _hwnd,
            NativeMethods.ModControl | NativeMethods.ModAlt,
            NativeMethods.VkSpace,
            OnHotkeyPressed);

        Log.Write($"hotkey ready={_hotkey.IsRegistered}");

        BuildBrandMenu();

        // 先探一次图标行右缘，启动时就能贴着图标摆好；之后图标增减时立刻跟着挪
        TaskbarProbe.Changed += OnTaskbarStripChanged;
        TaskbarProbe.Start();

        // 用 Windows App SDK 自己的“非客户区”拖动：把左侧图标那一小块声明成标题栏，
        // 系统就按标题栏的方式拖动窗口，拖动中由 WindowRectChanging 把窗口夹在任务栏这一条上。
        _nonClient = InputNonClientPointerSource.GetForWindowId(_appWindow.Id);
        _nonClient.EnteringMoveSize += (_, _) => _dragging = true;
        _nonClient.WindowRectChanging += OnWindowRectChanging;
        _nonClient.ExitedMoveSize += OnExitedMoveSize;

        RepositionPill();
        UpdateFooter();

        Activated += OnWindowActivated;
        _appWindow.Closing += (_, args) =>
        {
            args.Cancel = true;
            _appWindow.Hide();
        };

        _ = InitializeEngineAsync();
    }

    #region 任务栏定位

    private bool _repositioning;

    /// <summary>跟随任务栏：位置、尺寸、显隐都跟它对齐。</summary>
    private void RepositionPill()
    {
        if (_repositioning || _dragging) return;

        _repositioning = true;
        try
        {
            RepositionPillCore();
        }
        finally
        {
            _repositioning = false;
        }
    }

    private void RepositionPillCore()
    {
        var taskbar = TaskbarLocator.FindTaskbar();
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;

        if (taskbar == IntPtr.Zero || !NativeMethods.IsWindowVisible(taskbar))
        {
            if (_hiddenWithTaskbar) return;

            _hiddenWithTaskbar = true;
            _results.HidePanel();
            _appWindow.Hide();
            return;
        }

        _hiddenWithTaskbar = false;

        var canAttach = TaskbarLocator.IsHorizontal(taskbar);

        if (!TaskbarLocator.TryGetPillRect(taskbar, _hwnd, scale, _settings.PillRightInset, out var rect))
        {
            // 任务栏竖排或位于顶部时贴不进去，退回屏幕底部居中浮动
            var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
            var work = area.WorkArea;
            var width = (int)Math.Round(TaskbarLocator.PillWidth * scale);
            var height = (int)Math.Round(TaskbarLocator.PillHeight * scale);
            var bottom = work.Y + work.Height - (int)Math.Round(8 * scale);
            var center = work.X + work.Width / 2;

            rect = new NativeMethods.Rect
            {
                Left = center - width / 2,
                Top = bottom - height,
                Right = center + width / 2,
                Bottom = bottom
            };
        }

        NativeMethods.GetWindowRect(taskbar, out var bar);

        // 先摆位置：不设 owner，靠 TOPMOST + 定时重提 z 序压住任务栏
        NativeMethods.MoveWindow(_hwnd, rect.Left, rect.Top, rect.Width, rect.Height, true);
        WindowChrome.StripFrame(_hwnd);

        // 圆角交给 XAML：窗口区域裁切是 1 位掩码，两端会有明显锯齿。
        // 代价是窗口变成一个矩形，所以用任务栏自己的颜色把矩形铺满，边缘就看不出来了。
        WindowChrome.NoRound(_hwnd);
        ApplyTaskbarTint(rect);

        UpdateDragRegions(rect.Width, rect.Height);
        EnsureOnTop();

        NativeMethods.GetWindowRect(_hwnd, out var actual);
        var signature = $"{actual.Left},{actual.Top} {actual.Width}x{actual.Height}";

        if (signature != _lastLayoutLog)
        {
            _lastLayoutLog = signature;
            Log.Write($"want={rect.Left},{rect.Top} {rect.Width}x{rect.Height} actual={signature} scale={scale}");
        }

        _results.PlaceAbove(rect.Left, canAttach ? bar.Top : rect.Top, scale);
    }

    /// <summary>图标行变宽/变窄了（后台线程触发），回到 UI 线程重新摆一次。</summary>
    private void OnTaskbarStripChanged()
    {
        DispatcherQueue.TryEnqueue(RepositionPill);
    }

    #endregion

    #region 拖动

    /// <summary>左侧放大镜那一块宽多少（DIP），这块当作“抓手”用来拖动。</summary>
    private const double DragGripWidth = 28;

    /// <summary>
    /// 把胶囊左侧图标区声明成窗口标题栏（非客户区），系统就会用标准的方式拖动它，
    /// 文字框和品牌按钮仍然归 XAML，输入、选中、右键菜单都不受影响。
    /// </summary>
    private void UpdateDragRegions(int width, int height)
    {
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var grip = Math.Min(width, (int)Math.Round(DragGripWidth * scale));
        if (grip < 8) grip = 8;

        _nonClient.SetRegionRects(
            NonClientRegionKind.Caption,
            new[] { new RectInt32(0, 0, grip, height) });
    }

    /// <summary>拖动过程中把窗口夹在任务栏这一行，横向也别拖出屏幕、别压到图标。</summary>
    private void OnWindowRectChanging(InputNonClientPointerSource sender, WindowRectChangingEventArgs args)
    {
        if (!_dragging) return;

        var taskbar = TaskbarLocator.FindTaskbar();
        if (taskbar == IntPtr.Zero || !TaskbarLocator.TryGetBand(taskbar, out var band)) return;

        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;
        var floor = TaskbarLocator.GetDragFloor(scale);
        var minLeft = floor == int.MinValue ? band.Left : Math.Max(band.Left, floor);

        var rect = args.NewWindowRect;
        var left = Math.Clamp(rect.X, minLeft, Math.Max(minLeft, band.Right - rect.Width));
        var top = band.Top + (band.Height - rect.Height) / 2;

        args.NewWindowRect = new RectInt32(left, top, rect.Width, rect.Height);
    }

    /// <summary>
    /// 松手：落点就是默认槽位（贴着最右边那一簇）就清掉自定义位置、恢复自动跟随，
    /// 拖到别处才记住这个位置。
    /// </summary>
    private void OnExitedMoveSize(InputNonClientPointerSource sender, ExitedMoveSizeEventArgs args)
    {
        _dragging = false;

        var taskbar = TaskbarLocator.FindTaskbar();
        var scale = NativeMethods.GetDpiForWindow(_hwnd) / 96.0;

        if (taskbar != IntPtr.Zero &&
            TaskbarLocator.TryGetPillRect(taskbar, _hwnd, scale, null, out var autoRect))
        {
            NativeMethods.GetWindowRect(_hwnd, out var rect);

            if (Math.Abs(rect.Left - autoRect.Left) <= 32)
            {
                if (_settings.PillRightInset is not null)
                {
                    _settings.PillRightInset = null;
                    _settings.Save();
                }

                RepositionPill();
                return;
            }
        }

        RememberPillPosition();
        RepositionPill();
    }

    /// <summary>松手后记住“右缘离任务栏右缘多远”，下次启动还放回这个槽位。</summary>
    private void RememberPillPosition()
    {
        var taskbar = TaskbarLocator.FindTaskbar();
        if (taskbar == IntPtr.Zero || !TaskbarLocator.TryGetBand(taskbar, out var band)) return;

        NativeMethods.GetWindowRect(_hwnd, out var rect);
        _settings.PillRightInset = Math.Max(0, band.Right - rect.Right);
        _settings.Save();

        Log.Write($"pill moved inset={_settings.PillRightInset}");
    }

    #endregion

    #region 生命周期

    /// <summary>窗口句柄是否还有效（任务栏重启会销毁子窗口）。</summary>
    public bool IsAlive => NativeMethods.IsWindow(_hwnd);

    /// <summary>重建前清理旧窗口的附属窗口。</summary>
    public void Detach()
    {
        try
        {
            _results.HidePanel();
        }
        catch
        {
            // 窗口已经没了
        }

        try
        {
            _nonClient.ClearRegionRects(NonClientRegionKind.Caption);
        }
        catch
        {
        }

        _hotkey?.Dispose();
    }

    public void ShowAndFocus()
    {
        _appWindow.Show();
        EnsureOnTop();

        var fg = NativeMethods.SetForegroundWindow(_hwnd);
        Activate();
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();

        // 热键唤出时，即使抢前台失败（Windows 的前台锁），也要让结果面板出现在眼前
        _results.SetFooter(FooterText());
        _results.ShowPanel();

        Log.Write($"hotkey show fg={fg} active={_active}");
    }

    /// <summary>开机启动时不抢焦点。</summary>
    public void ShowWithoutActivation()
    {
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
        EnsureOnTop();
    }

    /// <summary>
    /// 让胶囊一直贴在任务栏上面。
    /// 它是普通顶层窗口（任务栏不允许第三方窗口变成它的子窗口——那样会拿不到键盘焦点），
    /// 所以只做两件事：确认 TOPMOST 位还在；发现被任务栏或压在任务栏上的挂件盖住时才处理。
    /// 被任务栏盖住就重提 z 序；被播放条/媒体卡片盖住就让开位置，绝不压住别人的图标。
    /// </summary>
    private void EnsureOnTop()
    {
        if (!NativeMethods.IsWindow(_hwnd) || !NativeMethods.IsWindowVisible(_hwnd)) return;

        var ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        var lostTopmost = (ex & NativeMethods.WsExTopmost) == 0;
        var cover = FindCover();

        if (cover == CoverKind.Widget)
        {
            RepositionPill();
            return;
        }

        if (!lostTopmost && cover == CoverKind.None) return;

        if (lostTopmost && _appWindow.Presenter is OverlappedPresenter presenter)
        {
            // WinUI 在 Show 之后会重设一次窗口样式，这时只有 presenter 能把 TOPMOST 真正设上
            presenter.IsAlwaysOnTop = false;
            presenter.IsAlwaysOnTop = true;
        }

        WindowChrome.MakeTopmost(_hwnd);

        var now = $"0x{NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64():X}";
        if (now != _lastExLog)
        {
            _lastExLog = now;
            Log.Write($"pill ex={now}");
        }
    }

    private enum CoverKind
    {
        None,
        Taskbar,
        Widget
    }

    /// <summary>
    /// 胶囊中心那个点现在归谁。被任务栏盖住返回 Taskbar；
    /// 被压在任务栏这一条上的播放条/媒体卡片盖住返回 Widget；
    /// 被开始菜单、结果面板这类窗口盖住返回 None——那种时候不该硬顶上去。
    /// </summary>
    private CoverKind FindCover()
    {
        NativeMethods.GetWindowRect(_hwnd, out var self);
        var probe = new NativeMethods.Point { X = self.Left + self.Width / 2, Y = self.Top + self.Height / 2 };

        var hit = NativeMethods.WindowFromPoint(probe);
        if (hit == IntPtr.Zero) return CoverKind.None;

        var root = NativeMethods.GetAncestor(hit, NativeMethods.GaRoot);
        if (root == IntPtr.Zero || root == _hwnd) return CoverKind.None;

        if (NativeMethods.GetWindowThreadProcessId(root, out var hitPid) == 0) return CoverKind.None;
        if (NativeMethods.GetWindowThreadProcessId(_hwnd, out var selfPid) == 0) return CoverKind.None;
        if (hitPid == selfPid) return CoverKind.None;

        var taskbar = TaskbarLocator.FindTaskbar();
        if (root == taskbar) return CoverKind.Taskbar;

        var className = NativeMethods.GetClassName(root);
        if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return CoverKind.Taskbar;

        // 压在任务栏这一条上、又不是系统窗口的，就是别人家的挂件
        if (NativeMethods.GetWindowRect(root, out var cover) && cover.Height <= self.Height * 4) return CoverKind.Widget;

        return CoverKind.None;
    }

    private string? _lastExLog;

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        _active = e.WindowActivationState != WindowActivationState.Deactivated;

        if (_active)
        {
            _results.SetFooter(FooterText());
            if (QueryBox.Text.Trim().Length == 0)
                _results.ShowIdle("把文件拖到搜索框上", "之后就能在这里搜到文件里的任何内容");

            _results.ShowPanel();
        }
        else if (!_ingesting)
        {
            _results.HidePanel();
        }
    }

    private void OnHotkeyPressed() => DispatcherQueue.TryEnqueue(ShowAndFocus);

    #endregion

    #region 引擎

    private async Task InitializeEngineAsync()
    {
        try
        {
            var engine = await App.GetEngineAsync();
            engine.Ingestion.Progress = new UiProgress<IngestProgress>(DispatcherQueue, OnIngestProgress);

            _engine = engine;
            UpdateFooter();

            if (!string.IsNullOrEmpty(engine.EmbedderError))
                _results.ShowProgress("关键词检索模式");
        }
        catch (Exception ex)
        {
            _results.ShowProgress("索引初始化失败：" + ex.Message);
        }
    }

    private void OnIngestProgress(IngestProgress progress)
    {
        if (string.IsNullOrEmpty(progress.FileName)) return;

        _results.ShowProgress(progress.Stage == "向量化"
            ? $"{progress.FileName} {progress.Completed}/{progress.Total}"
            : $"{progress.FileName} · {progress.Stage}");
    }

    private async Task IngestAsync(IReadOnlyList<string> paths)
    {
        if (_engine is null)
        {
            _results.ShowProgress("索引还在初始化，请稍候");
            return;
        }

        _ingesting = true;
        _results.ShowPanel();
        _results.ShowProgress($"正在处理 {paths.Count} 个文件…");

        try
        {
            var results = await _engine.Ingestion.IngestAsync(paths, ResolveConflict);

            var added = results.Count(r => r.Status is IngestStatus.Added or IngestStatus.Replaced);
            var skipped = results.Count(r => r.Status == IngestStatus.Skipped);
            var failed = results.Where(r => r.Status is IngestStatus.Failed or IngestStatus.Unsupported).ToList();

            _results.ShowProgress(failed.Count > 0
                ? $"已加入 {added} 个，{failed.Count} 个失败：{failed[0].Message}"
                : added > 0
                    ? $"已加入 {added} 个文档"
                    : skipped > 0 ? "内容已存在，未重复加入" : "没有可加入的文件");

            UpdateFooter();

            if (QueryBox.Text.Trim().Length > 0)
                await RunSearchAsync();
            else if (added > 0)
                _results.ShowIdle($"已加入 {added} 个文档", "直接输入关键词就能搜到里面的内容");
        }
        catch (Exception ex)
        {
            _results.ShowProgress("加入失败：" + ex.Message);
        }
        finally
        {
            _ingesting = false;
        }
    }

    /// <summary>入库线程阻塞等这个回调，UI 线程在结果面板里问用户。</summary>
    private ConflictDecision ResolveConflict(ConflictInfo info)
    {
        if (DispatcherQueue.HasThreadAccess) return ConflictDecision.Skip;

        var completion = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    _results.ShowPanel();
                    completion.TrySetResult(await _results.AskConflictAsync(info));
                }
                catch
                {
                    completion.TrySetResult(ConflictDecision.Skip);
                }
            }))
        {
            return ConflictDecision.Skip;
        }

        return completion.Task.Wait(TimeSpan.FromMinutes(2))
            ? completion.Task.Result
            : ConflictDecision.Skip;
    }

    #endregion

    #region 检索

    private void OnQueryTextChanged(object sender, TextChangedEventArgs e)
    {
        // 打字时把右侧让给输入框自带的清除按钮（×），空闲时再放回“文档库”按钮
        BrandChip.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        _debounce.Stop();
        _debounce.Start();
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunSearchAsync()
    {
        if (_engine is null) return;

        var query = QueryBox.Text.Trim();

        if (query.Length == 0)
        {
            _searchCts?.Cancel();
            SetBusy(false);
            _results.SetFooter(FooterText());
            _results.ShowIdle("把文件拖到搜索框上", "之后就能在这里搜到文件里的任何内容");
            return;
        }

        _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;

        SetBusy(true);

        try
        {
            var hits = await _engine.SearchAsync(query, MaxHits, cts.Token);
            if (cts.IsCancellationRequested) return;

            _results.SetFooter(FooterText());
            _results.ShowHits(hits, query);
            _results.ShowPanel();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _results.ShowProgress("检索失败：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts)) SetBusy(false);
        }
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                _results.MoveSelection(1);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                _results.MoveSelection(-1);
                e.Handled = true;
                break;

            case VirtualKey.Enter:
                if (_results.SelectedHit is { } hit)
                {
                    ResultsWindow.OpenDocument(hit.DocPath);
                    _results.HidePanel();
                }
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (QueryBox.Text.Length > 0)
                    QueryBox.Text = string.Empty;
                else
                    _results.HidePanel();
                e.Handled = true;
                break;

            case VirtualKey.O when IsControlDown():
                _ = PickAndIngestAsync();
                e.Handled = true;
                break;
        }
    }

    private static bool IsControlDown()
    {
        var state = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control);
        return state.HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    #endregion

    #region 拖放

    private void OnDragEnter(object sender, DragEventArgs e) => AcceptDrag(e);

    private void OnDragOver(object sender, DragEventArgs e) => AcceptDrag(e);

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        var position = e.GetPosition(RootGrid);
        var inside = position.X >= 0 && position.Y >= 0 &&
                     position.X <= RootGrid.ActualWidth && position.Y <= RootGrid.ActualHeight;

        if (!inside) DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void AcceptDrag(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "加入知识库";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;

        DropOverlay.Visibility = Visibility.Visible;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var deferral = e.GetDeferral();

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();

            if (paths.Count > 0) await IngestAsync(paths);
        }
        catch (Exception ex)
        {
            _results.ShowProgress("读取拖入内容失败：" + ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    #endregion

    #region 外观

    private Brush _pillIdleBrush = new SolidColorBrush(Colors.Transparent);
    private Brush _pillHoverBrush = new SolidColorBrush(Colors.Transparent);
    private List<Windows.UI.Color>? _taskbarTint;

    /// <summary>
    /// 胶囊要看起来是一层压在任务栏上的半透明玻璃，但窗口本身不透明，
    /// 所以直接取任务栏在这块位置的真实颜色：窗口底色铺成它，胶囊用“它 + 一点白”。
    /// 横向取 5 个点——浅色主题下任务栏会随壁纸在几十个像素里就有几个色阶的深浅，
    /// 只取一个中点的话，胶囊两端的矩形会明显比周围暗一截。
    /// </summary>
    private void ApplyTaskbarTint(NativeMethods.Rect rect)
    {
        const double sampleY = 2;   // 胶囊上沿往上一点点，仍在任务栏里
        var offsets = new[] { 0.02, 0.26, 0.50, 0.74, 0.98 };

        var colors = new List<Windows.UI.Color>(offsets.Length);
        foreach (var t in offsets)
        {
            var x = rect.Left + (int)Math.Round((rect.Width - 1) * t);
            if (NativeMethods.TrySampleScreen(x, rect.Top - (int)sampleY) is { } c)
                colors.Add(c);
        }

        if (colors.Count == 0)
        {
            Log.Write("tint sample failed");
            return;
        }

        // 胶囊底色正常情况下跟任务栏一致；万一「Windows 模式」和「应用模式」一深一浅
        // （任务栏亮、应用深，或反过来），就按应用主题画一个自己的底色，
        // 否则浅底配浅字直接看不见。
        var taskbarIsLight = IsLight(colors);
        var appIsLight = Application.Current.RequestedTheme != ApplicationTheme.Dark;

        var pillBase = colors;
        var pillIsLight = taskbarIsLight;

        if (taskbarIsLight != appIsLight)
        {
            pillIsLight = appIsLight;
            pillBase = new List<Windows.UI.Color>
            {
                pillIsLight
                    ? Windows.UI.Color.FromArgb(0xFF, 0xEF, 0xF2, 0xF3)
                    : Windows.UI.Color.FromArgb(0xFF, 0x22, 0x24, 0x26)
            };
        }

        if (_taskbarTint is { } old &&
            old.Count == colors.Count &&
            old.SequenceEqual(colors) &&
            _pillIsLight == pillIsLight)
        {
            return;
        }

        _taskbarTint = colors;
        _pillIsLight = pillIsLight;

        // 把窗口的矩形铺成任务栏的横向渐变，圆角外的四个小三角就“消失”在任务栏里
        RootGrid.Background = BuildTintBrush(colors, lighten: 0);

        _pillIdleBrush = BuildTintBrush(pillBase, lighten: 0.145);
        _pillHoverBrush = BuildTintBrush(pillBase, lighten: 0.200);

        Shell.Background = _pillIdleBrush;
    }

    private static bool IsLight(List<Windows.UI.Color> colors)
    {
        var sum = 0.0;
        foreach (var c in colors)
            sum += 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;

        return sum / colors.Count > 150;
    }

    private bool? _pillIsLight;

    private Brush BuildTintBrush(List<Windows.UI.Color> colors, double lighten)
    {
        if (colors.Count == 1) colors = new List<Windows.UI.Color> { colors[0], colors[0] };

        var brush = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint = new Windows.Foundation.Point(1, 0.5)
        };

        for (var i = 0; i < colors.Count; i++)
        {
            var offset = colors.Count == 1 ? 0 : (double)i / (colors.Count - 1);
            var color = lighten > 0 ? Lighten(colors[i], lighten) : colors[i];
            brush.GradientStops.Add(new GradientStop { Offset = offset, Color = color });
        }

        return brush;
    }

    private static Windows.UI.Color Lighten(Windows.UI.Color color, double amount) =>
        Windows.UI.Color.FromArgb(
            0xFF,
            (byte)Math.Round(color.R + (255 - color.R) * amount),
            (byte)Math.Round(color.G + (255 - color.G) * amount),
            (byte)Math.Round(color.B + (255 - color.B) * amount));

    private void OnRootPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        Shell.Background = _pillHoverBrush;
        Shell.BorderBrush = ThemeBrush("PillStrokeHover");
    }

    private void OnRootPointerExited(object sender, PointerRoutedEventArgs e)
    {
        Shell.Background = _pillIdleBrush;
        Shell.BorderBrush = ThemeBrush("PillStrokeIdle");
    }

    private void OnBrandPointerEntered(object sender, PointerRoutedEventArgs e) =>
        BrandChip.Background = ThemeBrush("HoverFill");

    private void OnBrandPointerExited(object sender, PointerRoutedEventArgs e) =>
        BrandChip.Background = new SolidColorBrush(Colors.Transparent);

    /// <summary>按当前主题取应用自己定义的画刷。</summary>
    private static Brush ThemeBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
            return brush;

        return new SolidColorBrush(Colors.Transparent);
    }

    #endregion

    #region 菜单

    private void BuildBrandMenu()
    {
        var menu = new MenuFlyout();

        var add = new MenuFlyoutItem { Text = "添加文件…", Icon = new FontIcon { Glyph = "\uE710" } };
        add.Click += async (_, _) => await PickAndIngestAsync();

        var clear = new MenuFlyoutItem { Text = "清空知识库", Icon = new FontIcon { Glyph = "\uE74D" } };
        clear.Click += (_, _) => ClearKnowledgeBase();

        var quit = new MenuFlyoutItem { Text = "退出 DocuFind", Icon = new FontIcon { Glyph = "\uE7E8" } };
        quit.Click += (_, _) =>
        {
            _hotkey?.Dispose();
            _engine?.Dispose();
            Application.Current.Exit();
        };

        menu.Items.Add(add);
        menu.Items.Add(clear);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);

        BrandChip.ContextFlyout = menu;
        BrandChip.Tapped += (_, _) => menu.ShowAt(BrandChip);
    }

    private async Task PickAndIngestAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            ViewMode = Windows.Storage.Pickers.PickerViewMode.List,
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };

        InitializeWithWindow.Initialize(picker, _hwnd);

        foreach (var extension in DocuFind.Core.Parsing.DocumentParserFactory.SupportedExtensions)
            picker.FileTypeFilter.Add(extension);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null || files.Count == 0) return;

        await IngestAsync(files.Select(f => f.Path).ToList());
    }

    private void ClearKnowledgeBase()
    {
        if (_engine is null) return;

        var answer = NativeMethods.MessageBox(
            _hwnd,
            "会删除所有已收录的文档索引（原文件不受影响）。\n\n确定继续？",
            "清空知识库",
            NativeMethods.MbYesNo | NativeMethods.MbIconWarning);

        if (answer != 6) return; // IDYES

        _engine.ClearAll();
        QueryBox.Text = string.Empty;
        UpdateFooter();
        _results.ShowIdle("知识库已清空", "重新把文件拖到搜索框上即可");
    }

    #endregion

    #region 辅助

    private void UpdateFooter() => _results.SetFooter(FooterText());

    private string FooterText()
    {
        if (_engine is null) return string.Empty;

        var (documents, chunks) = _engine.GetCounts();
        return documents == 0 ? "还没有收录文档" : $"共 {documents} 篇文档 · {chunks} 个片段";
    }

    #endregion
}
