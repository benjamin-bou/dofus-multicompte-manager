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

/// <summary>Boutons de souris utilisables comme raccourci (les clics gauche et droit sont exclus).</summary>
public enum MouseButton
{
    None = 0,
    /// <summary>Clic molette.</summary>
    Middle,
    /// <summary>Bouton latéral « précédent » (XBUTTON1).</summary>
    X1,
    /// <summary>Bouton latéral « suivant » (XBUTTON2).</summary>
    X2,
}

/// <summary>
/// Combinaison de touches sous forme texte, par exemple « Ctrl+Shift+F2 ».
/// La touche peut aussi être un bouton de souris : « Souris4 », « Souris5 » ou « Molette ».
/// </summary>
public sealed record KeyGesture(KeyModifiers Modifiers, string Key)
{
    public const string Mouse4Name = "Souris4";
    public const string Mouse5Name = "Souris5";
    public const string MiddleName = "Molette";

    /// <summary>Bouton de souris désigné par la touche, ou <see cref="MouseButton.None"/> pour une touche du clavier.</summary>
    public MouseButton MouseButton => ParseMouseButton(Key);

    public bool IsMouse => MouseButton != MouseButton.None;

    public static MouseButton ParseMouseButton(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "souris4" or "mouse4" or "xbutton1" => MouseButton.X1,
        "souris5" or "mouse5" or "xbutton2" => MouseButton.X2,
        "molette" or "middle" or "mousemiddle" or "middleclick" => MouseButton.Middle,
        _ => MouseButton.None,
    };

    public static string MouseButtonName(MouseButton button) => button switch
    {
        MouseButton.X1 => Mouse4Name,
        MouseButton.X2 => Mouse5Name,
        MouseButton.Middle => MiddleName,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

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
