using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SideTodo;

public sealed partial class Widget
{
    ArchiveWindow? archiveWindow;
    void OpenArchive()
    {
        if (archiveWindow != null) { archiveWindow.Activate(); return; }
        detailHover.Stop();
        archiveWindow = new ArchiveWindow(this, store, RestoreArchived, diagnostic);
        archiveWindow.Closed += (_, _) => archiveWindow = null;
        archiveWindow.Show();
    }
    bool RestoreArchived(Guid id)
    {
        var task = store.Data.Tasks.FirstOrDefault(t => t.Id == id);
        if (task == null || !task.Done) return false;
        // Keep a currently open detail editor from writing back an old Done state.
        if (detailWindow?.IsDirty == true) { detailWindow.PinAndActivate(); return false; }
        detailWindow?.Dismiss();
        var completed = task.CompletedAt; CompletionArchive.SetDone(task, false);
        if (!Save()) { task.Done = true; task.CompletedAt = completed; return false; }
        if (expanded) { RenderRows(); ResizePanel(); }
        return true;
    }
}

public sealed class ArchiveWindow : Window
{
    readonly Store store;
    readonly Func<Guid, bool> restore;
    readonly StackPanel rows = new();
    readonly TextBlock count, status;
    readonly Button sort;
    bool newestFirst = true;
    public ArchiveWindow(Window owner, Store store, Func<Guid, bool> restore, bool diagnostic)
    {
        this.store = store; this.restore = restore;
        Title = "완료 보관함 · SideTodo"; Width = 430; Height = Math.Min(540, SystemParameters.WorkArea.Height - 24);
        WindowStartupLocation = WindowStartupLocation.CenterScreen; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = diagnostic; Owner = owner; FontFamily = owner.FontFamily; UseLayoutRounding = true;
        if (!diagnostic) DesktopIntegration.HideFromSwitcher(this);
        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition()); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var close = Look.Button("×", Close); close.FontSize = 17; DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        var grip = Look.Label("⠿  완료 보관함", 14); grip.Cursor = Cursors.SizeAll; grip.MouseLeftButtonDown += (_, e) => { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }; header.Children.Add(grip); layout.Children.Add(header);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        sort = Look.Button("최근 완료순 ↓", () => { newestFirst = !newestFirst; Refresh(); }); sort.Foreground = Look.Muted; DockPanel.SetDock(sort, Dock.Right); toolbar.Children.Add(sort);
        count = Look.Label("", 11, Look.Muted); toolbar.Children.Add(count); Grid.SetRow(toolbar, 1); layout.Children.Add(toolbar);
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 2); layout.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; status = Look.Label("", 11, Look.Accent); status.TextWrapping = TextWrapping.Wrap; status.Margin = new Thickness(0, 0, 0, 8); footer.Children.Add(status);
        var export = Look.Button("내보내기", Export); export.Background = Look.Surface; export.HorizontalAlignment = HorizontalAlignment.Right; export.ToolTip = "Markdown / JSON"; footer.Children.Add(export); Grid.SetRow(footer, 3); layout.Children.Add(footer);
        Content = Look.Card(layout, "#F01B1B1B", 16);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Refresh();
    }
    public void Refresh()
    {
        var tasks = CompletionArchive.Items(store.Data.Tasks, newestFirst); rows.Children.Clear();
        count.Text = tasks.Count + "개"; sort.Content = newestFirst ? "최근 완료순 ↓" : "오래된 완료순 ↑";
        if (tasks.Count == 0) { var empty = Look.Label("완료한 일이 없습니다.", 12, Look.Muted); empty.Margin = new Thickness(4, 26, 4, 0); rows.Children.Add(empty); }
        foreach (var task in tasks)
        {
            var content = new StackPanel { Margin = new Thickness(12) };
            var top = new DockPanel(); var button = Look.Button("복구", () => { if (restore(task.Id)) { status.Text = "복구했습니다. 원래 날짜와 메모가 유지됩니다."; Refresh(); } else status.Text = "편집 중인 일정을 먼저 완료하거나 닫아주세요."; });
            button.Foreground = Look.Accent; DockPanel.SetDock(button, Dock.Right); top.Children.Add(button);
            var title = Look.Label(task.Title, 13); title.TextWrapping = TextWrapping.Wrap; title.Margin = new Thickness(0, 0, 8, 0); top.Children.Add(title); content.Children.Add(top);
            var date = Look.Label(task.CompletedAt?.ToLocalTime().ToString("yyyy.MM.dd  HH:mm:ss") ?? "완료 시간 미기록", 10, Look.Muted); date.Margin = new Thickness(0, 6, 0, 0); content.Children.Add(date);
            if (!string.IsNullOrWhiteSpace(task.Notes))
            {
                var note = new TextBox { Text = task.Notes, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Look.Text, FontSize = 11, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0) };
                var expander = new Expander { Header = "메모", Foreground = Look.Muted, FontSize = 11, Content = note, Margin = new Thickness(0, 8, 0, 0) }; content.Children.Add(expander);
            }
            var card = Look.Card(content, "#252525", 10); card.BorderThickness = new Thickness(0); card.Margin = new Thickness(0, 0, 0, 8); rows.Children.Add(card);
        }
    }
    void Export()
    {
        if (!store.Data.Tasks.Any(t => t.Done)) { status.Text = "내보낼 완료 항목이 없습니다."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = "완료 기록 내보내기", FileName = "SideTodo-completed-" + DateTime.Now.ToString("yyyyMMdd"), Filter = "Markdown (*.md)|*.md|JSON (*.json)|*.json", DefaultExt = ".md", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string data = Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase) ? CompletionArchive.Json(store.Data.Tasks) : CompletionArchive.Markdown(store.Data.Tasks);
            File.WriteAllText(dialog.FileName, data, new UTF8Encoding(false)); status.Text = "저장했습니다 · " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception e) { status.Text = "저장하지 못했습니다 · " + e.Message; }
    }
}
