namespace DofusMulticompte.Core;

/// <summary>
/// Reconnaît qu'une fenêtre Dofus 3 est en combat grâce au bouton vert « FIN DE TOUR » (ou « PRÊT »
/// en phase de placement), en bas à droite de l'interface. Pixels BGRA ou BGR32, 4 octets par pixel.
/// <para>
/// Mesures sur une vraie capture en combat (réduite à 480 px) : centre (0,88 ; 0,86), 10,6 % × 4,1 %
/// de la fenêtre, proportions 3,9, remplissage 80 %. Hors combat, les zones de même couleur (prairies)
/// sont irrégulières : remplissage 30-45 %, proportions 0,7-3,3. Seuils avec marge.
/// </para>
/// </summary>
public static class CombatDetector
{
    private const double MinCenterX = 0.60;
    private const double MinCenterY = 0.60;
    private const double MinAreaRatio = 0.001;
    private const double MinWidthRatio = 0.05;
    private const double MaxWidthRatio = 0.18;
    private const double MinHeightRatio = 0.02;
    private const double MaxHeightRatio = 0.08;
    private const double MinAspect = 2.6;
    private const double MaxAspect = 5.5;
    private const double MinFill = 0.65;

    public static bool IsInCombat(ReadOnlySpan<byte> pixels, int width, int height, int stride) =>
        PlayButtonDetector.FindCandidates(pixels, width, height, stride, MinAreaRatio).Any(LooksLikeEndTurnButton);

    public static bool LooksLikeEndTurnButton(PlayButtonDetector.Candidate c) =>
        c.Center.X >= MinCenterX && c.Center.Y >= MinCenterY &&
        c.WidthRatio >= MinWidthRatio && c.WidthRatio <= MaxWidthRatio &&
        c.HeightRatio >= MinHeightRatio && c.HeightRatio <= MaxHeightRatio &&
        c.Aspect >= MinAspect && c.Aspect <= MaxAspect && c.Fill >= MinFill;
}
