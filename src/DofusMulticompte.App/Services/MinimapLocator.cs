using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DofusMulticompte.App.Services;

/// <summary>Zone d'image de la minimap dans la fenêtre du jeu (pixels de la zone client) et pion du personnage.</summary>
public sealed record MinimapRegion(Int32Rect Content, Point Marker, bool MarkerFound);

/// <summary>
/// Retrouve la minimap de Dofus 3 dans une capture de la fenêtre : son titre « MINI-CARTE » est lu par OCR,
/// puis la barre de titre (gris bleuté uni) donne la largeur du panneau, et la zone d'image s'étend sous la
/// barre jusqu'au cadre sombre du bas. Mesures relevées sur le client : barre #454A55 de 36 px de haut,
/// cadre du bas #31313A, pion du personnage bleu pur (#004ABC à #0051CB) au centre de la zone.
/// </summary>
public static class MinimapLocator
{
    public static async Task<MinimapRegion?> LocateAsync(BitmapSource image, MinimapRegion? previous)
    {
        var px = Pixels(image, out var w, out var h);

        // La minimap n'a pas bougé : on revérifie juste la barre de titre au même endroit.
        if (previous is not null && LooksLikeBar(px, w, previous.Content.X + 4, previous.Content.Y - 8))
            return WithMarker(px, w, h, previous.Content);

        // Sinon, OCR : d'abord le quart bas droit (position par défaut), puis toute la fenêtre.
        foreach (var region in new[] { new Int32Rect(w / 2, h / 2, w - w / 2, h - h / 2), new Int32Rect(0, 0, w, h) })
        {
            var lines = await ScreenOcr.ReadAsync(image, region, scale: region.Width < w ? 1.6 : 1.0);
            var title = lines.FirstOrDefault(l => IsTitle(l.Text));
            if (title is null) continue;
            if (FromTitle(px, w, h, title.Bounds) is not { } content) continue;
            // Une infobulle du jeu peut masquer une partie de la barre : on garde la zone connue si elle est plus large au même endroit.
            if (previous is not null && Math.Abs(previous.Content.Y - content.Y) <= 4 && previous.Content.Width > content.Width
                && previous.Content.X <= content.X && previous.Content.X + previous.Content.Width >= content.X + content.Width)
                content = previous.Content;
            return WithMarker(px, w, h, content);
        }
        return null;
    }

    private static bool IsTitle(string text)
    {
        var t = new string(text.Where(char.IsLetter).ToArray()).ToUpperInvariant();
        return t.Contains("MINICARTE") || t.Contains("MINIMAP") || t.Contains("MINICARTA");
    }

    /// <summary>Délimite la zone d'image à partir du titre : largeur de la barre, puis hauteur jusqu'au cadre du bas.</summary>
    private static Int32Rect? FromTitle(byte[] px, int w, int h, Rect title)
    {
        var y = (int)(title.Top + title.Height / 2);
        if (y < 0 || y >= h) return null;

        // Bords gauche et droit : on suit la barre, en tolérant le texte et le pictogramme. Mesuré sur plusieurs
        // lignes de la barre et on garde le plus large : une infobulle peut en masquer une partie.
        int Edge(int from, int step, int row)
        {
            int x = from, misses = 0, lastBar = from;
            while (x > 0 && x < w - 1 && misses < 12)
            {
                x += step;
                if (IsBar(px, w, x, row)) { misses = 0; lastBar = x; } else misses++;
            }
            return lastBar;
        }
        int left = int.MaxValue, right = int.MinValue;
        foreach (var dy in new[] { 0, -8, 8, -13, 13 })
        {
            var row = Math.Clamp(y + dy, 0, h - 1);
            left = Math.Min(left, Edge((int)title.Left, -1, row));
            right = Math.Max(right, Edge((int)title.Right, +1, row));
        }

        // Bas de la barre : on descend tant que la couleur de barre continue (colonne juste à droite du bord).
        var probeX = Math.Min(w - 1, left + 6);
        var barBottom = y;
        while (barBottom + 1 < h && IsBar(px, w, probeX, barBottom + 1)) barBottom++;

        var top = barBottom + 2;
        var bottom = FrameBottom(px, w, h, left, right, top);
        var content = new Int32Rect(left + 2, top, Math.Max(1, right - left - 4), Math.Max(1, bottom - top));
        return content.Width >= 80 && content.Height >= 60 ? content : null;
    }

    /// <summary>Première ligne sous la zone d'image presque entièrement « cadre sombre » ; sinon le bas de la fenêtre.</summary>
    private static int FrameBottom(byte[] px, int w, int h, int left, int right, int top)
    {
        for (var y = top + 40; y < h; y++)
        {
            int frame = 0, n = 0;
            for (var x = left + 8; x < right - 8; x += 6)
            {
                n++;
                var i = (y * w + x) * 4;
                int b = px[i], g = px[i + 1], r = px[i + 2];
                if (r < 70 && g < 70 && b < 80 && Math.Abs(r - g) < 14) frame++;
            }
            if (n > 0 && frame >= n * 0.8) return y;
        }
        return h;
    }

    private static MinimapRegion WithMarker(byte[] px, int w, int h, Int32Rect content)
    {
        // Pion : bleu pur, cherché dans la moitié centrale de la zone.
        long sx = 0, sy = 0, n = 0;
        int x0 = content.X + content.Width / 4, x1 = content.X + content.Width * 3 / 4;
        int y0 = content.Y + content.Height / 4, y1 = content.Y + content.Height * 3 / 4;
        for (var y = Math.Max(0, y0); y < Math.Min(h, y1); y++)
            for (var x = Math.Max(0, x0); x < Math.Min(w, x1); x++)
            {
                var i = (y * w + x) * 4;
                int b = px[i], g = px[i + 1], r = px[i + 2];
                if (b > 150 && b - r > 90 && b - g > 40) { sx += x; sy += y; n++; }
            }
        var center = new Point(content.X + content.Width / 2.0, content.Y + content.Height / 2.0);
        return n >= 20 ? new MinimapRegion(content, new Point(sx / (double)n, sy / (double)n), true) : new MinimapRegion(content, center, false);
    }

    private static bool LooksLikeBar(byte[] px, int w, int x, int y)
    {
        var hits = 0;
        for (var dx = 0; dx < 40; dx += 4) if (IsBar(px, w, x + dx, y)) hits++;
        return hits >= 8;
    }

    /// <summary>Gris bleuté uni de la barre de titre (#454A55 environ).</summary>
    private static bool IsBar(byte[] px, int w, int x, int y)
    {
        var i = (y * w + x) * 4;
        if (x < 0 || y < 0 || i + 2 >= px.Length) return false;
        int b = px[i], g = px[i + 1], r = px[i + 2];
        return r is >= 50 and <= 95 && g is >= 50 and <= 95 && b is >= 70 and <= 110 && b >= r && b - r <= 30;
    }

    public static byte[] Pixels(BitmapSource image, out int width, out int height)
    {
        var src = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Bgr32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
        width = src.PixelWidth;
        height = src.PixelHeight;
        var px = new byte[width * height * 4];
        src.CopyPixels(px, width * 4, 0);
        return px;
    }

    /// <summary>Zone d'image en niveaux de gris, réduite de moitié (pour mesurer le glissement entre deux images).</summary>
    public static byte[] Grayscale(byte[] px, int w, Int32Rect r, out int gw, out int gh)
    {
        gw = r.Width / 2;
        gh = r.Height / 2;
        var g = new byte[gw * gh];
        for (var y = 0; y < gh; y++)
            for (var x = 0; x < gw; x++)
            {
                var i = ((r.Y + y * 2) * w + r.X + x * 2) * 4;
                g[y * gw + x] = (byte)((px[i] * 11 + px[i + 1] * 59 + px[i + 2] * 30) / 100);
            }
        return g;
    }
}
