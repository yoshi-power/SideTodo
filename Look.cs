using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Input;

namespace SideTodo;

public static class Look
{
    public static readonly Brush Text = Brush("#F1F1F1"), Muted = Brush("#969696"), Accent = Brush("#E9D7C8"), Surface = Brush("#292929");
    public static SolidColorBrush Brush(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    public static TextBlock Label(string text, double size = 12, Brush? color = null) => new() { Text = text, FontSize = size, Foreground = color ?? Text, VerticalAlignment = VerticalAlignment.Center };

    // Always replace an animation from its current displayed value; reversing never snaps.
    public static void Animate(Animatable target, DependencyProperty property, double to, int ms = 220, Action? finished = null)
    {
        double from = (double)target.GetValue(property);
        target.SetValue(property, to);
        var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? ms : 1))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        if (finished != null) animation.Completed += (_, _) => finished();
        target.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }
    public static void Fade(UIElement target, double to, int ms = 160, Action? finished = null)
    {
        double from = target.Opacity;
        target.SetValue(UIElement.OpacityProperty, to);
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? ms : 1)) { FillBehavior = FillBehavior.Stop };
        if (finished != null) a.Completed += (_, _) => finished();
        target.BeginAnimation(UIElement.OpacityProperty, a, HandoffBehavior.SnapshotAndReplace);
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Foreground = primary ? Brush("#171717") : Text, Background = primary ? Text : Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8, 5, 8, 5), Cursor = Cursors.Hand, FontSize = 11, FocusVisualStyle = null };
        var border = new FrameworkElementFactory(typeof(Border)); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        var content = new FrameworkElementFactory(typeof(ContentPresenter)); content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        button.MouseEnter += (_, _) => Fade(button, .66); button.MouseLeave += (_, _) => Fade(button, 1);
        button.Click += (_, _) => action(); return button;
    }
    public static CheckBox Check(bool value)
    {
        var check = new CheckBox { IsChecked = value, Width = 14, Height = 14, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, FocusVisualStyle = null };
        var square = new FrameworkElementFactory(typeof(Border), "square");
        square.SetValue(Border.BorderBrushProperty, Brush("#858585")); square.SetValue(Border.BorderThicknessProperty, new Thickness(1)); square.SetValue(Border.CornerRadiusProperty, new CornerRadius(3)); square.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var tick = new FrameworkElementFactory(typeof(TextBlock), "tick"); tick.SetValue(TextBlock.TextProperty, "✓"); tick.SetValue(TextBlock.FontSizeProperty, 10.0); tick.SetValue(TextBlock.ForegroundProperty, Brush("#141414")); tick.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); tick.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); tick.SetValue(UIElement.OpacityProperty, 0.0); square.AppendChild(tick);
        var template = new ControlTemplate(typeof(CheckBox)) { VisualTree = square };
        var active = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; active.Setters.Add(new Setter(Border.BackgroundProperty, Text, "square")); active.Setters.Add(new Setter(UIElement.OpacityProperty, 1.0, "tick")); template.Triggers.Add(active);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "square")); template.Triggers.Add(focus);
        check.Template = template; return check;
    }
    public static Border Card(UIElement child, string color = "#E61B1B1B", double radius = 14) => new() { Background = Brush(color), CornerRadius = new CornerRadius(radius), BorderBrush = Brush("#35FFFFFF"), BorderThickness = new Thickness(1), Child = child };
    public static TextBox Input(string value, bool multiline = false)
    {
        var input = new TextBox { Text = value, Background = Surface, Foreground = Text, CaretBrush = Accent, SelectionBrush = Brush("#776C62"), BorderBrush = Brush("#444444"), BorderThickness = new Thickness(1), Padding = new Thickness(9), FontSize = 12, AcceptsReturn = multiline, TextWrapping = TextWrapping.Wrap, Height = multiline ? 78 : 36, VerticalContentAlignment = VerticalAlignment.Center, FocusVisualStyle = null };
        var border = new FrameworkElementFactory(typeof(Border), "field"); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty)); border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
        // TextBoxView already consumes TextBox.Padding. Applying it again to the
        // host leaves the single-line editor with no drawable/clickable height.
        var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost"); host.SetValue(UIElement.FocusableProperty, false); border.AppendChild(host);
        var template = new ControlTemplate(typeof(TextBox)) { VisualTree = border };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "field")); template.Triggers.Add(focus); input.Template = template;
        return input;
    }
    public static RadioButton Segment(string text, string group)
    {
        var button = new RadioButton { Content = text, GroupName = group, Foreground = Muted, Background = Brushes.Transparent, Padding = new Thickness(12, 7, 12, 7), FontSize = 11, Cursor = Cursors.Hand, FocusVisualStyle = null, HorizontalContentAlignment = HorizontalAlignment.Center };
        var border = new FrameworkElementFactory(typeof(Border), "segment"); border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7)); border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty)); border.SetValue(Border.BorderThicknessProperty, new Thickness(1)); border.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        var content = new FrameworkElementFactory(typeof(TextBlock), "caption"); content.SetValue(TextBlock.TextProperty, new TemplateBindingExtension(ContentControl.ContentProperty)); content.SetValue(TextBlock.ForegroundProperty, Muted); content.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty)); content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center); content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center); border.AppendChild(content);
        var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
        var selected = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, Text, "segment")); selected.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brush("#181818"), "caption")); template.Triggers.Add(selected);
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true }; focus.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "segment")); template.Triggers.Add(focus);
        button.Template = template; button.MouseEnter += (_, _) => Fade(button, .8); button.MouseLeave += (_, _) => Fade(button, 1);
        return button;
    }
}
