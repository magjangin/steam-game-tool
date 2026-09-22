using steam_game_tool;

namespace SteamGameTool.Tests;

/// <summary>
/// docs/04-판별-로직.md 의 「판정 요약표」를 그대로 테스트로 옮긴 것.
/// 판별 규칙을 바꾸면 이 파일과 그 문서를 함께 고쳐야 한다.
/// </summary>
public class BackendDetectionTests
{
    // ── Mono 판정 ────────────────────────────────────────────────

    [Fact] // 규칙 1-A: 최상위 MonoBleedingEdge 단독으로 성립
    public void 최상위_MonoBleedingEdge_만_있어도_Mono()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("MonoOnlyRuntime", "MonoBleedingEdge/");

        Assert.True(game.HasUnityMono);
        Assert.False(game.HasUnityIl2Cpp);
    }

    [Theory] // 규칙 1-B: Managed 안의 대표 어셈블리 4종
    [InlineData("Assembly-CSharp.dll")]
    [InlineData("Assembly-CSharp-firstpass.dll")]
    [InlineData("UnityEngine.dll")]
    [InlineData("UnityEngine.CoreModule.dll")]
    public void Managed_안의_대표_어셈블리는_Mono(string assemblyName)
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("MonoGame", $"game_Data/Managed/{assemblyName}");

        Assert.True(game.HasUnityMono);
        Assert.False(game.HasUnityIl2Cpp);
    }

    [Fact] // 규칙 1-C: 구버전 Unity — _Data\Mono 런타임 + Managed 폴더
    public void 구버전_Mono런타임과_Managed_폴더_조합은_Mono()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("OldUnity",
            "game_Data/Mono/",
            "game_Data/Managed/SomeUnknownName.dll");

        Assert.True(game.HasUnityMono);
    }

    [Fact] // 규칙 1-C: MonoBleedingEdge 가 _Data 옆(하위 깊이)에 있어도 성립
    public void 하위_폴더의_MonoBleedingEdge와_Managed_조합도_Mono()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("NestedPlayer",
            "bin/MonoBleedingEdge/",
            "bin/game_Data/Managed/SomeUnknownName.dll");

        Assert.True(game.HasUnityMono);
        // 이제 하위 폴더의 MonoBleedingEdge 도 정상 감지된다.
        Assert.True(game.HasMonoBleedingEdge);
    }

    // ── IL2CPP 판정 ──────────────────────────────────────────────

    [Fact] // 규칙 2-A: 가장 확실한 증거
    public void global_metadata_는_IL2CPP()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("Il2CppGame",
            "game_Data/il2cpp_data/Metadata/global-metadata.dat");

        Assert.True(game.HasUnityIl2Cpp);
        Assert.False(game.HasUnityMono);
    }

    [Fact] // 규칙 2-B: 네이티브 플레이어 파일 쌍
    public void GameAssembly와_UnityPlayer_쌍은_IL2CPP()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("Il2CppPair",
            "game_Data/",
            "GameAssembly.dll",
            "UnityPlayer.dll");

        Assert.True(game.HasUnityIl2Cpp);
        Assert.False(game.HasUnityMono);
    }

    // ── 판정되지 않아야 하는 경우 ─────────────────────────────────

    [Fact] // UnityPlayer.dll 은 양쪽 공통이라 단독으로는 근거가 아니다
    public void UnityPlayer_단독으로는_아무것도_판정하지_않는다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("PlayerOnly", "game_Data/", "UnityPlayer.dll");

        Assert.Equal(UnityBackend.Unknown, game.Backend);
    }

    [Fact] // 규칙 2-B 는 쌍을 요구한다
    public void GameAssembly_단독으로는_IL2CPP가_아니다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("HalfPair", "game_Data/", "GameAssembly.dll");

        Assert.False(game.HasUnityIl2Cpp);
    }

    [Fact] // Managed 폴더만으로는 백엔드를 판정하지 않는다 (마커만 붙는다)
    public void 빈_Managed_폴더는_백엔드를_판정하지_않는다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("ManagedOnly", "game_Data/Managed/");

        Assert.Equal(UnityBackend.Unknown, game.Backend);
        Assert.True(game.HasManaged);
    }

    [Fact]
    public void Unity가_아닌_폴더는_마커가_없다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("NotUnity", "bin/whatever.dll", "readme.txt");

        Assert.Equal(UnityBackend.Unknown, game.Backend);
        Assert.False(game.HasAny);
    }

    // ── 플레이어별 백엔드 보존 ─────────────────────────

    [Fact] // 서로 다른 플레이어의 백엔드는 모두 보존한다
    public void 서로_다른_플레이어의_Mono와_IL2CPP를_보존한다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("MixedBackend",
            "Launcher/launcher_Data/Managed/Assembly-CSharp.dll",
            "Game/game_Data/il2cpp_data/Metadata/global-metadata.dat");

        Assert.True(game.HasUnityMono);
        Assert.True(game.HasUnityIl2Cpp);
        Assert.Equal(UnityBackend.Mono | UnityBackend.Il2Cpp, game.Backend);
    }

    // ── 공개 API 일치 ────────────────────────────────────────────

    [Fact]
    public void DetectUnityBackend는_Scan과_같은_결과를_낸다()
    {
        using var lib = new FakeLibrary();
        var dir = lib.AddGame("Consistency", "game_Data/Managed/Assembly-CSharp.dll");
        var fromScan = lib.Scan().Games.Single().Backend;

        Assert.Equal(fromScan, SteamScanner.DetectUnityBackend(dir));
    }
}
