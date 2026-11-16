using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class TurnPolicyTests
{
    private static AppSettings Settings(bool followers, bool leader, bool front = false) => new()
    {
        LeaderName = "Meneur",
        AutoPassFollowers = followers,
        AutoPassLeader = leader,
        BringTurnWindowToFront = front,
    };

    [Fact]
    public void ToutDecoche_AucuneAction()
    {
        var s = Settings(false, false);
        Assert.Equal(TurnAction.None, TurnPolicy.Decide(s, "Meneur"));
        Assert.Equal(TurnAction.None, TurnPolicy.Decide(s, "Suiveur"));
    }

    [Fact]
    public void TousSaufMeneur_PasseLesSuiveursPasLeMeneur()
    {
        var s = Settings(followers: true, leader: false);
        Assert.Equal(TurnAction.AutoPass, TurnPolicy.Decide(s, "Suiveur"));
        Assert.Equal(TurnAction.None, TurnPolicy.Decide(s, "Meneur"));
    }

    [Fact]
    public void MeneurAussi_PasseTout()
    {
        var s = Settings(followers: true, leader: true);
        Assert.Equal(TurnAction.AutoPass, TurnPolicy.Decide(s, "Suiveur"));
        Assert.Equal(TurnAction.AutoPass, TurnPolicy.Decide(s, "Meneur"));
    }

    [Fact]
    public void MeneurSeul_PasseUniquementLeMeneur()
    {
        var s = Settings(followers: false, leader: true);
        Assert.Equal(TurnAction.None, TurnPolicy.Decide(s, "Suiveur"));
        Assert.Equal(TurnAction.AutoPass, TurnPolicy.Decide(s, "Meneur"));
    }

    [Fact]
    public void MeneurReconnuSansTenirCompteDeLaCasse()
    {
        var s = Settings(followers: true, leader: false);
        Assert.Equal(TurnAction.None, TurnPolicy.Decide(s, "MENEUR"));
    }

    [Fact]
    public void PremierPlan_SAppliqueQuandLeTourNEstPasPasse()
    {
        var s = Settings(followers: true, leader: false, front: true);
        Assert.Equal(TurnAction.BringToFront, TurnPolicy.Decide(s, "Meneur"));
        Assert.Equal(TurnAction.AutoPass, TurnPolicy.Decide(s, "Suiveur"));
    }

    [Fact]
    public void PersonnageExclu_NEstJamaisPasse()
    {
        var s = Settings(followers: true, leader: true, front: true);
        s.GetOrCreateProfile("Suiveur").IncludedInAutomation = false;

        Assert.False(TurnPolicy.ShouldAutoPass(s, "Suiveur"));
        Assert.Equal(TurnAction.BringToFront, TurnPolicy.Decide(s, "Suiveur"));
    }

    [Fact]
    public void SansMeneur_TousSontDesSuiveurs()
    {
        var s = Settings(followers: true, leader: false);
        s.LeaderName = null;
        Assert.Equal(TurnAction.AutoPass, TurnPolicy.Decide(s, "Meneur"));
    }

    [Fact]
    public void InstanceNonConnectee_AucuneAction()
    {
        var s = Settings(true, true, true);
        Assert.Equal(TurnAction.None, TurnPolicy.Decide(s, null));
    }
}
