using steam_game_tool;

namespace SteamGameTool.Tests;

public class ExportAndEvidenceTests
{
    [Fact]
    public void TxtUsesCanonicalNamesAndCrLfWhilePreservingDisplayNames()
    {
        var names = new[] { " Marfusha", "Warriors of the Nile ", "Aotenjo:\u00a0Infinite\u00a0Hands",
            "Touhou\u00a0Hero\u00a0of\u00a0Ice\u00a0Fairy", "Ro\u0308ki", "Erza’s Trial" };
        var games = names.Select(n => new SteamGame { Name = n, InstallDir = n }).ToArray();
        var text = GameExport.ToText(games, "Games:");
        Assert.DoesNotContain('\u00a0', text);
        Assert.DoesNotContain('\u0308', text);
        Assert.DoesNotContain('\u2019', text);
        Assert.Contains("1. Aotenjo: Infinite Hands\r\n", text);
        Assert.Contains("3. Marfusha\r\n", text);
        Assert.Contains("6. Warriors of the Nile\r\n\r\nTotal: 6\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", ""));
        Assert.Equal(names, games.Select(g => g.DisplayName));
    }

    [Fact]
    public void NestedUsesPlayerStructureNotSameNameFolder()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Jaded", "Jaded/", "Jaded_Data/Managed/Assembly-CSharp.dll",
            "Jaded.app/Contents/MonoBleedingEdge/");
        lib.AddGame("Different", "Windows/player_Data/Managed/Assembly-CSharp.dll");
        var result = lib.Scan();
        Assert.Equal("Different", Assert.Single(SteamScanner.Filter(result, Marker.NestedUnity)).Name);
        Assert.True(result.Games.Single(g => g.Name == "Jaded").HasNestedMatch);
    }

    [Fact]
    public void RuntimeClassificationIsLocalToEachPlayer()
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle("Mixed", "old/old_Data/Mono/", "old/old_Data/Managed/Assembly-CSharp.dll",
            "new/new_Data/Managed/Assembly-CSharp.dll", "new/MonoBleedingEdge/");
        Assert.False(g.IsBackendUnresolved); // A stable low-confidence fallback preserves the game.
        Assert.False(g.HasLegacyMono);
        Assert.True(g.HasModernMono);
        Assert.Contains(g.BackendEvidence, e => e.RuntimeKind == "mono-runtime" && !e.HasMonoBleedingEdge);
        Assert.Contains(g.BackendEvidence, e => e.RuntimeKind == "monobleedingedge-runtime" && e.MonoRuntimePath is null);
    }

    [Fact]
    public void BundledAnniversaryDoesNotClassifyOriginal()
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle("Original", "_anniversary/edna_Data/Managed/Assembly-CSharp.dll",
            "_anniversary/MonoBleedingEdge/");
        Assert.Equal(UnityBackend.Mono, g.Backend); // Observed evidence is retained.
        Assert.False(g.IsBackendUnresolved);
        Assert.Equal("low", g.Selection.Confidence);
        Assert.True(g.HasMonoBleedingEdge);
        Assert.True(Assert.Single(g.BackendEvidence).IsBundled);
    }

    [Fact]
    public void DeepPlayerAndRuntimeOnlyHaveLocatedEvidence()
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle("Hot", "archive/build/hot_Data/Managed/Assembly-CSharp.dll",
            "archive/build/MonoBleedingEdge/");
        var e = Assert.Single(g.BackendEvidence);
        Assert.Equal(Path.Combine("archive", "build", "MonoBleedingEdge"), e.MonoBleedingEdgePath);
        Assert.Equal(1, g.DataDirectoryCount);
        using var other = new FakeLibrary();
        var fallback = other.ScanSingle("RuntimeOnly", "sub/MonoBleedingEdge/");
        Assert.Equal("monobleedingedge-runtime", Assert.Single(fallback.BackendEvidence).Rule);
        Assert.Equal(0, fallback.DataDirectoryCount);
    }

    [Fact]
    public void ManagedAssemblyAloneDoesNotProveLegacyRuntime()
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle("UnknownRuntime", "game_Data/Managed/Assembly-CSharp.dll");
        Assert.True(g.HasUnityMono);
        Assert.False(g.HasLegacyMono);
        Assert.False(g.HasModernMono);
    }
}
