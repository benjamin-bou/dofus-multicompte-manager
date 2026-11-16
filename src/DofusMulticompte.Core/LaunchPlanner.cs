namespace DofusMulticompte.Core;

/// <summary>Prépare le lancement d'instances via Ankama Launcher.</summary>
public static class LaunchPlanner
{
    /// <summary>
    /// Lien profond compris par Ankama Launcher : il ouvre la page du jeu et lance une instance
    /// sur le compte actif du launcher (même action que le bouton « Jouer »).
    /// </summary>
    public static string BuildLaunchUri(string gameUid, string release)
    {
        if (string.IsNullOrWhiteSpace(gameUid) || string.IsNullOrWhiteSpace(release))
            throw new ArgumentException("Le jeu et la version du launcher sont obligatoires.");
        return $"zaap://actual/games/game/{Uri.EscapeDataString(gameUid.Trim())}/{Uri.EscapeDataString(release.Trim())}?launch";
    }

    /// <summary>Personnages connus mais sans instance ouverte, dans l'ordre choisi par l'utilisateur.</summary>
    public static IReadOnlyList<string> OfflineCharacters(AppSettings settings, IEnumerable<string> onlineNames)
    {
        var online = new HashSet<string>(onlineNames, StringComparer.OrdinalIgnoreCase);
        return settings.Characters
            .Where(kv => !online.Contains(kv.Key))
            .OrderBy(kv => kv.Value.Order)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => kv.Key)
            .ToList();
    }
}
