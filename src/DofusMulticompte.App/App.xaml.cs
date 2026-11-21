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

        _singleInstance = new Mutex(true, "DofusMulticompte.SingleInstance", out var isFirst);
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
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
