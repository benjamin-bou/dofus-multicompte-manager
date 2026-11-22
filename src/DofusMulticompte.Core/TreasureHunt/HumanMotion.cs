namespace DofusMulticompte.Core.TreasureHunt;

/// <summary>Un point du trajet de la souris, et le temps à attendre avant le suivant.</summary>
public readonly record struct PathPoint(double X, double Y, int DelayMs);

/// <summary>
/// Délais et trajets de souris d'allure humaine : durées variables avec quelques pauses plus
/// longues, trajectoires courbes qui accélèrent puis ralentissent, cible jamais exactement au même pixel.
/// </summary>
public sealed class HumanMotion(Random random)
{
    public HumanMotion() : this(Random.Shared) { }

    /// <summary>
    /// Délai entre <paramref name="minMs"/> et <paramref name="maxMs"/>, plus souvent vers le milieu
    /// (somme de deux tirages), avec de temps en temps une hésitation plus longue.
    /// </summary>
    public int Delay(int minMs, int maxMs)
    {
        if (maxMs <= minMs) return Math.Max(0, minMs);
        var t = (random.NextDouble() + random.NextDouble()) / 2;
        var ms = minMs + t * (maxMs - minMs);
        if (random.NextDouble() < 0.08) ms *= 1.4 + random.NextDouble() * 0.9;
        return (int)Math.Round(ms);
    }

    /// <summary>Point au hasard autour du centre visé, plus souvent près du centre (rayon en pixels).</summary>
    public (double X, double Y) Jitter(double x, double y, double radius)
    {
        var angle = random.NextDouble() * Math.PI * 2;
        var r = radius * Math.Sqrt(random.NextDouble()) * (0.35 + 0.65 * random.NextDouble());
        return (x + Math.Cos(angle) * r, y + Math.Sin(angle) * r);
    }

    /// <summary>
    /// Trajet de la souris de <paramref name="from"/> à <paramref name="to"/> : courbe de Bézier
    /// déviée d'un côté, vitesse en cloche, petits tremblements, et parfois un léger dépassement
    /// corrigé à la fin. Le dernier point est exactement la cible.
    /// </summary>
    public IReadOnlyList<PathPoint> MousePath((double X, double Y) from, (double X, double Y) to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 2) return [new PathPoint(to.X, to.Y, Delay(8, 20))];

        // Durée selon la loi de Fitts (plus loin = plus long, mais pas proportionnellement).
        var durationMs = (110 + 95 * Math.Log2(1 + distance / 12)) * (0.85 + random.NextDouble() * 0.35);
        var steps = (int)Math.Clamp(distance / (9 + random.NextDouble() * 6), 10, 70);

        // Points de contrôle décalés perpendiculairement au trajet : la courbe part d'un côté.
        var nx = -dy / distance;
        var ny = dx / distance;
        var bend = distance * (0.04 + random.NextDouble() * 0.16) * (random.NextDouble() < 0.5 ? -1 : 1);
        var c1 = (X: from.X + dx * 0.3 + nx * bend, Y: from.Y + dy * 0.3 + ny * bend);
        var c2 = (X: from.X + dx * 0.75 + nx * bend * 0.4, Y: from.Y + dy * 0.75 + ny * bend * 0.4);

        // Dépassement occasionnel sur les longs trajets, rattrapé ensuite.
        var overshoot = distance > 250 && random.NextDouble() < 0.25;
        var end = overshoot
            ? (X: to.X + dx / distance * (3 + random.NextDouble() * 6), Y: to.Y + dy / distance * (3 + random.NextDouble() * 6))
            : to;

        var points = new List<PathPoint>(steps + 4);
        var stepDelay = durationMs / steps;
        for (var i = 1; i <= steps; i++)
        {
            var t = Ease((double)i / steps);
            var (x, y) = Bezier(from, c1, c2, end, t);
            if (i < steps)
            {
                x += (random.NextDouble() - 0.5) * 1.2;
                y += (random.NextDouble() - 0.5) * 1.2;
            }
            points.Add(new PathPoint(x, y, (int)Math.Max(1, Math.Round(stepDelay * (0.7 + random.NextDouble() * 0.6)))));
        }

        if (overshoot)
        {
            var corrections = 3 + random.Next(3);
            for (var i = 1; i <= corrections; i++)
            {
                var t = (double)i / corrections;
                points.Add(new PathPoint(end.X + (to.X - end.X) * t, end.Y + (to.Y - end.Y) * t, Delay(12, 28)));
            }
        }
        points[^1] = new PathPoint(to.X, to.Y, points[^1].DelayMs);
        return points;
    }

    // Accélère pendant les 40 premiers % du temps, puis ralentit plus longuement en approchant de la cible.
    // Les deux phases se raccordent sans à-coup de vitesse (42 % du trajet parcouru à la bascule).
    private static double Ease(double t)
    {
        const double t0 = 0.4, p0 = 0.42;
        return t < t0
            ? p0 * (t / t0) * (t / t0)
            : p0 + (1 - p0) * (1 - Math.Pow(1 - (t - t0) / (1 - t0), 2.2));
    }

    private static (double X, double Y) Bezier((double X, double Y) p0, (double X, double Y) p1, (double X, double Y) p2, (double X, double Y) p3, double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;
        return (a * p0.X + b * p1.X + c * p2.X + d * p3.X, a * p0.Y + b * p1.Y + c * p2.Y + d * p3.Y);
    }
}
