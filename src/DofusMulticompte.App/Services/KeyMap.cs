using System.Windows.Input;

namespace DofusMulticompte.App.Services;

/// <summary>Conversion entre noms de touches WPF (« F1 », « Right », « A »…) et codes virtuels Windows.</summary>
public static class KeyMap
{
    public static bool TryGetVirtualKey(string keyName, out uint vk)
    {
        vk = 0;
        if (!Enum.TryParse<Key>(keyName, ignoreCase: true, out var key) || key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0;
    }

    public static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System;

    /// <summary>Touches qui nécessitent le drapeau « étendu » pour SendInput.</summary>
    public static bool IsExtended(ushort vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or // PageUp, PageDown, End, Home
        0x25 or 0x26 or 0x27 or 0x28 or // flèches
        0x2D or 0x2E or                 // Insert, Delete
        0x5B or 0x5C or 0x6F or 0x90;   // Win, /, NumLock
}
