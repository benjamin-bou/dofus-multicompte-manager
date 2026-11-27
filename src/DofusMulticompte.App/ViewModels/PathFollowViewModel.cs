using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DofusMulticompte.App.Controls;
using DofusMulticompte.App.Interop;
using DofusMulticompte.App.Services;
using DofusMulticompte.Core.ResourcePaths;
using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.App.ViewModels;

/// <summary>
/// Page « Chemins » : chemins de récolte importés depuis des captures, et suivi en jeu avec une
/// superposition sur la minimap. Rien n'est envoyé au jeu : on lit l'écran et on dessine par-dessus.
/// </summary>
public sealed class PathFollowViewModel : ObservableObject, IDisposable
{
    /// <summary>Proportions d'une carte du monde (largeur / hauteur), mesurées sur les captures : 76,5 / 54,75.</summary>
    private const double MapAspect = 76.5 / 54.75;

    private readonly MainViewModel _main;
    private readonly ResourcePathStore _store = new(ResourcePathStore.DefaultDirectory);
    private readonly DispatcherTimer _timer;
    private PathOverlayWindow? _overlay;
    private PathProgress? _progress;
    private ResourcePath? _active;
    private bool _busy;
    private IntPtr _followed;
    private MinimapRegion? _minimap;
    private DateTime _nextMinimapSearch;

    // Calibrage : image de la minimap sur la carte précédente, et changement de carte en attente de mesure.
    private (MapPosition Pos, byte[] Gray, int W, int H)? _lastFrame;
    private (MapPosition From, MapPosition To, byte[] Gray, int W, int H)? _pendingCalibration;
    private readonly List<double> _widthSamples = [];
    private readonly List<double> _heightSamples = [];

    private string _positionText = "—";
    private string _minimapText = "—";
    private string _progressText = "Aucun chemin suivi.";
    private string _scaleText = "";

    public PathFollowViewModel(MainViewModel main)
    {
        _main = main;
        foreach (var p in _store.LoadAll()) Paths.Add(p);

        ImportFileCommand = new RelayCommand(_ => ImportFromFile());
        ImportClipboardCommand = new RelayCommand(_ => ImportFromClipboard(), _ => Clipboard.ContainsImage());
        FollowCommand = new RelayCommand(p => { if (p is ResourcePath path) Follow(path); });
        DeleteCommand = new RelayCommand(p => { if (p is ResourcePath path) Delete(path); });
        StopCommand = new RelayCommand(_ => Stop(), _ => _active is not null);
        PreviousStepCommand = new RelayCommand(_ => Jump(-1), _ => _progress is not null);
        NextStepCommand = new RelayCommand(_ => Jump(+1), _ => _progress is not null);
        RestartCommand = new RelayCommand(_ => { _progress?.JumpTo(0); RefreshTexts(); }, _ => _progress is not null);
        RecalibrateCommand = new RelayCommand(_ => Recalibrate());

        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, async (_, _) => await TickAsync(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
        _highlighter = new InteractiveHighlighter(IsDofusWindow) { Key = main.Settings.HighlightKey };

        if (main.Settings.ActivePathName is { } name && Paths.FirstOrDefault(p => p.Name == name) is { } saved) Follow(saved);
        RefreshScaleText();
    }

    public ObservableCollection<ResourcePath> Paths { get; } = [];

    // ---------------------------------------------------------------- mise en évidence

    private readonly InteractiveHighlighter _highlighter;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<uint, bool> _dofusProcesses = new();

    /// <summary>Appui régulier sur la touche de mise en évidence (désactivé à chaque démarrage).</summary>
    public bool HighlightEnabled
    {
        get => _highlighter.IsRunning;
        set
        {
            if (value == _highlighter.IsRunning) return;
            if (value) _highlighter.Start(); else _highlighter.Stop();
            OnPropertyChanged();
            _main.Log(value
                ? $"Mise en évidence des éléments interactifs : appui sur « {HighlightKey} » 2 à 3 fois par seconde (en pause quand tu tapes)."
                : "Mise en évidence des éléments interactifs arrêtée.");
        }
    }

    public string HighlightKey
    {
        get => _main.Settings.HighlightKey;
        set
        {
            // Touche seule : une combinaison (Ctrl+…) déclencherait d'autres actions du jeu.
            var key = Core.KeyGesture.TryParse(value, out var g) && g.Modifiers == Core.KeyModifiers.None && !g.IsMouse ? g.Key : "Y";
            _main.Settings.HighlightKey = key;
            _highlighter.Key = key;
            _main.SaveSettings();
            OnPropertyChanged();
        }
    }

    /// <summary>Vrai si la fenêtre appartient à un client Dofus (nom de processus), utilisable depuis n'importe quel fil.</summary>
    private bool IsDofusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return _dofusProcesses.GetOrAdd(pid, id =>
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById((int)id);
                return _main.Settings.ProcessNames.Any(n => string.Equals(n, p.ProcessName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { return false; }
        });
    }

    public ICommand ImportFileCommand { get; }
    public ICommand ImportClipboardCommand { get; }
    public ICommand FollowCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand PreviousStepCommand { get; }
    public ICommand NextStepCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand RecalibrateCommand { get; }

    public string? ActiveName => _active?.Name;
    public bool IsFollowing => _active is not null;
    public bool HasPaths => Paths.Count > 0;

    public string PositionText { get => _positionText; private set => Set(ref _positionText, value); }
    public string MinimapText { get => _minimapText; private set => Set(ref _minimapText, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string ScaleText { get => _scaleText; private set => Set(ref _scaleText, value); }

    // ---------------------------------------------------------------- import

    private void ImportFromFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Capture d'un chemin",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp|Tous les fichiers|*.*",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var frame = BitmapFrame.Create(new Uri(dialog.FileName), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            OpenImport(frame, System.IO.Path.GetFileNameWithoutExtension(dialog.FileName));
        }
        catch (Exception ex) when (ex is NotSupportedException or System.IO.IOException or UriFormatException)
        {
            MessageBox.Show($"Impossible de lire cette image : {ex.Message}", "Chemins", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportFromClipboard()
    {
        if (Clipboard.GetImage() is { } image) OpenImport(image, "Chemin collé");
    }

    private void OpenImport(BitmapSource image, string suggestedName)
    {
        var window = new PathImportWindow(image, suggestedName, ReadCurrentPositionAsync) { Owner = Application.Current.MainWindow };
        if (window.ShowDialog() != true || window.Result is not { } path) return;

        var existing = Paths.FirstOrDefault(p => string.Equals(p.Name, path.Name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is not null) Paths.Remove(existing);
        _store.Save(path);
        Paths.Add(path);
        OnPropertyChanged(nameof(HasPaths));
        _main.Log($"Chemin enregistré : {path.Name} ({path.Steps.Count} étapes, départ {path.Steps[0]})");
        Follow(path);
    }

    /// <summary>Position lue sur la fenêtre Dofus au premier plan (sinon le meneur, sinon la première en jeu).</summary>
    public async Task<MapPosition?> ReadCurrentPositionAsync()
    {
        var hwnd = PickWindow();
        if (hwnd == IntPtr.Zero) return null;
        var capture = await WindowCapture.CaptureAsync(hwnd, 10000);
        return capture.Image is { } img ? await ScreenOcr.ReadMapPositionAsync(img) : null;
    }

    private void Delete(ResourcePath path)
    {
        if (MessageBox.Show($"Supprimer le chemin « {path.Name} » ?", "Chemins", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_active == path) Stop();
        _store.Delete(path.Name);
        Paths.Remove(path);
        OnPropertyChanged(nameof(HasPaths));
    }

    // ---------------------------------------------------------------- suivi

    private void Follow(ResourcePath path)
    {
        _active = path;
        _progress = new PathProgress(path.Steps);
        _main.Settings.ActivePathName = path.Name;
        _main.SaveSettings();
        _overlay ??= new PathOverlayWindow();
        _timer.Start();
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(IsFollowing));
        ProgressText = $"Suivi de « {path.Name} » : place-toi sur le chemin, la position est lue en haut à gauche du jeu.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Stop()
    {
        _timer.Stop();
        _active = null;
        _progress = null;
        _overlay?.Hide();
        _main.Settings.ActivePathName = null;
        _main.SaveSettings();
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(IsFollowing));
        ProgressText = "Aucun chemin suivi.";
        CommandManager.InvalidateRequerySuggested();
    }

    private void Jump(int delta)
    {
        if (_progress is null) return;
        _progress.JumpTo(_progress.Index + delta);
        RefreshTexts();
    }

    private void Recalibrate()
    {
        _main.Settings.MinimapCellWidthRatio = 0;
        _main.Settings.MinimapCellHeightRatio = 0;
        _main.SaveSettings();
        _pendingCalibration = null;
        _widthSamples.Clear();
        _heightSamples.Clear();
        RefreshScaleText();
    }

    /// <summary>Fenêtre suivie : Dofus au premier plan, sinon la dernière suivie, sinon le meneur, sinon la première en jeu.</summary>
    private IntPtr PickWindow()
    {
        var online = _main.Characters.Where(c => c.IsOnline && c.IsConnected).ToList();
        var fg = NativeMethods.GetForegroundWindow();
        if (online.FirstOrDefault(c => c.Handle == fg) is { } front) return front.Handle;
        if (online.Any(c => c.Handle == _followed)) return _followed;
        return (online.FirstOrDefault(c => c.IsLeader) ?? online.FirstOrDefault())?.Handle ?? IntPtr.Zero;
    }

    private async Task TickAsync()
    {
        if (_busy || _progress is null) return;
        _busy = true;
        try
        {
            var hwnd = PickWindow();
            if (hwnd != _followed) { _followed = hwnd; _minimap = null; _lastFrame = null; _pendingCalibration = null; }
            if (hwnd == IntPtr.Zero)
            {
                PositionText = "Aucun personnage en jeu";
                _overlay?.Hide();
                return;
            }

            var capture = await WindowCapture.CaptureAsync(hwnd, 10000);
            if (capture.Image is not { } image) { _overlay?.Hide(); return; } // fenêtre réduite ou gelée

            var position = await ScreenOcr.ReadMapPositionAsync(image);
            if (position is { } pos)
            {
                PositionText = pos.ToString();
                _progress.Update(pos);
            }

            // Minimap : relocalisée toutes les 5 s au plus si on l'a perdue (fermée, déplacée…).
            if (_minimap is null && DateTime.UtcNow < _nextMinimapSearch) { RefreshTexts(); HideOverlayUnlessFront(hwnd); return; }
            var minimap = await MinimapLocator.LocateAsync(image, _minimap);
            if (minimap is null)
            {
                _minimap = null;
                _nextMinimapSearch = DateTime.UtcNow.AddSeconds(5);
                MinimapText = "Introuvable : affiche la mini-carte du jeu (titre « MINI-CARTE »)";
                _overlay?.Hide();
                RefreshTexts();
                return;
            }
            _minimap = minimap;
            RefreshScaleText(image.PixelHeight);
            MinimapText = $"Trouvée ({minimap.Content.Width}×{minimap.Content.Height} px){(minimap.MarkerFound ? "" : ", pion non repéré")}";

            if (position is { } p2) Calibrate(image, minimap, p2);
            RefreshTexts();
            UpdateOverlay(hwnd, image.PixelHeight, minimap);
        }
        catch (Exception ex)
        {
            _main.Log($"Suivi du chemin : {ex.Message}");
        }
        finally { _busy = false; }
    }

    private void HideOverlayUnlessFront(IntPtr hwnd)
    {
        if (NativeMethods.GetForegroundWindow() != hwnd) _overlay?.Hide();
    }

    /// <summary>
    /// Échelle de la minimap : au changement de carte, l'image glisse d'exactement une carte. On compare la
    /// dernière image prise sur l'ancienne carte avec une image prise une seconde après l'arrivée (le temps
    /// que la minimap se recentre).
    /// </summary>
    private void Calibrate(BitmapSource image, MinimapRegion minimap, MapPosition pos)
    {
        var px = MinimapLocator.Pixels(image, out var w, out _);
        var gray = MinimapLocator.Grayscale(px, w, minimap.Content, out var gw, out var gh);

        if (_pendingCalibration is { } pending && pending.To == pos && pending.W == gw && pending.H == gh)
        {
            _pendingCalibration = null;
            var shift = MinimapShiftEstimator.Estimate(pending.Gray, gray, gw, gh, maxShift: Math.Min(gw, gh) / 2 - 2);
            int dx = pending.To.X - pending.From.X, dy = pending.To.Y - pending.From.Y;
            _main.Log(shift is { } m
                ? $"Mini-carte : glissement ({m.Dx * 2}, {m.Dy * 2}) px pour un pas de ({dx}, {dy}), fiabilité {m.Confidence:P0}"
                : "Mini-carte : glissement non mesurable");
            if (shift is { Confidence: > 0.25 } s)
            {
                // Aller vers la droite fait glisser l'image vers la gauche : signes opposés attendus.
                var h = image.PixelHeight;
                var settings = _main.Settings;
                if (dx != 0 && s.Dx * dx < 0 && Math.Abs(s.Dx) >= 6)
                {
                    settings.MinimapCellWidthRatio = Median(_widthSamples, Math.Abs(s.Dx) * 2.0 / h);
                    if (_heightSamples.Count == 0) settings.MinimapCellHeightRatio = settings.MinimapCellWidthRatio / MapAspect;
                }
                else if (dy != 0 && s.Dy * dy < 0 && Math.Abs(s.Dy) >= 4)
                {
                    settings.MinimapCellHeightRatio = Median(_heightSamples, Math.Abs(s.Dy) * 2.0 / h);
                    if (_widthSamples.Count == 0) settings.MinimapCellWidthRatio = settings.MinimapCellHeightRatio * MapAspect;
                }
                _main.SaveSettings();
                RefreshScaleText(h);
            }
        }

        if (_lastFrame is { } last && last.Pos != pos && Math.Abs(last.Pos.X - pos.X) + Math.Abs(last.Pos.Y - pos.Y) == 1 && last.W == gw && last.H == gh)
            _pendingCalibration = (last.Pos, pos, last.Gray, gw, gh); // mesure à la prochaine image, une fois recentrée
        _lastFrame = (pos, gray, gw, gh);
    }

    private void UpdateOverlay(IntPtr hwnd, int clientHeight, MinimapRegion minimap)
    {
        if (_overlay is null || _progress is null || _progress.Current is not { } current) { _overlay?.Hide(); return; }
        // Affichée seulement quand le jeu est au premier plan, pour ne pas recouvrir les autres applications.
        var fg = NativeMethods.GetForegroundWindow();
        if (fg != hwnd || NativeMethods.IsIconic(hwnd)) { _overlay.Hide(); return; }

        var origin = new NativeMethods.POINT { X = minimap.Content.X, Y = minimap.Content.Y };
        NativeMethods.ClientToScreen(hwnd, ref origin);
        _overlay.PlaceOver(new Int32Rect(origin.X, origin.Y, minimap.Content.Width, minimap.Content.Height));

        var s = _main.Settings;
        var calibrated = s.MinimapCellWidthRatio > 0 && s.MinimapCellHeightRatio > 0;
        // La minimap est centrée sur la CARTE en cours (le pion, lui, montre où se tient le personnage dans
        // cette carte : relevé à 23 px du centre après un déplacement) : le centre de la zone est donc le centre
        // de la carte actuelle, et les autres cartes s'en déduisent.
        _overlay.Render(new PathOverlayState(
            _progress.Steps, _progress.Index, _progress.Status, current, _progress.Target,
            new Point(minimap.Content.Width / 2.0, minimap.Content.Height / 2.0),
            s.MinimapCellWidthRatio * clientHeight, s.MinimapCellHeightRatio * clientHeight, calibrated));
    }

    private void RefreshTexts()
    {
        if (_progress is null || _active is null) return;
        ProgressText = _progress.Status switch
        {
            PathStatus.Unknown => "En attente de la position…",
            PathStatus.Finished => $"« {_active.Name} » terminé ({_progress.Steps.Count} étapes).",
            PathStatus.OffPath => $"Hors chemin : rejoins {_progress.Target} (étape {_progress.Index + 1}/{_progress.Steps.Count}).",
            _ => $"Étape {_progress.Index + 1}/{_progress.Steps.Count} · prochaine carte {_progress.Target}",
        };
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshScaleText(int clientHeight = 0)
    {
        var s = _main.Settings;
        var size = clientHeight > 0 ? $" : une carte = {s.MinimapCellWidthRatio * clientHeight:F0} × {s.MinimapCellHeightRatio * clientHeight:F0} px" : "";
        ScaleText = s.MinimapCellWidthRatio > 0
            ? $"Mesurée automatiquement{size}. À refaire si tu changes le zoom de la mini-carte."
            : "Pas encore mesurée : elle le sera au premier changement de carte (en attendant, une flèche indique la direction).";
    }

    /// <summary>Ajoute une mesure et renvoie la médiane des 3 dernières : une mesure ratée (animation) ne fausse pas l'échelle.</summary>
    private static double Median(List<double> samples, double value)
    {
        samples.Add(value);
        if (samples.Count > 3) samples.RemoveAt(0);
        var sorted = samples.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    public void Dispose()
    {
        _timer.Stop();
        _highlighter.Dispose();
        _overlay?.Close();
    }
}
