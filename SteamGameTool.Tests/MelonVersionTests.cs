using steam_game_tool;

namespace SteamGameTool.Tests;

public class MelonVersionTests
{
    [Theory]
    [InlineData("v0.7.3", "0.7.3")]
    [InlineData("0.7.1+0a690474c6c66ad458c05a6aa9199ad91b87fa4b", "0.7.1")]
    [InlineData("0.8.0-ci.2548", "0.8.0")]
    [InlineData("0.7.3.2446", "0.7.3.2446")]
    public void ParsesTagsAndProductVersions(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), MelonVersions.Parse(text));

    [Theory]
    [InlineData("")]
    [InlineData("0.0.0.0")]
    [InlineData("nightly")]
    public void RejectsMissingVersions(string text) => Assert.Null(MelonVersions.Parse(text));

    [Theory]
    [InlineData("0.6.1", false)]
    [InlineData("0.7.1", false)]
    [InlineData("0.7.2", false)]
    [InlineData("0.7.3", true)]
    [InlineData("0.7.3.0", true)]
    [InlineData("0.7.4", true)]
    // CI 빌드는 번호와 상관없이 정식판으로 바꾼다. Halchemist 는 0.7.3.2497, Repit 은 0.8.0.2548 이었다.
    [InlineData("0.7.3.2446", false)]
    [InlineData("0.7.3.2497", false)]
    [InlineData("0.7.4.2600", false)]
    [InlineData("0.8.0.2548", false)]
    public void ModernRuleNeedsAtLeast073(string installed, bool ok) =>
        Assert.Equal(ok, MelonVersions.Modern.IsSatisfiedBy(Version.Parse(installed)));

    [Theory]
    [InlineData("0.7.3.2497", "0.7.3", false)]
    [InlineData("0.7.3", "0.7.3.2497", true)]
    [InlineData("0.7.3.2497", "0.7.3.2446", true)]
    [InlineData("0.7.3.2446", "0.7.3.2497", false)]
    [InlineData("0.8.0.2548", "0.7.3", true)]
    [InlineData("0.7.2", "0.7.3", false)]
    public void CiBuildIsBelowItsRelease(string installed, string minimum, bool ok) =>
        Assert.Equal(ok, MelonVersions.AtLeast(Version.Parse(installed), Version.Parse(minimum)));

    [Theory]
    [InlineData("0.7.3.2497", "0.7.3-ci.2497")]
    [InlineData("0.7.3.0", "0.7.3")]
    [InlineData("0.7.3", "0.7.3")]
    [InlineData("0.6", "0.6")]
    public void FormatsCiBuildsLikeMelonLoaderLog(string version, string expected) =>
        Assert.Equal(expected, MelonVersions.Format(Version.Parse(version)));

    [Fact]
    public void ExactRuleMatchesSameReleaseIncludingNightly()
    {
        var exact = new MelonRequirement("정확히 0.7.1", new Version(0, 7, 1), null);
        Assert.True(exact.IsSatisfiedBy(Version.Parse("0.7.1.0")));
        Assert.True(exact.IsSatisfiedBy(Version.Parse("0.7.1.2300")));
        Assert.False(exact.IsSatisfiedBy(Version.Parse("0.7.3")));
        Assert.False(exact.IsSatisfiedBy(null));
        Assert.False(MelonVersions.Modern.IsSatisfiedBy(null));
    }

    private static MelonRelease Release(string tag, bool prerelease = false) =>
        new(tag, MelonVersions.Parse(tag)!, prerelease, new Dictionary<PeArch, MelonAsset>());

    [Fact]
    public void ModernPicksNewestStableAtLeastMinimum()
    {
        var releases = new[] { Release("v0.8.0", prerelease: true), Release("v0.7.3"), Release("v0.7.2"), Release("v0.7.1"), Release("v0.6.1") };
        Assert.Equal("v0.7.3", MelonVersions.Modern.Pick(releases)!.Tag);
        Assert.Equal("v0.7.1", new MelonRequirement("정확히", new Version(0, 7, 1), null).Pick(releases)!.Tag);
        Assert.Null(MelonVersions.Modern.Pick([Release("v0.7.2")]));
    }

    [Fact]
    public void OnlyCychronicleLikeGamesGetAnAutoRule()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Cup", "Cup_Data/Managed/Assembly-CSharp.dll", "Cup_Data/Mono/", "Cup.exe");
        lib.AddGame("Gv", "Gv_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/", "Gv.exe");
        lib.AddGame("Il", "Il_Data/il2cpp_data/Metadata/global-metadata.dat", "GameAssembly.dll", "UnityPlayer.dll", "Il.exe");
        var games = lib.Scan().Games.ToDictionary(g => g.Name);

        Assert.Null(MelonVersions.For(games["Cup"]));   // 구형 레거시(Cuphead 등)는 자동 설치하지 않는다
        Assert.Same(MelonVersions.Modern, MelonVersions.For(games["Gv"]));
        Assert.Same(MelonVersions.Modern, MelonVersions.For(games["Il"]));
    }

    [Fact]
    public void ReadsSha512InEitherEncodingReleasesUse()
    {
        var hex = string.Concat(Enumerable.Repeat("52F0FAAA", 16));
        Assert.Equal(hex, MelonReleaseClient.ReadSha512(System.Text.Encoding.ASCII.GetBytes(hex)));          // v0.7.0
        Assert.Equal(hex, MelonReleaseClient.ReadSha512([0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes(hex)])); // v0.6.1
        Assert.Equal(hex, MelonReleaseClient.ReadSha512(System.Text.Encoding.UTF8.GetBytes(hex.ToLowerInvariant() + "  MelonLoader.x64.zip\n"))?.ToUpperInvariant());
        Assert.Null(MelonReleaseClient.ReadSha512(System.Text.Encoding.ASCII.GetBytes("not a hash")));
    }

    [Fact]
    public void ParsesGitHubReleaseAssets()
    {
        const string json = """
        [
          { "tag_name": "v0.7.3", "prerelease": false, "assets": [
              { "name": "MelonLoader.x64.zip", "browser_download_url": "https://x/0.7.3/x64", "size": 20155622 },
              { "name": "MelonLoader.x86.zip", "browser_download_url": "https://x/0.7.3/x86", "size": 19861489 },
              { "name": "MelonLoader.Linux.x64.zip", "browser_download_url": "https://x/0.7.3/linux", "size": 1 },
              { "name": "MelonLoader.Installer.exe", "browser_download_url": "https://x/0.7.3/installer", "size": 1 } ] },
          { "tag_name": "v0.6.1", "prerelease": false, "assets": [
              { "name": "MelonLoader.x64.zip", "browser_download_url": "https://x/0.6.1/x64", "size": 22743856 },
              { "name": "MelonLoader.x64.sha512", "browser_download_url": "https://x/0.6.1/x64.sha512", "size": 128 } ] },
          { "tag_name": "latest-nightly", "prerelease": true, "assets": [] }
        ]
        """;
        var releases = MelonVersions.ParseReleases(json);

        Assert.Equal(["v0.7.3", "v0.6.1"], releases.Select(r => r.Tag));
        Assert.Equal([PeArch.X86, PeArch.X64], releases[0].Zips.Keys.Order());
        Assert.Equal("https://x/0.7.3/x86", releases[0].Zips[PeArch.X86].Url);
        Assert.Null(releases[0].Zips[PeArch.X64].Sha512Url);
        Assert.Equal("https://x/0.6.1/x64.sha512", releases[1].Zips[PeArch.X64].Sha512Url);
        Assert.False(releases[1].Zips.ContainsKey(PeArch.X86));
        Assert.Equal(22743856, releases[1].Zips[PeArch.X64].Size);
    }
}
