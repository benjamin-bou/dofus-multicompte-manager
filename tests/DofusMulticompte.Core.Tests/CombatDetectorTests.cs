using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class CombatDetectorTests
{
    // Proportions d'une capture réduite à 480 px de large d'une fenêtre 2048 × 1350.
    private const int W = 480, H = 316;

    private static readonly (byte R, byte G, byte B) Lime = (180, 205, 20);
    private static readonly (byte R, byte G, byte B) Floor = (120, 95, 70);

    private static byte[] Image((byte R, byte G, byte B) background)
    {
        var px = new byte[W * H * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = background.B;
            px[i + 1] = background.G;
            px[i + 2] = background.R;
            px[i + 3] = 255;
        }
        return px;
    }

    /// <summary>
    /// Remplit un rectangle. Avec <paramref name="holeEvery"/>, laisse un pixel sur <c>holeEvery</c> en colonnes et
    /// une ligne sur deux : des trous isolés, comme le texte d'un bouton, qui ne coupent pas la zone en morceaux.
    /// </summary>
    private static void Fill(byte[] px, int x0, int y0, int w, int h, (byte R, byte G, byte B) c, int holeEvery = 0)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                if (holeEvery > 0 && x % holeEvery == 0 && y % 2 == 0) continue;
                var i = (y * W + x) * 4;
                px[i] = c.B;
                px[i + 1] = c.G;
                px[i + 2] = c.R;
            }
    }

    [Fact]
    public void BoutonFinDeTourEnBasADroite_EnCombat()
    {
        var px = Image(Floor);
        // 10,6 % × 4,1 % centré en (0,88 ; 0,86), avec des trous pour le texte « FIN DE TOUR ».
        Fill(px, 397, 265, 51, 13, Lime, holeEvery: 7);
        Assert.True(CombatDetector.IsInCombat(px, W, H, W * 4));
    }

    [Fact]
    public void MemeBoutonAuCentre_PasEnCombat()
    {
        // Une fenêtre surgissante au centre (par exemple une demande d'échange) a aussi un bouton vert.
        var px = Image(Floor);
        Fill(px, 215, 150, 51, 13, Lime);
        Assert.False(CombatDetector.IsInCombat(px, W, H, W * 4));
    }

    [Fact]
    public void PrairieIrreguliere_PasEnCombat()
    {
        var px = Image(Floor);
        // Grande zone vert-jaune mal remplie, comme l'herbe d'une carte.
        Fill(px, 300, 200, 160, 100, Lime, holeEvery: 2);
        Fill(px, 380, 250, 80, 40, Lime, holeEvery: 3);
        Assert.False(CombatDetector.IsInCombat(px, W, H, W * 4));
    }

    [Fact]
    public void ImageSansVert_PasEnCombat() =>
        Assert.False(CombatDetector.IsInCombat(Image(Floor), W, H, W * 4));

    [Fact]
    public void BarreFineEnBas_PasEnCombat()
    {
        // Barre d'expérience : très allongée et peu haute.
        var px = Image(Floor);
        Fill(px, 360, 300, 110, 3, Lime);
        Assert.False(CombatDetector.IsInCombat(px, W, H, W * 4));
    }
}
