namespace DofusMulticompte.Core;

/// <summary>Position relative (0..1) dans la zone client d'une fenêtre.</summary>
public readonly record struct RelativePoint(double X, double Y);

/// <summary>
/// Repère un bouton vert « Jouer » de Dofus 3 dans une capture de la fenêtre (pixels BGRA ou BGR32,
/// 4 octets par pixel). Deux écrans en ont un : l'accueil (bouton au centre, sous le logo vert)
/// et le choix du personnage (bouton en bas, sous le personnage).
/// <para>
/// Couleurs mesurées sur le client : vert-jaune (R ≈ 125-165, G ≈ 150-190, B ≈ 0-25). On isole chaque
/// zone de cette couleur et on garde celle qui a la forme d'un bouton : le logo, plus grand et irrégulier,
/// ou une prairie étalée sur tout l'écran, sont écartés.
/// </para>
/// </summary>
public static class PlayButtonDetector
{
    // Mesures sur les deux écrans réels (capture réduite à 480 px) : le bouton fait 11 % x 7,5-8 % de la
    // fenêtre, proportions 2,4-2,5, remplissage 69-71 % (le texte blanc « JOUER » laisse des trous).
    // Fausses zones vues en jeu (prairies) : proportions 1,7-2,0, remplissage 39-44 %. Seuils avec marge.
    private const double SearchTopRatio = 0.25;
    private const double MinAreaRatio = 0.003;
    private const double MinWidthRatio = 0.06;
    private const double MaxWidthRatio = 0.20;
    private const double MinHeightRatio = 0.04;
    private const double MaxHeightRatio = 0.12;
    private const double MinAspect = 1.9;
    private const double MaxAspect = 3.3;
    private const double MinFill = 0.55;

    public static bool IsButtonColor(byte r, byte g, byte b) =>
        g >= 120 && b <= 45 && g - b >= 100 && g >= r + 15 && r >= 60;

    /// <summary>Zone connexe de couleur « bouton » : taille relative, proportions et remplissage.</summary>
    public readonly record struct Candidate(RelativePoint Center, double WidthRatio, double HeightRatio, double Aspect, double Fill, double AreaRatio);

    /// <summary>Toutes les zones de couleur « bouton » assez grandes, pour le diagnostic et le réglage des seuils.</summary>
    public static List<Candidate> FindCandidates(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        var list = new List<Candidate>();
        Scan(pixels, width, height, stride, c => { if (c.AreaRatio >= MinAreaRatio) list.Add(c); });
        return list;
    }

    /// <returns>Le centre du bouton, ou null s'il n'est pas trouvé.</returns>
    public static RelativePoint? Find(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        Candidate? best = null;
        Scan(pixels, width, height, stride, c =>
        {
            // Le bouton est sous le logo (accueil) ou sous le personnage (choix) : on garde le plus bas.
            if (LooksLikeButton(c) && (best is null || c.Center.Y > best.Value.Center.Y)) best = c;
        });
        return best?.Center;
    }

    private static bool LooksLikeButton(Candidate c) =>
        c.AreaRatio >= MinAreaRatio &&
        c.WidthRatio >= MinWidthRatio && c.WidthRatio <= MaxWidthRatio &&
        c.HeightRatio >= MinHeightRatio && c.HeightRatio <= MaxHeightRatio &&
        c.Aspect >= MinAspect && c.Aspect <= MaxAspect && c.Fill >= MinFill;

    private static void Scan(ReadOnlySpan<byte> pixels, int width, int height, int stride, Action<Candidate> onComponent)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || pixels.Length < stride * height) return;

        var top = (int)(height * SearchTopRatio);
        var mask = new bool[width * height];
        for (var y = top; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                mask[y * width + x] = IsButtonColor(row[i + 2], row[i + 1], row[i]);
            }
        }

        var visited = new bool[width * height];
        var stack = new Stack<int>();

        for (var start = top * width; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start]) continue;

            // Parcours de la zone connexe (4 voisins).
            long count = 0, sumX = 0, sumY = 0;
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                int x = p % width, y = p / width;
                count++;
                sumX += x;
                sumY += y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                if (x > 0) Visit(p - 1);
                if (x < width - 1) Visit(p + 1);
                if (y > top) Visit(p - width);
                if (y < height - 1) Visit(p + width);
            }

            int w = maxX - minX + 1, h = maxY - minY + 1;
            onComponent(new Candidate(
                new RelativePoint((sumX / (double)count + 0.5) / width, (sumY / (double)count + 0.5) / height),
                w / (double)width,
                h / (double)height,
                w / (double)h,
                count / (double)(w * h),
                count / (double)(width * height)));
        }

        void Visit(int q)
        {
            if (!mask[q] || visited[q]) return;
            visited[q] = true;
            stack.Push(q);
        }
    }
}
