using DofusMulticompte.Core;

namespace DofusMulticompte.Core.Tests;

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
