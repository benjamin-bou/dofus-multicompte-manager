using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DofusMulticompte.App.Services;
using DofusMulticompte.Core;
using DofusMulticompte.Core.Updates;

namespace DofusMulticompte.App.ViewModels;

/// <summary>
/// Mise à jour intégrée : vérifie les releases GitHub au démarrage puis toutes les 6 heures, propose la
/// nouvelle version (notification une fois par version, encart dans le menu latéral) et l'installe sur demande.
/// </summary>
public sealed class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer;
    private ReleaseInfo? _available;
    private bool _busy;
    private string _status = "";
    private string? _notifiedVersion;

    public UpdateViewModel(MainViewModel main)
    {
        _main = main;
        CheckCommand = new RelayCommand(async _ => await CheckAsync(manual: true), _ => !_busy);
        InstallCommand = new RelayCommand(async _ => await InstallAsync(), _ => _available is not null && !_busy);
        OpenReleaseCommand = new RelayCommand(_ => OpenRelease());

        _timer = new DispatcherTimer(FirstCheckDelay, DispatcherPriority.Background, async (_, _) =>
        {
            _timer!.Interval = CheckInterval;
            if (_main.Settings.CheckForUpdates) await CheckAsync(manual: false);
        }, Dispatcher.CurrentDispatcher);
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand OpenReleaseCommand { get; }

    public string CurrentVersionText => $"Version {AppUpdater.CurrentVersion}";
    public bool IsAvailable => _available is not null;
    public string AvailableText => _available is { } r ? $"Version {r.Version} disponible" : "";
    public bool IsBusy => _busy;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public bool CheckForUpdates
    {
        get => _main.Settings.CheckForUpdates;
        set
        {
            _main.Settings.CheckForUpdates = value;
            _main.SaveSettings();
            OnPropertyChanged();
        }
    }

    private async Task CheckAsync(bool manual)
    {
        if (_busy) return;
        if (AppUpdater.Repository is null)
        {
            Status = "Copie de développement : les mises à jour automatiques ne concernent que l'exécutable publié sur GitHub.";
            return;
        }
        SetBusy(true);
        if (manual) Status = "Vérification…";
        try
        {
            var release = await AppUpdater.CheckAsync();
            if (release is null)
            {
                if (manual) Status = "Impossible de joindre GitHub. Réessaie plus tard.";
                return;
            }
            if (!ReleaseFeed.IsNewer(release, AppUpdater.CurrentVersion))
            {
                _available = null;
                Status = $"À jour (dernière version publiée : {release.Version}).";
                return;
            }

            _available = release;
            Status = AppUpdater.CanSelfUpdate
                ? "Une nouvelle version est disponible."
                : "Une nouvelle version est disponible : cette copie de développement ne peut pas se mettre à jour, télécharge-la depuis GitHub.";
            if (_notifiedVersion != release.Tag)
            {
                _notifiedVersion = release.Tag;
                _main.Log($"Nouvelle version disponible : {release.Version} (version actuelle {AppUpdater.CurrentVersion}).");
                _main.ShowNotification("mise-a-jour", new NotificationContent(NotificationKind.Progress, "Mise à jour disponible",
                    [new TextPart($"Version {release.Version}", true), new TextPart(" : ouvre Multicompte Dofus et clique sur « Mettre à jour ».")], null));
            }
        }
        finally
        {
            SetBusy(false);
            OnPropertyChanged(nameof(IsAvailable));
            OnPropertyChanged(nameof(AvailableText));
        }
    }

    private async Task InstallAsync()
    {
        if (_available is not { } release) return;
        if (!AppUpdater.CanSelfUpdate)
        {
            OpenRelease();
            return;
        }
        SetBusy(true);
        try
        {
            Status = "Téléchargement… 0 %";
            var progress = new Progress<double>(p => Status = $"Téléchargement… {p:P0}");
            await AppUpdater.InstallAsync(release, progress);
            Status = "Redémarrage…";
            _main.Log($"Mise à jour vers la version {release.Version} : redémarrage.");
            Application.Current.Shutdown(); // la nouvelle version attend la fermeture de celle-ci
        }
        catch (UpdateException ex)
        {
            Status = ex.Message;
            _main.Log($"Mise à jour impossible : {ex.Message}");
            SetBusy(false);
        }
    }

    private void OpenRelease()
    {
        var url = _available is { PageUrl.Length: > 0 } r ? r.PageUrl
            : AppUpdater.Repository is { } repo ? $"https://github.com/{repo}/releases/latest" : null;
        if (url is null)
        {
            Status = "Cette copie n'a pas été publiée par l'automatisation : aucune source de mise à jour connue.";
            return;
        }
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        OnPropertyChanged(nameof(IsBusy));
        CommandManager.InvalidateRequerySuggested();
    }
}
