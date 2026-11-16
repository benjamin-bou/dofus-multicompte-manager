namespace DofusMulticompte.Core;

/// <summary>Comment les touches sont envoyées aux fenêtres Dofus.</summary>
public enum InputMode
{
    /// <summary>Met la fenêtre au premier plan, envoie la touche, puis rend la main à la fenêtre précédente. Fiable.</summary>
    Foreground = 0,
    /// <summary>Poste la touche à la fenêtre sans la mettre au premier plan. Plus discret, mais Unity peut l'ignorer.</summary>
    Background = 1,
}

/// <summary>Réglages propres à un personnage, retrouvés par son pseudo.</summary>
public sealed class CharacterProfile
{
    public int? Level { get; set; }
    public int Order { get; set; } = int.MaxValue;
    /// <summary>Si faux, le personnage est ignoré par toutes les automatisations.</summary>
    public bool IncludedInAutomation { get; set; } = true;
}

public sealed class HotkeySettings
{
    public string NextCharacter { get; set; } = "Ctrl+Right";
    public string PreviousCharacter { get; set; } = "Ctrl+Left";
    public string FocusLeader { get; set; } = "Ctrl+Up";
}

public sealed class AppSettings
{
    /// <summary>Pseudo du meneur.</summary>
    public string? LeaderName { get; set; }

    /// <summary>Passe automatiquement le tour de tous les personnages sauf le meneur.</summary>
    public bool AutoPassFollowers { get; set; }

    /// <summary>Passe aussi automatiquement le tour du meneur.</summary>
    public bool AutoPassLeader { get; set; }

    /// <summary>Met au premier plan la fenêtre du personnage dont c'est le tour.</summary>
    public bool BringTurnWindowToFront { get; set; } = true;

    /// <summary>Touche « Passer son tour » configurée dans Dofus.</summary>
    public string PassTurnKey { get; set; } = "F1";

    public int PassDelayMinMs { get; set; } = 500;
    public int PassDelayMaxMs { get; set; } = 1200;

    /// <summary>Délai pendant lequel les nouveaux clignotements d'une même fenêtre sont ignorés.</summary>
    public int TurnSignalCooldownMs { get; set; } = 2500;

    public InputMode InputMode { get; set; } = InputMode.Foreground;

    /// <summary>Noms de processus considérés comme des clients Dofus (sans « .exe »).</summary>
    public List<string> ProcessNames { get; set; } = ["Dofus"];

    public HotkeySettings Hotkeys { get; set; } = new();

    public Dictionary<string, CharacterProfile> Characters { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public CharacterProfile GetOrCreateProfile(string characterName)
    {
        if (!Characters.TryGetValue(characterName, out var profile))
        {
            profile = new CharacterProfile { Order = Characters.Count == 0 ? 0 : Characters.Values.Max(p => p.Order == int.MaxValue ? 0 : p.Order) + 1 };
            Characters[characterName] = profile;
        }
        return profile;
    }

    public bool IsLeader(string? characterName) =>
        characterName is not null && LeaderName is not null &&
        string.Equals(characterName, LeaderName, StringComparison.OrdinalIgnoreCase);
}
