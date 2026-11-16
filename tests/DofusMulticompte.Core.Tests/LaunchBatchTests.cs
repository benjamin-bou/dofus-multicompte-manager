using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class LaunchBatchTests
{
    private static string Text(NotificationContent n) => string.Concat(n.Message.Select(p => p.Text));

    [Fact]
    public void UnSeulLancement_ConnexionReussie()
    {
        var b = new LaunchBatch(DateTime.UtcNow);
        b.AddLaunch();

        var n = b.OnConnected("Tikoko");

        Assert.Equal(NotificationKind.Success, n.Kind);
        Assert.Equal("Connexion réussie", n.Title);
        Assert.Equal("Tikoko est en jeu.", Text(n));
        Assert.True(n.Message[0].Highlight);
        Assert.True(b.IsComplete);
    }

    [Fact]
    public void PlusieursLancements_ProgressionPuisTousConnectes()
    {
        var b = new LaunchBatch(DateTime.UtcNow);
        b.AddLaunch(); b.AddLaunch(); b.AddLaunch();

        var first = b.OnConnected("Tikoko");
        Assert.Equal(NotificationKind.Progress, first.Kind);
        Assert.Equal("Tikoko est en jeu · 1/3 connectés.", Text(first));
        Assert.Equal(1 / 3.0, first.Progress!.Value, 3);

        b.OnConnected("Brindille");
        var last = b.OnConnected("Pikel");
        Assert.Equal(NotificationKind.Success, last.Kind);
        Assert.Equal("Tous connectés", last.Title);
        Assert.Equal("3/3 personnages en jeu : Tikoko, Brindille, Pikel", Text(last));
    }

    [Fact]
    public void Relances_MentionneesDansLaConnexion()
    {
        var b = new LaunchBatch(DateTime.UtcNow);
        b.AddLaunch();
        b.AddRetry(); b.AddRetry();

        Assert.Equal("Tikoko est en jeu après 2 relances.", Text(b.OnConnected("Tikoko", retriesForThisInstance: 2)));
    }

    [Fact]
    public void ConnexionDuPremierCoup_JamaisDeMentionDeRelance()
    {
        var b = new LaunchBatch(DateTime.UtcNow);
        b.AddLaunch();

        Assert.DoesNotContain("relance", Text(b.OnConnected("Tikoko", retriesForThisInstance: 0)));
    }

    [Fact]
    public void Groupe_LesRelancesDUnPersonnageNeSontPasAttribueesAuxAutres()
    {
        var b = new LaunchBatch(DateTime.UtcNow);
        b.AddLaunch(); b.AddLaunch(); b.AddLaunch();
        b.AddRetry(); // Tikoko a été relancé une fois

        Assert.Equal("Tikoko est en jeu après 1 relance · 1/3 connectés.", Text(b.OnConnected("Tikoko", 1)));
        Assert.Equal("Brindille est en jeu · 2/3 connectés.", Text(b.OnConnected("Brindille", 0)));
        Assert.Equal("3/3 personnages en jeu (1 relance au total) : Tikoko, Brindille, Pikel", Text(b.OnConnected("Pikel", 0)));
    }

    [Fact]
    public void Groupe_SansAucuneRelance_RecapitulatifSansMention()
    {
        var b = new LaunchBatch(DateTime.UtcNow);
        b.AddLaunch(); b.AddLaunch();
        b.OnConnected("Tikoko");

        Assert.Equal("2/2 personnages en jeu : Tikoko, Brindille", Text(b.OnConnected("Brindille")));
    }

    [Fact]
    public void CanJoin_GroupeTermineOuTropAncien_Refuse()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var b = new LaunchBatch(start);
        b.AddLaunch();
        Assert.True(b.CanJoin(start.AddMinutes(1)));
        Assert.False(b.CanJoin(start.AddMinutes(6)));

        b.OnConnected("Tikoko");
        Assert.False(b.CanJoin(start.AddMinutes(1)));
    }

    [Fact]
    public void RelaunchNotice_AvecEtSansPseudo()
    {
        var named = LaunchBatch.RelaunchNotice("Tikoko", "bloquée sur les nuages de chargement", 3);
        Assert.Equal(NotificationKind.Warning, named.Kind);
        Assert.Equal("Tikoko bloquée sur les nuages de chargement. Relance, essai 3.", Text(named));

        var anonymous = LaunchBatch.RelaunchNotice(null, "gelée", 2);
        Assert.Equal("Une instance gelée. Relance, essai 2.", Text(anonymous));
    }
}
