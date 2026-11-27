using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.Core.ResourcePaths;

public enum PathStatus
{
    /// <summary>Position pas encore lue.</summary>
    Unknown,
    /// <summary>Sur le chemin.</summary>
    OnPath,
    /// <summary>Sur une carte qui ne fait pas partie du chemin.</summary>
    OffPath,
    /// <summary>Dernière étape atteinte.</summary>
    Finished,
}

/// <summary>
/// Avancement sur un chemin. Une même carte peut revenir plusieurs fois (aller-retour dans une colonne) :
/// on avance donc toujours vers la PROCHAINE occurrence de la carte actuelle après l'étape en cours,
/// jamais vers une occurrence passée.
/// </summary>
public sealed class PathProgress(IReadOnlyList<MapPosition> steps)
{
    /// <summary>Étape où l'on se trouve (index dans <see cref="Steps"/>).</summary>
    public int Index { get; private set; }
    public PathStatus Status { get; private set; } = PathStatus.Unknown;
    public MapPosition? Current { get; private set; }
    public IReadOnlyList<MapPosition> Steps { get; } = steps;

    public MapPosition? Next => Index + 1 < Steps.Count ? Steps[Index + 1] : null;

    /// <summary>Met à jour l'avancement avec la position lue en jeu. Renvoie vrai si quelque chose a changé.</summary>
    public bool Update(MapPosition position)
    {
        var before = (Index, Status, Current);
        Current = position;
        if (Steps.Count == 0) return false;

        if (Status == PathStatus.Unknown)
        {
            // Première lecture : on se place sur la première occurrence de la carte, sinon on vise le départ.
            var first = IndexOf(position, 0);
            Index = first >= 0 ? first : 0;
            Status = first >= 0 ? PathStatus.OnPath : PathStatus.OffPath;
        }
        else if (Steps[Index] == position)
        {
            Status = PathStatus.OnPath; // toujours sur l'étape en cours
        }
        else
        {
            // Plus loin sur le chemin (y compris en ayant sauté des étapes) ; sinon retour en arrière.
            var ahead = IndexOf(position, Index + 1);
            var behind = ahead < 0 ? LastIndexOf(position, Index - 1) : -1;
            if (ahead >= 0) { Index = ahead; Status = PathStatus.OnPath; }
            else if (behind >= 0) { Index = behind; Status = PathStatus.OnPath; }
            else Status = PathStatus.OffPath;
        }

        if (Status == PathStatus.OnPath && Index == Steps.Count - 1) Status = PathStatus.Finished;
        return before != (Index, Status, Current);
    }

    /// <summary>Carte à rejoindre : l'étape suivante, ou l'étape en cours si l'on s'en est écarté.</summary>
    public MapPosition? Target => Status switch
    {
        PathStatus.OffPath => Steps[Index],
        PathStatus.Finished => null,
        _ => Next,
    };

    public void JumpTo(int index)
    {
        Index = Math.Clamp(index, 0, Math.Max(0, Steps.Count - 1));
        Status = Current == Steps[Index] ? PathStatus.OnPath : PathStatus.OffPath;
    }

    private int IndexOf(MapPosition p, int from)
    {
        for (var i = Math.Max(0, from); i < Steps.Count; i++) if (Steps[i] == p) return i;
        return -1;
    }

    private int LastIndexOf(MapPosition p, int from)
    {
        for (var i = Math.Min(from, Steps.Count - 1); i >= 0; i--) if (Steps[i] == p) return i;
        return -1;
    }
}
