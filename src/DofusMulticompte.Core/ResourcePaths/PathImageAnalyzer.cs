namespace DofusMulticompte.Core.ResourcePaths;

/// <summary>Case du quadrillage d'une capture (colonne, ligne), indépendante des coordonnées du monde.</summary>
public readonly record struct GridCell(int Col, int Row);

/// <summary>Quadrillage d'une capture : taille d'une case et position de la première ligne, en pixels.</summary>
public readonly record struct GridGeometry(double CellWidth, double CellHeight, double OffsetX, double OffsetY)
{
    public GridCell CellAt(double x, double y) =>
        new((int)Math.Floor((x - OffsetX) / CellWidth), (int)Math.Floor((y - OffsetY) / CellHeight));

    public (double X, double Y) CenterOf(GridCell c) =>
        (OffsetX + (c.Col + 0.5) * CellWidth, OffsetY + (c.Row + 0.5) * CellHeight);
}

public sealed record PathExtraction(
    GridGeometry Grid,
    IReadOnlyList<GridCell> Cells,
    IReadOnlyList<(double X, double Y)> Polyline,
    int SegmentsUsed,
    int SegmentsFound,
    int DirectionVote)
{
    /// <summary>Tous les traits rouges détectés font partie du même chemin continu.</summary>
    public bool IsComplete => SegmentsFound > 0 && SegmentsUsed == SegmentsFound;
}

/// <summary>
/// Lit une capture de « chemin optimisé » : la carte du monde de Dofus avec son quadrillage (une case par
/// carte du jeu) et un tracé rouge fait de traits horizontaux et verticaux, avec des flèches aux coins.
/// <para>
/// 1. Le quadrillage est retrouvé par la périodicité des lignes fines, plus sombres que leurs voisines.<br/>
/// 2. Les traits rouges sont isolés en segments (les pointes de flèche, courtes, sont ignorées), puis les
///    morceaux coupés par une icône sont recollés.<br/>
/// 3. Les segments sont enchaînés coin par coin ; le sens est voté grâce aux pointes de flèche.<br/>
/// 4. On suit la ligne obtenue et on note chaque case traversée : une case peut l'être plusieurs fois
///    (aller-retour dans une même colonne, dessiné avec deux traits décalés).
/// </para>
/// Pixels attendus : BGRA ou BGR32, 4 octets par pixel.
/// </summary>
public static class PathImageAnalyzer
{
    private const int RunMinLength = 22;      // une pointe de flèche est plus courte
    private const int RunGap = 3;             // trous tolérés dans un trait (anticrénelage)
    private const int SegmentMinLength = 28;
    private const double MergeMaxGap = 30;    // morceau d'un même trait coupé par une icône
    private const double MergeMaxOffset = 6;
    private const double CornerMaxDistance = 26;

    public static bool IsPathRed(byte r, byte g, byte b) => r > 170 && g < 110 && b < 110 && r - g > 90;

    public static PathExtraction? Analyze(byte[] pixels, int width, int height, int stride)
    {
        if (width < 50 || height < 50 || pixels.Length < stride * height) return null;
        var img = new Image(pixels, width, height, stride);

        var grid = DetectGrid(img);
        if (grid is not { } g) return null;

        var segments = FindSegments(img);
        if (segments.Count == 0) return null;

        var (order, links) = Chain(segments);
        if (order.Count == 0) return null;

        var vote = DirectionVote(img, order);
        if (vote < 0)
        {
            // Le vote désigne l'autre bout comme départ : on reparcourt depuis la fin.
            var last = order[^1];
            var startFromEnd = new End(last.Seg, !last.EnterAtA);
            order = Walk(startFromEnd, links);
        }

        var polyline = BuildPolyline(order);
        var cells = CellsAlong(polyline, g);
        return new PathExtraction(g, cells, polyline, order.Count, segments.Count, Math.Abs(vote));
    }

    // ---------------------------------------------------------------- quadrillage

    /// <summary>Période et phase des lignes du quadrillage, horizontalement et verticalement.</summary>
    public static GridGeometry? DetectGrid(byte[] pixels, int width, int height, int stride) =>
        DetectGrid(new Image(pixels, width, height, stride));

    private static GridGeometry? DetectGrid(Image img)
    {
        var cols = Period(LineProfile(img, vertical: true));
        var rows = Period(LineProfile(img, vertical: false));
        if (cols is null || rows is null) return null;
        return new GridGeometry(cols.Value.Spacing, rows.Value.Spacing, cols.Value.Offset, rows.Value.Offset);
    }

    private static double[] LineProfile(Image img, bool vertical)
    {
        int n = vertical ? img.W : img.H, m = vertical ? img.H : img.W;
        var p = new double[n];
        for (var i = 2; i < n - 2; i++)
        {
            double s = 0;
            for (var j = 0; j < m; j += 2)
            {
                double c = vertical ? img.Lum(i, j) : img.Lum(j, i);
                double a = vertical ? img.Lum(i - 2, j) : img.Lum(j, i - 2);
                double b = vertical ? img.Lum(i + 2, j) : img.Lum(j, i + 2);
                s += Math.Max(0, (a + b) / 2 - c); // plus sombre que ses voisins : ligne fine
            }
            p[i] = s / (m / 2.0);
        }
        return p;
    }

    private static (double Spacing, double Offset)? Period(double[] p)
    {
        var mean = p.Average();
        if (mean <= 0) return null;
        var scores = new List<(double Lag, double Score)>();
        for (var lag = 15.0; lag <= Math.Min(160, p.Length / 4.0); lag += 0.25)
            scores.Add((lag, BestPhase(p, lag, step: 1).Score / mean));
        if (scores.Count == 0) return null;

        // Un multiple du vrai pas (2, 3 cases…) obtient un aussi bon score, voire meilleur, puisque ses lignes
        // tombent aussi sur le quadrillage. On teste donc les sous-multiples du meilleur pas et on garde le plus
        // petit qui reste proche (mesuré sur une vraie capture : pas réel 76,5 px → 4,93 ; double 153 px → 6,04).
        var (bestLag, best) = scores.MaxBy(s => s.Score);
        if (best < 2) return null; // pas de quadrillage net
        var chosen = bestLag;
        foreach (var k in new[] { 4, 3, 2 })
        {
            var near = scores.Where(s => Math.Abs(s.Lag - bestLag / k) <= 1.5).ToList();
            if (near.Count == 0) continue;
            var candidate = near.MaxBy(s => s.Score);
            if (candidate.Score >= best * 0.7) { chosen = candidate.Lag; break; }
        }
        return (chosen, BestPhase(p, chosen, step: 0.25).Phase);
    }

    private static (double Phase, double Score) BestPhase(double[] p, double lag, double step)
    {
        double bestPhase = 0, bestScore = -1;
        for (var ph = 0.0; ph < lag; ph += step)
        {
            double s = 0;
            var c = 0;
            for (var x = ph; x < p.Length - 1; x += lag) { s += p[(int)Math.Round(x)]; c++; }
            if (c > 0 && s / c > bestScore) { bestScore = s / c; bestPhase = ph; }
        }
        return (bestPhase, bestScore);
    }

    // ---------------------------------------------------------------- segments

    private sealed class Segment
    {
        public bool Vertical;
        public double PosSum;
        public int PosCount, PosMax, A, B;
        public double Pos => PosSum / PosCount;
        public (double X, double Y) PointA => Vertical ? (Pos, A) : (A, Pos);
        public (double X, double Y) PointB => Vertical ? (Pos, B) : (B, Pos);
    }

    private static List<Segment> FindSegments(Image img)
    {
        var segments = Runs(img, vertical: true).Concat(Runs(img, vertical: false)).ToList();

        // Doublons parallèles (épaisseur du trait) et morceaux alignés coupés par une icône.
        var changed = true;
        while (changed)
        {
            changed = false;
            for (var i = 0; i < segments.Count && !changed; i++)
                for (var j = i + 1; j < segments.Count && !changed; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    if (a.Vertical != b.Vertical || Math.Abs(a.Pos - b.Pos) > MergeMaxOffset) continue;
                    if (Math.Max(a.A, b.A) - Math.Min(a.B, b.B) > MergeMaxGap) continue;
                    a.PosSum += b.PosSum;
                    a.PosCount += b.PosCount;
                    a.PosMax = Math.Max(a.PosMax, b.PosMax);
                    a.A = Math.Min(a.A, b.A);
                    a.B = Math.Max(a.B, b.B);
                    segments.RemoveAt(j);
                    changed = true;
                }
        }
        return segments;
    }

    /// <summary>Suites de pixels rouges le long de chaque colonne (ou ligne), regroupées en segments.</summary>
    private static List<Segment> Runs(Image img, bool vertical)
    {
        int lines = vertical ? img.W : img.H, length = vertical ? img.H : img.W;
        var runs = new List<(int Pos, int A, int B)>();
        for (var pos = 0; pos < lines; pos++)
        {
            int start = -1, last = -1;
            for (var t = 0; t <= length; t++)
            {
                var red = t < length && (vertical ? img.Red(pos, t) : img.Red(t, pos));
                if (red)
                {
                    if (start < 0) start = t;
                    last = t;
                }
                else if (start >= 0 && t - last > RunGap)
                {
                    if (last - start >= RunMinLength) runs.Add((pos, start, last));
                    start = -1;
                }
            }
        }

        var segments = new List<Segment>();
        foreach (var r in runs)
        {
            var s = segments.LastOrDefault(s => r.Pos - s.PosMax <= 2 && r.A <= s.B + 4 && r.B >= s.A - 4);
            if (s is null)
                segments.Add(new Segment { Vertical = vertical, PosSum = r.Pos, PosCount = 1, PosMax = r.Pos, A = r.A, B = r.B });
            else
            {
                s.PosSum += r.Pos;
                s.PosCount++;
                s.PosMax = r.Pos;
                s.A = Math.Min(s.A, r.A);
                s.B = Math.Max(s.B, r.B);
            }
        }
        return segments.Where(s => s.B - s.A >= SegmentMinLength).ToList();
    }

    // ---------------------------------------------------------------- chaînage

    /// <summary>Extrémité d'un segment ; <c>EnterAtA</c> vrai si le parcours entre par l'extrémité A.</summary>
    private sealed record End(Segment Seg, bool EnterAtA)
    {
        public (double X, double Y) Entry => EnterAtA ? Seg.PointA : Seg.PointB;
        public (double X, double Y) Exit => EnterAtA ? Seg.PointB : Seg.PointA;
    }

    private static (List<End> Order, Dictionary<(Segment, bool), End> Links) Chain(List<Segment> segments)
    {
        // Coins : chaque extrémité est reliée à l'extrémité la plus proche d'un segment perpendiculaire.
        var ends = segments.SelectMany(s => new[] { (s, true), (s, false) }).ToList();
        var pairs = new List<(double D, (Segment S, bool IsA) A, (Segment S, bool IsA) B)>();
        for (var i = 0; i < ends.Count; i++)
            for (var j = i + 1; j < ends.Count; j++)
            {
                var a = ends[i];
                var b = ends[j];
                if (a.s == b.s || a.s.Vertical == b.s.Vertical) continue;
                var d = Dist(Point(a), Point(b));
                if (d < CornerMaxDistance) pairs.Add((d, a, b));
            }

        // Clé : (segment, extrémité A ?) -> extrémité du segment suivant par laquelle on entre.
        var links = new Dictionary<(Segment, bool), End>();
        foreach (var (_, a, b) in pairs.OrderBy(p => p.D))
        {
            if (links.ContainsKey(a) || links.ContainsKey(b)) continue;
            links[a] = new End(b.S, b.IsA);
            links[b] = new End(a.S, a.IsA);
        }

        // Départ : une extrémité libre (le vote de sens corrigera si besoin).
        var free = ends.Where(e => !links.ContainsKey(e)).ToList();
        if (free.Count == 0) return ([], links);
        var start = free.First();
        var order = Walk(new End(start.s, start.Item2), links);

        // Plusieurs morceaux disjoints : on garde le plus long.
        foreach (var other in free.Skip(1))
        {
            var candidate = Walk(new End(other.s, other.Item2), links);
            if (candidate.Count > order.Count) order = candidate;
        }
        return (order, links);

        static (double X, double Y) Point((Segment S, bool IsA) e) => e.IsA ? e.S.PointA : e.S.PointB;
    }

    private static List<End> Walk(End start, Dictionary<(Segment, bool), End> links)
    {
        var order = new List<End>();
        var seen = new HashSet<Segment>();
        var cur = start;
        while (seen.Add(cur.Seg))
        {
            order.Add(cur);
            // On sort par l'autre extrémité du segment.
            if (!links.TryGetValue((cur.Seg, !cur.EnterAtA), out var next)) break;
            cur = next;
        }
        return order;
    }

    /// <summary>
    /// Positif si le parcours suit les flèches. Une pointe déborde des deux côtés du trait juste avant son
    /// extrémité ; au coin, le trait suivant ne part que d'un côté : on compte donc le plus petit des deux côtés.
    /// </summary>
    private static int DirectionVote(Image img, List<End> order)
    {
        var vote = 0;
        foreach (var e in order)
        {
            var arrive = e.Exit;
            var leave = e.Entry;
            var dir = e.Seg.Vertical ? Math.Sign(arrive.Y - leave.Y) : Math.Sign(arrive.X - leave.X);
            vote += Math.Sign(Barbs(img, arrive, e.Seg.Vertical, dir) - Barbs(img, leave, e.Seg.Vertical, -dir));
        }
        return vote;
    }

    private static int Barbs(Image img, (double X, double Y) tip, bool vertical, int dirAlong)
    {
        int side1 = 0, side2 = 0;
        for (var t = 2; t <= 16; t++)
            for (var l = 4; l <= 11; l++)
            {
                if (vertical)
                {
                    var y = (int)(tip.Y - dirAlong * t);
                    if (img.Red((int)tip.X - l, y)) side1++;
                    if (img.Red((int)tip.X + l, y)) side2++;
                }
                else
                {
                    var x = (int)(tip.X - dirAlong * t);
                    if (img.Red(x, (int)tip.Y - l)) side1++;
                    if (img.Red(x, (int)tip.Y + l)) side2++;
                }
            }
        return Math.Min(side1, side2);
    }

    // ---------------------------------------------------------------- cases

    private static List<(double X, double Y)> BuildPolyline(List<End> order)
    {
        var poly = new List<(double X, double Y)> { order[0].Entry };
        for (var i = 0; i < order.Count; i++)
        {
            var exit = order[i].Exit;
            // Coin : milieu entre la sortie de ce segment et l'entrée du suivant.
            poly.Add(i + 1 < order.Count ? Mid(exit, order[i + 1].Entry) : exit);
        }
        return poly;
    }

    private static List<GridCell> CellsAlong(List<(double X, double Y)> poly, GridGeometry grid)
    {
        var cells = new List<GridCell>();
        for (var i = 1; i < poly.Count; i++)
        {
            var a = poly[i - 1];
            var b = poly[i];
            var n = Math.Max(1, (int)(Dist(a, b) / 2));
            for (var k = 0; k <= n; k++)
            {
                var c = grid.CellAt(a.X + (b.X - a.X) * k / n, a.Y + (b.Y - a.Y) * k / n);
                if (cells.Count == 0 || cells[^1] != c) cells.Add(c);
            }
        }
        return cells;
    }

    private static double Dist((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static (double X, double Y) Mid((double X, double Y) a, (double X, double Y) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private readonly struct Image(byte[] px, int w, int h, int stride)
    {
        public int W => w;
        public int H => h;

        public double Lum(int x, int y)
        {
            var i = y * stride + x * 4;
            return 0.11 * px[i] + 0.59 * px[i + 1] + 0.3 * px[i + 2];
        }

        public bool Red(int x, int y)
        {
            if (x < 0 || y < 0 || x >= w || y >= h) return false;
            var i = y * stride + x * 4;
            return IsPathRed(px[i + 2], px[i + 1], px[i]);
        }
    }
}
