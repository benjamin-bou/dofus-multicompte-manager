using DofusMulticompte.Core.Updates;

namespace DofusMulticompte.Core.Tests;

public class ReleaseFeedTests
{
    // Forme de la vraie réponse de l'API GitHub (champs utiles seulement), dépôt fictif.
    private const string Sample = """
        {
          "html_url": "https://github.com/proprietaire/depot/releases/tag/v1.2.0",
          "tag_name": "v1.2.0",
          "draft": false,
          "prerelease": false,
          "body": "Notes de version",
          "assets": [
            { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.invalid/notes.txt" },
            {
              "name": "MulticompteDofus.exe",
              "size": 71934976,
              "digest": "sha256:ADFF4C62E2AF0A005C19E88F305211EC892C04AD96BC4AE9A2C77B66FD65E7F8",
              "browser_download_url": "https://github.com/proprietaire/depot/releases/download/v1.2.0/MulticompteDofus.exe"
            }
          ]
        }
        """;

    [Fact]
    public void Parse_LitVersionExecutableEtEmpreinte()
    {
        var r = ReleaseFeed.Parse(Sample);

        Assert.NotNull(r);
        Assert.Equal(new Version(1, 2, 0), r.Version);
        Assert.Equal("v1.2.0", r.Tag);
        Assert.EndsWith("/v1.2.0/MulticompteDofus.exe", r.AssetUrl);
        Assert.Equal(71934976, r.AssetSize);
        Assert.Equal("adff4c62e2af0a005c19e88f305211ec892c04ad96bc4ae9a2c77b66fd65e7f8", r.Sha256);
        Assert.Equal("Notes de version", r.Notes);
    }

    [Fact]
    public void Parse_SansExecutable_ReleaseSansTelechargement()
    {
        var r = ReleaseFeed.Parse("""{ "tag_name": "v1.3.0", "assets": [] }""");
        Assert.NotNull(r);
        Assert.Null(r.AssetUrl);
        Assert.Null(r.Sha256);
    }

    [Theory]
    [InlineData("""{ "tag_name": "v2.0.0", "prerelease": true }""")]
    [InlineData("""{ "tag_name": "v2.0.0", "draft": true }""")]
    [InlineData("""{ "tag_name": "nouvelle-version" }""")]
    [InlineData("pas du json")]
    [InlineData("[]")]
    public void Parse_PreversionBrouillonOuIllisible_Null(string json) => Assert.Null(ReleaseFeed.Parse(json));

    [Theory]
    [InlineData("proprietaire/depot", true)]
    [InlineData("depot", false)]
    [InlineData("/depot", false)]
    [InlineData("a/b/c", false)]
    [InlineData("", false)]
    public void IsValidRepository(string repo, bool expected) => Assert.Equal(expected, ReleaseFeed.IsValidRepository(repo));

    [Fact]
    public void LatestReleaseApi_AdresseDeLApi() =>
        Assert.Equal("https://api.github.com/repos/proprietaire/depot/releases/latest", ReleaseFeed.LatestReleaseApi("proprietaire/depot"));

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V10.0.3", "10.0.3")]
    public void ParseTag_Valide(string tag, string expected) => Assert.Equal(Version.Parse(expected), ReleaseFeed.ParseTag(tag));

    [Theory]
    [InlineData("v1.2.0-beta")]
    [InlineData("")]
    [InlineData("latest")]
    public void ParseTag_Refuse(string tag) => Assert.Null(ReleaseFeed.ParseTag(tag));

    [Theory]
    [InlineData("1.1.0.0", true)]   // version d'assembly à 4 nombres
    [InlineData("1.2.0.0", false)]  // même version : pas de mise à jour
    [InlineData("1.3.0", false)]    // version locale plus récente (build de développement)
    [InlineData("1.0.0", true)]
    public void IsNewer(string current, bool expected)
    {
        var r = ReleaseFeed.Parse(Sample)!;
        Assert.Equal(expected, ReleaseFeed.IsNewer(r, Version.Parse(current)));
    }
}
