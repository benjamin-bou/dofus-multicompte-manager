namespace DofusMulticompte.Core;

/// <summary>
/// Filtre les signaux répétés pour une même clé (une fenêtre Dofus) : Dofus peut faire
/// clignoter sa fenêtre plusieurs fois, et Windows fait aussi clignoter une fenêtre quand
/// on lui refuse le premier plan. Sans ce filtre, on passerait le tour plusieurs fois.
/// </summary>
public sealed class SignalDebouncer<TKey> where TKey : notnull
{
    private readonly Dictionary<TKey, DateTime> _blockedUntil = new();
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();

    public SignalDebouncer(Func<DateTime>? now = null) => _now = now ?? (() => DateTime.UtcNow);

    /// <summary>Renvoie vrai si le signal doit être traité, et bloque la clé pendant <paramref name="cooldown"/>.</summary>
    public bool TryAccept(TKey key, TimeSpan cooldown)
    {
        lock (_lock)
        {
            var now = _now();
            if (_blockedUntil.TryGetValue(key, out var until) && now < until) return false;
            _blockedUntil[key] = now + cooldown;
            return true;
        }
    }

    /// <summary>Ignore les signaux de cette clé pendant <paramref name="duration"/> (ex. après avoir agi dessus).</summary>
    public void Suppress(TKey key, TimeSpan duration)
    {
        lock (_lock)
        {
            var until = _now() + duration;
            if (!_blockedUntil.TryGetValue(key, out var current) || current < until)
                _blockedUntil[key] = until;
        }
    }
}
