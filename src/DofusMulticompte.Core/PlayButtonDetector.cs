namespace DofusMulticompte.Core;

/// <summary>Position relative (0..1) dans la zone client d'une fenêtre.</summary>
public readonly record struct RelativePoint(double X, double Y);

/// <summary>
/// Repère le bouton vert « Jouer » de l'écran de choix du personnage de Dofus 3 dans une capture
/// de la fenêtre (pixels BGRA ou BGR32, 4 octets par pixel). Couleurs mesurées sur le client :
/// vert-jaune (R ≈ 125-165, G ≈ 150-190, B ≈ 0-25), alors que le décor est beige, bleu ou violet.
/// </summary>
public static class PlayButtonDetector
{
    // Le bouton occupe environ 8 % x 7 % de la fenêtre.
    private const double MinAreaRatio = 0.0025;
    private const double MaxWidthRatio = 0.25;
    private const double MaxHeightRatio = 0.15;

    public static bool IsButtonColor(byte r, byte g, byte b) =>
        g >= 120 && b <= 45 && g - b >= 100 && g >= r + 15 && r >= 60;

    /// <returns>Le centre du bouton, ou null s'il n'est pas trouvé.</returns>
    public static RelativePoint? Find(ReadOnlySpan<byte> pixels, int width, int height, int stride)
    {
        if (width <= 0 || height <= 0 || stride < width * 4 || pixels.Length < stride * height) return null;

        // On ne regarde que la moitié basse : le bouton est sous le personnage.
        var top = height / 2;
        long count = 0, sumX = 0, sumY = 0;
        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;

        for (var y = top; y < height; y++)
        {
            var row = pixels.Slice(y * stride, width * 4);
            for (var x = 0; x < width; x++)
            {
                var i = x * 4;
                if (!IsButtonColor(row[i + 2], row[i + 1], row[i])) continue;
                count++;
                sumX += x;
                sumY += y;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (count < MinAreaRatio * width * height) return null;
        if (maxX - minX > MaxWidthRatio * width || maxY - minY > MaxHeightRatio * height) return null; // zone trop étalée : pas un bouton

        return new RelativePoint((sumX / (double)count + 0.5) / width, (sumY / (double)count + 0.5) / height);
    }
}
