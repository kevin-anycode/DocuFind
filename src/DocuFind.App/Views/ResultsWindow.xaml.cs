using DocuFind.App.Interop;
using DocuFind.Core.Ingestion;
using DocuFind.Core.Search;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;
using WinRT.Interop;

namespace DocuFind.App.Views;

/// <summary>
/// 结果面板：从任务栏上方弹出，不抢焦点（输入始终留在胶囊里）。
/// </summary>
public sealed partial class ResultsWindow : Window
{
    private const int PanelWidth = 600;
    private const int PanelHeight = 440;

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;
    private readonly List<SearchHit> _hits = new();
    private readonly List<Border> _rows = new();

    private TaskCompletionSource<ConflictDecision>? _conflict;
    private int _selected = -1;
    private bool _visible;

    public ResultsWindow()
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
            presenter.IsAlwaysOnTop = true;
        }

        _appWindow.IsShownInSwitchers = false;

        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            // 无合成器支持时用纯色底
        }

        WindowChrome.MakeNoActivate(_hwnd);
        WindowChrome.RemoveBorder(_hwnd);
        WindowChrome.StripFrame(_hwnd);
    }

    public SearchHit? SelectedHit =>
        _selected >= 0 && _selected < _hits.Count ? _hits[_selected] : null;

    #region 摆放与显隐

    /// <summary>贴着任务栏上沿弹出，左边缘与胶囊对齐。</summary>
    public void PlaceAbove(int left, int taskbarTop, double scale)
    {
        var width = (int)Math.Round(PanelWidth * scale);
        var height = (int)Math.Round(PanelHeight * scale);
        var margin = (int)Math.Round(8 * scale);

        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var work = area.WorkArea;

        var x = left;
        if (x + width > work.X + work.Width - margin) x = work.X + work.Width - width - margin;
        if (x < work.X + margin) x = work.X + margin;

        var y = taskbarTop - height - margin;
        if (y < work.Y + margin) y = work.Y + margin;

        _appWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
        WindowChrome.StripFrame(_hwnd);
        WindowChrome.Round(_hwnd, width, height, (int)Math.Round(8 * scale));
    }

    public void ShowPanel()
    {
        if (_visible) return;

        RootGrid.Opacity = 0;
        NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShowNoActivate);
        WindowChrome.MakeTopmost(_hwnd);
        _visible = true;

        // 弹出时做一次很短的淡入，和系统弹出层的手感一致
        var fade = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(fade, RootGrid);
        Storyboard.SetTargetProperty(fade, "Opacity");

        var board = new Storyboard();
        board.Children.Add(fade);
        board.Begin();
    }

    public void HidePanel()
    {
        if (!_visible) return;

        _appWindow.Hide();
        _visible = false;

        _conflict?.TrySetResult(ConflictDecision.Skip);
        _conflict = null;
        ConflictCard.Visibility = Visibility.Collapsed;
    }

    #endregion

    #region 内容

    public void ShowIdle(string title, string detail)
    {
        ClearRows();
        EmptyTitle.Text = title;
        EmptyDetail.Text = detail;
        EmptyPanel.Visibility = Visibility.Visible;
    }

    public void ShowHits(IReadOnlyList<SearchHit> hits, string query)
    {
        ClearRows();
        _hits.AddRange(hits);
        _selected = hits.Count > 0 ? 0 : -1;

        for (var i = 0; i < hits.Count; i++)
        {
            var row = CreateRow(hits[i], i);
            _rows.Add(row);
            ResultsStack.Children.Add(row);
        }

        if (hits.Count == 0)
        {
            EmptyTitle.Text = $"没有找到「{query}」";
            EmptyDetail.Text = "试试更短的关键词，或者先把相关文档拖进来";
            EmptyPanel.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
        }

        UpdateSelection(scroll: false);
        ResultsScroller.ChangeView(null, 0, null, true);
    }

    public void ShowProgress(string text) => StatusText.Text = text;

    public void SetFooter(string text) => FooterText.Text = text;

    public void MoveSelection(int delta)
    {
        if (_hits.Count == 0) return;

        _selected = _selected < 0 ? 0 : (_selected + delta + _hits.Count) % _hits.Count;
        UpdateSelection(scroll: true);
    }

    /// <summary>重名冲突：面板内直接给三个选项，避免弹窗抢焦点。</summary>
    public Task<ConflictDecision> AskConflictAsync(ConflictInfo info)
    {
        _conflict?.TrySetResult(ConflictDecision.Skip);
        _conflict = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);

        ConflictTitle.Text = "已存在同名文档";
        ConflictDetail.Text = $"知识库里已有一份《{info.Existing.FileName}》，要用新文件替换它吗？";
        ConflictCard.Visibility = Visibility.Visible;

        return _conflict.Task;
    }

    private void ResolveConflict(ConflictDecision decision)
    {
        ConflictCard.Visibility = Visibility.Collapsed;
        _conflict?.TrySetResult(decision);
        _conflict = null;
    }

    private void OnConflictOverwrite(object sender, RoutedEventArgs e) => ResolveConflict(ConflictDecision.Overwrite);
    private void OnConflictKeepBoth(object sender, RoutedEventArgs e) => ResolveConflict(ConflictDecision.KeepBoth);
    private void OnConflictSkip(object sender, RoutedEventArgs e) => ResolveConflict(ConflictDecision.Skip);

    #endregion

    #region 行渲染

    private void ClearRows()
    {
        ResultsStack.Children.Clear();
        _rows.Clear();
        _hits.Clear();
        _selected = -1;
        StatusText.Text = string.Empty;
    }

    private Border CreateRow(SearchHit hit, int index)
    {
        var row = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(6, 8, 8, 8),
            Background = new SolidColorBrush(Colors.Transparent)
        };

        var muted = ResolveBrush("MutedForeground", Color.FromArgb(0xFF, 0xA2, 0xA6, 0xAC));
        var strong = ResolveBrush("StrongForeground", Color.FromArgb(0xFF, 0xF3, 0xF4, 0xF6));

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 选中时才出现的那根强调竖条
        var bar = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = ResolveBrush("HighlightForeground", Color.FromArgb(0xFF, 0x8F, 0xD0, 0xFF)),
            Margin = new Thickness(0, 2, 0, 2),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(bar, 0);
        grid.Children.Add(bar);

        // 文件类型徽标
        var badge = new Border
        {
            Width = 26,
            Height = 26,
            CornerRadius = new CornerRadius(6),
            Background = ResolveBrush("BadgeFill", Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 1, 0, 0)
        };
        badge.Child = new FontIcon
        {
            Glyph = GlyphFor(hit.FileName),
            FontSize = 13,
            Foreground = muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(badge);

        var text = new StackPanel { Spacing = 3 };
        text.Children.Add(new TextBlock
        {
            Text = hit.FileName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = strong
        });
        text.Children.Add(BuildSnippet(hit));
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.45
        };

        if (hit.Page > 0)
        {
            actions.Children.Add(new TextBlock
            {
                Text = $"第 {hit.Page} 页",
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = muted
            });
        }

        actions.Children.Add(CreateIconButton("\uE8B7", "打开所在文件夹", () => OpenFolder(hit.DocPath)));
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        row.Child = grid;
        row.ContextFlyout = BuildRowMenu(hit);
        row.Tag = new RowParts { Bar = bar, Actions = actions };

        AutomationProperties.SetName(row, $"{hit.FileName}，{hit.Snippet}");
        ToolTipService.SetToolTip(row, hit.DocPath);

        row.Tapped += (_, _) =>
        {
            _selected = index;
            UpdateSelection(scroll: false);
            OpenDocument(hit.DocPath);
            HidePanel();
        };

        row.PointerEntered += (_, _) =>
        {
            if (index != _selected)
                row.Background = ResolveBrush("HoverFill", Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

            actions.Opacity = 1;
        };

        row.PointerExited += (_, _) =>
        {
            if (index != _selected)
            {
                row.Background = new SolidColorBrush(Colors.Transparent);
                actions.Opacity = 0.45;
            }
        };

        return row;
    }

    /// <summary>一行里需要跟着选中/悬停一起变的那几块。</summary>
    private sealed class RowParts
    {
        public required Border Bar { get; init; }
        public required StackPanel Actions { get; init; }
    }

    private TextBlock BuildSnippet(SearchHit hit)
    {
        var block = new TextBlock
        {
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 17,
            Foreground = ResolveBrush("SnippetForeground", Color.FromArgb(0xFF, 0xC2, 0xC6, 0xCD))
        };

        var snippet = hit.Snippet;
        var highlight = ResolveBrush("HighlightForeground", Color.FromArgb(0xFF, 0x8F, 0xD0, 0xFF));
        var cursor = 0;

        foreach (var range in hit.Highlights)
        {
            var start = Math.Clamp(range.Start, 0, snippet.Length);
            var length = Math.Clamp(range.Length, 0, snippet.Length - start);
            if (length <= 0 || start < cursor) continue;

            if (start > cursor)
                block.Inlines.Add(new Run { Text = snippet[cursor..start] });

            block.Inlines.Add(new Run
            {
                Text = snippet.Substring(start, length),
                FontWeight = FontWeights.SemiBold,
                Foreground = highlight
            });

            cursor = start + length;
        }

        if (cursor < snippet.Length)
            block.Inlines.Add(new Run { Text = snippet[cursor..] });

        return block;
    }

    private static Button CreateIconButton(string glyph, string tooltip, Action onClick)
    {
        var button = new Button
        {
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 11.5 },
            VerticalAlignment = VerticalAlignment.Center
        };

        AutomationProperties.SetName(button, tooltip);
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => onClick();
        button.Tapped += (_, e) => e.Handled = true;

        return button;
    }

    private MenuFlyout BuildRowMenu(SearchHit hit)
    {
        var menu = new MenuFlyout();

        var open = new MenuFlyoutItem { Text = "打开文档", Icon = new FontIcon { Glyph = "\uE8E5" } };
        open.Click += (_, _) => { OpenDocument(hit.DocPath); HidePanel(); };

        var folder = new MenuFlyoutItem { Text = "打开所在文件夹", Icon = new FontIcon { Glyph = "\uE8B7" } };
        folder.Click += (_, _) => OpenFolder(hit.DocPath);

        var copy = new MenuFlyoutItem { Text = "复制文件路径", Icon = new FontIcon { Glyph = "\uE8C8" } };
        copy.Click += (_, _) => CopyToClipboard(hit.DocPath);

        menu.Items.Add(open);
        menu.Items.Add(folder);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(copy);

        return menu;
    }

    private void UpdateSelection(bool scroll)
    {
        var selectedFill = ResolveBrush("SelectedFill", Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF));
        var transparent = new SolidColorBrush(Colors.Transparent);

        for (var i = 0; i < _rows.Count; i++)
        {
            var selected = i == _selected;
            _rows[i].Background = selected ? selectedFill : transparent;

            if (_rows[i].Tag is RowParts parts)
            {
                parts.Bar.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
                parts.Actions.Opacity = selected ? 1 : 0.45;
            }
        }

        if (scroll && _selected >= 0 && _selected < _rows.Count)
            _rows[_selected].StartBringIntoView();
    }

    #endregion

    #region 辅助

    private static string GlyphFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "\uEA90",
            ".xlsx" or ".xlsm" or ".csv" => "\uE9F9",
            _ => "\uE8A5"
        };

    internal static void OpenDocument(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // 文件被删或没有关联程序
        }
    }

    internal static void OpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }
    }

    private static void CopyToClipboard(string text)
    {
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(text);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    private static Brush ResolveBrush(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush)
            return brush;

        return new SolidColorBrush(fallback);
    }

    #endregion
}
