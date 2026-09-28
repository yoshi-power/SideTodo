using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SideTodo;

public sealed partial class Widget
{
    internal async Task RunSmokeTest()
    {
        var log = new System.Collections.Generic.List<string>();
        void Assert(bool condition, string label) { if (!condition) throw new Exception(label); log.Add("PASS " + label); }
        void Enter(TextBox input) => input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input), Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        async Task WaitUntil(Func<bool> ready) { for (int i = 0; i < 80 && !ready(); i++) await Task.Delay(50); }
        async Task WaitForDetailClose()
        {
            for (int i = 0; i < 100 && detailWindow != null; i++) await Task.Delay(50);
            if (detailWindow != null) throw new Exception("Detail editor did not close");
        }
        try
        {
            // Keep physical mouse position from interfering with deterministic state tests.
            proximity.Stop();
            detailHover.Stop(); suppressDetailUntil = DateTime.MaxValue;
            await Task.Delay(350);
            Assert(DesktopIntegration.IsHiddenFromSwitcher(this), "Widget is a tool window excluded from Alt+Tab and taskbar");
            Assert(DesktopIntegration.NearMarker(new Point(35, -20)) && DesktopIntegration.NearMarker(new Point(39, 70)) && !DesktopIntegration.NearMarker(new Point(100, 0)), "Proximity opens well outside visible bar without a large input-blocking window");
            quick.Focus(); quick.Text = "첫 번째 한글 항목"; Enter(quick);
            await Task.Delay(50);
            Assert(store.Data.Tasks.Count == 1 && quick.Text == "" && quick.IsKeyboardFocused, "Enter saves and keeps focus on next blank checkbox");
            quick.Text = "두 번째 항목"; Enter(quick);
            Assert(store.Data.Tasks.Count == 2, "Consecutive Enter creates independent tasks");
            Enter(quick); Assert(store.Data.Tasks.Count == 2, "Empty Enter creates no blank task");
            await Task.Delay(1750);
            await WaitUntil(() => Math.Abs(Width - 246) < 1);
            Assert(Math.Abs(Width - 246) < 1, $"Input settles back to compact width after delay (width={Width:F1}, editing={editing})");
            quick.Text = "보관할 초안"; await Task.Delay(320);
            await WaitUntil(() => Math.Abs(Width - 302) < 1 && composeFrame.ActualHeight >= 48);
            Assert(Math.Abs(Width - 302) < 1 && quick.FontSize == 14 && composeFrame.ActualHeight >= 48 && submit.IsVisible, $"Typing provides a larger highlighted editor, readable text and an Enter button (width={Width:F1}, field={composeFrame.ActualHeight:F1})");
            quick.Text = string.Concat(Enumerable.Repeat("긴 제목을 끝까지 확인합니다 ", 12));
            await Task.Delay(450); UpdateLayout();
            await WaitUntil(() => Width > 400 && quick.LineCount > 1);
            Assert(Width > 400 && quick.LineCount > 1 && quick.ActualHeight > 35, "Long inline text grows the window and wraps into visible lines");
            quick.Text = "보관할 초안"; await Task.Delay(350);
            SwitchTab(true); Assert(quick.Text == "", "Each tab has an independent draft");
            quick.Text = "나중에 할 일"; Enter(quick);
            Assert(store.Data.Tasks.Last().Due == null, "Future inline task stays undated");
            SwitchTab(false); Assert(quick.Text == "보관할 초안", "Switching tabs restores draft");
            var firstRow = (Grid)rows.Children[0]; var box = (CheckBox)firstRow.Children[0];
            box.IsChecked = true; box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            await Task.Delay(400);
            Assert(store.Data.Tasks[0].Done && rows.Children.Count == 1, "Completed task fades and leaves the list");
            showDone = true; RenderRows(); Assert(rows.Children.Count == 2, "Completed task remains recoverable");
            Collapse(); await Task.Delay(50); Expand(false); await Task.Delay(320);
            await WaitUntil(() => Width >= 245.5 && card.Opacity > .99);
            Assert(expanded && card.Visibility == Visibility.Visible && card.Opacity > .99 && Width >= 245.5, $"Interrupted collapse reverses without hiding reopened panel (expanded={expanded}, visible={card.Visibility}, opacity={card.Opacity:F2}, width={Width:F1})");
            Collapse(); await Task.Delay(350);
            await WaitUntil(() => Math.Abs(Width - 12) < 1 && card.Visibility == Visibility.Hidden);
            Assert(Math.Abs(Width - 12) < 1 && Math.Abs(Height - 44) < 1 && card.Visibility == Visibility.Hidden, "Idle state is only a 4 by 28 marker in a 12 by 44 hit area");
            Expand(false); Assert(quick.Text == "보관할 초안", "Closing preserves unsaved inline draft");
            var reload = new Store(store.PathName); reload.Load();
            Assert(reload.Data.Tasks.Count == 3 && reload.Data.Tasks[0].Done, "Inline additions and completion persist to disk");
            var savedTitle = store.Data.Tasks[1].Title;
            store.Data.Tasks[1].Title = string.Concat(Enumerable.Repeat("저장한 긴 제목도 생략하지 않습니다 ", 10));
            RenderRows(); ResizePanel(); await Task.Delay(450); UpdateLayout();
            Assert(RowTitles().Any(t => t.LineCount > 1 && t.ActualHeight > 35), "Saved long titles expand their row and remain wrapped, not truncated");
            store.Data.Tasks[1].Title = savedTitle; RenderRows(); ResizePanel();
            Exception? detailFailure = null;
            DetailTestHook = async (dialog, title, notes, now, future, dated, save) =>
            {
                try
                {
                    await Task.Delay(300);
                    Assert(DesktopIntegration.IsHiddenFromSwitcher(dialog), "Detail editor is also excluded from Alt+Tab");
                    title.Focus();
                    title.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, title, "상세 일정 제목 입력")) { RoutedEvent = TextCompositionManager.TextInputEvent });
                    dialog.UpdateLayout();
                    var caret = title.GetRectFromCharacterIndex(0);
                    Assert(title.Text == "상세 일정 제목 입력" && caret.Height >= 12 && title.ActualHeight >= 40, "Detail title accepts routed text input and has a visible caret/text area");
                    var hit = title.InputHitTest(new Point(title.ActualWidth / 2, title.ActualHeight / 2));
                    Assert(hit != null, "Title center has a usable click target");
                    notes.Focus(); notes.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, notes, "메모 입력 확인")) { RoutedEvent = TextCompositionManager.TextInputEvent });
                    Assert(notes.Text == "메모 입력 확인", "Memo remains independently editable");
                    var editor = (DetailEditor)dialog;
                    double originalHeight = dialog.ActualHeight;
                    notes.Text = string.Join("\n", Enumerable.Repeat("길게 쓰는 메모도 전체 내용을 편하게 읽고 수정합니다.", 22));
                    await Task.Delay(250); dialog.UpdateLayout();
                    Assert(notes.ActualHeight > 300 && dialog.ActualHeight > originalHeight && notes.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, "Long memo grows its editor and window without an inner scrollbar");
                    notes.Text = string.Join("\n", Enumerable.Repeat("화면 높이를 넘는 긴 메모", 180));
                    await Task.Delay(150); dialog.UpdateLayout();
                    var savePosition = save.TranslatePoint(new Point(), dialog);
                    Assert(dialog.ActualHeight <= SystemParameters.WorkArea.Height - 20 && savePosition.Y + save.ActualHeight <= dialog.ActualHeight, "Screen-height memo keeps the save button reachable");
                    notes.Text = "메모 입력 확인"; await Task.Delay(150);
                    double movedLeft = Math.Min(dialog.Left + 40, SystemParameters.WorkArea.Right - dialog.ActualWidth - 10); dialog.Left = movedLeft;
                    notes.AppendText(" "); await Task.Delay(100);
                    Assert(Math.Abs(dialog.Left - movedLeft) < 1 && editor.DragHandle.Cursor == Cursors.SizeAll, "Moved detail editor stays in place while editing and exposes a drag handle");
                    future.IsChecked = true; Assert(now.IsChecked == false && future.IsChecked == true, "Segmented Today/Future selection is exclusive");
                    future.ApplyTemplate();
                    var selectedSurface = (Border)future.Template.FindName("segment", future);
                    Assert(((SolidColorBrush)selectedSurface.Background).Color == ((SolidColorBrush)Look.Text).Color, "Selected schedule segment has a visible white fill");
                    dated.IsChecked = true; Assert(future.IsChecked == false && dated.IsChecked == true, "Date segment selects independently");
                    Assert(editor.Calendar.IsVisible, "Date segment reveals the themed inline calendar");
                    editor.Calendar.Select(new DateTime(2028, 2, 29)); editor.Calendar.ChangeMonth(1); editor.Calendar.ChangeMonth(-1);
                    Assert(editor.Calendar.SelectedDate == new DateTime(2028, 2, 29), "Calendar retains leap-day selection while browsing months");
                    save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
                catch (Exception e) { detailFailure = e; dialog.Close(); }
            };
            Edit(null);
            await WaitForDetailClose();
            DetailTestHook = null;
            if (detailFailure != null) throw detailFailure;
            Assert(store.Data.Tasks.Last().Title == "상세 일정 제목 입력" && store.Data.Tasks.Last().Notes == "메모 입력 확인" && store.Data.Tasks.Last().Due == new DateTime(2028, 2, 29), "Detail title, notes and calendar date save together");
            Expand(false); UpdateLayout();
            var hoverItem = store.Data.Tasks[1];
            Edit(hoverItem, (Grid)rows.Children[0]); await Task.Delay(100);
            var hoverEditor = detailWindow!;
            Assert(!hoverEditor.ShowActivated && !hoverEditor.IsPinned && IsEnabled && !hoverEditor.IsDirty, "Hover detail opens beside the task without taking focus or disabling the list");
            Assert(hoverEditor.Left >= Left + ActualWidth - 45, "Hover editor is positioned beside the source row");
            hoverEditor.NotesInput.Text = "호버 창에서 수정한 메모";
            await Task.Delay(800);
            Assert(detailWindow == hoverEditor && hoverEditor.IsPinned && hoverEditor.ShouldStayOpen(), "Editing a hover card pins it so mouse leave cannot discard changes");
            hoverEditor.Commit(); await WaitForDetailClose();
            Assert(store.Data.Tasks.Single(t => t.Id == hoverItem.Id).Notes == "호버 창에서 수정한 메모", "Hover and explicit editors update the same saved task");
            var archived = store.Data.Tasks[0];
            Assert(archived.Done && archived.CompletedAt.HasValue, "Checkbox records completion time for archive ordering");
            var originalDue = archived.Due; var originalNotes = archived.Notes;
            OpenArchive(); await Task.Delay(150);
            Assert(archiveWindow?.IsVisible == true && DesktopIntegration.IsHiddenFromSwitcher(archiveWindow), "Completed archive opens without an Alt+Tab entry");
            Assert(RestoreArchived(archived.Id) && !archived.Done && archived.CompletedAt == null && archived.Due == originalDue && archived.Notes == originalNotes, "Archive restores the original task and preserves schedule and notes");
            archiveWindow!.Refresh(); archiveWindow.Close();
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "ui-smoke-result.txt"), log);
            Application.Current.Shutdown(0);
        }
        catch (Exception e)
        {
            log.Add("FAIL " + e); File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "ui-smoke-result.txt"), log);
            Application.Current.Shutdown(1);
        }
    }
}
