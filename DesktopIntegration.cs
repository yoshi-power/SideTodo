using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SideTodo;

internal static class DesktopIntegration
{
    const int ExtendedStyle = -20;
    const long ToolWindow = 0x80, AppWindow = 0x40000;
    [StructLayout(LayoutKind.Sequential)] struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetStyle64(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetStyle32(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetStyle64(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] static extern int SetStyle32(IntPtr window, int index, int value);
    static long GetStyle(IntPtr window) => IntPtr.Size == 8 ? GetStyle64(window, ExtendedStyle).ToInt64() : GetStyle32(window, ExtendedStyle);
    internal static void HideFromSwitcher(Window window)
    {
        window.ShowInTaskbar = false;
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            long style = (GetStyle(handle) | ToolWindow) & ~AppWindow;
            if (IntPtr.Size == 8) SetStyle64(handle, ExtendedStyle, new IntPtr(style)); else SetStyle32(handle, ExtendedStyle, (int)style);
        };
    }
    internal static bool IsHiddenFromSwitcher(Window window)
    {
        long style = GetStyle(new WindowInteropHelper(window).Handle);
        return (style & ToolWindow) != 0 && (style & AppWindow) == 0 && !window.ShowInTaskbar;
    }
    internal static Point? CursorIn(Window window)
    {
        if (!window.IsVisible || PresentationSource.FromVisual(window) == null || !GetCursorPos(out var p)) return null;
        return window.PointFromScreen(new Point(p.X, p.Y));
    }
    internal static bool NearMarker(Point p) => p.X >= -4 && p.X <= 40 && p.Y >= -28 && p.Y <= 72;
}
