using System.Windows.Media;
using System.Windows.Media.Imaging;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.ViewModels;

/// <summary>Une instance Dofus détectée.</summary>
public sealed class CharacterViewModel : ObservableObject
{
    private readonly MainViewModel _owner;
    private string _title = "";
    private ParsedTitle? _parsed;
    private bool _isLeader;
    private bool _isActive;
    private bool _isTurn;
    private BitmapSource? _thumbnail;

    private bool _isLaunching;

    /// <summary>Instance Dofus ouverte.</summary>
    public CharacterViewModel(MainViewModel owner, IntPtr hwnd, int processId)
    {
        _owner = owner;
        Handle = hwnd;
        ProcessId = processId;
    }

    /// <summary>Personnage connu dont aucune instance n'est ouverte.</summary>
    public CharacterViewModel(MainViewModel owner, string characterName, string? className)
    {
        _owner = owner;
        _parsed = new ParsedTitle(characterName, className ?? "", DofusClasses.Find(className), "", "");
    }

    public IntPtr Handle { get; }
    public int ProcessId { get; }

    /// <summary>Vrai si une fenêtre Dofus correspond à cette carte.</summary>
    public bool IsOnline => Handle != IntPtr.Zero;

    /// <summary>Un lancement a été demandé au launcher pour ce personnage.</summary>
    public bool IsLaunching
    {
        get => _isLaunching;
        set
        {
            if (Set(ref _isLaunching, value)) OnPropertyChanged(nameof(AutomationSummary));
        }
    }

    public string Title
    {
        get => _title;
        private set => Set(ref _title, value);
    }

    /// <summary>Met à jour le titre ; renvoie vrai si le personnage connecté a changé.</summary>
    public bool UpdateTitle(string title)
    {
        if (title == _title) return false;
        Title = title;
        var parsed = WindowTitleParser.Parse(title);
        var changed = parsed?.CharacterName != _parsed?.CharacterName;
        _parsed = parsed;
        OnPropertyChanged(nameof(CharacterName));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ClassName));
        OnPropertyChanged(nameof(ClassInitial));
        OnPropertyChanged(nameof(ClassBrush));
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(IncludedInAutomation));
        OnPropertyChanged(nameof(Order));
        RefreshAutomationState();
        return changed;
    }

    /// <summary>Pseudo du personnage, ou null tant qu'aucun personnage n'est connecté sur cette instance.</summary>
    public string? CharacterName => _parsed?.CharacterName;
    public bool IsConnected => _parsed is not null;
    public string DisplayName => _parsed?.CharacterName ?? "Non connecté";
    public string ClassName => _parsed is null
        ? $"PID {ProcessId}"
        : _parsed.KnownClass?.Name ?? (string.IsNullOrWhiteSpace(_parsed.ClassName) ? "Classe inconnue" : _parsed.ClassName);
    public string ClassInitial => _parsed is null || string.IsNullOrWhiteSpace(_parsed.ClassName) ? "?" : ClassName[..1].ToUpperInvariant();

    public Brush ClassBrush
    {
        get
        {
            var hex = _parsed?.KnownClass?.AccentHex ?? "#5A6270";
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    private CharacterProfile? Profile => CharacterName is { } n ? _owner.Settings.GetOrCreateProfile(n) : null;

    /// <summary>Niveau saisi par l'utilisateur (le client ne l'expose pas), mémorisé par pseudo.</summary>
    public string Level
    {
        get => Profile?.Level?.ToString() ?? "";
        set
        {
            if (Profile is not { } p) return;
            p.Level = int.TryParse(value, out var lvl) && lvl is >= 1 and <= 200 ? lvl : null;
            OnPropertyChanged();
            _owner.SaveSettings();
        }
    }

    public bool IncludedInAutomation
    {
        get => Profile?.IncludedInAutomation ?? false;
        set
        {
            if (Profile is not { } p) return;
            p.IncludedInAutomation = value;
            OnPropertyChanged();
            RefreshAutomationState();
            _owner.SaveSettings();
        }
    }

    public bool IsLeader
    {
        get => _isLeader;
        set
        {
            if (Set(ref _isLeader, value)) RefreshAutomationState();
        }
    }

    /// <summary>Fenêtre actuellement au premier plan.</summary>
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    /// <summary>C'est le tour de ce personnage (dernier signal de tour reçu).</summary>
    public bool IsTurn
    {
        get => _isTurn;
        set => Set(ref _isTurn, value);
    }

    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set => Set(ref _thumbnail, value);
    }

    public int Order => Profile?.Order ?? int.MaxValue;

    /// <summary>Résumé de ce qui se passera au début du tour de ce personnage.</summary>
    public string AutomationSummary => !IsOnline
        ? (IsLaunching ? "Lancement demandé au launcher…" : "Hors ligne")
        : TurnPolicy.Decide(_owner.Settings, CharacterName) switch
    {
        TurnAction.AutoPass => "Tour passé automatiquement",
        TurnAction.BringToFront => "Mis au premier plan à son tour",
        _ => IsConnected ? "Aucune action automatique" : "En attente de connexion",
    };

    public bool WillAutoPass => IsOnline && TurnPolicy.ShouldAutoPass(_owner.Settings, CharacterName);

    public void RefreshAutomationState()
    {
        OnPropertyChanged(nameof(AutomationSummary));
        OnPropertyChanged(nameof(WillAutoPass));
    }
}
