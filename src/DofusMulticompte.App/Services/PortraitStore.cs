using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Garde la dernière image de chaque personnage sur le disque (dans %APPDATA%, jamais ailleurs)
/// pour l'afficher quand son instance est fermée.
/// </summary>
public sealed class PortraitStore(string directory)
{
    public static string DefaultDirectory =>
        DofusMulticompte.Core.AppPaths.Sub("portraits");

    // Le pseudo est haché pour obtenir un nom de fichier valide quel que soit le caractère utilisé.
    private string PathFor(string characterName) =>
        Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(characterName.ToLowerInvariant())))[..32] + ".png");

    public void Save(string characterName, BitmapSource image)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(PathFor(characterName));
            encoder.Save(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public BitmapSource? Load(string characterName)
    {
        var path = PathFor(characterName);
        if (!File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // ne garde pas le fichier ouvert
            image.UriSource = new Uri(path);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException) { return null; }
    }

    public void Delete(string characterName)
    {
        try { File.Delete(PathFor(characterName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
