using System.Text.RegularExpressions;

namespace DofusMulticompte.Core.TreasureHunt;

/// <summary>Lit des coordonnées « -83, -48 » dans un texte reconnu à l'écran.</summary>
public static partial class PositionParser
{
    [GeneratedRegex(@"(?<![\d])(-?\s?\d{1,3})\s*[,.;]\s*(-?\s?\d{1,3})(?![\d])")]
    private static partial Regex Pair();

    /// <summary>Premières coordonnées plausibles du texte (entre -255 et 255), ou null.</summary>
    public static MapPosition? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        // L'OCR rend parfois le signe moins par un tiret long, et le zéro par la lettre O.
        var clean = text.Replace('—', '-').Replace('–', '-').Replace('−', '-');
        foreach (Match m in Pair().Matches(clean))
        {
            if (int.TryParse(m.Groups[1].Value.Replace(" ", ""), out var x)
                && int.TryParse(m.Groups[2].Value.Replace(" ", ""), out var y)
                && Math.Abs(x) <= 255 && Math.Abs(y) <= 255)
                return new MapPosition(x, y);
        }
        return null;
    }
}
