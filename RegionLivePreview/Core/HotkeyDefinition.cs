using System.Text.Json.Serialization;

namespace RegionLivePreview.Core;

/// <summary>
/// 与 Win32 RegisterHotKey 的 MOD_* 取值保持一致，便于直接传递。
/// </summary>
[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<HotkeyModifiers>))]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Win = 8
}

public sealed class HotkeyDefinition
{
    public HotkeyModifiers Modifiers { get; set; } = HotkeyModifiers.Alt | HotkeyModifiers.Shift;

    /// <summary>Win32 虚拟键码，默认是 S（0x53）。</summary>
    public int KeyCode { get; set; } = 0x53;

    public HotkeyDefinition Clone() => new()
    {
        Modifiers = Modifiers,
        KeyCode = KeyCode
    };

    public bool IsSameAs(HotkeyDefinition? other)
    {
        return other is not null && other.Modifiers == Modifiers && other.KeyCode == KeyCode;
    }

    public void Normalize()
    {
        Modifiers &= HotkeyModifiers.Alt | HotkeyModifiers.Control | HotkeyModifiers.Shift | HotkeyModifiers.Win;
        if (KeyCode is < 0x01 or > 0xFE)
        {
            KeyCode = 0x53;
        }
    }
}

public static class HotkeyFormatter
{
    public static string Format(HotkeyDefinition hotkey)
    {
        var parts = new List<string>(5);
        if ((hotkey.Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((hotkey.Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((hotkey.Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((hotkey.Modifiers & HotkeyModifiers.Win) != 0)
        {
            parts.Add(OperatingSystem.IsMacOS() ? "Command" : "Win");
        }

        if (OperatingSystem.IsMacOS())
        {
            for (var index = 0; index < parts.Count; index++)
            {
                if (parts[index] == "Alt")
                {
                    parts[index] = "Option";
                }
            }
        }

        parts.Add(VirtualKeyMap.GetName(hotkey.KeyCode));
        return string.Join(" + ", parts);
    }

    public static string ModifierHelp => OperatingSystem.IsMacOS()
        ? "Ctrl / Option / Shift / Command"
        : "Ctrl / Alt / Shift / Win";
}

public static class VirtualKeyMap
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.Ordinal)
    {
        ["Space"] = 0x20,
        ["Return"] = 0x0D,
        ["Enter"] = 0x0D,
        ["Tab"] = 0x09,
        ["Back"] = 0x08,
        ["Escape"] = 0x1B,
        ["Delete"] = 0x2E,
        ["Insert"] = 0x2D,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["PrintScreen"] = 0x2C,
        ["Pause"] = 0x13,
        ["OemTilde"] = 0xC0,
        ["OemMinus"] = 0xBD,
        ["OemPlus"] = 0xBB,
        ["OemOpenBrackets"] = 0xDB,
        ["OemCloseBrackets"] = 0xDD,
        ["OemSemicolon"] = 0xBA,
        ["OemQuotes"] = 0xDE,
        ["OemComma"] = 0xBC,
        ["OemPeriod"] = 0xBE,
        ["OemSlash"] = 0xBF,
        ["OemQuestion"] = 0xBF,
        ["OemPipe"] = 0xDC,
        ["OemBackslash"] = 0xDC,
        ["Multiply"] = 0x6A,
        ["Add"] = 0x6B,
        ["Subtract"] = 0x6D,
        ["Divide"] = 0x6F,
        ["Decimal"] = 0x6E
    };

    private static readonly Dictionary<int, string> DisplayNames = BuildDisplayNames();

    private static readonly Dictionary<char, int> MacLetterKeys = new()
    {
        ['A'] = 0x00,
        ['S'] = 0x01,
        ['D'] = 0x02,
        ['F'] = 0x03,
        ['H'] = 0x04,
        ['G'] = 0x05,
        ['Z'] = 0x06,
        ['X'] = 0x07,
        ['C'] = 0x08,
        ['V'] = 0x09,
        ['B'] = 0x0B,
        ['Q'] = 0x0C,
        ['W'] = 0x0D,
        ['E'] = 0x0E,
        ['R'] = 0x0F,
        ['Y'] = 0x10,
        ['T'] = 0x11,
        ['O'] = 0x1F,
        ['U'] = 0x20,
        ['I'] = 0x22,
        ['P'] = 0x23,
        ['L'] = 0x25,
        ['J'] = 0x26,
        ['K'] = 0x28,
        ['N'] = 0x2D,
        ['M'] = 0x2E
    };

    private static readonly Dictionary<char, int> MacDigitKeys = new()
    {
        ['1'] = 0x12,
        ['2'] = 0x13,
        ['3'] = 0x14,
        ['4'] = 0x15,
        ['6'] = 0x16,
        ['5'] = 0x17,
        ['9'] = 0x19,
        ['7'] = 0x1A,
        ['8'] = 0x1C,
        ['0'] = 0x1D
    };

    /// <summary>
    /// 把 Avalonia 的按键转换为 Win32 虚拟键码。返回 0 表示不支持。
    /// </summary>
    public static int FromAvaloniaKey(Avalonia.Input.Key key)
    {
        var name = key.ToString();

        if (name.Length == 1 && name[0] is >= 'A' and <= 'Z')
        {
            return name[0];
        }

        if (name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]))
        {
            return '0' + (name[1] - '0');
        }

        if (name.Length is >= 2 and <= 3 && name[0] == 'F' &&
            int.TryParse(name.AsSpan(1), out var functionIndex) &&
            functionIndex is >= 1 and <= 24)
        {
            return 0x70 + functionIndex - 1;
        }

        if (name.StartsWith("NumPad", StringComparison.Ordinal) &&
            int.TryParse(name.AsSpan(6), out var numPadIndex) &&
            numPadIndex is >= 0 and <= 9)
        {
            return 0x60 + numPadIndex;
        }

        return NamedKeys.TryGetValue(name, out var vk) ? vk : 0;
    }

    public static string GetName(int keyCode)
    {
        if (keyCode is >= 0x41 and <= 0x5A)
        {
            return ((char)keyCode).ToString();
        }

        if (keyCode is >= 0x30 and <= 0x39)
        {
            return ((char)keyCode).ToString();
        }

        if (keyCode is >= 0x70 and <= 0x87)
        {
            return $"F{keyCode - 0x70 + 1}";
        }

        if (keyCode is >= 0x60 and <= 0x69)
        {
            return $"Num{keyCode - 0x60}";
        }

        if (DisplayNames.TryGetValue(keyCode, out var name))
        {
            return name;
        }

        return $"VK_{keyCode:X2}";
    }

    /// <summary>
    /// 配置沿用 Windows 虚拟键码以保持跨平台兼容，这里转换为 Carbon 键盘硬件键码。
    /// </summary>
    public static uint ToMacVirtualKey(int windowsVirtualKey)
    {
        if (windowsVirtualKey is >= 0x41 and <= 0x5A
            && MacLetterKeys.TryGetValue((char)windowsVirtualKey, out var letter))
        {
            return (uint)letter;
        }

        if (windowsVirtualKey is >= 0x30 and <= 0x39
            && MacDigitKeys.TryGetValue((char)windowsVirtualKey, out var digit))
        {
            return (uint)digit;
        }

        if (windowsVirtualKey is >= 0x70 and <= 0x87)
        {
            var functionIndex = windowsVirtualKey - 0x70 + 1;
            return functionIndex switch
            {
                1 => 0x7A,
                2 => 0x78,
                3 => 0x63,
                4 => 0x76,
                5 => 0x60,
                6 => 0x61,
                7 => 0x62,
                8 => 0x64,
                9 => 0x65,
                10 => 0x6D,
                11 => 0x67,
                12 => 0x6F,
                13 => 0x69,
                14 => 0x6B,
                15 => 0x71,
                16 => 0x6A,
                17 => 0x40,
                18 => 0x4F,
                19 => 0x50,
                20 => 0x5A,
                _ => 0
            };
        }

        if (windowsVirtualKey is >= 0x60 and <= 0x69)
        {
            var numberPadDigit = windowsVirtualKey - 0x60;
            return numberPadDigit == 0 ? 0x52u : (uint)(0x52 + numberPadDigit);
        }

        return windowsVirtualKey switch
        {
            0x20 => 0x31, // Space
            0x0D => 0x24, // Return
            0x09 => 0x30, // Tab
            0x08 => 0x33, // Backspace / Delete
            0x1B => 0x35, // Escape
            0x2E => 0x75, // Forward delete
            0x24 => 0x73, // Home
            0x23 => 0x77, // End
            0x21 => 0x74, // Page up
            0x22 => 0x79, // Page down
            0x25 => 0x7B, // Left
            0x26 => 0x7E, // Up
            0x27 => 0x7C, // Right
            0x28 => 0x7D, // Down
            0xC0 => 0x32, // `
            0xBD => 0x1B, // -
            0xBB => 0x18, // =
            0xDB => 0x21, // [
            0xDD => 0x1E, // ]
            0xBA => 0x29, // ;
            0xDE => 0x27, // '
            0xBC => 0x2B, // ,
            0xBE => 0x2F, // .
            0xBF => 0x2C, // /
            0xDC => 0x2A, // \
            0x6A => 0x43, // keypad *
            0x6B => 0x45, // keypad +
            0x6D => 0x4E, // keypad -
            0x6F => 0x4B, // keypad /
            0x6E => 0x41, // keypad .
            _ => 0
        };
    }

    public static bool IsModifierKey(Avalonia.Input.Key key)
    {
        return key is Avalonia.Input.Key.LeftCtrl
            or Avalonia.Input.Key.RightCtrl
            or Avalonia.Input.Key.LeftAlt
            or Avalonia.Input.Key.RightAlt
            or Avalonia.Input.Key.LeftShift
            or Avalonia.Input.Key.RightShift
            or Avalonia.Input.Key.LWin
            or Avalonia.Input.Key.RWin;
    }

    private static Dictionary<int, string> BuildDisplayNames()
    {
        var map = new Dictionary<int, string>();
        foreach (var pair in NamedKeys)
        {
            // 同一个 VK 有多个名字时，保留先出现的可读名称。
            map.TryAdd(pair.Value, pair.Key == "Enter" ? "Return" : pair.Key);
        }

        return map;
    }
}
