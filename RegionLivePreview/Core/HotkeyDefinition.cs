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
        if ((hotkey.Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(VirtualKeyMap.GetName(hotkey.KeyCode));
        return string.Join(" + ", parts);
    }
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
