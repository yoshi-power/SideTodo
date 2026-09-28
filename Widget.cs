using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace SideTodo;

public sealed partial class Widget : Window
{
    readonly Store store;
    readonly Grid root = new() { Background = Brushes.Transparent, ClipToBounds = true };
    readonly StackPanel rows = new();
    readonly Grid panel = new() { Margin = new Thickness(13, 10, 13, 10), MinWidth = 214 };
    readonly Border card, marker;
    readonly TextBox quick;
    readonly TextBlock placeholder;
    readonly Border composeFrame;
    readonly Button submit;
    readonly Button todayTab, laterTab;
    readonly ScrollViewer scroll;
    readonly DispatcherTimer proximity = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromMilliseconds(650) };
    readonly DispatcherTimer settle = new() { Interval = TimeSpan.FromMilliseconds(1100) };
    readonly DispatcherTimer dayTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly Forms.NotifyIcon tray;
    readonly Dictionary<bool, string> drafts = new() { [false] = "", [true] = "" };
    bool expanded, editing, later, showDone, typing, switching, dragging, composing;
    bool closing, modalMenu;
    readonly bool diagnostic;
    DateTime suppressProximityUntil;
    double targetWidth, targetHeight;
    TextBox? activeTitle;
    int transition;
    int listVersion;
    DateTime renderedDay = DateTime.Today;
    Point dragStart;
    public Widget(Store store, bool diagnostic = false)
    {
        this.store = store;
        this.diagnostic = diagnostic;
        PrepareDetailHover();
        Title = "SideTodo"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; FontFamily = new FontFamily("Segoe UI, Malgun Gothic");
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        if (diagnostic) ShowInTaskbar = true; else DesktopIntegration.HideFromSwitcher(this);
        Left = SystemParameters.WorkArea.Left; Top = ClampY(store.Data.Y); Width = 12; Height = 44; Content = root;
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Height = 27, Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        todayTab = Look.Button("오늘", () => SwitchTab(false)); laterTab = Look.Button("앞으로", () => SwitchTab(true)); Grid.SetColumn(laterTab, 1); header.Children.Add(todayTab); header.Children.Add(laterTab);
        var grip = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeNS, ToolTip = "드래그해서 높이 조절" }; Grid.SetColumn(grip, 2); header.Children.Add(grip);
        grip.MouseLeftButtonDown += (_, e) => { dragStart = e.GetPosition(this); dragging = true; grip.CaptureMouse(); };
        grip.MouseMove += (_, e) => { if (dragging && e.LeftButton == MouseButtonState.Pressed) Top = ClampY(Top + e.GetPosition(this).Y - dragStart.Y); };
        grip.MouseLeftButtonUp += (_, _) => { dragging = false; grip.ReleaseMouseCapture(); store.Data.Y = Top; Save(); if (!IsMouseOver) hide.Start(); };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var archive = Look.Button("▤", OpenArchive); archive.FontSize = 14; archive.Padding = new Thickness(5, 1, 5, 1); archive.Foreground = Look.Muted; archive.ToolTip = "완료 보관함"; System.Windows.Automation.AutomationProperties.SetName(archive, "완료 보관함"); Grid.SetColumn(archive, 3); header.Children.Add(archive);
        var plus = Look.Button("+", () => Edit(null)); plus.FontSize = 18; plus.Padding = new Thickness(6, 0, 6, 1); plus.ToolTip = "자세한 일정"; Grid.SetColumn(plus, 4); header.Children.Add(plus); panel.Children.Add(header);
        scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly }; Grid.SetRow(scroll, 1); panel.Children.Add(scroll);
        var composer = new Grid { MinHeight = 29 }; composer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(23) }); composer.ColumnDefinitions.Add(new ColumnDefinition()); composer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var emptyCheck = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), BorderBrush = Look.Brush("#666666"), VerticalAlignment = VerticalAlignment.Center }; composer.Children.Add(emptyCheck);
        var inputArea = new Grid(); Grid.SetColumn(inputArea, 1); composer.Children.Add(inputArea);
        quick = InlineInput(""); quick.MinHeight = 28; quick.MaxLength = 300; quick.ToolTip = "Enter로 추가"; System.Windows.Automation.AutomationProperties.SetName(quick, "새 할 일");
        placeholder = Look.Label("할 일", 12, Look.Muted); placeholder.IsHitTestVisible = false; placeholder.Margin = new Thickness(0, 3, 0, 3); inputArea.Children.Add(quick); inputArea.Children.Add(placeholder);
        submit = Look.Button("↵", () => { if (CommitQuick()) { ScheduleSettle(); scroll.ScrollToEnd(); } }); submit.ToolTip = "추가 · Enter"; submit.Padding = new Thickness(6, 3, 3, 3); submit.FontSize = 15; submit.Foreground = Look.Accent; submit.Visibility = Visibility.Collapsed; Grid.SetColumn(submit, 2); composer.Children.Add(submit);
        TextCompositionManager.AddPreviewTextInputStartHandler(quick, (_, _) => composing = true);
        TextCompositionManager.AddPreviewTextInputHandler(quick, (_, _) => composing = false);
        quick.GotKeyboardFocus += (_, _) => { activeTitle = null; Grow(); };
        quick.LostKeyboardFocus += (_, _) => ScheduleSettle();
        quick.TextChanged += (_, _) => { placeholder.Visibility = quick.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; if (!switching) { drafts[later] = quick.Text; Grow(); } };
        quick.PreviewKeyDown += (_, e) =>
        {
            // ImeProcessed Enter belongs to Korean composition, not to the next row.
            if (e.Key != Key.Enter || composing) return;
            e.Handled = true;
            if (CommitQuick()) { ScheduleSettle(); scroll.ScrollToEnd(); }
        };
        composeFrame = new Border { Child = composer, CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Transparent), Background = new SolidColorBrush(Colors.Transparent), Padding = new Thickness(6, 0, 4, 0), Margin = new Thickness(0, 6, 0, 0) };
        Grid.SetRow(composeFrame, 2); panel.Children.Add(composeFrame);
        card = Look.Card(panel); card.Margin = new Thickness(3, 2, 2, 2); card.Opacity = 0; card.Visibility = Visibility.Hidden; card.IsHitTestVisible = false; root.Children.Add(card);
        marker = new Border { Width = 4, Height = 28, CornerRadius = new CornerRadius(2), Background = Look.Brush("#A0D0D0D0"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 8, 0, 0) }; root.Children.Add(marker);
        var context = new ContextMenu();
        var completed = new MenuItem { Header = "완료한 일", IsCheckable = true }; completed.Click += (_, _) => { showDone = completed.IsChecked; RenderRows(); ResizePanel(); }; context.Items.Add(completed);
        var exit = new MenuItem { Header = "종료" }; exit.Click += (_, _) => Application.Current.Shutdown(); context.Items.Add(exit); root.ContextMenu = context;
        context.Opened += (_, _) => { modalMenu = true; hide.Stop(); }; context.Closed += (_, _) => { modalMenu = false; if (!IsMouseOver) hide.Start(); };
        proximity.Tick += (_, _) => { if (!expanded && !editing && !modalMenu && DateTime.UtcNow >= suppressProximityUntil && NearMarker()) { Expand(false); if (!IsMouseOver) hide.Start(); } };
        proximity.Start();
        hide.Tick += (_, _) => { if (IsMouseOver || NearMarker() || editing || dragging || modalMenu || (panel.IsKeyboardFocusWithin && IsActive && (quick.Text.Length > 0 || activeTitle != null))) return; hide.Stop(); Collapse(); };
        settle.Tick += (_, _) => { settle.Stop(); if (!expanded || editing) return; SetTyping(false); ResizePanel(); };
        MouseEnter += (_, _) => { hide.Stop(); if (!expanded && DateTime.UtcNow >= suppressProximityUntil) Expand(false); };
        MouseLeave += (_, _) => { if (!editing && !dragging && !modalMenu) hide.Start(); };
        Deactivated += (_, _) => { if (!editing && !modalMenu && !dragging) Collapse(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !editing) { Collapse(); e.Handled = true; } };
        dayTimer.Tick += (_, _) => { if (renderedDay != DateTime.Today) { renderedDay = DateTime.Today; if (expanded && !panel.IsKeyboardFocusWithin) { RenderRows(); ResizePanel(); } } }; dayTimer.Start();
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "SideTodo", Visible = true };
        var menu = new Forms.ContextMenuStrip(); menu.Items.Add("열기", null, (_, _) => Dispatcher.Invoke(() => Expand(true))); menu.Items.Add("자세한 일정", null, (_, _) => Dispatcher.Invoke(() => Edit(null)));
        menu.Items.Add("완료한 일", null, (_, _) => Dispatcher.Invoke(() => { showDone = !showDone; completed.IsChecked = showDone; Expand(true); RenderRows(); ResizePanel(); }));
        menu.Items.Add("위치 초기화", null, (_, _) => Dispatcher.Invoke(() => { Top = ClampY(SystemParameters.WorkArea.Top + 160); store.Data.Y = Top; Save(); }));
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(() => Application.Current.Shutdown())); tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => Expand(true));
        Closed += (_, _) => { closing = true; tray.Dispose(); proximity.Stop(); hide.Stop(); settle.Stop(); dayTimer.Stop(); detailHover.Stop(); };
        RenderRows();
    }
    TextBox InlineInput(string text) => new() { Text = text, Background = Brushes.Transparent, Foreground = Look.Text, CaretBrush = Look.Accent, SelectionBrush = Look.Brush("#776C62"), BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4), FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, FocusVisualStyle = null };
    bool NearMarker() => DesktopIntegration.CursorIn(this) is Point p && DesktopIntegration.NearMarker(p);
    double ClampY(double y) => Math.Clamp(double.IsFinite(y) ? y : 160, SystemParameters.WorkArea.Top + 10, Math.Max(SystemParameters.WorkArea.Top + 10, SystemParameters.WorkArea.Bottom - 325));
    bool Save()
    {
        try { store.Save(); return true; }
        catch (Exception e) { bool previous = editing; editing = true; MessageBox.Show(this, "저장하지 못했습니다.\n" + e.Message, "SideTodo"); editing = previous; return false; }
    }
    void AnimateSize(double width, double height, int ms = 260, Action? finished = null)
    {
        targetWidth = width; targetHeight = height;
        double fromWidth = Width, fromHeight = Height;
        SetValue(WidthProperty, width); SetValue(HeightProperty, height);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? ms : 1);
        BeginAnimation(WidthProperty, new DoubleAnimation(fromWidth, width, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop }, HandoffBehavior.SnapshotAndReplace);
        var h = new DoubleAnimation(fromHeight, height, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        if (finished != null) h.Completed += (_, _) => finished();
        BeginAnimation(HeightProperty, h, HandoffBehavior.SnapshotAndReplace);
    }
    FormattedText MeasureText(string text, double size, double? width = null)
    {
        var measure = new FormattedText(string.IsNullOrEmpty(text) ? "가" : text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Look.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (width.HasValue) measure.MaxTextWidth = Math.Max(24, width.Value);
        return measure;
    }
    IEnumerable<TextBox> RowTitles() => rows.Children.OfType<Grid>().Select(row => row.Children.OfType<TextBox>().First());
    double PanelWidth()
    {
        double needed = Math.Max(typing ? 302 : 246, MeasureText(quick.Text, typing ? 14 : 12).WidthIncludingTrailingWhitespace + (typing ? 100 : 72));
        foreach (var title in RowTitles()) needed = Math.Max(needed, MeasureText(title.Text, title == activeTitle && typing ? 14 : 12).WidthIncludingTrailingWhitespace + 86);
        return Math.Min(Math.Ceiling(needed), Math.Min(560, SystemParameters.WorkArea.Width - 24));
    }
    double PanelHeight(double width)
    {
        double list = RowTitles().Sum(title => Math.Max(29, MeasureText(title.Text, title == activeTitle && typing ? 14 : 12, width - 86).Height + 10));
        double input = Math.Max(typing ? 52 : 29, MeasureText(quick.Text, typing ? 14 : 12, width - (typing ? 100 : 72)).Height + (typing ? 26 : 10));
        return Math.Min(Math.Ceiling(63 + list + input), Math.Min(520, SystemParameters.WorkArea.Height - 24));
    }
    void ResizePanel()
    {
        if (!expanded) return;
        double width = PanelWidth(), height = PanelHeight(width);
        double bottom = SystemParameters.WorkArea.Bottom - 8;
        if (Top + height > bottom) Top = Math.Max(SystemParameters.WorkArea.Top + 8, bottom - height);
        if (Math.Abs(targetWidth - width) > .5 || Math.Abs(targetHeight - height) > .5) AnimateSize(width, height);
    }
    void SetTyping(bool value)
    {
        typing = value;
        bool composingNew = value && activeTitle == null;
        var duration = TimeSpan.FromMilliseconds(180);
        ((SolidColorBrush)composeFrame.Background).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(composingNew ? Color.FromArgb(16, 255, 255, 255) : Colors.Transparent, duration));
        ((SolidColorBrush)composeFrame.BorderBrush).BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(composingNew ? Color.FromArgb(95, 233, 215, 200) : Colors.Transparent, duration));
        composeFrame.Padding = composingNew ? new Thickness(8, 9, 6, 9) : new Thickness(6, 0, 4, 0);
        quick.FontSize = composingNew ? 14 : 12; placeholder.FontSize = quick.FontSize;
        submit.Visibility = composingNew ? Visibility.Visible : Visibility.Collapsed;
        foreach (var title in RowTitles()) { title.FontSize = value && title == activeTitle ? 14 : 12; title.Background = value && title == activeTitle ? Look.Brush("#10FFFFFF") : Brushes.Transparent; }
    }
    void Grow() { if (!expanded || editing) return; SetTyping(true); settle.Stop(); ResizePanel(); }
    void ScheduleSettle() { settle.Stop(); settle.Start(); }
    void Collapse()
    {
        if (editing || !expanded || closing) return;
        hide.Stop(); settle.Stop(); expanded = false; SetTyping(false); suppressProximityUntil = DateTime.UtcNow.AddMilliseconds(650);
        drafts[later] = quick.Text; Keyboard.ClearFocus();
        int version = ++transition; card.IsHitTestVisible = false;
        Look.Fade(card, 0, 140); Look.Fade(marker, 1, 230);
        AnimateSize(12, 44, 260, () => { if (transition == version && !expanded) card.Visibility = Visibility.Hidden; });
    }
    public void Expand(bool activate)
    {
        hide.Stop();
        if (!expanded)
        {
            ++transition; expanded = true; SetTyping(false);
            card.Visibility = Visibility.Visible; card.IsHitTestVisible = true;
            RenderRows(); Look.Fade(marker, 0, 100); Look.Fade(card, 1, 240); ResizePanel();
        }
        if (activate) { Activate(); quick.Focus(); ScheduleSettle(); }
    }
    void SwitchTab(bool future)
    {
        if (later == future) return;
        drafts[later] = quick.Text; later = future;
        switching = true; quick.Text = drafts[later]; switching = false;
        SetTyping(false); RenderRows(); rows.Opacity = .35; Look.Fade(rows, 1, 170); ResizePanel();
    }
    bool CommitQuick()
    {
        var title = quick.Text.Trim();
        if (title.Length == 0) { ScheduleSettle(); return false; }
        var task = new Todo { Title = title, Due = later ? null : DateTime.Today };
        store.Data.Tasks.Add(task);
        if (!Save()) { store.Data.Tasks.Remove(task); return false; }
        switching = true; quick.Clear(); switching = false; drafts[later] = "";
        RenderRows(); ResizePanel(); quick.Focus(); return true;
    }
    void RenderRows()
    {
        ++listVersion;
        rows.Children.Clear();
        todayTab.Foreground = later ? Look.Muted : Look.Text; laterTab.Foreground = later ? Look.Text : Look.Muted;
        todayTab.Background = later ? Brushes.Transparent : Look.Brush("#18FFFFFF"); laterTab.Background = later ? Look.Brush("#18FFFFFF") : Brushes.Transparent;
        var visible = store.Data.Tasks.Where(t => Store.IsToday(t, DateTime.Today) != later && (showDone || !t.Done)).OrderBy(t => t.Done).ThenBy(t => t.Created);
        foreach (var task in visible) AddRow(task);
    }
    void AddRow(Todo task)
    {
        int version = listVersion;
        var row = new Grid { MinHeight = 29, Margin = new Thickness(7, 0, 1, 0), ClipToBounds = true, Tag = task };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(23) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var check = Look.Check(task.Done); check.HorizontalAlignment = HorizontalAlignment.Left; System.Windows.Automation.AutomationProperties.SetName(check, task.Title + " 완료"); row.Children.Add(check);
        var title = InlineInput(task.Title); title.MaxLength = 300; title.Foreground = task.Done ? Look.Muted : Look.Text; Grid.SetColumn(title, 1); row.Children.Add(title);
        var detail = Look.Button("···", () => Edit(task)); detail.Padding = new Thickness(4, 0, 4, 0); detail.Opacity = 0; detail.ToolTip = "자세히"; Grid.SetColumn(detail, 2); row.Children.Add(detail);
        row.MouseEnter += (_, _) => { Look.Fade(detail, .7); QueueDetailHover(task, row); }; row.MouseLeave += (_, _) => { Look.Fade(detail, 0); if (hoverAnchor == row) detailHover.Stop(); }; detail.GotKeyboardFocus += (_, _) => Look.Fade(detail, 1);
        row.PreviewMouseDown += (_, _) => detailHover.Stop();
        bool rowComposing = false;
        TextCompositionManager.AddPreviewTextInputStartHandler(title, (_, _) => rowComposing = true);
        TextCompositionManager.AddPreviewTextInputHandler(title, (_, _) => rowComposing = false);
        bool CommitTitle()
        {
            string text = title.Text.Trim(); if (text.Length == 0) { title.Text = task.Title; return true; }
            if (text == task.Title) return true;
            string old = task.Title; task.Title = text; if (Save()) return true; task.Title = old; return false;
        }
        title.GotKeyboardFocus += (_, _) => { activeTitle = title; Grow(); }; title.TextChanged += (_, _) => { if (title.IsKeyboardFocused) Grow(); };
        title.LostKeyboardFocus += (_, _) => { CommitTitle(); if (activeTitle == title) activeTitle = null; ScheduleSettle(); };
        title.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && !rowComposing) { e.Handled = true; if (CommitTitle()) { quick.Focus(); ScheduleSettle(); scroll.ScrollToEnd(); } } };
        check.Click += (_, _) =>
        {
            bool old = task.Done; var oldCompleted = task.CompletedAt; CompletionArchive.SetDone(task, check.IsChecked == true);
            if (!Save()) { task.Done = old; task.CompletedAt = oldCompleted; check.IsChecked = old; return; }
            archiveWindow?.Refresh();
            if (!task.Done || showDone) { title.Foreground = task.Done ? Look.Muted : Look.Text; return; }
            row.IsHitTestVisible = false;
            row.MinHeight = 0;
            Look.Fade(row, 0, 160);
            var anim = new DoubleAnimation(row.ActualHeight, 0, TimeSpan.FromMilliseconds(210)) { BeginTime = TimeSpan.FromMilliseconds(70), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            anim.Completed += (_, _) => { if (version != listVersion) return; rows.Children.Remove(row); ResizePanel(); };
            row.BeginAnimation(FrameworkElement.HeightProperty, anim);
        };
        rows.Children.Add(row);
    }
}
