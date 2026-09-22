using System.Text.Json;
using steam_game_tool;

namespace SteamGameTool.Tests;

public class NormalizationExportTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RootCannotHideInnerPlayer(bool outerMono)
    {
        using var lib = new FakeLibrary();
        var mono = "Managed/Assembly-CSharp.dll";
        var il2cpp = "il2cpp_data/Metadata/global-metadata.dat";
        var game = lib.ScanSingle("Mixed", "outer_Data/" + (outerMono ? mono : il2cpp),
            "Mixed/inner_Data/" + (outerMono ? il2cpp : mono));
        Assert.Equal(UnityBackend.Mono | UnityBackend.Il2Cpp, game.Backend);
        Assert.Equal(2, game.DataDirectoryCount);
        Assert.Equal("mixed", game.BackendSource);
        Assert.Contains(game.BackendEvidence, e => e.Layer == "outer");
        Assert.Contains(game.BackendEvidence, e => e.Layer == "inner");
    }

    [Theory]
    [InlineData(" Marfusha", "Marfusha")]
    [InlineData("Warriors of the Nile ", "Warriors of the Nile")]
    [InlineData("Aotenjo:\u00a0Infinite Hands", "Aotenjo: Infinite Hands")]
    [InlineData("Touhou\u00a0Hero of Ice Fairy", "Touhou Hero of Ice Fairy")]
    [InlineData("Ro\u0308ki", "Röki")]
    [InlineData("Märchen Forest", "Märchen Forest")]
    [InlineData("Erza’s Wheel of Fortune", "Erza's Wheel of Fortune")]
    public void NamesAreCanonical(string raw, string expected)
    {
        var game = new SteamGame { Name = raw, InstallDir = raw };
        Assert.Equal(raw, game.DisplayName);
        Assert.Equal(expected, game.NameKey);
        Assert.Equal(raw, game.InstallDir);
        Assert.Equal(expected, GameNames.Normalize(game.DisplayName));
    }

    [Fact]
    public void NestedExportRetainsBackendAndEvidence()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("Nested", "Nested/game_Data/Managed/Assembly-CSharp.dll");
        using var json = JsonDocument.Parse(GameExport.ToJson([game]));
        var row = json.RootElement.GetProperty("games")[0];
        Assert.True(row.GetProperty("hasNestedMatch").GetBoolean());
        Assert.Equal("Mono", row.GetProperty("backend").GetString());
        Assert.Equal("single", row.GetProperty("backendSource").GetString());
        Assert.Equal(Path.Combine("Nested", "game_Data"), row.GetProperty("backendDataPath").GetString());
    }

    [Fact]
    public void ViewListsAreWrittenTogetherWithExistingNames()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Modern", "game_Data/Managed/Assembly-CSharp.dll", "MonoBleedingEdge/");
        lib.AddGame("Legacy", "game_Data/Managed/Assembly-CSharp.dll", "Mono/");
        lib.AddGame("Native", "game_Data/il2cpp_data/Metadata/global-metadata.dat");
        lib.AddGame("Nested", "Nested/game_Data/Managed/Assembly-CSharp.dll");
        var result = lib.Scan();
        Marker[] view = [Marker.Any, Marker.UnityMono, Marker.MonoLegacy, Marker.MonoBleedingEdge, Marker.UnityIl2Cpp, Marker.NestedUnity];
        var dir = Directory.CreateTempSubdirectory("sgt-export-").FullName;
        try
        {
            var written = ExportLists.WriteAll(result, view, false, dir);
            Assert.Equal(
                ["list_legacy_mono_games.txt", "list_monobleedingedge_games.txt", "list_nested_unity_games.txt",
                 "list_unity_games.txt", "list_unity_il2cpp_games.txt", "list_unity_mono_games.txt"],
                Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            Assert.Equal(6, written.Count);
            foreach (var marker in view)
            {
                var bytes = File.ReadAllBytes(Path.Combine(dir, ExportLists.For(marker).FileName));
                Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
                Assert.Equal(ExportLists.Generate(result, marker, false), System.Text.Encoding.UTF8.GetString(bytes));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RootEvidenceAndUnknownEvidenceAreDistinct()
    {
        using var lib = new FakeLibrary();
        var root = lib.ScanSingle("Root", "game_Data/Managed/Assembly-CSharp.dll");
        Assert.Equal("single", root.BackendSource);
        var legacy = new SteamGame { Name = "Unknown", InstallDir = "Unknown", Backend = UnityBackend.Mono };
        Assert.Equal("unknown", legacy.BackendSource);
        Assert.Null(legacy.BackendDataPath);
    }
}
