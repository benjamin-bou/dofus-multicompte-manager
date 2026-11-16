using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DofusMulticompte.App.Interop;
using DofusMulticompte.App.Services;
using DofusMulticompte.Core;
using InputMode = DofusMulticompte.Core.InputMode;

namespace DofusMulticompte.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 200;

    private readonly SettingsStore _store;
    private readonly DofusWindowScanner _scanner = new();
    private readonly NativeMessageWindow _messages;
    private readonly TurnAutomationService _automation;
    private readonly DispatcherTimer _scanTimer;
    private readonly DispatcherTimer _thumbnailTimer;
    private readonly DispatcherTimer _rescanSoon;
    private readonly Dispatcher _dispatcher;
    private static readonly TimeSpan TurnBadgeDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LaunchBadgeDuration = TimeSpan.FromSeconds(90);
    private readonly PortraitStore _portraits = new(PortraitStore.DefaultDirectory);
    private readonly Dictionary<string, DateTime> _launchRequestedAt = new(StringComparer.OrdinalIgnoreCase);
    private bool _launchingAll;
    private bool _capturing;
    private DateTime _turnMarkedAt = DateTime.MinValue;
    private string _hotkeyStatus = "";

    public MainViewModel(SettingsStore store)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _store = store;
        Settings = store.Load();

        _automation = new TurnAutomationService(Log);
        _automation.TurnStarted += hwnd => _dispatcher.BeginInvoke(() => MarkTurn(hwnd));

        _messages = new NativeMessageWindow();
        _messages.WindowFlashed += OnWindowFlashed;
        _messages.WindowsChanged += _ => { _rescanSoon!.Stop(); _rescanSoon.Start(); };
        _messages.HotkeyPressed += OnHotkey;

        SetLeaderCommand = new RelayCommand(p => ToggleLeader(p as CharacterViewModel), p => p is CharacterViewModel { IsConnected: true });
        FocusCommand = new RelayCommand(p => { if (p is CharacterViewModel { IsOnline: true } c) WindowController.Focus(c.Handle); });
        MoveUpCommand = new RelayCommand(p => Move(p as CharacterViewModel, -1), p => p is CharacterViewModel { IsConnected: true });
        MoveDownCommand = new RelayCommand(p => Move(p as CharacterViewModel, +1), p => p is CharacterViewModel { IsConnected: true });
        TestPassCommand = new RelayCommand(
            p => { if (p is CharacterViewModel { IsOnline: true, CharacterName: { } n } c) _automation.PassTurnNow(c.Handle, n, Settings); },
            p => p is CharacterViewModel { IsOnline: true, IsConnected: true });
        LaunchCommand = new RelayCommand(
            p => { if (p is CharacterViewModel c) Launch(c); },
            p => p is CharacterViewModel { IsOnline: false, IsLaunching: false } && !_launchingAll);
        LaunchAllCommand = new RelayCommand(async _ => await LaunchAllAsync(), _ => !_launchingAll && OfflineCount > 0);
        LaunchNewCommand = new RelayCommand(_ => LaunchNewInstance(), _ => !_launchingAll);
        ForgetCommand = new RelayCommand(p => Forget(p as CharacterViewModel), p => p is CharacterViewModel { IsOnline: false });
        ClearLogCommand = new RelayCommand(_ => LogEntries.Clear());

        _rescanSoon = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => { _rescanSoon!.Stop(); Rescan(); }, _dispatcher);
        _rescanSoon.Stop();
        _scanTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Rescan(), _dispatcher);
        _thumbnailTimer = new DispatcherTimer(TimeSpan.FromSeconds(2.5), DispatcherPriority.Background, async (_, _) => await RefreshThumbnailsAsync(), _dispatcher);

        RegisterHotkeys();
        Rescan();
        _ = RefreshThumbnailsAsync();
        Log("Application démarrée. Les instances Dofus sont détectées automatiquement.");
    }

    public AppSettings Settings { get; }
    public ObservableCollection<CharacterViewModel> Characters { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    public ICommand SetLeaderCommand { get; }
    public ICommand FocusCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand TestPassCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand LaunchAllCommand { get; }
    public ICommand LaunchNewCommand { get; }
    public ICommand ForgetCommand { get; }
    public ICommand ClearLogCommand { get; }

    public int OfflineCount => Characters.Count(c => !c.IsOnline);
    public string LaunchAllLabel => OfflineCount > 0 ? $"Tout lancer ({OfflineCount})" : "Tout lancer";

    public string StatusText
    {
        get
        {
            var total = Characters.Count(c => c.IsOnline);
            var connected = Characters.Count(c => c.IsOnline && c.IsConnected);
            var leader = Settings.LeaderName is { } l && Characters.Any(c => c.IsLeader) ? $" · Meneur : {l}" : " · Aucun meneur actif";
            return total == 0
                ? "Aucune instance de Dofus détectée"
                : $"{total} instance{(total > 1 ? "s" : "")} · {connected} connectée{(connected > 1 ? "s" : "")}{leader}";
        }
    }

    public bool HasCharacters => Characters.Count > 0;

    // --- Réglages exposés à l'interface -------------------------------------------------

    public bool AutoPassFollowers
    {
        get => Settings.AutoPassFollowers;
        set => UpdateSetting(() => Settings.AutoPassFollowers = value);
    }

    public bool AutoPassLeader
    {
        get => Settings.AutoPassLeader;
        set => UpdateSetting(() => Settings.AutoPassLeader = value);
    }

    public bool BringTurnWindowToFront
    {
        get => Settings.BringTurnWindowToFront;
        set => UpdateSetting(() => Settings.BringTurnWindowToFront = value);
    }

    public string PassTurnKey
    {
        get => Settings.PassTurnKey;
        set => UpdateSetting(() => Settings.PassTurnKey = value);
    }

    public int PassDelayMinMs
    {
        get => Settings.PassDelayMinMs;
        set => UpdateSetting(() => Settings.PassDelayMinMs = Math.Clamp(value, 0, 10000));
    }

    public int PassDelayMaxMs
    {
        get => Settings.PassDelayMaxMs;
        set => UpdateSetting(() => Settings.PassDelayMaxMs = Math.Clamp(value, 0, 10000));
    }

    public bool UseBackgroundInput
    {
        get => Settings.InputMode == InputMode.Background;
        set => UpdateSetting(() => Settings.InputMode = value ? InputMode.Background : InputMode.Foreground);
    }

    public string HotkeyNext
    {
        get => Settings.Hotkeys.NextCharacter;
        set => UpdateSetting(() => Settings.Hotkeys.NextCharacter = value, RegisterHotkeys);
    }

    public string HotkeyPrevious
    {
        get => Settings.Hotkeys.PreviousCharacter;
        set => UpdateSetting(() => Settings.Hotkeys.PreviousCharacter = value, RegisterHotkeys);
    }

    public string HotkeyLeader
    {
        get => Settings.Hotkeys.FocusLeader;
        set => UpdateSetting(() => Settings.Hotkeys.FocusLeader = value, RegisterHotkeys);
    }

    public string HotkeyStatus
    {
        get => _hotkeyStatus;
        private set => Set(ref _hotkeyStatus, value);
    }

    private void UpdateSetting(Action apply, Action? after = null, [CallerMemberName] string? name = null)
    {
        apply();
        OnPropertyChanged(name);
        SaveSettings();
        foreach (var c in Characters) c.RefreshAutomationState();
        after?.Invoke();
    }

    public void SaveSettings()
    {
        try { _store.Save(Settings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log($"Impossible d'enregistrer les réglages : {ex.Message}"); }
    }

    // --- Détection des instances --------------------------------------------------------

    public void Start()
    {
        _scanTimer.Start();
        _thumbnailTimer.Start();
    }

    private void Rescan()
    {
        var windows = _scanner.Scan(Settings.ProcessNames);
        var seen = new HashSet<IntPtr>(windows.Select(w => w.Handle));
        var needsSort = false;

        // Fenêtres fermées : on garde la dernière image du personnage pour sa carte hors ligne.
        for (var i = Characters.Count - 1; i >= 0; i--)
        {
            var c = Characters[i];
            if (!c.IsOnline || seen.Contains(c.Handle)) continue;
            if (c.CharacterName is { } closedName && c.Thumbnail is { } last) _portraits.Save(closedName, last);
            Log($"Instance fermée : {c.DisplayName}");
            Characters.RemoveAt(i);
            needsSort = true;
        }

        foreach (var w in windows)
        {
            var vm = Characters.FirstOrDefault(c => c.Handle == w.Handle);
            if (vm is null)
            {
                vm = new CharacterViewModel(this, w.Handle, w.ProcessId);
                vm.UpdateTitle(w.Title);
                Characters.Add(vm);
                Log(vm.IsConnected ? $"Instance détectée : {vm.DisplayName} ({vm.ClassName})" : $"Instance détectée (PID {w.ProcessId}), personnage pas encore connecté");
                if (vm.IsConnected) RememberCharacter(vm);
                needsSort = true;
            }
            else if (vm.UpdateTitle(w.Title))
            {
                Log(vm.IsConnected ? $"Personnage connecté : {vm.DisplayName} ({vm.ClassName})" : $"Personnage déconnecté (PID {w.ProcessId})");
                if (vm.IsConnected) RememberCharacter(vm);
                needsSort = true;
            }
        }

        needsSort |= SyncOfflineCards();

        var foreground = NativeMethods.GetForegroundWindow();
        // Un tour de Dofus ne dépasse pas une minute : passé ce délai, le badge « Son tour » est périmé.
        var turnExpired = DateTime.UtcNow - _turnMarkedAt > TurnBadgeDuration;
        foreach (var c in Characters)
        {
            c.IsLeader = c.IsConnected && Settings.IsLeader(c.CharacterName);
            c.IsActive = c.IsOnline && c.Handle == foreground;
            if (turnExpired) c.IsTurn = false;
            if (c.IsLaunching && c.CharacterName is { } n &&
                (!_launchRequestedAt.TryGetValue(n, out var at) || DateTime.UtcNow - at > LaunchBadgeDuration))
                c.IsLaunching = false;
        }

        if (needsSort)
        {
            Sort();
            // L'état des boutons dépend de la connexion : WPF ne le réévalue seul qu'après une saisie utilisateur.
            CommandManager.InvalidateRequerySuggested();
        }
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasCharacters));
        OnPropertyChanged(nameof(OfflineCount));
        OnPropertyChanged(nameof(LaunchAllLabel));
    }

    /// <summary>Mémorise un personnage vu en jeu (et sa classe) pour pouvoir l'afficher et le lancer plus tard.</summary>
    private void RememberCharacter(CharacterViewModel vm)
    {
        var profile = Settings.GetOrCreateProfile(vm.CharacterName!);
        if (profile.ClassName != vm.ClassName)
        {
            profile.ClassName = vm.ClassName;
            SaveSettings();
        }
        _launchRequestedAt.Remove(vm.CharacterName!);
    }

    /// <summary>Ajoute une carte hors ligne pour chaque personnage connu sans fenêtre, et retire celles devenues inutiles.</summary>
    private bool SyncOfflineCards()
    {
        var changed = false;
        var onlineNames = Characters.Where(c => c.IsOnline && c.IsConnected).Select(c => c.CharacterName!).ToList();
        var offlineNames = new HashSet<string>(LaunchPlanner.OfflineCharacters(Settings, onlineNames), StringComparer.OrdinalIgnoreCase);

        for (var i = Characters.Count - 1; i >= 0; i--)
        {
            var c = Characters[i];
            if (c.IsOnline || offlineNames.Contains(c.CharacterName!)) continue;
            Characters.RemoveAt(i);
            changed = true;
        }

        foreach (var name in offlineNames)
        {
            if (Characters.Any(c => !c.IsOnline && string.Equals(c.CharacterName, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            Settings.Characters.TryGetValue(name, out var profile);
            var card = new CharacterViewModel(this, name, profile?.ClassName)
            {
                Thumbnail = _portraits.Load(name),
                IsLaunching = _launchRequestedAt.TryGetValue(name, out var at) && DateTime.UtcNow - at <= LaunchBadgeDuration,
            };
            Characters.Add(card);
            changed = true;
        }
        return changed;
    }

    /// <summary>Ordre choisi par l'utilisateur ; les instances où personne n'est encore connecté vont à la fin.</summary>
    private void Sort()
    {
        var ordered = Characters
            .OrderBy(c => c.IsConnected ? 0 : 1)
            .ThenBy(c => c.Order)
            .ThenBy(c => c.IsOnline ? 0 : 1)
            .ThenBy(c => c.ProcessId)
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var current = Characters.IndexOf(ordered[i]);
            if (current != i) Characters.Move(current, i);
        }
    }

    private async Task RefreshThumbnailsAsync()
    {
        if (_capturing) return;
        _capturing = true;
        try
        {
            var targets = Characters.Where(c => c.IsOnline).Select(c => (c, c.Handle)).ToList();
            foreach (var (vm, hwnd) in targets)
            {
                var image = await Task.Run(() => WindowCapture.Capture(hwnd, 480));
                if (image is not null) vm.Thumbnail = image; // fenêtre réduite : on garde la dernière image
            }
        }
        finally { _capturing = false; }
    }

    // --- Tours de jeu -------------------------------------------------------------------

    private void OnWindowFlashed(IntPtr hwnd)
    {
        var vm = Characters.FirstOrDefault(c => c.Handle == hwnd);
        if (vm is null) return;
        _automation.OnTurnSignal(hwnd, vm.CharacterName, Settings);
    }

    private void MarkTurn(IntPtr hwnd)
    {
        _turnMarkedAt = DateTime.UtcNow;
        foreach (var c in Characters) c.IsTurn = c.Handle == hwnd;
    }

    // --- Actions utilisateur ------------------------------------------------------------

    private void ToggleLeader(CharacterViewModel? vm)
    {
        if (vm?.CharacterName is not { } name) return;
        Settings.LeaderName = Settings.IsLeader(name) ? null : name;
        foreach (var c in Characters)
        {
            c.IsLeader = c.IsConnected && Settings.IsLeader(c.CharacterName);
            c.RefreshAutomationState();
        }
        SaveSettings();
        OnPropertyChanged(nameof(StatusText));
        Log(Settings.LeaderName is null ? "Plus de meneur défini" : $"Nouveau meneur : {name}");
    }

    private void Move(CharacterViewModel? vm, int delta)
    {
        if (vm is null) return;
        var connected = Characters.Where(c => c.IsConnected).ToList();
        var index = connected.IndexOf(vm);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= connected.Count) return;

        (connected[index], connected[target]) = (connected[target], connected[index]);
        for (var i = 0; i < connected.Count; i++)
            Settings.GetOrCreateProfile(connected[i].CharacterName!).Order = i;
        SaveSettings();
        Sort();
    }

    // --- Lancement via Ankama Launcher ----------------------------------------------------

    /// <summary>Demande une instance au launcher pour ce personnage. Renvoie faux si la demande a échoué.</summary>
    private bool Launch(CharacterViewModel vm)
    {
        if (vm.CharacterName is not { } name) return false;
        var error = GameLauncher.LaunchOne(Settings);
        if (error is not null)
        {
            Log($"Lancement impossible : {error}");
            return false;
        }
        _launchRequestedAt[name] = DateTime.UtcNow;
        vm.IsLaunching = true;
        Log($"Instance demandée au launcher pour {name} : connecte ce compte puis ce personnage dans la nouvelle fenêtre.");
        return true;
    }

    private void LaunchNewInstance()
    {
        var error = GameLauncher.LaunchOne(Settings);
        Log(error is null ? "Nouvelle instance demandée au launcher." : $"Lancement impossible : {error}");
    }

    /// <summary>Lance une instance par personnage hors ligne, avec une pause entre chaque pour ne pas saturer le launcher.</summary>
    private async Task LaunchAllAsync()
    {
        var targets = Characters.Where(c => !c.IsOnline && !c.IsLaunching).ToList();
        if (targets.Count == 0) return;

        _launchingAll = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            Log($"Lancement de {targets.Count} instance{(targets.Count > 1 ? "s" : "")}…");
            for (var i = 0; i < targets.Count; i++)
            {
                if (i > 0) await Task.Delay(Math.Clamp(Settings.LaunchIntervalMs, 1000, 30000));
                if (!Launch(targets[i])) break;
            }
        }
        finally
        {
            _launchingAll = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Retire un personnage hors ligne de la liste (ses réglages et son image sont supprimés).</summary>
    private void Forget(CharacterViewModel? vm)
    {
        if (vm is not { IsOnline: false, CharacterName: { } name }) return;
        Settings.Characters.Remove(name);
        if (Settings.IsLeader(name)) Settings.LeaderName = null;
        _launchRequestedAt.Remove(name);
        _portraits.Delete(name);
        SaveSettings();
        Characters.Remove(vm);
        OnPropertyChanged(nameof(OfflineCount));
        OnPropertyChanged(nameof(LaunchAllLabel));
        Log($"Personnage retiré de la liste : {name}");
    }

    private void OnHotkey(HotkeyAction action)
    {
        var ordered = Characters.Where(c => c.IsOnline && c.IsConnected).Select(c => c.Handle).ToList();
        IntPtr? target = action switch
        {
            HotkeyAction.NextCharacter => CharacterCycler.Next(ordered, NativeMethods.GetForegroundWindow(), +1),
            HotkeyAction.PreviousCharacter => CharacterCycler.Next(ordered, NativeMethods.GetForegroundWindow(), -1),
            HotkeyAction.FocusLeader => Characters.FirstOrDefault(c => c.IsLeader && c.IsOnline)?.Handle,
            _ => null,
        };
        if (target is { } hwnd) WindowController.Focus(hwnd);
    }

    private void RegisterHotkeys()
    {
        var failures = _messages.RegisterHotkeys(Settings.Hotkeys);
        HotkeyStatus = failures.Count == 0
            ? ""
            : $"Raccourci indisponible (déjà utilisé ou invalide) : {string.Join(", ", failures)}";
    }

    private void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        if (_dispatcher.CheckAccess()) Append();
        else _dispatcher.BeginInvoke(Append);

        void Append()
        {
            LogEntries.Insert(0, line);
            while (LogEntries.Count > MaxLogEntries) LogEntries.RemoveAt(LogEntries.Count - 1);
        }
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _thumbnailTimer.Stop();
        _rescanSoon.Stop();
        foreach (var c in Characters)
            if (c is { IsOnline: true, CharacterName: { } name, Thumbnail: { } image }) _portraits.Save(name, image);
        _messages.Dispose();
        _automation.Dispose();
    }
}
