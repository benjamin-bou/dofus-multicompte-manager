using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using DofusMulticompte.Core.Updates;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Mise à jour intégrée depuis les releases GitHub du dépôt public. L'exécutable téléchargé est posé à côté de
/// l'actuel, vérifié (taille et empreinte SHA-256 publiées par GitHub), puis les fichiers sont échangés :
/// un programme en cours d'exécution ne peut pas être écrasé, mais il peut être renommé. La nouvelle version
/// démarre ensuite et supprime l'ancienne. Les réglages, dans %APPDATA%, ne sont pas touchés.
/// </summary>
public sealed class AppUpdater
{
    /// <summary>Argument passé à la nouvelle version : elle attend la fermeture de l'ancienne avant de démarrer.</summary>
    public const string AfterUpdateArgument = "--apres-mise-a-jour";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub exige un User-Agent : nom générique de l'application, sans aucune information personnelle.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DofusMulticompte-Updater");
        return http;
    }

    public static Version CurrentVersion =>
        ReleaseFeed.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    /// <summary>
    /// Dépôt GitHub (« propriétaire/nom ») dont les releases servent de source de mise à jour. Il est inscrit dans
    /// l'exécutable par l'automatisation de publication (propriété UpdateRepository) et n'est pas écrit dans le code :
    /// une compilation locale sans ce réglage n'a pas de mise à jour automatique.
    /// </summary>
    public static string? Repository { get; } = typeof(AppUpdater).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "UpdateRepository" && ReleaseFeed.IsValidRepository(a.Value))?.Value;

    /// <summary>Chemin de l'exécutable en cours.</summary>
    public static string? ExecutablePath => Environment.ProcessPath;

    /// <summary>
    /// Vrai seulement pour l'exécutable publié (un seul fichier, .NET inclus) : une compilation de développement
    /// (dll + exe) ne doit pas être remplacée par l'exécutable de la release.
    /// </summary>
    public static bool CanSelfUpdate => string.IsNullOrEmpty(typeof(AppUpdater).Assembly.Location) && ExecutablePath is not null;

    /// <summary>Dernière release, ou null si GitHub est injoignable (pas de connexion, limite d'appels atteinte…).</summary>
    public static async Task<ReleaseInfo?> CheckAsync(CancellationToken ct = default)
    {
        if (Repository is not { } repository) return null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseFeed.LatestReleaseApi(repository));
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;
            return ReleaseFeed.Parse(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Télécharge et vérifie la nouvelle version, échange les fichiers puis lance la nouvelle version.
    /// L'appelant ferme ensuite l'application. Lève <see cref="UpdateException"/> avec un message lisible en cas d'échec.
    /// </summary>
    public static async Task InstallAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!CanSelfUpdate || ExecutablePath is not { } exe)
            throw new UpdateException("Cette copie de l'application n'est pas l'exécutable publié : télécharge la nouvelle version depuis GitHub.");
        if (release.AssetUrl is null)
            throw new UpdateException($"La version {release.Version} ne contient pas d'exécutable à télécharger.");
        if (release.Sha256 is null)
            throw new UpdateException("GitHub n'a pas fourni l'empreinte de l'exécutable : mise à jour refusée par sécurité.");

        var download = exe + ".nouveau";
        var old = exe + ".ancien";
        try
        {
            // 1. Téléchargement à côté de l'exécutable (même disque : l'échange est un simple renommage).
            using (var response = await Http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? release.AssetSize;
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = File.Create(download);
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0) progress?.Report(done / (double)total);
                }
            }

            // 2. Vérification : taille et empreinte publiées par GitHub.
            var info = new FileInfo(download);
            if (release.AssetSize > 0 && info.Length != release.AssetSize)
                throw new UpdateException($"Téléchargement incomplet ({info.Length} octets sur {release.AssetSize}).");
            string hash;
            await using (var stream = File.OpenRead(download))
                hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            if (hash != release.Sha256)
                throw new UpdateException("L'empreinte du fichier téléchargé ne correspond pas à celle publiée : mise à jour annulée.");

            // 3. Échange : l'exécutable en cours est renommé (autorisé), la nouvelle version prend sa place.
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try
            {
                File.Move(download, exe);
            }
            catch
            {
                File.Move(old, exe); // remet l'ancienne version en place
                throw;
            }

            // 4. Lancement de la nouvelle version, qui attendra la fermeture de celle-ci.
            Process.Start(new ProcessStartInfo(exe, AfterUpdateArgument) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
        }
        catch (UnauthorizedAccessException)
        {
            throw new UpdateException("Impossible d'écrire dans le dossier de l'application. Place MulticompteDofus.exe dans un dossier à toi (Documents, Bureau…) ou télécharge la nouvelle version depuis GitHub.");
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Téléchargement impossible : {ex.Message}");
        }
        catch (IOException ex)
        {
            throw new UpdateException($"Remplacement de l'exécutable impossible : {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(download)) File.Delete(download); } catch (IOException) { }
        }
    }

    /// <summary>Au démarrage après une mise à jour : supprime l'ancien exécutable (il peut rester verrouillé quelques instants).</summary>
    public static async Task CleanupAsync()
    {
        if (ExecutablePath is not { } exe) return;
        var old = exe + ".ancien";
        for (var i = 0; i < 20 && File.Exists(old); i++)
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { await Task.Delay(500); }
        }
    }
}

public sealed class UpdateException(string message) : Exception(message);
