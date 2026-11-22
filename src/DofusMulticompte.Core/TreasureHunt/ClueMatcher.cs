using System.Globalization;
using System.Text;

namespace DofusMulticompte.Core.TreasureHunt;

/// <summary>
/// Rapproche le texte lu à l'écran (OCR, donc parfois imparfait) du nom d'un élément de chasse,
/// puis choisit la carte la plus proche qui le contient.
/// </summary>
public static class ClueMatcher
{
    /// <summary>Ressemblance minimale (0 à 1) pour accepter un rapprochement.</summary>
    public const double MinScore = 0.78;

    /// <summary>Écart minimal avec le deuxième meilleur élément, pour ne pas confondre deux noms voisins.</summary>
    public const double MinMargin = 0.06;

    /// <summary>Minuscules, sans accents ni ponctuation, espaces simples.</summary>
    public static string Normalize(string text)
    {
        var expanded = text.Replace("œ", "oe").Replace("Œ", "oe").Replace("æ", "ae").Replace("Æ", "ae")
                           .Replace('’', '\'');
        var decomposed = expanded.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        var lastSpace = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastSpace = false;
            }
            else if (!lastSpace)
            {
                sb.Append(' ');
                lastSpace = true;
            }
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Ressemblance entre deux textes, de 0 (rien à voir) à 1 (identiques une fois normalisés).</summary>
    public static double Similarity(string a, string b)
    {
        var x = Normalize(a);
        var y = Normalize(b);
        if (x.Length == 0 && y.Length == 0) return 1;
        if (x.Length == 0 || y.Length == 0) return 0;
        return 1.0 - (double)Levenshtein(x, y) / Math.Max(x.Length, y.Length);
    }

    public static bool IsPhorreur(string clue) => Normalize(clue).Contains("phorreur");

    /// <summary>Élément dont le nom ressemble le plus au texte lu, s'il est assez net.</summary>
    public static (HuntPoi Poi, double Score)? Identify(string clue, IEnumerable<HuntPoi> pois)
    {
        var ranked = pois
            .GroupBy(p => p.Id).Select(g => g.First())
            .Select(p => (Poi: p, Score: Similarity(clue, p.Name)))
            .OrderByDescending(r => r.Score)
            .Take(2).ToList();
        if (ranked.Count == 0 || ranked[0].Score < MinScore) return null;
        if (ranked.Count > 1 && ranked[0].Score < 1 && ranked[0].Score - ranked[1].Score < MinMargin) return null;
        return ranked[0];
    }

    /// <summary>
    /// Trouve la carte de l'indice parmi les cartes situées dans la direction de la flèche.
    /// <paramref name="catalog"/> (tous les éléments connus) sert à reconnaître l'indice même s'il
    /// n'est sur aucune des cartes proposées ; à défaut, on se limite aux éléments de ces cartes.
    /// </summary>
    public static ClueResolution Resolve(string clue, IReadOnlyList<HuntMapCandidate> candidates, IEnumerable<HuntPoi>? catalog = null)
    {
        if (IsPhorreur(clue)) return new ClueResolution(ClueStatus.Phorreur);

        var pool = catalog ?? candidates.SelectMany(c => c.Pois);
        if (Identify(clue, pool) is not var (poi, score)) return new ClueResolution(ClueStatus.UnknownClue);

        var nearest = candidates
            .Where(c => c.Pois.Any(p => p.Id == poi.Id))
            .OrderBy(c => c.Distance)
            .FirstOrDefault();
        return nearest is null
            ? new ClueResolution(ClueStatus.NotInRange, poi, Score: score)
            : new ClueResolution(ClueStatus.Found, poi, nearest.Position, nearest.Distance, score);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
