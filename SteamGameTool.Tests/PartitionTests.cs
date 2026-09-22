using steam_game_tool;

namespace SteamGameTool.Tests;

public class PartitionTests
{
    [Theory]
    [InlineData("Mono/", "mono")]
    [InlineData("game_Data/Mono/EmbedRuntime/", "mono")]
    [InlineData("game_Data/Mono/x86_64/", "mono")]
    [InlineData("game_Data/MonoBleedingEdge/", "monobleedingedge")]
    [InlineData("MonoBleedingEdge/", "monobleedingedge")]
    public void RuntimeLayoutsAreResolvedWithoutDroppingGames(string layout, string runtime)
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle("Layout", "game_Data/Managed/Assembly-CSharp.dll", layout);
        Assert.Equal(runtime, g.MainPlayer!.Runtime);
        Assert.False(g.IsRuntimeUnresolved);
    }

    [Fact]
    public void EveryScannedEntryIsCoveredExactlyOnce()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Modern", "game_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/");
        lib.AddGame("Legacy", "game_Data/Managed/Assembly-CSharp.dll", "Mono/");
        lib.AddGame("RuntimeUnknown", "game_Data/Managed/Assembly-CSharp.dll");
        lib.AddGame("Native", "game_Data/il2cpp_data/Metadata/global-metadata.dat");
        lib.AddGame("Bundled", "_anniversary/game_Data/Managed/Assembly-CSharp.dll");
        lib.AddGame("Unknown", "readme.txt");
        var result = lib.Scan();
        foreach (var def in ExportLists.All) ExportLists.Generate(result, def.Marker, false);
        Assert.Equal(2, SteamScanner.Filter(result, Marker.RuntimeUnresolved).Count());
        Assert.Single(SteamScanner.Filter(result, Marker.BackendUnresolved));
        Assert.Equal(6, SteamScanner.Filter(result, Marker.Scanned).Count());
    }

    [Fact]
    public void LargestMatchedPlayerPreventsRuntimeOverlapAndTieIsResidual()
    {
        using var lib = new FakeLibrary();
        var dir = lib.AddGame("Two", "main_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/", "main.exe",
            "tool/tool_Data/Managed/Assembly-CSharp.dll", "tool/Mono/", "tool/tool.exe");
        File.WriteAllBytes(Path.Combine(dir, "main.exe"), new byte[20]);
        File.WriteAllBytes(Path.Combine(dir, "tool", "tool.exe"), new byte[10]);
        var g = lib.Scan().Games.Single();
        Assert.True(g.HasModernMono);
        Assert.False(g.HasLegacyMono);
        Assert.Equal(2, g.BackendEvidence.Count);
        File.WriteAllBytes(Path.Combine(dir, "tool", "tool.exe"), new byte[20]);
        var tie = lib.Scan().Games.Single();
        Assert.False(tie.IsBackendUnresolved);
        Assert.Equal("low", tie.Selection.Confidence);
    }

    [Fact]
    public void ExistingFoldersCannotSilentlyVanish()
    {
        Assert.Throws<InvalidOperationException>(() => ScanInventory.Compare(["existing"], [], _ => true));
        Assert.Contains("removed on disk: 1", ScanInventory.Compare(["removed"], [], _ => false));
        Assert.Contains("added: 1", ScanInventory.Compare([], ["new"], _ => true));
    }
}
