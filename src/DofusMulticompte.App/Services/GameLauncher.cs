using System.ComponentModel;
using System.Diagnostics;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Lance des instances de Dofus en passant par Ankama Launcher (lien « zaap:// »), comme le bouton
/// « Jouer » du launcher. L'authentification reste entièrement gérée par le launcher : l'application
/// ne lit ni ne transmet aucun identifiant.
/// </summary>
public static class GameLauncher
{
    /// <summary>Demande au launcher d'ouvrir une instance. Renvoie un message d'erreur, ou null si la demande est partie.</summary>
    public static string? LaunchOne(AppSettings settings)
    {
        try
        {
            var uri = LaunchPlanner.BuildLaunchUri(settings.LauncherGameUid, settings.LauncherRelease);
            using var _ = Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return null;
        }
        catch (Win32Exception)
        {
            return "Ankama Launcher n'est pas installé (le lien zaap:// n'est associé à aucune application).";
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
