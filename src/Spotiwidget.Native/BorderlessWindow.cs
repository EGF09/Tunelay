namespace Spotiwidget.Native;

public enum BorderlessResult
{
    MadeBorderless,
    Restored,

    /// <summary>The window already has no frame (e.g. a game in its own F11/exclusive fullscreen).</summary>
    AlreadyFrameless,

    /// <summary>Desktop, taskbar or one of our own windows.</summary>
    NotAllowed,
}

/// <summary>
/// Turns a windowed app into borderless fullscreen on its monitor (and back), the way
/// "Borderless Gaming" tools do: only window styles and position change, nothing is injected.
/// Borderless windows are composed by Windows, so topmost overlays can draw over them,
/// unlike exclusive fullscreen.
/// </summary>
public sealed class BorderlessWindow
{
    private const long FrameStyles = Win32.WS_CAPTION | Win32.WS_THICKFRAME | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX | Win32.WS_MAXIMIZEBOX;
    private const long FrameExStyles = Win32.WS_EX_DLGMODALFRAME | Win32.WS_EX_CLIENTEDGE | Win32.WS_EX_STATICEDGE;

    private readonly Dictionary<nint, Saved> _saved = new();

    private sealed record Saved(nint Style, nint ExStyle, Win32.RECT Rect, bool WasMaximized);

    public BorderlessResult ToggleForeground() => Toggle(Win32.GetForegroundWindow());

    public BorderlessResult Toggle(nint hwnd)
    {
        Prune();
        if (_saved.Remove(hwnd, out var saved))
        {
            Restore(hwnd, saved);
            return BorderlessResult.Restored;
        }

        if (hwnd == 0 || hwnd == Win32.GetShellWindow() || IsOwnProcess(hwnd) || IsShell(hwnd))
        {
            return BorderlessResult.NotAllowed;
        }

        var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        if ((style & Win32.WS_CAPTION) == 0)
        {
            return BorderlessResult.AlreadyFrameless;
        }

        var wasMaximized = Win32.IsZoomed(hwnd);
        if (wasMaximized)
        {
            Win32.ShowWindow(hwnd, Win32.SW_RESTORE);
        }

        Win32.GetWindowRect(hwnd, out var rect);
        var ex = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        _saved[hwnd] = new Saved(style, ex, rect, wasMaximized);

        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), ref info);
        var m = info.rcMonitor;

        Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, (nint)(style & ~FrameStyles));
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, (nint)(ex & ~FrameExStyles));
        Win32.SetWindowPos(hwnd, 0, m.Left, m.Top, m.Right - m.Left, m.Bottom - m.Top,
            Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER | Win32.SWP_NOOWNERZORDER | Win32.SWP_NOACTIVATE);
        return BorderlessResult.MadeBorderless;
    }

    /// <summary>Puts every window we changed back (e.g. on exit).</summary>
    public void RestoreAll()
    {
        Prune();
        foreach (var (hwnd, saved) in _saved)
        {
            Restore(hwnd, saved);
        }

        _saved.Clear();
    }

    private static void Restore(nint hwnd, Saved saved)
    {
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_STYLE, saved.Style);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, saved.ExStyle);
        var r = saved.Rect;
        Win32.SetWindowPos(hwnd, 0, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            Win32.SWP_FRAMECHANGED | Win32.SWP_NOZORDER | Win32.SWP_NOOWNERZORDER | Win32.SWP_NOACTIVATE);
        if (saved.WasMaximized)
        {
            Win32.ShowWindow(hwnd, 3); // SW_MAXIMIZE
        }
    }

    private void Prune()
    {
        foreach (var dead in _saved.Keys.Where(h => !Win32.IsWindow(h)).ToList())
        {
            _saved.Remove(dead);
        }
    }

    private static bool IsOwnProcess(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        return pid == Environment.ProcessId;
    }

    private static bool IsShell(nint hwnd)
    {
        var buffer = new char[64];
        var len = Win32.GetClassName(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Max(len, 0)) is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }
}
