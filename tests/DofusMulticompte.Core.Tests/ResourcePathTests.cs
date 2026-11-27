using DofusMulticompte.Core.ResourcePaths;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.Core.Tests;

/// <summary>Fausse capture de chemin : fond uni, quadrillage sombre, tracé rouge avec flèches aux coins.</summary>
internal sealed class FakePathImage
{
    // Assez grand pour que des pas de 2 cases (128 x 80) entrent dans la plage de recherche du quadrillage.
    public const int W = 1100, H = 700;
    public const double CellW = 64, CellH = 40, OffX = 10, OffY = 6;
    public readonly byte[] Px = new byte[W * H * 4];

    public FakePathImage()
    {
        var rng = new Random(42);
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var n = rng.Next(-8, 9); // un peu de texture, comme la carte du jeu
                Set(x, y, (byte)(185 + n), (byte)(178 + n), (byte)(95 + n));
            }
        for (var x = OffX; x < W; x += CellW)
            for (var y = 0; y < H; y++) Darken((int)x, y);
        for (var y = OffY; y < H; y += CellH)
            for (var x = 0; x < W; x++) Darken(x, (int)y);
    }

    public (double X, double Y) Center(int col, int row, double dx = 0) => (OffX + (col + 0.5) * CellW + dx, OffY + (row + 0.5) * CellH);

    /// <summary>Trace une ligne brisée (traits horizontaux/verticaux) avec une pointe de flèche à chaque sommet.</summary>
    public void DrawPath(params (double X, double Y)[] pts)
    {
        for (var i = 1; i < pts.Length; i++)
        {
            Line(pts[i - 1], pts[i]);
            Arrow(pts[i - 1], pts[i]);
        }
    }

    private void Line((double X, double Y) a, (double X, double Y) b)
    {
        var n = (int)Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        for (var k = 0; k <= n; k++)
        {
            var x = a.X + (b.X - a.X) * k / Math.Max(1, n);
            var y = a.Y + (b.Y - a.Y) * k / Math.Max(1, n);
            for (var t = -2; t <= 1; t++)
                if (a.X == b.X) Red((int)x + t, (int)y); else Red((int)x, (int)y + t);
        }
    }

    /// <summary>Triangle plein dont la pointe est sur <paramref name="tip"/>, orienté de <paramref name="from"/> vers la pointe.</summary>
    private void Arrow((double X, double Y) from, (double X, double Y) tip)
    {
        double ux = Math.Sign(tip.X - from.X), uy = Math.Sign(tip.Y - from.Y);
        for (var t = 0; t <= 12; t++)
            for (var l = -t * 0.7; l <= t * 0.7; l++)
                Red((int)(tip.X - ux * t + uy * l), (int)(tip.Y - uy * t + ux * l));
    }

    private void Set(int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        var i = (y * W + x) * 4;
        Px[i] = b; Px[i + 1] = g; Px[i + 2] = r; Px[i + 3] = 255;
    }

    private void Darken(int x, int y)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        var i = (y * W + x) * 4;
        for (var c = 0; c < 3; c++) Px[i + c] = (byte)Math.Max(0, Px[i + c] - 45);
    }

    private void Red(int x, int y) => Set(x, y, 230, 40, 35);
}

public class PathImageAnalyzerTests
{
    // Chemin : (1,1) → droite jusqu'à (5,1) → bas jusqu'à (5,4) → gauche jusqu'à (3,4)
    // → bas jusqu'à (3,6) puis remontée dans la même colonne par un trait décalé jusqu'à (3,8)… :
    // ici, descente (2,6)->(2,9) et remontée (2,9)->(2,7) avec deux traits décalés.
    private static FakePathImage Sample(bool reversed = false)
    {
        var img = new FakePathImage();
        var pts = new List<(double, double)>
        {
            img.Center(1, 1), img.Center(5, 1), img.Center(5, 4), img.Center(2, 4, dx: -12),
            img.Center(2, 9, dx: -12), img.Center(2, 9, dx: 14), img.Center(2, 7, dx: 14), img.Center(7, 7),
        };
        if (reversed) pts.Reverse();
        img.DrawPath(pts.ToArray());
        return img;
    }

    private static readonly GridCell[] Expected =
    [
        new(1, 1), new(2, 1), new(3, 1), new(4, 1), new(5, 1),
        new(5, 2), new(5, 3), new(5, 4),
        new(4, 4), new(3, 4), new(2, 4),
        new(2, 5), new(2, 6), new(2, 7), new(2, 8), new(2, 9),
        new(2, 8), new(2, 7), // remontée dans la même colonne : cases revisitées
        new(3, 7), new(4, 7), new(5, 7), new(6, 7), new(7, 7),
    ];

    [Fact]
    public void Quadrillage_RetrouveTailleEtDecalage()
    {
        var img = Sample();
        var g = PathImageAnalyzer.DetectGrid(img.Px, FakePathImage.W, FakePathImage.H, FakePathImage.W * 4);

        Assert.NotNull(g);
        Assert.InRange(g.Value.CellWidth, 63.5, 64.5);
        Assert.InRange(g.Value.CellHeight, 39.5, 40.5);
    }

    [Fact]
    public void Chemin_SuiteDeCasesDansLOrdre_AvecCasesRevisitees()
    {
        var img = Sample();
        var r = PathImageAnalyzer.Analyze(img.Px, FakePathImage.W, FakePathImage.H, FakePathImage.W * 4);

        Assert.NotNull(r);
        Assert.True(r.IsComplete, $"segments utilisés {r.SegmentsUsed}/{r.SegmentsFound}");
        Assert.Equal(Expected, r.Cells);
    }

    [Fact]
    public void Chemin_SensDonneParLesFleches_MemeDessineALEnvers()
    {
        // Même tracé, mais dessiné de la fin vers le début : les flèches pointent donc vers (1,1).
        var img = Sample(reversed: true);
        var r = PathImageAnalyzer.Analyze(img.Px, FakePathImage.W, FakePathImage.H, FakePathImage.W * 4);

        Assert.NotNull(r);
        Assert.Equal(Expected.Reverse(), r.Cells);
    }

    [Fact]
    public void ImageSansTrace_Null()
    {
        var img = new FakePathImage();
        Assert.Null(PathImageAnalyzer.Analyze(img.Px, FakePathImage.W, FakePathImage.H, FakePathImage.W * 4));
    }
}

public class ResourcePathModelTests
{
    [Fact]
    public void ToWorld_DecaleSelonLaCaseRepere()
    {
        // Repère relevé sur une vraie capture : la case (6,13) est le zaap d'Astrub, en 5,-18.
        var world = ResourcePath.ToWorld([new GridCell(6, 13), new GridCell(6, 14), new GridCell(7, 15)], new GridCell(6, 13), new MapPosition(5, -18));
        Assert.Equal([new MapPosition(5, -18), new MapPosition(5, -17), new MapPosition(6, -16)], world);
    }

    [Fact]
    public void Store_EnregistreEtRelit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dmc-chemins-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ResourcePathStore(dir);
            store.Save(new ResourcePath { Name = "Frêne / Astrub", Steps = [new(5, -18), new(5, -17)] });

            var all = store.LoadAll();
            Assert.Single(all);
            Assert.Equal("Frêne / Astrub", all[0].Name);
            Assert.Equal([new MapPosition(5, -18), new MapPosition(5, -17)], all[0].Steps);

            store.Delete("Frêne / Astrub");
            Assert.Empty(store.LoadAll());
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

public class PathProgressTests
{
    // Aller-retour dans une colonne : (8,-10) apparaît aux étapes 1 et 3.
    private static readonly MapPosition[] Steps =
    [
        new(9, -10), new(8, -10), new(8, -9), new(8, -10), new(8, -11), new(7, -11),
    ];

    [Fact]
    public void Avance_EtapeParEtape_EtGereLesCasesRevisitees()
    {
        var p = new PathProgress(Steps);
        p.Update(new(9, -10));
        Assert.Equal((0, PathStatus.OnPath), (p.Index, p.Status));
        Assert.Equal(new MapPosition(8, -10), p.Target);

        p.Update(new(8, -10));
        Assert.Equal(1, p.Index);
        p.Update(new(8, -9));
        Assert.Equal(2, p.Index);

        // Retour sur (8,-10) : c'est l'étape 3 (prochaine occurrence), pas un retour à l'étape 1.
        p.Update(new(8, -10));
        Assert.Equal(3, p.Index);
        Assert.Equal(new MapPosition(8, -11), p.Target);
    }

    [Fact]
    public void HorsChemin_ViseLEtapeEnCours_PuisReprend()
    {
        var p = new PathProgress(Steps);
        p.Update(new(9, -10));
        p.Update(new(20, 20));
        Assert.Equal(PathStatus.OffPath, p.Status);
        Assert.Equal(new MapPosition(9, -10), p.Target);

        p.Update(new(8, -10));
        Assert.Equal((1, PathStatus.OnPath), (p.Index, p.Status));
    }

    [Fact]
    public void EtapesSautees_EtFin()
    {
        var p = new PathProgress(Steps);
        p.Update(new(9, -10));
        p.Update(new(8, -11)); // a coupé : on avance directement
        Assert.Equal(4, p.Index);
        p.Update(new(7, -11));
        Assert.Equal(PathStatus.Finished, p.Status);
        Assert.Null(p.Target);
    }

    [Fact]
    public void PremiereLecture_HorsChemin_ViseLeDepart()
    {
        var p = new PathProgress(Steps);
        p.Update(new(0, 0));
        Assert.Equal((0, PathStatus.OffPath), (p.Index, p.Status));
        Assert.Equal(new MapPosition(9, -10), p.Target);
    }
}

public class MinimapShiftEstimatorTests
{
    private static byte[] Texture(int w, int h, int seed)
    {
        // Taches aléatoires lissées : assez de relief pour être reconnues une fois décalées.
        var rng = new Random(seed);
        var img = new byte[w * h];
        for (var k = 0; k < 120; k++)
        {
            int cx = rng.Next(w), cy = rng.Next(h), r = rng.Next(4, 14), v = rng.Next(40, 255);
            for (var y = Math.Max(0, cy - r); y < Math.Min(h, cy + r); y++)
                for (var x = Math.Max(0, cx - r); x < Math.Min(w, cx + r); x++)
                    img[y * w + x] = (byte)v;
        }
        return img;
    }

    private static byte[] Shift(byte[] src, int w, int h, int dx, int dy)
    {
        var dst = new byte[w * h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                int sx = x - dx, sy = y - dy;
                dst[y * w + x] = sx >= 0 && sy >= 0 && sx < w && sy < h ? src[sy * w + sx] : (byte)0;
            }
        return dst;
    }

    [Theory]
    [InlineData(-63, 0)]
    [InlineData(0, 45)]
    [InlineData(31, -22)]
    public void RetrouveLeDecalage(int dx, int dy)
    {
        const int w = 240, h = 180;
        var a = Texture(w, h, 7);
        var b = Shift(a, w, h, dx, dy);

        var r = MinimapShiftEstimator.Estimate(a, b, w, h, maxShift: 80);

        Assert.NotNull(r);
        Assert.Equal((dx, dy), (r.Value.Dx, r.Value.Dy));
        Assert.True(r.Value.Confidence > 0.5);
    }
}
