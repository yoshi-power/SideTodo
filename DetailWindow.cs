using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SideTodo;

public sealed partial class Widget
{
    internal Action<Window, TextBox, TextBox, RadioButton, RadioButton, RadioButton, Button>? DetailTestHook;
    DetailEditor? detailWindow;
    readonly DispatcherTimer detailHover = new() { Interval = TimeSpan.FromMilliseconds(480) };
    Todo? hoverTask;
    FrameworkElement? hoverAnchor;
    DateTime suppressDetailUntil;
    void PrepareDetailHover()
    {
        detailHover.Tick += (_, _) => { detailHover.Stop(); if (hoverTask != null && hoverAnchor?.IsMouseOver == true && !hoverAnchor.IsKeyboardFocusWithin && Mouse.LeftButton == MouseButtonState.Released && DateTime.UtcNow >= suppressDetailUntil) Edit(hoverTask, hoverAnchor); };
    }
    void QueueDetailHover(Todo task, FrameworkElement row)
    {
        if (DateTime.UtcNow < suppressDetailUntil || editing || row.IsKeyboardFocusWithin) return;
        hoverTask = task; hoverAnchor = row; detailHover.Stop(); detailHover.Start();
    }
    void Edit(Todo? task, FrameworkElement? anchor = null)
    {
        detailHover.Stop();
        if (detailWindow != null) { if (anchor == null) detailWindow.PinAndActivate(); return; }
        hide.Stop(); settle.Stop(); editing = true; if (anchor == null) Keyboard.ClearFocus();
        var current = task == null ? null : store.Data.Tasks.FirstOrDefault(t => t.Id == task.Id);
        if (task != null && current == null) { editing = false; return; }
        var editor = new DetailEditor(this, current, anchor, diagnostic, value =>
        {
            int index = store.Data.Tasks.FindIndex(t => t.Id == value.Id); Todo? old = index >= 0 ? store.Data.Tasks[index] : null;
            if (index < 0) store.Data.Tasks.Add(value); else store.Data.Tasks[index] = value;
            if (Save()) { archiveWindow?.Refresh(); return true; }
            if (index < 0) store.Data.Tasks.Remove(value); else store.Data.Tasks[index] = old!; return false;
        }, id =>
        {
            int index = store.Data.Tasks.FindIndex(t => t.Id == id); if (index < 0) return true;
            var old = store.Data.Tasks[index]; store.Data.Tasks.RemoveAt(index); if (Save()) { archiveWindow?.Refresh(); return true; } store.Data.Tasks.Insert(index, old); return false;
        }, later);
        detailWindow = editor;
        editor.Closed += (_, _) =>
        {
            detailWindow = null; editing = false; suppressDetailUntil = DateTime.UtcNow.AddMilliseconds(900);
            if (closing) return;
            if (!expanded) Expand(false); else { RenderRows(); ResizePanel(); }
            if (!IsMouseOver) hide.Start();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var next = rows.Children.OfType<FrameworkElement>().FirstOrDefault(r => r.IsMouseOver);
                if (next?.Tag is Todo nextTask && nextTask.Id != current?.Id) { suppressDetailUntil = DateTime.MinValue; QueueDetailHover(nextTask, next); }
            }), DispatcherPriority.Background);
        };
        editor.Loaded += (_, _) => DetailTestHook?.Invoke(editor, editor.TitleInput, editor.NotesInput, editor.TodayChoice, editor.FutureChoice, editor.DateChoice, editor.SaveButton);
        editor.Show();
    }
}

public sealed class DetailEditor : Window
{
    readonly Todo? task;
    readonly FrameworkElement? anchor;
    readonly Func<Todo, bool> save;
    readonly Func<Guid, bool> delete;
    readonly DispatcherTimer leave = new() { Interval = TimeSpan.FromMilliseconds(650) };
    readonly TextBlock error;
    readonly ScrollViewer viewport;
    bool initialized, dismissing, finished, pinned, moving, resizing;
    public bool IsDirty { get; private set; }
    public bool IsPinned => pinned;
    public TextBox TitleInput { get; }
    public TextBox NotesInput { get; }
    public RadioButton TodayChoice { get; }
    public RadioButton FutureChoice { get; }
    public RadioButton DateChoice { get; }
    public Button SaveButton { get; }
    public MiniCalendar Calendar { get; }
    internal FrameworkElement DragHandle { get; }

    public DetailEditor(Window owner, Todo? task, FrameworkElement? anchor, bool diagnostic, Func<Todo, bool> save, Func<Guid, bool> delete, bool future)
    {
        this.task = task; this.anchor = anchor; this.save = save; this.delete = delete; pinned = anchor == null;
        Title = task == null ? "새 일정 · SideTodo" : "일정 · SideTodo"; Width = 348; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height - 24;
        WindowStartupLocation = anchor == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.Manual;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = diagnostic; ShowActivated = anchor == null; Owner = owner; FontFamily = owner.FontFamily; UseLayoutRounding = true; Opacity = 0;
        if (!diagnostic) DesktopIntegration.HideFromSwitcher(this);
        var layout = new Grid { Margin = new Thickness(18) }; layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new Grid { Margin = new Thickness(0, 0, 0, 12), Height = 30 }; header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var drag = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeAll, ToolTip = "드래그해서 이동" };
        var heading = new DockPanel(); var grip = Look.Label("⠿", 15, Look.Muted); grip.Margin = new Thickness(0, 0, 8, 0); heading.Children.Add(grip); heading.Children.Add(Look.Label(task == null ? "새 일정" : "일정", 13)); drag.Child = heading; DragHandle = drag;
        drag.MouseLeftButtonDown += (_, e) => { if (e.LeftButton != MouseButtonState.Pressed) return; pinned = true; leave.Stop(); moving = true; try { DragMove(); } finally { moving = false; } e.Handled = true; };
        header.Children.Add(drag); var close = Look.Button("×", Dismiss); close.FontSize = 17; close.ToolTip = "닫기 · Esc"; Grid.SetColumn(close, 1); header.Children.Add(close); layout.Children.Add(header);
        var body = new StackPanel();
        TitleInput = Look.Input(task?.Title ?? ""); TitleInput.Height = double.NaN; TitleInput.MinHeight = 42; TitleInput.FontSize = 14; TitleInput.MaxLength = 300; System.Windows.Automation.AutomationProperties.SetName(TitleInput, "일정 제목"); body.Children.Add(TitleInput);
        var label = Look.Label("메모", 11, Look.Muted); label.Margin = new Thickness(0, 14, 0, 7); body.Children.Add(label);
        NotesInput = Look.Input(task?.Notes ?? "", true); NotesInput.Height = double.NaN; NotesInput.MinHeight = 86; NotesInput.MaxLength = 10000; NotesInput.VerticalContentAlignment = VerticalAlignment.Top; NotesInput.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled; NotesInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; System.Windows.Automation.AutomationProperties.SetName(NotesInput, "일정 메모"); body.Children.Add(NotesInput);
        var choices = new Grid(); for (int i = 0; i < 3; i++) choices.ColumnDefinitions.Add(new ColumnDefinition());
        TodayChoice = Look.Segment("오늘", "due"); FutureChoice = Look.Segment("앞으로", "due"); DateChoice = Look.Segment("날짜 지정", "due"); TodayChoice.Margin = FutureChoice.Margin = DateChoice.Margin = new Thickness(1);
        Grid.SetColumn(FutureChoice, 1); Grid.SetColumn(DateChoice, 2); choices.Children.Add(TodayChoice); choices.Children.Add(FutureChoice); choices.Children.Add(DateChoice);
        body.Children.Add(new Border { Child = choices, Background = Look.Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Margin = new Thickness(0, 15, 0, 0) });
        Calendar = new MiniCalendar(task?.Due ?? DateTime.Today.AddDays(1)); body.Children.Add(Calendar);
        void UpdateDate() { Calendar.Visibility = DateChoice.IsChecked == true ? Visibility.Visible : Visibility.Collapsed; Changed(); }
        TodayChoice.Checked += (_, _) => UpdateDate(); FutureChoice.Checked += (_, _) => UpdateDate(); DateChoice.Checked += (_, _) => UpdateDate(); Calendar.SelectionChanged += Changed;
        if (task != null) { if (task.Due == null) FutureChoice.IsChecked = true; else if (task.Due.Value.Date == DateTime.Today) TodayChoice.IsChecked = true; else DateChoice.IsChecked = true; } else if (future) FutureChoice.IsChecked = true; else TodayChoice.IsChecked = true;
        viewport = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly }; Grid.SetRow(viewport, 1); layout.Children.Add(viewport);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; error = Look.Label("", 11, Look.Accent); error.Visibility = Visibility.Collapsed; error.Margin = new Thickness(0, 0, 0, 8); footer.Children.Add(error);
        var actions = new DockPanel { LastChildFill = false }; SaveButton = Look.Button("완료", Commit, true); SaveButton.Padding = new Thickness(18, 7, 18, 7); DockPanel.SetDock(SaveButton, Dock.Right); actions.Children.Add(SaveButton);
        if (task != null) { var remove = Look.Button("삭제", () => { pinned = true; if (MessageBox.Show(this, "삭제할까요?", "SideTodo", MessageBoxButton.YesNo) == MessageBoxResult.Yes && delete(task.Id)) Dismiss(); }); remove.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(remove); }
        footer.Children.Add(actions); Grid.SetRow(footer, 2); layout.Children.Add(footer);
        var content = Look.Card(layout, "#F01B1B1B", 16); Content = content; var scale = new ScaleTransform(.97, .97); content.RenderTransform = scale; content.RenderTransformOrigin = new Point(.5, anchor == null ? .5 : 0);
        TitleInput.TextChanged += (_, _) => Changed(); NotesInput.TextChanged += (_, _) => Changed();
        NotesInput.SelectionChanged += (_, _) => { if (IsLoaded && NotesInput.IsKeyboardFocused) Dispatcher.BeginInvoke(new Action(KeepCaretVisible), DispatcherPriority.Background); };
        PreviewMouseDown += (_, _) => { pinned = true; leave.Stop(); };
        PreviewKeyDown += (_, e) => { pinned = true; if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; } else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { Commit(); e.Handled = true; } };
        MouseEnter += (_, _) => leave.Stop(); MouseLeave += (_, _) => { if (!pinned) leave.Start(); }; leave.Tick += (_, _) => { if (!ShouldStayOpen()) Dismiss(); };
        Closing += (_, e) => { if (!finished) { e.Cancel = true; Dismiss(); } }; Closed += (_, _) => leave.Stop();
        Loaded += (_, _) => { initialized = true; ResizeForContent(); if (anchor != null) { PlaceBesideAnchor(); leave.Start(); } Look.Fade(this, 1, 160); Look.Animate(scale, ScaleTransform.ScaleXProperty, 1, 190); Look.Animate(scale, ScaleTransform.ScaleYProperty, 1, 190); if (anchor == null) { TitleInput.Focus(); TitleInput.SelectAll(); } };
        SizeChanged += (_, _) => { if (initialized && !moving) ClampToWorkArea(); };
    }
    internal bool ShouldStayOpen() => pinned || IsDirty || moving || IsMouseOver || anchor?.IsMouseOver == true;
    public void PinAndActivate() { pinned = true; leave.Stop(); Activate(); }
    void Changed() { if (!initialized) return; IsDirty = true; pinned = true; leave.Stop(); ResizeForContent(); Dispatcher.BeginInvoke(new Action(KeepCaretVisible), DispatcherPriority.Background); }
    void ResizeForContent()
    {
        if (resizing) return; resizing = true;
        try
        {
            double Measure(string value, double size) => new FormattedText(string.IsNullOrEmpty(value) ? "가" : value, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, Look.Text, VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
            double noteWidth = NotesInput.Text.Split('\n').Select(line => Measure(line.TrimEnd('\r'), 12)).DefaultIfEmpty(0).Max();
            Width = Math.Min(Math.Max(348, Math.Max(Measure(TitleInput.Text, 14), noteWidth) + 62), Math.Min(620, SystemParameters.WorkArea.Width - 24));
            NotesInput.Height = double.NaN; TitleInput.Height = double.NaN;
        }
        finally { resizing = false; }
    }
    void KeepCaretVisible()
    {
        if (!IsLoaded || !NotesInput.IsKeyboardFocused || viewport.ScrollableHeight <= 0) return;
        var caret = NotesInput.GetRectFromCharacterIndex(NotesInput.CaretIndex); if (!caret.IsEmpty) NotesInput.BringIntoView(new Rect(caret.X, caret.Y, Math.Max(1, caret.Width), caret.Height + 12));
    }
    void PlaceBesideAnchor()
    {
        if (anchor == null) return;
        var pixels = anchor.PointToScreen(new Point(anchor.ActualWidth + 12, 0)); var source = PresentationSource.FromVisual(this); var position = source?.CompositionTarget?.TransformFromDevice.Transform(pixels) ?? pixels;
        Left = position.X; Top = position.Y; ClampToWorkArea();
    }
    internal void ClampToWorkArea()
    {
        var area = SystemParameters.WorkArea; Left = Math.Clamp(Left, area.Left + 8, Math.Max(area.Left + 8, area.Right - ActualWidth - 8)); Top = Math.Clamp(Top, area.Top + 8, Math.Max(area.Top + 8, area.Bottom - ActualHeight - 8));
    }
    void Error(string message) { error.Text = message; error.Visibility = Visibility.Visible; }
    public void Commit()
    {
        if (dismissing) return;
        if (string.IsNullOrWhiteSpace(TitleInput.Text)) { Error("할 일을 적어주세요."); TitleInput.Focus(); return; }
        var value = new Todo { Id = task?.Id ?? Guid.NewGuid(), Title = TitleInput.Text.Trim(), Notes = NotesInput.Text.Trim(), Due = TodayChoice.IsChecked == true ? DateTime.Today : DateChoice.IsChecked == true ? Calendar.SelectedDate : null, Done = task?.Done ?? false, CompletedAt = task?.CompletedAt, Created = task?.Created ?? DateTime.Now };
        if (save(value)) { IsDirty = false; Dismiss(); } else Error("저장하지 못했습니다.");
    }
    public void Dismiss() { if (dismissing) return; dismissing = true; leave.Stop(); Look.Fade(this, 0, 120, () => { finished = true; Close(); }); }
}
