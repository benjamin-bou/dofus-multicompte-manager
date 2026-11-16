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
        FocusCommand = new RelayCommand(p => { if (p is CharacterViewModel c) WindowController.Focus(c.Handle); });
        MoveUpCommand = new RelayCommand(p => Move(p as CharacterViewModel, -1), p => p is CharacterViewModel { IsConnected: true });
        MoveDownCommand = new RelayCommand(p => Move(p as CharacterViewModel, +1), p => p is CharacterViewModel { IsConnected: true });
        TestPassCommand = new RelayCommand(
            p => { if (p is CharacterViewModel { CharacterName: { } n } c) _automation.PassTurnNow(c.Handle, n, Settings); },
            p => p is CharacterViewModel { IsConnected: true });
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
    public ICommand ClearLogCommand { get; }

    public string StatusText
    {
        get
        {
            var total = Characters.Count;
            var connected = Characters.Count(c => c.IsConnected);
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

        for (var i = Characters.Count - 1; i >= 0; i--)
        {
            if (seen.Contains(Characters[i].Handle)) continue;
            Log($"Instance fermée : {Characters[i].DisplayName}");
            Characters.RemoveAt(i);
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
                needsSort = true;
            }
            else if (vm.UpdateTitle(w.Title))
            {
                Log(vm.IsConnected ? $"Personnage connecté : {vm.DisplayName} ({vm.ClassName})" : $"Personnage déconnecté (PID {w.ProcessId})");
                needsSort = true;
            }
        }

        var foreground = NativeMethods.GetForegroundWindow();
        // Un tour de Dofus ne dépasse pas une minute : passé ce délai, le badge « Son tour » est périmé.
        var turnExpired = DateTime.UtcNow - _turnMarkedAt > TurnBadgeDuration;
        foreach (var c in Characters)
        {
            c.IsLeader = c.IsConnected && Settings.IsLeader(c.CharacterName);
            c.IsActive = c.Handle == foreground;
            if (turnExpired) c.IsTurn = false;
        }

        if (needsSort) Sort();
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasCharacters));
    }

    private void Sort()
    {
        var ordered = Characters
            .OrderBy(c => c.IsConnected ? 0 : 1)
            .ThenBy(c => c.Order)
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
            var targets = Characters.Select(c => (c, c.Handle)).ToList();
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

    private void OnHotkey(HotkeyAction action)
    {
        var ordered = Characters.Where(c => c.IsConnected).Select(c => c.Handle).ToList();
        IntPtr? target = action switch
        {
            HotkeyAction.NextCharacter => CharacterCycler.Next(ordered, NativeMethods.GetForegroundWindow(), +1),
            HotkeyAction.PreviousCharacter => CharacterCycler.Next(ordered, NativeMethods.GetForegroundWindow(), -1),
            HotkeyAction.FocusLeader => Characters.FirstOrDefault(c => c.IsLeader)?.Handle,
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
        _messages.Dispose();
        _automation.Dispose();
    }
}
