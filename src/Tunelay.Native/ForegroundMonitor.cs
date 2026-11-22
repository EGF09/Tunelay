using System.Diagnostics;

namespace Tunelay.Native;

public sealed record ForegroundInfo(nint Handle, string ProcessName, bool IsFullscreen);

/// <summary>
/// Event-driven watcher for the foreground window. Raises <see cref="Changed"/> only when
/// the foreground app or its fullscreen state changes. No polling, no injection.
/// Create on a thread with a message loop (the WPF UI thread).
/// </summary>
public sealed class ForegroundMonitor : IDisposable
{
    // SHQueryUserNotificationState values that mean "user is busy fullscreen".
    private const int QUNS_BUSY = 2, QUNS_RUNNING_D3D_FULL_SCREEN = 3, QUNS_PRESENTATION_MODE = 4;

    private readonly Win32.WinEventProc _proc; // kept alive: the native hook holds a pointer to it
    private readonly nint _foregroundHook;
    private readonly nint _locationHook;
    private ForegroundInfo? _last;

    public ForegroundMonitor()
    {
        _proc = OnWinEvent;
        const uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, 0, _proc, 0, 0, flags);
        // Catches F11 / borderless toggles that resize the same window without a focus change.
        _locationHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE, 0, _proc, 0, 0, flags);
    }

    public ForegroundInfo? Current => _last;

    public event Action<ForegroundInfo>? Changed;

    /// <summary>Evaluates the current foreground window now (e.g. at startup).</summary>
    public void Refresh() => Evaluate(Win32.GetForegroundWindow());

    private void OnWinEvent(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == Win32.EVENT_OBJECT_LOCATIONCHANGE && (idObject != Win32.OBJID_WINDOW || hwnd != Win32.GetForegroundWindow()))
        {
            return;
        }

        Evaluate(hwnd);
    }

    private void Evaluate(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        var fullscreen = IsFullscreen(hwnd);
        if (_last is { } last && last.Handle == hwnd && last.IsFullscreen == fullscreen)
        {
            return;
        }

        var info = new ForegroundInfo(hwnd, ProcessName(hwnd), fullscreen);
        _last = info;
        Changed?.Invoke(info);
    }

    public static bool IsFullscreen(nint hwnd)
    {
        if (Win32.SHQueryUserNotificationState(out var state) == 0
            && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE)
        {
            return true;
        }

        if (hwnd == Win32.GetShellWindow() || IsDesktopClass(hwnd) || !Win32.GetWindowRect(hwnd, out var rect))
        {
            return false;
        }

        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        if (!Win32.GetMonitorInfo(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST), ref info))
        {
            return false;
        }

        var m = info.rcMonitor;
        return rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
    }

    private static bool IsDesktopClass(nint hwnd)
    {
        var buffer = new char[64];
        var len = Win32.GetClassName(hwnd, buffer, buffer.Length);
        var name = new string(buffer, 0, Math.Max(len, 0));
        return name is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    private static string ProcessName(nint hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out var pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "";
        }
    }

    public void Dispose()
    {
        Win32.UnhookWinEvent(_foregroundHook);
        Win32.UnhookWinEvent(_locationHook);
    }
}
