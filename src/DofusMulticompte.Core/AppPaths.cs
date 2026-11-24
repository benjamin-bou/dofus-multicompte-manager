namespace DofusMulticompte.Core;

/// <summary>Emplacement des données de l'application (réglages, images, caches).</summary>
public static class AppPaths
{
    /// <summary>
    /// <c>%APPDATA%\DofusMulticompte</c>, créé au premier enregistrement. La variable d'environnement
    /// <c>DOFUSMULTICOMPTE_DATA</c> permet d'utiliser un autre dossier (tests, version portable sur clé USB).
    /// </summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("DOFUSMULTICOMPTE_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DofusMulticompte");

    public static string Sub(string name) => Path.Combine(DataDirectory, name);
}
