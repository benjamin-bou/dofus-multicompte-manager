using System.IO;
using System.Net.Http;
using System.Windows.Media.Imaging;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Tête de base de chaque classe, téléchargée une seule fois depuis DofusDB puis gardée en cache
/// dans %APPDATA% (les images d'Ankama ne sont pas embarquées dans l'application).
/// Les demandes ne transmettent que l'identifiant de la classe.
/// </summary>
public sealed class ClassImageStore(string directory)
{
    private const string HeadUrl = "https://api.dofusdb.fr/img/heads/SmallHead_{0}0.png";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly Dictionary<int, BitmapSource?> _cache = [];
    private readonly HashSet<int> _pending = [];
    // Pas de nouvel essai avant ce délai après un échec (hors ligne, site indisponible…).
    private readonly Dictionary<int, DateTime> _retryAfter = [];

    /// <summary>Une image vient d'être chargée : les vues qui l'attendaient doivent se rafraîchir.</summary>
    public event Action? ImageLoaded;

    public static string DefaultDirectory =>
        DofusMulticompte.Core.AppPaths.Sub("classes");

    /// <summary>Renvoie l'image si elle est prête ; sinon lance son chargement et renvoie null. À appeler sur le thread de l'interface.</summary>
    public BitmapSource? Get(DofusClass? cls)
    {
        if (cls is null) return null;
        if (_cache.TryGetValue(cls.BreedId, out var image)) return image;
        if (_pending.Contains(cls.BreedId)) return null;
        if (_retryAfter.TryGetValue(cls.BreedId, out var at) && DateTime.UtcNow < at) return null;

        var fromDisk = Load(PathFor(cls.BreedId));
        if (fromDisk is not null)
        {
            _cache[cls.BreedId] = fromDisk;
            return fromDisk;
        }
        _ = DownloadAsync(cls.BreedId);
        return null;
    }

    private string PathFor(int breedId) => Path.Combine(directory, $"head_{breedId}.png");

    private async Task DownloadAsync(int breedId)
    {
        _pending.Add(breedId);
        try
        {
            var bytes = await Http.GetByteArrayAsync(string.Format(HeadUrl, breedId));
            var image = Decode(bytes);
            if (image is null) throw new InvalidDataException("Image illisible");
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(PathFor(breedId), bytes);
            _cache[breedId] = image;
            ImageLoaded?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            _retryAfter[breedId] = DateTime.UtcNow.AddMinutes(5);
        }
        finally
        {
            _pending.Remove(breedId);
        }
    }

    private static BitmapSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException) { return null; }
    }

    private static BitmapSource? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try { return Decode(File.ReadAllBytes(path)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
