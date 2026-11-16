namespace DofusMulticompte.Core;

public enum NotificationKind { Success, Progress, Warning, Error }

/// <summary>Morceau de texte d'une notification ; <c>Highlight</c> le met en valeur (pseudo, compteur).</summary>
public readonly record struct TextPart(string Text, bool Highlight = false);

/// <summary>Contenu d'une notification, indépendant de son affichage.</summary>
public sealed record NotificationContent(NotificationKind Kind, string Title, IReadOnlyList<TextPart> Message, double? Progress);

/// <summary>
/// Groupe de lancements proches dans le temps (« Tout lancer », ou plusieurs « Lancer » d'affilée) :
/// une seule notification suit sa progression, « 1/3 connectés », « 2/3 »…
/// </summary>
public sealed class LaunchBatch
{
    /// <summary>Un nouveau lancement rejoint le groupe en cours s'il n'est pas terminé et date de moins de 5 min.</summary>
    public static readonly TimeSpan JoinWindow = TimeSpan.FromMinutes(5);

    private readonly List<string> _connectedNames = [];

    public LaunchBatch(DateTime startedAt)
    {
        Id = Guid.NewGuid().ToString("N");
        StartedAt = startedAt;
    }

    public string Id { get; }
    public DateTime StartedAt { get; }
    public int Total { get; private set; }
    public int Connected => _connectedNames.Count;
    public int Retries { get; private set; }
    public IReadOnlyList<string> ConnectedNames => _connectedNames;
    public bool IsComplete => Total > 0 && Connected >= Total;

    public bool CanJoin(DateTime now) => !IsComplete && now - StartedAt < JoinWindow;

    public void AddLaunch() => Total++;

    public void AddRetry() => Retries++;

    /// <summary>
    /// Note qu'une instance du groupe est entrée en jeu et renvoie la notification correspondante.
    /// <paramref name="retriesForThisInstance"/> : relances subies par cette instance-là (0 si connectée du premier coup).
    /// </summary>
    public NotificationContent OnConnected(string characterName, int retriesForThisInstance = 0)
    {
        _connectedNames.Add(characterName);
        if (Total < Connected) Total = Connected; // garde-fou : jamais « 3/2 »

        // Les relances d'un personnage ne concernent que lui ; le récapitulatif final donne le total du groupe.
        var own = AfterRetries(retriesForThisInstance);

        if (Total <= 1)
        {
            List<TextPart> single = [new(characterName, true), new(" est en jeu" + own + ".")];
            return new(NotificationKind.Success, "Connexion réussie", single, 1.0);
        }

        if (IsComplete)
        {
            var total = Retries switch { 0 => "", 1 => " (1 relance au total)", _ => $" ({Retries} relances au total)" };
            List<TextPart> all = [new($"{Connected}/{Total}", true), new(" personnages en jeu" + total + " : "), new(string.Join(", ", _connectedNames), true)];
            return new(NotificationKind.Success, "Tous connectés", all, 1.0);
        }

        List<TextPart> partial = [new(characterName, true), new(" est en jeu" + own + " · "), new($"{Connected}/{Total}", true), new(" connectés.")];
        return new(NotificationKind.Progress, "Connexions en cours", partial, Connected / (double)Total);
    }

    /// <summary>Rien du tout si la connexion a réussi du premier coup.</summary>
    private static string AfterRetries(int retries) => retries switch
    {
        <= 0 => "",
        1 => " après 1 relance",
        _ => $" après {retries} relances",
    };

    /// <summary>Notification brève quand une instance du groupe reste bloquée et doit être relancée.</summary>
    public static NotificationContent RelaunchNotice(string? characterName, string reason, int attempt)
    {
        List<TextPart> parts =
        [
            characterName is null ? new("Une instance") : new(characterName, true),
            new($" {reason}. Relance, essai "),
            new(attempt.ToString(), true),
            new("."),
        ];
        return new(NotificationKind.Warning, "Connexion bloquée", parts, null);
    }
}
