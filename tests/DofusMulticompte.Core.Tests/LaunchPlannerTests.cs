using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class LaunchPlannerTests
{
    [Fact]
    public void BuildLaunchUri_FormatAttenduParLeLauncher() =>
        Assert.Equal("zaap://actual/games/game/dofus/dofus3?launch", LaunchPlanner.BuildLaunchUri("dofus", "dofus3"));

    [Theory]
    [InlineData("", "dofus3")]
    [InlineData("dofus", " ")]
    public void BuildLaunchUri_ParametresVides_Refuses(string game, string release) =>
        Assert.Throws<ArgumentException>(() => LaunchPlanner.BuildLaunchUri(game, release));

    [Fact]
    public void OfflineCharacters_ExclutLesConnectesEtRespecteLOrdre()
    {
        var s = new AppSettings();
        s.GetOrCreateProfile("Premier");
        s.GetOrCreateProfile("Deuxieme");
        s.GetOrCreateProfile("Troisieme");
        s.Characters["Premier"].Order = 2;
        s.Characters["Troisieme"].Order = 0;

        var offline = LaunchPlanner.OfflineCharacters(s, ["deuxieme"]); // casse différente

        Assert.Equal(["Troisieme", "Premier"], offline);
    }

    [Fact]
    public void OfflineCharacters_AucunPersonnageConnu_ListeVide() =>
        Assert.Empty(LaunchPlanner.OfflineCharacters(new AppSettings(), []));
}
