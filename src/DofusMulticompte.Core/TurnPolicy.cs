namespace DofusMulticompte.Core;

public enum TurnAction
{
    /// <summary>Ne rien faire.</summary>
    None,
    /// <summary>Envoyer la touche « Passer son tour » à la fenêtre.</summary>
    AutoPass,
    /// <summary>Mettre la fenêtre du personnage au premier plan.</summary>
    BringToFront,
}

/// <summary>Décide quoi faire quand un personnage commence son tour.</summary>
public static class TurnPolicy
{
    public static bool ShouldAutoPass(AppSettings settings, string? characterName)
    {
        if (characterName is null) return false;
        if (settings.Characters.TryGetValue(characterName, out var profile) && !profile.IncludedInAutomation)
            return false;
        return settings.IsLeader(characterName) ? settings.AutoPassLeader : settings.AutoPassFollowers;
    }

    public static TurnAction Decide(AppSettings settings, string? characterName)
    {
        if (characterName is null) return TurnAction.None;
        if (ShouldAutoPass(settings, characterName)) return TurnAction.AutoPass;
        if (settings.BringTurnWindowToFront) return TurnAction.BringToFront;
        return TurnAction.None;
    }
}
