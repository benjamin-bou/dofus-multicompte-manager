using DofusMulticompte.Core.TreasureHunt;

namespace DofusMulticompte.Core.Tests;

public class TreasureHuntTests
{
    private static readonly HuntPoi Teapot = new(1021, "Théière à rayures");
    private static readonly HuntPoi Doll = new(993, "Poupée koalak");
    private static readonly HuntPoi Rock = new(999, "Rocher taillé en arètes de poisson");
    private static readonly HuntPoi Skull = new(1, "Crâne de Crâ");
    private static readonly HuntPoi Skull2 = new(2, "Crâne de Craqueleur");

    private static List<HuntMapCandidate> Candidates() =>
    [
        new(new MapPosition(-19, -36), 6, [Doll]),
        new(new MapPosition(-20, -36), 5, [Teapot, Doll, Rock]),
        new(new MapPosition(-17, -36), 8, [Teapot]),
    ];

    [Theory]
    [InlineData("Théière à rayures", "theiere a rayures")]
    [InlineData("  Œuf   d’Abraknyde ! ", "oeuf d abraknyde")]
    [InlineData("Rocher taillé en arètes", "rocher taille en aretes")]
    public void Normalize_strips_accents_case_and_punctuation(string input, string expected) =>
        Assert.Equal(expected, ClueMatcher.Normalize(input));

    [Fact]
    public void Resolve_picks_the_nearest_map_with_the_clue()
    {
        var r = ClueMatcher.Resolve("Poupée koalak", Candidates());
        Assert.Equal(ClueStatus.Found, r.Status);
        Assert.Equal(new MapPosition(-20, -36), r.Target);
        Assert.Equal(5, r.Distance);
        Assert.Equal("/travel -20,-36", r.TravelCommand);
    }

    [Fact]
    public void Resolve_tolerates_ocr_mistakes()
    {
        var r = ClueMatcher.Resolve("Theiere a rayvres", Candidates());
        Assert.Equal(ClueStatus.Found, r.Status);
        Assert.Equal(Teapot.Id, r.Poi!.Id);
    }

    [Fact]
    public void Resolve_flags_phorreur_clues()
    {
        Assert.Equal(ClueStatus.Phorreur, ClueMatcher.Resolve("Phorreur sournois", Candidates()).Status);
    }

    [Fact]
    public void Resolve_rejects_unreadable_text()
    {
        Assert.Equal(ClueStatus.UnknownClue, ClueMatcher.Resolve("xq zzv", Candidates()).Status);
    }

    [Fact]
    public void Resolve_reports_a_known_clue_out_of_range()
    {
        var r = ClueMatcher.Resolve("Crâne de Crâ", Candidates(), catalog: [Teapot, Doll, Rock, Skull]);
        Assert.Equal(ClueStatus.NotInRange, r.Status);
        Assert.Equal(Skull.Id, r.Poi!.Id);
        Assert.Null(r.Target);
    }

    [Fact]
    public void Identify_refuses_when_two_names_are_too_close()
    {
        // « Statue de Bouftox » est à une lettre des deux noms : mieux vaut s'arrêter que deviner.
        HuntPoi a = new(10, "Statue de Bouftou"), b = new(11, "Statue de Boufton");
        Assert.Null(ClueMatcher.Identify("Statue de Bouftox", [a, b]));
        Assert.Equal(a.Id, ClueMatcher.Identify("Statue de Bouftou", [a, b])!.Value.Poi.Id);
        Assert.Equal(Skull.Id, ClueMatcher.Identify("Crâne de Crâ", [Skull, Skull2])!.Value.Poi.Id);
    }

    [Theory]
    [InlineData("-83, -48", -83, -48)]
    [InlineData("Serre du Royalmouth - Quatrième salle\n-83, -48", -83, -48)]
    [InlineData("—12,7", -12, 7)]
    [InlineData("4 , - 19", 4, -19)]
    public void PositionParser_reads_coordinates(string text, int x, int y) =>
        Assert.Equal(new MapPosition(x, y), PositionParser.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("Quatrième salle")]
    [InlineData("1234, 5")]
    public void PositionParser_ignores_text_without_coordinates(string text) =>
        Assert.Null(PositionParser.Parse(text));

    [Fact]
    public void MapPosition_steps_in_each_direction()
    {
        var p = new MapPosition(0, 0);
        Assert.Equal(new MapPosition(3, 0), p.Step(HuntDirection.Right, 3));
        Assert.Equal(new MapPosition(0, 2), p.Step(HuntDirection.Down, 2));
        Assert.Equal(new MapPosition(-1, 0), p.Step(HuntDirection.Left));
        Assert.Equal(new MapPosition(0, -4), p.Step(HuntDirection.Up, 4));
    }

    [Fact]
    public void Delay_stays_in_a_sensible_range()
    {
        var motion = new HumanMotion(new Random(1));
        var delays = Enumerable.Range(0, 2000).Select(_ => motion.Delay(200, 400)).ToList();
        Assert.All(delays, d => Assert.InRange(d, 200, 400 * 2.3));
        Assert.True(delays.Distinct().Count() > 100, "les délais doivent varier");
        Assert.Contains(delays, d => d > 400); // quelques hésitations plus longues
    }

    [Fact]
    public void MousePath_ends_exactly_on_target_and_curves()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            var motion = new HumanMotion(new Random(seed));
            var path = motion.MousePath((100, 100), (900, 500));
            Assert.Equal((900.0, 500.0), (path[^1].X, path[^1].Y));
            Assert.InRange(path.Count, 10, 80);
            Assert.All(path, p => Assert.InRange(p.DelayMs, 1, 200));
            var total = path.Sum(p => p.DelayMs);
            Assert.InRange(total, 250, 1500);
            // Le trajet n'est pas une ligne droite : au moins un point s'écarte de la droite.
            var maxDeviation = path.Max(p => Math.Abs((p.X - 100) * 400 - (p.Y - 100) * 800) / Math.Sqrt(800 * 800 + 400 * 400));
            Assert.True(maxDeviation > 5, $"seed {seed} : trajet trop droit ({maxDeviation:F1} px)");
        }
    }

    [Fact]
    public void MousePath_is_never_the_same_twice()
    {
        var motion = new HumanMotion(new Random(7));
        var a = motion.MousePath((0, 0), (500, 300));
        var b = motion.MousePath((0, 0), (500, 300));
        Assert.NotEqual(a.Select(p => (Math.Round(p.X), Math.Round(p.Y))), b.Select(p => (Math.Round(p.X), Math.Round(p.Y))));
    }

    [Fact]
    public void Jitter_stays_within_radius()
    {
        var motion = new HumanMotion(new Random(3));
        for (var i = 0; i < 500; i++)
        {
            var (x, y) = motion.Jitter(50, 50, 6);
            Assert.True(Math.Sqrt((x - 50) * (x - 50) + (y - 50) * (y - 50)) <= 6.0001);
        }
    }
}
