using System.Windows;
using System.Windows.Media;
using Tunelay.Native;

namespace Tunelay.App.Views;

/// <summary>Keeps windows on a real monitor (any monitor, not just the primary one).</summary>
public static class ScreenPlacement
{
    /// <summary>Work area, in DIPs, of the monitor nearest to the given DIP point.</summary>
    public static Rect WorkAreaNear(Visual visual, double left, double top)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var (l, t, r, b) = Monitors.WorkAreaNear((int)(left * dpi.DpiScaleX), (int)(top * dpi.DpiScaleY));
        return new Rect(l / dpi.DpiScaleX, t / dpi.DpiScaleY, (r - l) / dpi.DpiScaleX, (b - t) / dpi.DpiScaleY);
    }

    /// <summary>Moves the window fully inside the work area of the monitor it is mostly on.</summary>
    public static void Clamp(Window w)
    {
        var width = w.ActualWidth;
        var height = w.ActualHeight;
        var area = WorkAreaNear(w, w.Left + width / 2, w.Top + height / 2);
        w.Left = Math.Clamp(w.Left, area.Left, Math.Max(area.Left, area.Right - width));
        w.Top = Math.Clamp(w.Top, area.Top, Math.Max(area.Top, area.Bottom - height));
    }
}
