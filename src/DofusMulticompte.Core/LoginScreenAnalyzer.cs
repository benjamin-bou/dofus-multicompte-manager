namespace DofusMulticompte.Core;

public enum LoginScreenState
{
    /// <summary>Écran non reconnu (autre menu, fenêtre surgissante…) : on ne fait rien.</summary>
    Unknown,
    /// <summary>Choix du personnage avec le bouton « Jouer » visible.</summary>
    PlayButton,
    /// <summary>Fond de nuages seul, sans liste de personnages ni bouton : chargement en cours ou bloqué.</summary>
    LoadingClouds,
    /// <summary>
    /// Écran noir avec quelques lignes de texte en haut à gauche (journal de démarrage du client,
    /// par exemple « Error: Unable to complete SSL connection ») : démarrage en cours ou bloqué.
    /// </summary>
    BlackScreen,
    /// <summary>La fenêtre ne répond plus (client gelé) : aucune image n'a pu être capturée.</summary>
    NotResponding,
}

public readonly record struct LoginScreenAnalysis(LoginScreenState State, RelativePoint? PlayButton);

/// <summary>
/// Analyse une capture de l'écran de connexion de Dofus 3 (pixels BGRA ou BGR32, 4 octets par pixel).
/// Seuils mesurés sur de vraies captures, réduites à 480 px de large :
/// <list type="bullet">
/// <item>zone du panneau des personnages : 94 % de pixels sombres bleutés quand la liste est affichée, 0 % quand le client est bloqué ;</item>
/// <item>ciel (haut droit) : 99-100 % de pixels bleus, violets ou turquoise sur l'écran de connexion, 6 % en jeu.</item>
/// </list>
/// </summary>
public static class LoginScreenAnalyzer
{
    private const double MaxPanelRatioWhenEmpty = 0.20;
    private const double MinSkyRatio = 0.85;
    private const double MinBlackRatio = 0.95;

    /// <summary>Fond de l'écran noir de démarrage (dégradé de 0 à ~25 sur chaque canal).</summary>
    private static bool IsNearBlack(byte r, byte g, byte b) => r < 40 && g < 40 && b < 45;

    public static LoginScreenAnalysis Analyze(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || pixels.Length < stride * height)
            return new(LoginScreenState.Unknown, null);

        if (PlayButtonDetector.Find(pixels, width, height, stride) is { } button)
            return new(LoginScreenState.PlayButton, button);

        // Le texte du journal n'occupe que quelques lignes : le reste de l'écran est quasi noir.
        if (Ratio(pixels, width, height, stride, 0.0, 1.0, 0.0, 1.0, IsNearBlack) >= MinBlackRatio)
            return new(LoginScreenState.BlackScreen, null);

        var panel = Ratio(pixels, width, height, stride, 0.08, 0.32, 0.35, 0.70, IsPanelColor);
        var sky = Ratio(pixels, width, height, stride, 0.40, 0.90, 0.10, 0.50, IsSkyColor);

        return panel < MaxPanelRatioWhenEmpty && sky >= MinSkyRatio
            ? new(LoginScreenState.LoadingClouds, null)
            : new(LoginScreenState.Unknown, null);
    }

    /// <summary>Fond bleu nuit uni du panneau de la liste des personnages.</summary>
    private static bool IsPanelColor(byte r, byte g, byte b) => r < 60 && g < 60 && b < 100 && b > r;

    /// <summary>Ciel étoilé et nuages : dominante bleue, violette ou turquoise.</summary>
    private static bool IsSkyColor(byte r, byte g, byte b) => b >= r && b + 10 >= g;

    private static double Ratio(ReadOnlySpan<byte> pixels, int width, int height, int stride,
        double x0, double x1, double y0, double y1, Func<byte, byte, byte, bool> match)
    {
        int xa = (int)(width * x0), xb = (int)(width * x1), ya = (int)(height * y0), yb = (int)(height * y1);
        long total = 0, hits = 0;
        for (var y = ya; y < yb; y++)
            for (var x = xa; x < xb; x++)
            {
                var i = y * stride + x * 4;
                total++;
                if (match(pixels[i + 2], pixels[i + 1], pixels[i])) hits++;
            }
        return total == 0 ? 0 : hits / (double)total;
    }
}
