using steam_game_tool;

namespace SteamGameTool.Tests;

public class SelectionFallbackTests
{
    [Fact]
    public void ChronoArkDataWinsOverRuntimeOnlyArchitectureFolders()
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle("Chrono Ark", "ChronoArk_Data/Managed/Assembly-CSharp.dll",
            "ChronoArk_Data/MonoBleedingEdge/", "x64/Release/MonoBleedingEdge/", "x86/Debug/MonoBleedingEdge/");
        Assert.Equal("ChronoArk_Data", g.MainPlayer!.DataPath);
        Assert.False(g.IsBackendUnresolved);
    }

    [Theory]
    [InlineData("Chrono Ark", "ChronoArk", "normalized-install-name")]
    [InlineData("TerraTech", "TerraTechWin64", "prefix-install-name")]
    public void NameFallbackRetainsPlayer(string install, string data, string reason)
    {
        using var lib = new FakeLibrary();
        var g = lib.ScanSingle(install, data + "_Data/Managed/Assembly-CSharp.dll",
            "tool/Other_Data/Managed/Assembly-CSharp.dll");
        Assert.Equal(reason, g.Selection.Reason);
        Assert.Equal(data + "_Data", g.MainPlayer!.DataPath);
    }

    [Fact]
    public void UnityRegressionIsDetectedEvenWhenFolderCountIsUnchanged()
    {
        Assert.Throws<InvalidOperationException>(() => ScanInventory.ValidateUnityRetention(["Chrono Ark"], [], _ => true));
        ScanInventory.ValidateUnityRetention(["Actually removed"], [], _ => false);
    }
}
