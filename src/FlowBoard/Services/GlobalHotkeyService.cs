using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace FlowBoard.Services;

/// <summary>
/// System-wide hotkeys (they work while FlowBoard is minimized or in the tray) using Win32 RegisterHotKey.
/// Hotkeys are written as text, e.g. "Ctrl+Alt+Space" or "Win+Shift+N".
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Dictionary<int, Action> _actions = new();
    private HwndSource? _source;
    private IntPtr _hwnd;

    public void Attach(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source?.AddHook(WndProc);
    }

    /// <summary>Registers (or replaces) hotkey <paramref name="id"/>. Returns false if the text is invalid or another app owns it.</summary>
    public bool Register(int id, string? hotkey, Action action)
    {
        Unregister(id);
        if (_hwnd == IntPtr.Zero || !TryParse(hotkey, out var modifiers, out var key)) return false;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_hwnd, id, ToNative(modifiers) | ModNoRepeat, vk)) return false;
        _actions[id] = action;
        return true;
    }

    public void Unregister(int id)
    {
        if (_actions.Remove(id) && _hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys.ToList()) Unregister(id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static uint ToNative(ModifierKeys m) =>
        (m.HasFlag(ModifierKeys.Alt) ? ModAlt : 0) | (m.HasFlag(ModifierKeys.Control) ? ModControl : 0)
        | (m.HasFlag(ModifierKeys.Shift) ? ModShift : 0) | (m.HasFlag(ModifierKeys.Windows) ? ModWin : 0);

    /// <summary>Parses "Ctrl+Alt+Space". Global hotkeys need at least one modifier.</summary>
    public static bool TryParse(string? text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win" or "windows": modifiers |= ModifierKeys.Windows; break;
                default:
                    var name = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse(name, true, out key)) return false;
                    break;
            }
        }

        return key != Key.None && modifiers != ModifierKeys.None;
    }

    /// <summary>Formats a key press as hotkey text ("Ctrl+Shift+K"); null for modifier-only presses.</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        var k = key.ToString();
        if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) k = k[1..];
        parts.Add(k);
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
