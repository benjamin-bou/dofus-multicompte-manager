using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.App.Services;

/// <summary>Une ligne de texte reconnue, avec sa zone dans l'image d'origine (en pixels).</summary>
public sealed record OcrLineResult(string Text, Rect Bounds);

/// <summary>
/// Lecture de texte à l'écran avec la reconnaissance intégrée à Windows (langue française),
/// sans rien installer ni rien envoyer sur Internet.
/// </summary>
public static class ScreenOcr
{
    private static readonly OcrEngine? Engine =
        OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("fr-FR"))
        ?? OcrEngine.TryCreateFromUserProfileLanguages();

    public static bool IsAvailable => Engine is not null;

    /// <summary>
    /// Lit le texte d'une zone de l'image. La zone est agrandie <paramref name="scale"/> fois avant
    /// lecture : le texte de l'interface est petit, et l'OCR le lit bien mieux une fois grossi.
    /// </summary>
    public static async Task<IReadOnlyList<OcrLineResult>> ReadAsync(BitmapSource image, Int32Rect region, double scale = 2.5)
    {
        if (Engine is null) return [];
        region = Clip(region, image.PixelWidth, image.PixelHeight);
        if (region.Width < 4 || region.Height < 4) return [];

        // L'OCR refuse les images plus grandes que sa limite : on réduit l'agrandissement si besoin.
        var max = (double)OcrEngine.MaxImageDimension;
        scale = Math.Min(scale, Math.Min(max / region.Width, max / region.Height));

        BitmapSource source = new CroppedBitmap(image, region);
        if (Math.Abs(scale - 1) > 0.01)
            source = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        source = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

        var w = source.PixelWidth;
        var h = source.PixelHeight;
        var pixels = new byte[w * h * 4];
        source.CopyPixels(pixels, w * 4, 0);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255; // les captures n'ont pas d'alpha

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, w, h, BitmapAlphaMode.Premultiplied);
        var result = await Engine.RecognizeAsync(bitmap);

        var lines = new List<OcrLineResult>();
        foreach (var line in result.Lines)
        {
            double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.X + r.Width);
                bottom = Math.Max(bottom, r.Y + r.Height);
            }
            var bounds = line.Words.Count == 0
                ? Rect.Empty
                : new Rect(region.X + left / scale, region.Y + top / scale, (right - left) / scale, (bottom - top) / scale);
            lines.Add(new OcrLineResult(line.Text, bounds));
        }
        return lines;
    }

    /// <summary>
    /// Position de la carte affichée en haut à gauche du jeu (« -83, -48 », sous le nom de la zone).
    /// La zone lue est proportionnelle à la taille de la fenêtre.
    /// </summary>
    public static async Task<MapPosition?> ReadMapPositionAsync(BitmapSource image)
    {
        var region = new Int32Rect(0, (int)(image.PixelHeight * 0.025), (int)(image.PixelWidth * 0.2), (int)(image.PixelHeight * 0.085));
        var lines = await ReadAsync(image, region);
        // La ligne qui ne contient que des coordonnées est la bonne ; sinon, premières coordonnées trouvées.
        foreach (var line in lines)
            if (PositionParser.Parse(line.Text) is { } p && line.Text.Count(char.IsLetter) == 0)
                return p;
        foreach (var line in lines)
            if (PositionParser.Parse(line.Text) is { } p)
                return p;
        return null;
    }

    private static Int32Rect Clip(Int32Rect r, int w, int h)
    {
        var x = Math.Clamp(r.X, 0, w);
        var y = Math.Clamp(r.Y, 0, h);
        return new Int32Rect(x, y, Math.Clamp(r.Width, 0, w - x), Math.Clamp(r.Height, 0, h - y));
    }
}
