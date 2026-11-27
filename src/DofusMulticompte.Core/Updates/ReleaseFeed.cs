using System.Text.Json;

namespace DofusMulticompte.Core.Updates;

/// <summary>Dernière version publiée, telle que décrite par l'API des releases GitHub.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string PageUrl,
    string Notes,
    string? AssetUrl,
    long AssetSize,
    string? Sha256);

/// <summary>
/// Lecture des releases GitHub du dépôt public : la mise à jour intégrée compare la dernière version publiée
/// à la version en cours et télécharge l'exécutable joint à la release. Aucune donnée n'est envoyée.
/// </summary>
public static class ReleaseFeed
{
    /// <summary>
    /// Adresse de l'API pour un dépôt « propriétaire/nom ». Le dépôt n'est pas écrit dans le code : il est inscrit
    /// dans l'exécutable au moment de la compilation par l'automatisation de publication.
    /// </summary>
    public static string LatestReleaseApi(string repository) => $"https://api.github.com/repos/{repository}/releases/latest";

    /// <summary>Vrai pour un nom de dépôt de la forme « propriétaire/nom ».</summary>
    public static bool IsValidRepository(string? repository) =>
        repository is { Length: > 2 } r && r.Count(c => c == '/') == 1 && !r.StartsWith('/') && !r.EndsWith('/') && !r.Contains(' ');

    /// <summary>Nom de l'exécutable joint à chaque release (produit par l'automatisation de publication).</summary>
    public const string AssetName = "MulticompteDofus.exe";

    /// <summary>« v1.2.0 » → 1.2.0 ; les suffixes de préversion (« -beta ») et les tags illisibles sont refusés.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var t = tag.Trim().TrimStart('v', 'V');
        if (t.Contains('-') || t.Contains('+')) return null;
        return Version.TryParse(t, out var v) ? Normalize(v) : null;
    }

    /// <summary>Ramène une version à trois nombres (1.2 → 1.2.0, 1.2.0.0 → 1.2.0) pour comparer sans surprise.</summary>
    public static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    public static bool IsNewer(ReleaseInfo release, Version current) => release.Version > Normalize(current);

    /// <summary>Lit la réponse de <c>/releases/latest</c>. Renvoie null si le JSON ou le tag sont illisibles.</summary>
    public static ReleaseInfo? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            var tag = Str(root, "tag_name");
            if (ParseTag(tag) is not { } version) return null;

            string? url = null, sha = null;
            long size = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (!string.Equals(Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    url = Str(a, "browser_download_url");
                    size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0;
                    // GitHub publie l'empreinte de chaque fichier : « sha256:… ».
                    var digest = Str(a, "digest");
                    if (digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest["sha256:".Length..].ToLowerInvariant();
                    break;
                }
            }

            return new ReleaseInfo(version, tag!, Str(root, "html_url") ?? "", Str(root, "body") ?? "", url, size, sha);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
