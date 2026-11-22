using System.Windows.Input;
using System.Windows.Interop;

namespace Tunelay.Native;

public sealed record HotkeyGesture(uint Modifiers, uint VirtualKey, string Text)
{
    /// <summary>Parses strings like "Ctrl+Alt+Space" or "Win+Shift+F9".</summary>
    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint mods = 0;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= Win32.MOD_CONTROL; break;
                case "alt": mods |= Win32.MOD_ALT; break;
                case "shift": mods |= Win32.MOD_SHIFT; break;
                case "win" or "windows": mods |= Win32.MOD_WIN; break;
                default:
                    if (key is not null || !Enum.TryParse<Key>(raw, ignoreCase: true, out var k))
                    {
                        return false;
                    }

                    key = k;
                    break;
            }
        }

        if (key is null || (mods == 0 && !IsStandalone(key.Value)))
        {
            return false;
        }

        gesture = new HotkeyGesture(mods, (uint)KeyInterop.VirtualKeyFromKey(key.Value), text);
        return true;
    }

    /// <summary>
    /// Keys allowed without a modifier: they don't type text, so a bare press is a safe global trigger.
    /// (The laptop Fn key itself never reaches Windows, so it can't be used.)
    /// </summary>
    public static bool IsStandalone(Key key) =>
        key is >= Key.F1 and <= Key.F24 or Key.Pause or Key.Scroll or Key.Insert;

    /// <summary>Formats a key press from a WPF key event, e.g. for a "press a shortcut" box.</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System || (modifiers == ModifierKeys.None && !IsStandalone(key)))
        {
            return null;
        }

        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }
}

/// <summary>System-wide hotkeys via RegisterHotKey on a hidden message window. Create and use on the UI thread.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        _source = new HwndSource(new HwndSourceParameters("Tunelay.Hotkeys")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0,
            ParentWindow = new nint(-3), // HWND_MESSAGE: message-only window
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Returns false when the combo is invalid or already taken by another app.</summary>
    public bool Register(HotkeyGesture gesture, Action handler)
    {
        var id = _nextId++;
        if (!Win32.RegisterHotKey(_source.Handle, id, gesture.Modifiers | Win32.MOD_NOREPEAT, gesture.VirtualKey))
        {
            return false;
        }

        _handlers[id] = handler;
        return true;
    }

    /// <summary>Registers a bare key (no modifiers), e.g. Esc while a menu is open. Returns the id or 0.</summary>
    public int RegisterKey(System.Windows.Input.Key key, Action handler)
    {
        var id = _nextId++;
        if (!Win32.RegisterHotKey(_source.Handle, id, Win32.MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            return 0;
        }

        _handlers[id] = handler;
        return id;
    }

    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
        {
            Win32.UnregisterHotKey(_source.Handle, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            Win32.UnregisterHotKey(_source.Handle, id);
        }

        _handlers.Clear();
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _handlers.TryGetValue((int)wParam, out var handler))
        {
            handled = true;
            handler();
        }

        return 0;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
