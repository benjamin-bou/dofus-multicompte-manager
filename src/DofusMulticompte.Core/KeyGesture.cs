namespace DofusMulticompte.Core;

/// <summary>Modificateurs, avec les mêmes valeurs que MOD_* de RegisterHotKey.</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Combinaison de touches sous forme texte, par exemple « Ctrl+Shift+F2 ».</summary>
public sealed record KeyGesture(KeyModifiers Modifiers, string Key)
{
    public static bool TryParse(string? text, out KeyGesture gesture)
    {
        gesture = new KeyGesture(KeyModifiers.None, "");
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;

        var mods = KeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl" or "control" or "ctrl.": mods |= KeyModifiers.Ctrl; break;
                case "alt": mods |= KeyModifiers.Alt; break;
                case "shift" or "maj": mods |= KeyModifiers.Shift; break;
                case "win" or "windows": mods |= KeyModifiers.Win; break;
                default: return false;
            }
        }

        var key = parts[^1];
        if (key.Length == 0) return false;
        gesture = new KeyGesture(mods, key);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(KeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(KeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}
