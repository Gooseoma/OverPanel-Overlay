using System.Runtime.InteropServices;

namespace Overpanel.Overlay.Interop;

/// <summary>Win32-хоткеи для показа/скрытия оверлея.</summary>
public static class NativeMethods
{
    public const int WmHotkey = 0x0312;

    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

/// <summary>Разбор строк вида "Ctrl+Shift+O".</summary>
public static class HotkeyParser
{
    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = NativeMethods.ModNoRepeat;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var keyFound = false;

        foreach (var rawPart in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (rawPart.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.ModControl;
                    break;
                case "shift":
                    modifiers |= NativeMethods.ModShift;
                    break;
                case "alt":
                    modifiers |= NativeMethods.ModAlt;
                    break;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.ModWin;
                    break;
                default:
                    if (!TryParseKey(rawPart, out virtualKey))
                    {
                        return false;
                    }

                    keyFound = true;
                    break;
            }
        }

        return keyFound && virtualKey != 0;
    }

    private static bool TryParseKey(string token, out uint virtualKey)
    {
        virtualKey = 0;

        if (token.Length == 1)
        {
            var ch = char.ToUpperInvariant(token[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = ch;
                return true;
            }

            if (ch == ' ')
            {
                virtualKey = 0x20;
                return true;
            }

            return false;
        }

        if ((token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(token[1..], out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + functionKey - 1);
            return true;
        }

        switch (token.ToLowerInvariant())
        {
            case "space":
                virtualKey = 0x20;
                return true;
            case "tab":
                virtualKey = 0x09;
                return true;
            case "enter":
                virtualKey = 0x0D;
                return true;
            case "escape":
            case "esc":
                virtualKey = 0x1B;
                return true;
            case "insert":
                virtualKey = 0x2D;
                return true;
            case "home":
                virtualKey = 0x24;
                return true;
            case "end":
                virtualKey = 0x23;
                return true;
            default:
                return false;
        }
    }
}
