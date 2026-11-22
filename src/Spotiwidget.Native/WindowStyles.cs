using System.Security.Cryptography;
using Spotiwidget.Core.Spotify;

namespace Spotiwidget.Native;

/// <summary>Extended-style helpers for a desktop overlay that never steals focus.</summary>
public static class WindowStyles
{
    /// <summary>Tool window (no taskbar/Alt-Tab entry), never activated, layered.</summary>
    public static void MakeOverlay(nint hwnd) =>
        Update(hwnd, style => style | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_LAYERED);

    /// <summary>When on, mouse input passes through to whatever is behind the window.</summary>
    public static void SetClickThrough(nint hwnd, bool on) =>
        Update(hwnd, style => on ? style | Win32.WS_EX_TRANSPARENT : style & ~Win32.WS_EX_TRANSPARENT);

    /// <summary>
    /// Moves the window to the top of the always-on-top band without activating it.
    /// Fullscreen games are often topmost themselves and win when they get focus, so call this again after they do.
    /// </summary>
    public static void BringToTopmost(nint hwnd) =>
        Win32.SetWindowPos(hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    /// <summary>Dark title bar for a normal window (Windows 10 20H1+ / 11; ignored elsewhere).</summary>
    public static void UseDarkTitleBar(nint hwnd)
    {
        var on = 1;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
    }

    public static bool IsClickThrough(nint hwnd) =>
        (Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TRANSPARENT) != 0;

    private static void Update(nint hwnd, Func<nint, nint> change)
    {
        var style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE, change(style));
    }
}

/// <summary>Keeps a window at the bottom of the z-order, i.e. on the desktop behind every other window.</summary>
public sealed class DesktopPin
{
    private nint _hwnd;

    /// <summary>While false (e.g. in move mode) the window may be raised normally.</summary>
    public bool Enabled { get; private set; } = true;

    public void Attach(System.Windows.Interop.HwndSource source)
    {
        _hwnd = source.Handle;
        source.AddHook(Hook);
        SendToBottom(_hwnd);
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        if (enabled && _hwnd != 0)
        {
            SendToBottom(_hwnd);
        }
    }

    public static void SendToBottom(nint hwnd) =>
        Win32.SetWindowPos(hwnd, Win32.HWND_BOTTOM, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_WINDOWPOSCHANGING && Enabled)
        {
            // Whenever something tries to raise the window (e.g. a click), push it back to the bottom.
            var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.WINDOWPOS>(lParam);
            if ((pos.flags & Win32.SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = Win32.HWND_BOTTOM;
                System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
            }
        }

        return 0;
    }
}

public static class Monitors
{
    /// <summary>Work area (physical pixels) of the monitor nearest to the given pixel point.</summary>
    public static (int Left, int Top, int Right, int Bottom) WorkAreaNear(int x, int y)
    {
        var monitor = Win32.MonitorFromPoint(new Win32.POINT { X = x, Y = y }, Win32.MONITOR_DEFAULTTONEAREST);
        var info = new Win32.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>() };
        Win32.GetMonitorInfo(monitor, ref info);
        var w = info.rcWork;
        return (w.Left, w.Top, w.Right, w.Bottom);
    }
}

public static class CursorPos
{
    public static (int X, int Y) Position() =>
        Win32.GetCursorPos(out var p) ? (p.X, p.Y) : (int.MinValue, int.MinValue);
}

/// <summary>Encrypts tokens for the current Windows user (DPAPI).</summary>
public sealed class DpapiProtector : ITokenProtector
{
    private static readonly byte[] Entropy = "Spotiwidget.tokens.v1"u8.ToArray();

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] cipher) => ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
}
