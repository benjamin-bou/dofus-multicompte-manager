using System.Globalization;
using System.Text;

namespace DofusMulticompte.Core;

/// <summary>Une classe jouable de Dofus, avec une couleur d'accent pour l'interface.</summary>
public sealed record DofusClass(string Name, string AccentHex, IReadOnlyList<string> Aliases);

public static class DofusClasses
{
    public static readonly IReadOnlyList<DofusClass> All =
    [
        new("Féca", "#D9A441", ["Feca"]),
        new("Osamodas", "#8B5A3C", ["Osa"]),
        new("Enutrof", "#C9A227", ["Enu"]),
        new("Sram", "#6E6E8F", []),
        new("Xélor", "#3F7CC7", ["Xelor"]),
        new("Ecaflip", "#D46A3A", ["Eca"]),
        new("Eniripsa", "#D9557B", ["Eni"]),
        new("Iop", "#C8402F", []),
        new("Crâ", "#5E9E45", ["Cra"]),
        new("Sadida", "#3E8E5A", ["Sadi"]),
        new("Sacrieur", "#A3262E", ["Sacri", "Sacrier"]),
        new("Pandawa", "#4FA3A0", ["Panda"]),
        new("Roublard", "#8C4F9E", ["Rogue"]),
        new("Zobal", "#7A6BB8", ["Masqueraider"]),
        new("Steamer", "#2F8C9A", ["Foggernaut"]),
        new("Eliotrope", "#2EA6C9", ["Elio"]),
        new("Huppermage", "#5B7FD6", ["Hupper"]),
        new("Ouginak", "#9C6B3A", ["Ougi"]),
        new("Forgelance", "#B5733A", ["Forge"]),
    ];

    /// <summary>Retrouve une classe à partir d'un texte (insensible à la casse et aux accents).</summary>
    public static DofusClass? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var key = Normalize(text);
        foreach (var c in All)
        {
            if (Normalize(c.Name) == key) return c;
            foreach (var alias in c.Aliases)
                if (Normalize(alias) == key) return c;
        }
        return null;
    }

    public static string Normalize(string text)
    {
        var decomposed = text.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
