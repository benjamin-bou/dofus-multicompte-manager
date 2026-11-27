using System.Windows;
using DofusMulticompte.App.ViewModels;
using DofusMulticompte.Core;

namespace DofusMulticompte.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Après une mise à jour, l'ancienne version est encore en train de se fermer : on l'attend un peu.
        var afterUpdate = e.Args.Contains(Services.AppUpdater.AfterUpdateArgument);
        _singleInstance = new Mutex(false, "DofusMulticompte.SingleInstance");
        bool isFirst;
        try { isFirst = _singleInstance.WaitOne(afterUpdate ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { isFirst = true; } // l'ancienne version s'est fermée sans le rendre
        if (!isFirst)
        {
            MessageBox.Show("Le gestionnaire multicompte est déjà ouvert.", "Multicompte Dofus", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _viewModel = new MainViewModel(new SettingsStore(SettingsStore.DefaultPath));
        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
        // Barre flottante : sans propriétaire, pour rester visible quand la fenêtre principale est réduite.
        _ = new Controls.CharacterBarWindow(_viewModel);
        _viewModel.Start();
        if (afterUpdate)
        {
            _ = Services.AppUpdater.CleanupAsync();
            _viewModel.Log($"Mise à jour installée : version {Services.AppUpdater.CurrentVersion}.");
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        try { _singleInstance?.ReleaseMutex(); } catch (ApplicationException) { } // pas détenu (seconde instance refusée)
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
