namespace DofusMulticompte.Core.TreasureHunt;

/// <summary>Direction d'une flèche de chasse, avec les valeurs attendues par l'API DofusDB.</summary>
public enum HuntDirection
{
    Right = 0,
    Down = 2,
    Left = 4,
    Up = 6,
}

/// <summary>Coordonnées d'une carte du monde.</summary>
public readonly record struct MapPosition(int X, int Y)
{
    public override string ToString() => $"{X},{Y}";

    /// <summary>Position atteinte en avançant de <paramref name="maps"/> cartes dans une direction (y augmente vers le bas).</summary>
    public MapPosition Step(HuntDirection direction, int maps = 1) => direction switch
    {
        HuntDirection.Right => new(X + maps, Y),
        HuntDirection.Left => new(X - maps, Y),
        HuntDirection.Down => new(X, Y + maps),
        HuntDirection.Up => new(X, Y - maps),
        _ => this,
    };
}

/// <summary>Élément de décor recherché dans une chasse (« Théière à rayures »…).</summary>
public sealed record HuntPoi(int Id, string Name);

/// <summary>Carte située dans la direction de la flèche, avec les éléments qu'on y trouve.</summary>
public sealed record HuntMapCandidate(MapPosition Position, int Distance, IReadOnlyList<HuntPoi> Pois);

public enum ClueStatus
{
    /// <summary>Carte trouvée.</summary>
    Found,
    /// <summary>Indice « Phorreur » : un PNJ mobile, absent des bases de données.</summary>
    Phorreur,
    /// <summary>Le texte lu ne correspond à aucun élément connu (lecture ratée, ou élément nouveau).</summary>
    UnknownClue,
    /// <summary>Élément reconnu, mais absent des cartes dans cette direction.</summary>
    NotInRange,
}

public sealed record ClueResolution(
    ClueStatus Status,
    HuntPoi? Poi = null,
    MapPosition? Target = null,
    int Distance = 0,
    double Score = 0)
{
    /// <summary>Commande de déplacement automatique du jeu vers la carte trouvée.</summary>
    public string? TravelCommand => Target is { } t ? TreasureHunt.TravelCommand.For(t) : null;
}

public static class TravelCommand
{
    public static string For(MapPosition position) => $"/travel {position.X},{position.Y}";
}
