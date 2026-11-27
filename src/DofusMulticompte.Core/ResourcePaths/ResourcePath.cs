using System.Text.Json;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.Core.ResourcePaths;

/// <summary>Chemin de récolte enregistré : une suite de cartes du monde, dans l'ordre de passage.</summary>
public sealed class ResourcePath
{
    public string Name { get; set; } = "";
    public List<MapPosition> Steps { get; set; } = [];

    /// <summary>
    /// Convertit les cases d'une capture en cartes du monde, à partir d'une case repère dont on connaît
    /// les coordonnées (le départ du chemin, en général un zaap).
    /// </summary>
    public static List<MapPosition> ToWorld(IEnumerable<GridCell> cells, GridCell anchorCell, MapPosition anchorPosition) =>
        cells.Select(c => new MapPosition(anchorPosition.X + c.Col - anchorCell.Col, anchorPosition.Y + c.Row - anchorCell.Row)).ToList();
}

/// <summary>Chemins enregistrés en JSON, un fichier par chemin, dans le dossier de l'application.</summary>
public sealed class ResourcePathStore(string directory)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultDirectory => AppPaths.Sub("chemins");

    public IReadOnlyList<ResourcePath> LoadAll()
    {
        if (!Directory.Exists(directory)) return [];
        var list = new List<ResourcePath>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (JsonSerializer.Deserialize<ResourcePath>(File.ReadAllText(file), Options) is { Steps.Count: > 0 } p) list.Add(p);
            }
            catch (Exception ex) when (ex is JsonException or IOException) { }
        }
        return list.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public void Save(ResourcePath path)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(FileFor(path.Name), JsonSerializer.Serialize(path, Options));
    }

    public void Delete(string name)
    {
        var file = FileFor(name);
        if (File.Exists(file)) File.Delete(file);
    }

    private string FileFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        return Path.Combine(directory, (safe.Length == 0 ? "chemin" : safe) + ".json");
    }
}
