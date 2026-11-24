using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Cartes et éléments de chasse, lus dans l'API publique de DofusDB (sans passer par son site),
/// puis gardés sur le disque : une même recherche ne repart jamais sur le réseau avant 14 jours.
/// Seules la position, la direction et la langue sont envoyées.
/// </summary>
public sealed class HuntDataClient
{
    private const string Api = "https://api.dofusdb.fr";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(14);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly string _cachePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, CacheEntry>? _cache;
    private IReadOnlyList<HuntPoi>? _catalog;

    public HuntDataClient(string? directory = null)
    {
        directory ??= DofusMulticompte.Core.AppPaths.Sub("hunts");
        Directory.CreateDirectory(directory);
        _cachePath = Path.Combine(directory, "dofusdb-cache.json");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DofusMulticompte/1.0");
    }

    /// <summary>Cartes dans la direction de la flèche (jusqu'à 10 cartes), triées de la plus proche à la plus lointaine.</summary>
    public async Task<IReadOnlyList<HuntMapCandidate>> GetCandidatesAsync(MapPosition from, HuntDirection direction, CancellationToken ct = default)
    {
        var key = $"hunt:{from.X}:{from.Y}:{(int)direction}";
        var json = await GetCachedAsync(key, () => $"{Api}/treasure-hunt?x={from.X}&y={from.Y}&direction={(int)direction}&world=0&$limit=50", ct);
        return ParseCandidates(json);
    }

    /// <summary>Tous les éléments de chasse connus (environ 180), pour reconnaître le texte lu à l'écran.</summary>
    public async Task<IReadOnlyList<HuntPoi>> GetCatalogAsync(CancellationToken ct = default)
    {
        if (_catalog is not null) return _catalog;
        var all = new List<HuntPoi>();
        for (var skip = 0; skip < 2000; skip += 50)
        {
            var json = await GetCachedAsync($"poi:{skip}", () => $"{Api}/point-of-interest?$limit=50&$skip={skip}", ct);
            var node = JsonNode.Parse(json);
            var data = node?["data"]?.AsArray() ?? [];
            foreach (var item in data)
                if (ParsePoi(item) is { } poi) all.Add(poi);
            var total = node?["total"]?.GetValue<int>() ?? 0;
            if (data.Count == 0 || skip + data.Count >= total) break;
        }
        return _catalog = all;
    }

    internal static IReadOnlyList<HuntMapCandidate> ParseCandidates(string json)
    {
        var list = new List<HuntMapCandidate>();
        foreach (var item in JsonNode.Parse(json)?["data"]?.AsArray() ?? [])
        {
            if (item is null) continue;
            var x = item["posX"]?.GetValue<int>();
            var y = item["posY"]?.GetValue<int>();
            var distance = item["distance"]?.GetValue<int>();
            if (x is null || y is null || distance is null) continue;
            var pois = (item["pois"]?.AsArray() ?? []).Select(ParsePoi).OfType<HuntPoi>().ToList();
            list.Add(new HuntMapCandidate(new MapPosition(x.Value, y.Value), distance.Value, pois));
        }
        return list.OrderBy(c => c.Distance).ToList();
    }

    private static HuntPoi? ParsePoi(JsonNode? node)
    {
        var id = node?["id"]?.GetValue<int>();
        var name = node?["name"]?["fr"]?.GetValue<string>();
        return id is null || string.IsNullOrWhiteSpace(name) ? null : new HuntPoi(id.Value, name);
    }

    private async Task<string> GetCachedAsync(string key, Func<string> url, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _cache ??= LoadCache();
            if (_cache.TryGetValue(key, out var entry) && DateTime.UtcNow - entry.SavedUtc < CacheLifetime)
                return entry.Json;

            try
            {
                var json = await _http.GetStringAsync(url(), ct);
                _cache[key] = new CacheEntry(DateTime.UtcNow, json);
                SaveCache();
                return json;
            }
            catch when (entry is not null && !ct.IsCancellationRequested)
            {
                return entry.Json; // hors ligne : une réponse ancienne vaut mieux que rien
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private Dictionary<string, CacheEntry> LoadCache()
    {
        try
        {
            if (File.Exists(_cachePath))
                return JsonSerializer.Deserialize<Dictionary<string, CacheEntry>>(File.ReadAllText(_cachePath), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return [];
    }

    private void SaveCache()
    {
        try
        {
            var tmp = _cachePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_cache, Json));
            File.Move(tmp, _cachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record CacheEntry(DateTime SavedUtc, string Json);
}
