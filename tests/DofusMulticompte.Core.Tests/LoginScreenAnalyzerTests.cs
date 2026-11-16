using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class LoginScreenAnalyzerTests
{
    private const int W = 480, H = 280;

    private static byte[] Image((byte R, byte G, byte B) background)
    {
        var px = new byte[W * H * 4];
        Fill(px, 0, 0, W, H, background);
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
                px[i + 3] = 255;
            }
    }

    // Teintes relevées sur de vraies captures du client.
    private static readonly (byte, byte, byte) Nuages = (77, 71, 102);
    private static readonly (byte, byte, byte) NuageTurquoise = (53, 119, 133);
    private static readonly (byte, byte, byte) Panneau = (26, 28, 48);
    private static readonly (byte, byte, byte) BoutonJouer = (140, 168, 2);
    private static readonly (byte, byte, byte) Herbe = (150, 175, 40);

    [Fact]
    public void NuagesSeuls_ChargementBloque()
    {
        var px = Image(Nuages);
        Fill(px, 0, 150, W, 130, NuageTurquoise);

        Assert.Equal(LoginScreenState.LoadingClouds, LoginScreenAnalyzer.Analyze(px, W, H, W * 4).State);
    }

    [Fact]
    public void ListeDesPersonnagesAffichee_PasBloque()
    {
        var px = Image(Nuages);
        Fill(px, 24, 70, 140, 160, Panneau); // panneau à gauche, encore sans bouton

        Assert.Equal(LoginScreenState.Unknown, LoginScreenAnalyzer.Analyze(px, W, H, W * 4).State);
    }

    [Fact]
    public void BoutonJouerVisible_RenvoieSaPosition()
    {
        var px = Image(Nuages);
        Fill(px, 24, 70, 140, 160, Panneau);
        Fill(px, 292, 215, 40, 20, BoutonJouer);

        var a = LoginScreenAnalyzer.Analyze(px, W, H, W * 4);

        Assert.Equal(LoginScreenState.PlayButton, a.State);
        Assert.NotNull(a.PlayButton);
    }

    [Fact]
    public void ScenesEnJeu_JamaisConsidereesCommeBloquees()
    {
        // Prairie : ni ciel bleuté, ni panneau.
        Assert.NotEqual(LoginScreenState.LoadingClouds, LoginScreenAnalyzer.Analyze(Image(Herbe), W, H, W * 4).State);
    }

    [Fact]
    public void EcranNoirAvecQuelquesLignesDeTexte_EcranNoir()
    {
        var px = Image((5, 8, 12));
        Fill(px, 0, 220, W, 60, (0, 14, 20)); // dégradé bleuté en bas
        for (var line = 0; line < 3; line++) Fill(px, 4, 2 + line * 6, 100, 3, (220, 220, 220)); // texte en haut à gauche

        Assert.Equal(LoginScreenState.BlackScreen, LoginScreenAnalyzer.Analyze(px, W, H, W * 4).State);
    }

    [Fact]
    public void ScenesSombresAvecInterface_PasEcranNoir()
    {
        // Une carte de nuit avec l'interface du jeu reste bien au-dessus du seuil de pixels clairs.
        var px = Image((20, 22, 30));
        Fill(px, 0, 200, W, 80, (90, 80, 70));

        Assert.NotEqual(LoginScreenState.BlackScreen, LoginScreenAnalyzer.Analyze(px, W, H, W * 4).State);
    }

    [Fact]
    public void BufferInvalide_Inconnu() =>
        Assert.Equal(LoginScreenState.Unknown, LoginScreenAnalyzer.Analyze(new byte[4], W, H, W * 4).State);
}
