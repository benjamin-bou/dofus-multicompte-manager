using System.Text.RegularExpressions;

namespace DofusMulticompte.Core;

/// <summary>Informations extraites du titre d'une fenêtre Dofus connectée.</summary>
public sealed record ParsedTitle(string CharacterName, string ClassName, DofusClass? KnownClass, string Version, string Channel);

/// <summary>
/// Lit le titre des fenêtres Dofus. Une fois un personnage connecté, le client affiche
/// « Pseudo - Classe - 3.7.13.17 - Release ». Avant connexion, le titre ne suit pas ce format.
/// </summary>
public static partial class WindowTitleParser
{
    [GeneratedRegex(@"^\s*(?<name>.+?)\s+-\s+(?<class>.+?)\s+-\s+(?<version>\d+(?:\.\d+)+)\s+-\s+(?<channel>.+?)\s*$")]
    private static partial Regex TitleRegex();

    public static ParsedTitle? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = TitleRegex().Match(title);
        if (!m.Success) return null;

        var className = m.Groups["class"].Value.Trim();
        return new ParsedTitle(
            m.Groups["name"].Value.Trim(),
            className,
            DofusClasses.Find(className),
            m.Groups["version"].Value,
            m.Groups["channel"].Value.Trim());
    }
}
