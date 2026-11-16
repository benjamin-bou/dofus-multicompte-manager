using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class PlayButtonDetectorTests
{
    private const int W = 400, H = 240;

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

    private static void Fill(byte[] px, int x0, int y0, int w, int h, (byte R, byte G, byte B) c)
    {
        for (var y = y0; y < y0 + h; y++)
            for (var x = x0; x < x0 + w; x++)
            {
                var i = (y * W + x) * 4;
                px[i] = c.B;
                px[i + 1] = c.G;
                px[i + 2] = c.R;
            }
    }

    // Couleurs relevées sur une vraie capture de l'écran de choix du personnage.
    private static readonly (byte, byte, byte) ButtonGreen = (140, 168, 2);
    private static readonly (byte, byte, byte) Plateau = (199, 156, 136);
    private static readonly (byte, byte, byte) Aura = (53, 119, 133);
    private static readonly (byte, byte, byte) Ciel = (77, 71, 102);

    [Fact]
    public void Find_BoutonSurFondDeDecor_RenvoieSonCentre()
    {
        var px = Image(Ciel);
        Fill(px, 220, 150, 150, 40, Plateau);
        Fill(px, 100, 100, 60, 30, Aura);
        Fill(px, 244, 182, 32, 16, ButtonGreen); // bouton centré en (260, 190)

        var p = PlayButtonDetector.Find(px, W, H, W * 4);

        Assert.NotNull(p);
        Assert.InRange(p.Value.X, 0.64, 0.66);
        Assert.InRange(p.Value.Y, 0.78, 0.80);
    }

    [Fact]
    public void Find_EcranDAccueil_BoutonAuCentreSousUnLogoVert()
    {
        var px = Image(Ciel);
        // Logo vert irrégulier, haut sur l'écran, en partie dans la zone de recherche.
        Fill(px, 170, 20, 60, 50, ButtonGreen);
        Fill(px, 185, 70, 30, 12, ButtonGreen);
        // Bouton « Jouer » au centre, un peu au-dessus de la mi-hauteur : centré en (200, 115).
        Fill(px, 176, 107, 48, 16, ButtonGreen);

        var p = PlayButtonDetector.Find(px, W, H, W * 4);

        Assert.NotNull(p);
        Assert.InRange(p.Value.X, 0.49, 0.51);
        Assert.InRange(p.Value.Y, 0.47, 0.49);
    }

    [Fact]
    public void Find_SansBouton_RenvoieNull()
    {
        var px = Image(Ciel);
        Fill(px, 220, 150, 150, 40, Plateau);
        Assert.Null(PlayButtonDetector.Find(px, W, H, W * 4));
    }

    [Fact]
    public void Find_VertEtaleSurTouteLaFenetre_RenvoieNull()
    {
        // Une prairie en jeu a des teintes proches : une zone verte étalée n'est pas un bouton.
        var px = Image((150, 180, 30));
        Assert.Null(PlayButtonDetector.Find(px, W, H, W * 4));
    }

    [Fact]
    public void Find_PetiteTacheVerte_RenvoieNull()
    {
        var px = Image(Ciel);
        Fill(px, 250, 190, 4, 4, ButtonGreen);
        Assert.Null(PlayButtonDetector.Find(px, W, H, W * 4));
    }

    [Fact]
    public void Find_BufferInvalide_RenvoieNull() =>
        Assert.Null(PlayButtonDetector.Find(new byte[10], W, H, W * 4));

    [Theory]
    [InlineData("Dofus 3.7.13.17 - Release", true)]
    [InlineData("Dofus 3.8.0.1 - Beta", true)]
    [InlineData("Dofus", false)]
    [InlineData("Tikoko - Zobal - 3.7.13.17 - Release", false)]
    public void IsPreGameTitle(string title, bool expected) =>
        Assert.Equal(expected, WindowTitleParser.IsPreGameTitle(title));
}
