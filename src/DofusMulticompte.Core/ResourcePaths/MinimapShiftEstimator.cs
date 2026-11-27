namespace DofusMulticompte.Core.ResourcePaths;

/// <summary>
/// Mesure de combien de pixels l'image de la minimap a glissé entre deux instants. Quand le personnage
/// change de carte, la minimap (centrée sur lui) glisse d'exactement une carte : on en déduit l'échelle,
/// en pixels par carte, sans aucun réglage.
/// <para>Images en niveaux de gris (un octet par pixel), de même taille.</para>
/// </summary>
public static class MinimapShiftEstimator
{
    /// <summary>
    /// Décalage (dx, dy) tel que <c>after(x, y) ≈ before(x - dx, y - dy)</c>, cherché dans ±<paramref name="maxShift"/> px.
    /// Le centre (le pion du personnage, immobile) est ignoré. Renvoie null si aucun décalage n'est net.
    /// </summary>
    public static (int Dx, int Dy, double Confidence)? Estimate(byte[] before, byte[] after, int width, int height, int maxShift)
    {
        if (before.Length < width * height || after.Length < width * height || maxShift < 1) return null;

        // Recherche grossière (pas de 2), puis affinage au pixel autour du meilleur décalage.
        var coarse = Search(before, after, width, height, -maxShift, maxShift, -maxShift, maxShift, 2);
        if (coarse is null) return null;
        var fine = Search(before, after, width, height, coarse.Value.Dx - 2, coarse.Value.Dx + 2, coarse.Value.Dy - 2, coarse.Value.Dy + 2, 1);
        if (fine is null) return null;

        // Confiance : écart entre le meilleur score et le score sans décalage.
        var still = Cost(before, after, width, height, 0, 0, 3);
        var confidence = still <= 0 ? 0 : 1 - fine.Value.Cost / still;
        return (fine.Value.Dx, fine.Value.Dy, confidence);
    }

    private static (int Dx, int Dy, double Cost)? Search(byte[] a, byte[] b, int w, int h, int x0, int x1, int y0, int y1, int step)
    {
        (int, int, double)? best = null;
        for (var dy = y0; dy <= y1; dy += step)
            for (var dx = x0; dx <= x1; dx += step)
            {
                var c = Cost(a, b, w, h, dx, dy, 2);
                if (best is null || c < best.Value.Item3) best = (dx, dy, c);
            }
        return best;
    }

    /// <summary>Différence moyenne entre les pixels qui se recouvrent une fois décalés (échantillonnage tous les <paramref name="sample"/> px).</summary>
    private static double Cost(byte[] a, byte[] b, int w, int h, int dx, int dy, int sample)
    {
        int cx = w / 2, cy = h / 2, rx = w / 8, ry = h / 8;
        long sum = 0, n = 0;
        for (var y = Math.Max(0, dy); y < Math.Min(h, h + dy); y += sample)
            for (var x = Math.Max(0, dx); x < Math.Min(w, w + dx); x += sample)
            {
                if (Math.Abs(x - cx) < rx && Math.Abs(y - cy) < ry) continue; // pion du personnage
                sum += Math.Abs(b[y * w + x] - a[(y - dy) * w + (x - dx)]);
                n++;
            }
        // Peu de recouvrement : décalage peu fiable.
        return n < w * h / (sample * sample * 4) ? double.MaxValue : sum / (double)n;
    }
}
