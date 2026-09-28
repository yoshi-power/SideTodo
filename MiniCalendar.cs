using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SideTodo;

public sealed class MiniCalendar : Border
{
    readonly Grid days = new();
    readonly TextBlock monthLabel;
    DateTime month;
    public DateTime SelectedDate { get; private set; }
    public event Action? SelectionChanged;
    public MiniCalendar(DateTime selected)
    {
        SelectedDate = selected.Date; month = new DateTime(selected.Year, selected.Month, 1);
        Background = Look.Brush("#232323"); CornerRadius = new CornerRadius(11); Padding = new Thickness(10); Margin = new Thickness(0, 10, 0, 0);
        var body = new StackPanel(); var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var next = Look.Button("›", () => ChangeMonth(1)); next.FontSize = 19; next.ToolTip = "다음 달"; DockPanel.SetDock(next, Dock.Right); header.Children.Add(next);
        var previous = Look.Button("‹", () => ChangeMonth(-1)); previous.FontSize = 19; previous.ToolTip = "이전 달"; DockPanel.SetDock(previous, Dock.Left); header.Children.Add(previous);
        monthLabel = Look.Label("", 12); monthLabel.HorizontalAlignment = HorizontalAlignment.Center; header.Children.Add(monthLabel); body.Children.Add(header);
        for (int c = 0; c < 7; c++) days.ColumnDefinitions.Add(new ColumnDefinition());
        for (int r = 0; r < 7; r++) days.RowDefinitions.Add(new RowDefinition { Height = new GridLength(r == 0 ? 23 : 30) });
        body.Children.Add(days);
        var today = Look.Button("오늘로", () => Select(DateTime.Today)); today.Foreground = Look.Muted; today.HorizontalAlignment = HorizontalAlignment.Center; today.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(today);
        Child = body; Render();
        PreviewKeyDown += (_, e) => { int offset = e.Key switch { Key.Left => -1, Key.Right => 1, Key.Up => -7, Key.Down => 7, _ => 0 }; if (offset != 0) { Select(SelectedDate.AddDays(offset)); e.Handled = true; } };
    }
    public void ChangeMonth(int amount) { month = month.AddMonths(amount); Render(); }
    public void Select(DateTime date) { SelectedDate = date.Date; month = new DateTime(date.Year, date.Month, 1); Render(); SelectionChanged?.Invoke(); }
    void Render()
    {
        days.Children.Clear(); monthLabel.Text = month.ToString("yyyy년 M월", CultureInfo.GetCultureInfo("ko-KR"));
        string[] names = { "일", "월", "화", "수", "목", "금", "토" };
        for (int c = 0; c < 7; c++) { var label = Look.Label(names[c], 10, Look.Muted); label.HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(label, c); days.Children.Add(label); }
        var start = month.AddDays(-(int)month.DayOfWeek);
        for (int i = 0; i < 42; i++)
        {
            var date = start.AddDays(i); bool selected = date == SelectedDate;
            var day = Look.Button(date.Day.ToString(), () => Select(date), selected); day.Padding = new Thickness(0); day.Margin = new Thickness(2); day.FontSize = 11;
            if (!selected) { day.Foreground = date.Month == month.Month ? Look.Text : Look.Brush("#626262"); if (date == DateTime.Today) day.Background = Look.Brush("#484039"); }
            System.Windows.Automation.AutomationProperties.SetName(day, date.ToString("yyyy년 M월 d일") + (selected ? " 선택됨" : ""));
            Grid.SetColumn(day, i % 7); Grid.SetRow(day, i / 7 + 1); days.Children.Add(day);
        }
    }
}
