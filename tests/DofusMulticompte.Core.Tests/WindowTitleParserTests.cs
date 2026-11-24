using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

public class ForeignClientTitleTests
{
    // Le titre reprend la langue du client : chaque classe doit être reconnue quelle que soit la langue.
    [Theory]
    [InlineData("Tikoko - Rogue - 3.7.14.18 - Release", 13)]
    [InlineData("Tikoko - Tymador - 3.7.14.18 - Release", 13)]
    [InlineData("Tikoko - Halsabschneider - 3.7.14.18 - Release", 13)]
    [InlineData("Tikoko - Ladino - 3.7.14.18 - Release", 13)]
    [InlineData("Tikoko - Foggernaut - 3.7.14.18 - Release", 15)]
    [InlineData("Tikoko - Yopuka - 3.7.14.18 - Release", 8)]
    [InlineData("Tikoko - Zurcarák - 3.7.14.18 - Release", 6)]
    [InlineData("Tikoko - Sacrógrito - 3.7.14.18 - Release", 11)]
    [InlineData("Tikoko - Übermagier - 3.7.14.18 - Release", 17)]
    [InlineData("Tikoko - Kilorf - 3.7.14.18 - Release", 18)]
    [InlineData("Tikoko - Speerschmied - 3.7.14.18 - Release", 20)]
    [InlineData("Tikoko - Forjalança - 3.7.14.18 - Beta", 20)]
    public void Recognizes_class_names_of_every_client_language(string title, int breedId) =>
        Assert.Equal(breedId, WindowTitleParser.Parse(title)!.KnownClass!.BreedId);
}

public class WindowTitleParserTests
{
    [Fact]
    public void Parse_TitreDofus3Connecte_ExtraitPseudoClasseVersion()
    {
        // Format relevé sur un vrai client Dofus 3 (pseudo fictif).
        var parsed = WindowTitleParser.Parse("Tikoko - Zobal - 3.7.13.17 - Release");

        Assert.NotNull(parsed);
        Assert.Equal("Tikoko", parsed.CharacterName);
        Assert.Equal("Zobal", parsed.ClassName);
        Assert.Equal("Zobal", parsed.KnownClass?.Name);
        Assert.Equal("3.7.13.17", parsed.Version);
        Assert.Equal("Release", parsed.Channel);
    }

    [Theory]
    [InlineData("Cra-Mignon - Crâ - 3.7.13.17 - Release", "Cra-Mignon", "Crâ")]
    [InlineData("Le-Sacri - Sacrieur - 3.8.0.1 - Beta", "Le-Sacri", "Sacrieur")]
    [InlineData("Xel - Xelor - 3.7.13.17 - Release", "Xel", "Xélor")]
    public void Parse_PseudosAvecTiretsEtClassesSansAccent(string title, string name, string className)
    {
        var parsed = WindowTitleParser.Parse(title);

        Assert.NotNull(parsed);
        Assert.Equal(name, parsed.CharacterName);
        Assert.Equal(className, parsed.KnownClass?.Name);
    }

    [Fact]
    public void Parse_ClasseInconnue_GardeLeTexteBrut()
    {
        var parsed = WindowTitleParser.Parse("Bob - Nouvelleclasse - 3.9.0.0 - Release");

        Assert.NotNull(parsed);
        Assert.Equal("Nouvelleclasse", parsed.ClassName);
        Assert.Null(parsed.KnownClass);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Dofus")]
    [InlineData("Dofus 3.7.13.17")]
    [InlineData("Ankama Launcher")]
    public void Parse_FenetreNonConnectee_RenvoieNull(string? title)
    {
        Assert.Null(WindowTitleParser.Parse(title));
    }
}
