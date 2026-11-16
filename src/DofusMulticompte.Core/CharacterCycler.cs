namespace DofusMulticompte.Core;

/// <summary>Calcule le personnage suivant ou précédent dans l'ordre défini par l'utilisateur.</summary>
public static class CharacterCycler
{
    /// <param name="orderedKeys">Les fenêtres, déjà triées dans l'ordre voulu.</param>
    /// <param name="current">La fenêtre active, ou null si ce n'est pas une fenêtre Dofus.</param>
    /// <param name="step">+1 pour suivant, -1 pour précédent.</param>
    public static T? Next<T>(IReadOnlyList<T> orderedKeys, T? current, int step) where T : struct
    {
        if (orderedKeys.Count == 0) return null;
        var index = -1;
        if (current is { } cur)
            for (var i = 0; i < orderedKeys.Count; i++)
                if (EqualityComparer<T>.Default.Equals(orderedKeys[i], cur)) { index = i; break; }

        if (index < 0) return step >= 0 ? orderedKeys[0] : orderedKeys[^1];
        var next = ((index + step) % orderedKeys.Count + orderedKeys.Count) % orderedKeys.Count;
        return orderedKeys[next];
    }
}
