using System.Globalization;
using System.Text;

namespace DofusMulticompte.Core;

/// <summary>
/// Une classe jouable de Dofus, avec une couleur d'accent pour l'interface.
/// <paramref name="BreedId"/> est l'identifiant de la classe dans les données du jeu (1 à 18, puis 20 pour Forgelance).
/// </summary>
public sealed record DofusClass(string Name, string AccentHex, IReadOnlyList<string> Aliases, int BreedId);

public static class DofusClasses
{
    // Les alias couvrent les noms officiels des clients anglais, espagnol, allemand et portugais :
    // le titre de la fenêtre reprend la langue du client.
    public static readonly IReadOnlyList<DofusClass> All =
    [
        new("Féca", "#D9A441", ["Feca"], 1),
        new("Osamodas", "#8B5A3C", ["Osa"], 2),
        new("Enutrof", "#C9A227", ["Enu", "Anutrof"], 3),
        new("Sram", "#6E6E8F", [], 4),
        new("Xélor", "#3F7CC7", ["Xelor"], 5),
        new("Ecaflip", "#D46A3A", ["Eca", "Zurcarák"], 6),
        new("Eniripsa", "#D9557B", ["Eni", "Aniripsa"], 7),
        new("Iop", "#C8402F", ["Yopuka"], 8),
        new("Crâ", "#5E9E45", ["Cra", "Ocra"], 9),
        new("Sadida", "#3E8E5A", ["Sadi"], 10),
        new("Sacrieur", "#A3262E", ["Sacri", "Sacrier", "Sacrógrito"], 11),
        new("Pandawa", "#4FA3A0", ["Panda"], 12),
        new("Roublard", "#8C4F9E", ["Rogue", "Tymador", "Halsabschneider", "Ladino"], 13),
        new("Zobal", "#7A6BB8", ["Masqueraider", "Maskerador"], 14),
        new("Steamer", "#2F8C9A", ["Foggernaut"], 15),
        new("Eliotrope", "#2EA6C9", ["Elio", "Eliotrop", "Selotrop"], 16),
        new("Huppermage", "#5B7FD6", ["Hupper", "Übermagier", "Hipermago", "Huppermago"], 17),
        new("Ouginak", "#9C6B3A", ["Ougi", "Uginak", "Kilorf"], 18),
        new("Forgelance", "#B5733A", ["Forge", "Speerschmied", "Forjalanza", "Forjalança"], 20),
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
