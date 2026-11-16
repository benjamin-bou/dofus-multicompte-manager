using System.Collections.Concurrent;
using DofusMulticompte.App.Interop;
using DofusMulticompte.Core;

namespace DofusMulticompte.App.Services;

/// <summary>
/// Réagit au début de tour d'un personnage (signalé par le clignotement de sa fenêtre)
/// en passant son tour ou en mettant sa fenêtre au premier plan. Les actions sont
/// exécutées une par une sur un thread dédié, pour ne jamais mélanger deux envois de touches.
/// </summary>
public sealed class TurnAutomationService : IDisposable
{
    private sealed record WorkItem(IntPtr Hwnd, string CharacterName, TurnAction Action, string Key, InputMode Mode, int DelayMs);

    private readonly BlockingCollection<(string Label, Action Action)> _queue = new();
    private readonly SignalDebouncer<IntPtr> _debouncer = new();
    private readonly Thread _worker;
    private readonly Action<string> _log;

    /// <summary>Déclenché (sur le thread de travail) quand le tour d'un personnage commence.</summary>
    public event Action<IntPtr>? TurnStarted;

    public TurnAutomationService(Action<string> log)
    {
        _log = log;
        _worker = new Thread(Run) { IsBackground = true, Name = "TurnAutomation" };
        _worker.Start();
    }

    /// <summary>À appeler quand une fenêtre Dofus clignote. <paramref name="characterName"/> est null si personne n'est connecté.</summary>
    public void OnTurnSignal(IntPtr hwnd, string? characterName, AppSettings settings)
    {
        if (characterName is null) return;
        if (!_debouncer.TryAccept(hwnd, TimeSpan.FromMilliseconds(Math.Max(500, settings.TurnSignalCooldownMs))))
            return;

        TurnStarted?.Invoke(hwnd);
        var action = TurnPolicy.Decide(settings, characterName);
        if (action == TurnAction.None)
        {
            _log($"Début de tour : {characterName}");
            return;
        }

        var min = Math.Max(0, Math.Min(settings.PassDelayMinMs, settings.PassDelayMaxMs));
        var max = Math.Max(min, settings.PassDelayMaxMs);
        var delay = action == TurnAction.AutoPass ? Random.Shared.Next(min, max + 1) : 0;
        Enqueue(new WorkItem(hwnd, characterName, action, settings.PassTurnKey, settings.InputMode, delay));
    }

    /// <summary>Passe le tour immédiatement (bouton de test de l'interface).</summary>
    public void PassTurnNow(IntPtr hwnd, string characterName, AppSettings settings) =>
        Enqueue(new WorkItem(hwnd, characterName, TurnAction.AutoPass, settings.PassTurnKey, settings.InputMode, 0));

    public void BringToFront(IntPtr hwnd, string label) =>
        Enqueue(new WorkItem(hwnd, label, TurnAction.BringToFront, "", InputMode.Foreground, 0));

    /// <summary>Exécute une action sur le thread de travail, à la suite des autres (jamais en parallèle).</summary>
    public void Enqueue(string label, Action action) => _queue.Add((label, action));

    private void Enqueue(WorkItem item) => Enqueue(item.CharacterName, () => Execute(item));

    private void Run()
    {
        foreach (var (label, action) in _queue.GetConsumingEnumerable())
        {
            try { action(); }
            catch (Exception ex) { _log($"Erreur pour {label} : {ex.Message}"); }
        }
    }

    private void Execute(WorkItem item)
    {
        // Si on n'obtient pas le premier plan, Windows fait clignoter la fenêtre :
        // ce clignotement ne doit pas être pris pour un nouveau tour.
        _debouncer.Suppress(item.Hwnd, TimeSpan.FromSeconds(3));

        switch (item.Action)
        {
            case TurnAction.BringToFront:
                _log(WindowController.Focus(item.Hwnd)
                    ? $"Au premier plan : {item.CharacterName}"
                    : $"Impossible de mettre {item.CharacterName} au premier plan");
                break;

            case TurnAction.AutoPass:
                if (item.DelayMs > 0) Thread.Sleep(item.DelayMs);
                var previous = NativeMethods.GetForegroundWindow();
                var ok = WindowController.SendKey(item.Hwnd, item.Key, item.Mode);
                if (item.Mode == InputMode.Foreground && previous != IntPtr.Zero && previous != item.Hwnd)
                {
                    Thread.Sleep(80);
                    WindowController.Focus(previous);
                }
                _debouncer.Suppress(item.Hwnd, TimeSpan.FromSeconds(3));
                _log(ok
                    ? $"Tour passé automatiquement : {item.CharacterName} ({item.Key})"
                    : $"Échec du passage de tour pour {item.CharacterName} (touche « {item.Key} » invalide ou fenêtre inaccessible)");
                break;
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(2));
        _queue.Dispose();
    }
}
