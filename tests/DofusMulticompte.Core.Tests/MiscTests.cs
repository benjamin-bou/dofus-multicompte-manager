using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class SignalDebouncerTests
{
    [Fact]
    public void SignalRepete_IgnorePendantLeDelai()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var d = new SignalDebouncer<int>(() => now);

        Assert.True(d.TryAccept(1, TimeSpan.FromSeconds(2)));
        Assert.False(d.TryAccept(1, TimeSpan.FromSeconds(2)));
        Assert.True(d.TryAccept(2, TimeSpan.FromSeconds(2))); // autre fenêtre : indépendant

        now = now.AddSeconds(2.1);
        Assert.True(d.TryAccept(1, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void Suppress_BloqueLesSignauxApresUneAction()
    {
        var now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var d = new SignalDebouncer<int>(() => now);

        d.Suppress(1, TimeSpan.FromSeconds(3));
        now = now.AddSeconds(2);
        Assert.False(d.TryAccept(1, TimeSpan.FromSeconds(1)));
        now = now.AddSeconds(1.5);
        Assert.True(d.TryAccept(1, TimeSpan.FromSeconds(1)));
    }
}

public class KeyGestureTests
{
    [Theory]
    [InlineData("F1", KeyModifiers.None, "F1")]
    [InlineData("Ctrl+Right", KeyModifiers.Ctrl, "Right")]
    [InlineData("ctrl + shift + F2", KeyModifiers.Ctrl | KeyModifiers.Shift, "F2")]
    [InlineData("Alt+Maj+D1", KeyModifiers.Alt | KeyModifiers.Shift, "D1")]
    public void TryParse_Valide(string text, KeyModifiers mods, string key)
    {
        Assert.True(KeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(key, g.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Hyper+F1")]
    public void TryParse_Invalide(string text) => Assert.False(KeyGesture.TryParse(text, out _));

    [Theory]
    [InlineData("Souris4", KeyModifiers.None, MouseButton.X1)]
    [InlineData("Souris5", KeyModifiers.None, MouseButton.X2)]
    [InlineData("Molette", KeyModifiers.None, MouseButton.Middle)]
    [InlineData("Ctrl+souris5", KeyModifiers.Ctrl, MouseButton.X2)]
    [InlineData("Mouse4", KeyModifiers.None, MouseButton.X1)]
    public void TryParse_BoutonSouris(string text, KeyModifiers mods, MouseButton button)
    {
        Assert.True(KeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(button, g.MouseButton);
        Assert.True(g.IsMouse);
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("Ctrl+Right")]
    [InlineData("D4")]
    public void Clavier_NEstPasUnBoutonSouris(string text)
    {
        Assert.True(KeyGesture.TryParse(text, out var g));
        Assert.False(g.IsMouse);
    }

    [Fact]
    public void BoutonSouris_NomAllerRetour()
    {
        foreach (var b in new[] { MouseButton.X1, MouseButton.X2, MouseButton.Middle })
            Assert.Equal(b, KeyGesture.ParseMouseButton(KeyGesture.MouseButtonName(b)));
    }

    [Fact]
    public void ToString_OrdreStable() =>
        Assert.Equal("Ctrl+Alt+Shift+F5", new KeyGesture(KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Ctrl, "F5").ToString());
}

public class CharacterCyclerTests
{
    private static readonly IReadOnlyList<int> Windows = [10, 20, 30];

    [Theory]
    [InlineData(10, 1, 20)]
    [InlineData(30, 1, 10)]
    [InlineData(10, -1, 30)]
    [InlineData(20, -1, 10)]
    public void Next_TourneEnBoucle(int current, int step, int expected) =>
        Assert.Equal(expected, CharacterCycler.Next(Windows, current, step));

    [Fact]
    public void Next_FenetreHorsListe_PartDuDebutOuDeLaFin()
    {
        Assert.Equal(10, CharacterCycler.Next(Windows, 99, 1));
        Assert.Equal(30, CharacterCycler.Next(Windows, null, -1));
    }

    [Fact]
    public void Next_ListeVide_Null() => Assert.Null(CharacterCycler.Next<int>([], 1, 1));
}

public class SettingsStoreTests
{
    [Fact]
    public void SaveLoad_ConserveLesReglagesEtLaCasseDesPseudos()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dmc-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(Path.Combine(dir, "settings.json"));
            var s = new AppSettings { LeaderName = "Tikoko", AutoPassFollowers = true, PassTurnKey = "F2", InputMode = InputMode.Background };
            s.GetOrCreateProfile("Tikoko").Level = 42;
            store.Save(s);

            var loaded = store.Load();
            Assert.Equal("Tikoko", loaded.LeaderName);
            Assert.True(loaded.AutoPassFollowers);
            Assert.Equal("F2", loaded.PassTurnKey);
            Assert.Equal(InputMode.Background, loaded.InputMode);
            Assert.Equal(42, loaded.Characters["tikoko"].Level); // recherche insensible à la casse
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_FichierCorrompu_RenvoieDesReglagesParDefaut()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dmc-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "settings.json");
            File.WriteAllText(path, "{ pas du json");
            var loaded = new SettingsStore(path).Load();
            Assert.Null(loaded.LeaderName);
            Assert.True(File.Exists(path + ".corrompu"));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetOrCreateProfile_AjouteALaFinDeLOrdre()
    {
        var s = new AppSettings();
        Assert.Equal(0, s.GetOrCreateProfile("A").Order);
        Assert.Equal(1, s.GetOrCreateProfile("B").Order);
        Assert.Equal(0, s.GetOrCreateProfile("a").Order);
    }
}
