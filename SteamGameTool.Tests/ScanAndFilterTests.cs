using steam_game_tool;

namespace SteamGameTool.Tests;

/// <summary>부가 마커, 스캔 결과 구성, 필터 동작에 대한 테스트.</summary>
public class ScanAndFilterTests
{
    // ── 부가 마커 ────────────────────────────────────────────────

    [Fact]
    public void 하위_깊이의_MonoBleedingEdge도_잡힌다()
    {
        using var lib = new FakeLibrary();
        var top = lib.ScanSingle("TopLevel", "MonoBleedingEdge/");

        using var lib2 = new FakeLibrary();
        var deep = lib2.ScanSingle("Deep", "sub/MonoBleedingEdge/");

        Assert.True(top.HasMonoBleedingEdge);
        Assert.True(deep.HasMonoBleedingEdge);
    }

    [Fact]
    public void 하위_깊이의_Managed_폴더도_HasManaged로_잡힌다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("DeepManaged", "a/b/c/Managed/");

        Assert.True(game.HasManaged);
    }

    [Fact]
    public void BackUpThisFolder_내부의_Managed는_무시된다()
    {
        using var lib = new FakeLibrary();
        // IL2CPP 백업 폴더 안에 있는 Managed 는 개발자 디버깅 백업이므로 잡지 않아야 한다.
        var game = lib.ScanSingle("Il2CppWithBackup",
            "game_Data/il2cpp_data/Metadata/global-metadata.dat",
            "game_BackUpThisFolder_ButDontShipItWithYourGame/Managed/Assembly-CSharp.dll");

        Assert.True(game.HasUnityIl2Cpp);
        Assert.False(game.HasUnityMono);
        Assert.False(game.HasManaged);
    }

    [Fact]
    public void 폴더명과_같은_하위폴더가_있으면_중첩()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("Doubled", "Doubled/");

        Assert.True(game.HasNestedMatch);
        Assert.Contains("중첩", game.Tags);
    }

    [Fact]
    public void 이름에_Demo나_Demon이_들어가도_Managed_폴더가_있으면_HasManaged가_켜진다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("Some Game Demo", "game_Data/Managed/Assembly-CSharp.dll");
        var demon = lib.ScanSingle("Hell is other demons", "game_Data/Managed/Assembly-CSharp.dll");

        Assert.True(game.HasManaged);
        Assert.True(demon.HasManaged);
        Assert.True(game.HasUnityMono);
        Assert.True(demon.HasUnityMono);
    }

    // ── 태그 ─────────────────────────────────────────────────────

    [Fact]
    public void 태그는_정해진_순서로_나온다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("AllMarkers",
            "MonoBleedingEdge/",
            "game_Data/Managed/Assembly-CSharp.dll",
            "game_Data/il2cpp_data/Metadata/global-metadata.dat",
            "AllMarkers/");

        Assert.Equal(
            ["Unity IL2CPP", "MonoBleedingEdge", "Managed", "중첩"],
            game.Tags);
    }

    [Fact]
    public void Mono게임의_태그도_정해진_순서로_나온다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("MonoMarkers",
            "MonoBleedingEdge/",
            "game_Data/Managed/Assembly-CSharp.dll",
            "MonoMarkers/");

        Assert.Equal(
            ["Unity Mono", "MonoBleedingEdge", "Managed", "중첩"],
            game.Tags);
    }

    [Fact] // 태그 문자열은 Converters.TagBrush 의 switch 와 정확히 일치해야 한다
    public void 태그_문자열은_배지_색상_변환기와_일치한다()
    {
        using var lib = new FakeLibrary();
        var game = lib.ScanSingle("ColorCheck",
            "MonoBleedingEdge/",
            "game_Data/Managed/Assembly-CSharp.dll",
            "game_Data/il2cpp_data/Metadata/global-metadata.dat",
            "ColorCheck/");

        var fallback = Converters.TagBrush.Convert(
            "존재하지 않는 태그", typeof(object), null!, null!);

        foreach (var tag in game.Tags)
        {
            var brush = Converters.TagBrush.Convert(tag, typeof(object), null!, null!);
            Assert.NotNull(brush);
            // 기본값(회색)으로 떨어졌다면 두 곳의 문자열이 어긋난 것이다.
            Assert.NotEqual(fallback!.ToString(), brush!.ToString());
        }
    }

    // ── 스캔 결과 구성 ───────────────────────────────────────────

    [Fact]
    public void 마커가_없는_폴더도_Games에는_포함된다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Marked", "MonoBleedingEdge/");
        lib.AddGame("Unmarked", "readme.txt");

        var result = lib.Scan();

        Assert.Equal(2, result.Games.Count);
        Assert.Single(result.Games, g => g.HasAny);
    }

    [Fact]
    public void 결과는_이름순으로_정렬된다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Charlie");
        lib.AddGame("alpha");
        lib.AddGame("Bravo");

        var names = lib.Scan().Games.Select(g => g.Name).ToArray();

        Assert.Equal(["alpha", "Bravo", "Charlie"], names);
    }

    [Fact]
    public void 집계값이_실제_개수와_맞는다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Mono1", "MonoBleedingEdge/");
        lib.AddGame("Mono2", "game_Data/Managed/UnityEngine.dll");
        lib.AddGame("Il2Cpp1", "game_Data/il2cpp_data/Metadata/global-metadata.dat");
        lib.AddGame("Nested1", "Nested1/");

        var r = lib.Scan();

        Assert.Equal(2, r.UnityMonoCount);
        Assert.Equal(1, r.UnityIl2CppCount);
        Assert.Equal(1, r.MonoBleedingEdgeCount);
        Assert.Equal(1, r.ManagedCount);   // Mono2 의 Managed
        Assert.Equal(1, r.NestedCount);
    }

    [Fact]
    public void 스캔한_루트가_기록된다()
    {
        using var lib = new FakeLibrary();
        var result = lib.Scan();

        Assert.Equal([lib.CommonDir], result.Roots);
    }

    [Fact]
    public void 존재하지_않는_루트는_건너뛴다()
    {
        var missing = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N"));
        var result = SteamScanner.Scan([missing]);

        Assert.Empty(result.Roots);
        Assert.Empty(result.Games);
    }

    [Fact]
    public void 스캔할_루트가_없으면_progress로_알린다()
    {
        var messages = new List<string>();
        var missing = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N"));

        SteamScanner.Scan([missing], messages.Add);

        Assert.Contains(messages, m => m.Contains("찾지 못했습니다"));
    }

    [Fact]
    public void 여러_루트를_한번에_스캔한다()
    {
        using var a = new FakeLibrary();
        using var b = new FakeLibrary();
        a.AddGame("FromA", "MonoBleedingEdge/");
        b.AddGame("FromB", "game_Data/il2cpp_data/Metadata/global-metadata.dat");

        var result = SteamScanner.Scan([a.CommonDir, b.CommonDir]);

        Assert.Equal(2, result.Roots.Count);
        Assert.Equal(["FromA", "FromB"], result.Games.Select(g => g.Name));
    }

    // ── 필터 ─────────────────────────────────────────────────────

    [Fact]
    public void Any_필터는_Unity_마커가_있는_것만_돌려주며_비Unity_중첩은_제외한다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("Marked", "MonoBleedingEdge/");
        lib.AddGame("Unmarked", "readme.txt");
        lib.AddGame("NonUnityNested", "NonUnityNested/"); // 비-Unity 중첩 폴더

        var filtered = SteamScanner.Filter(lib.Scan(), Marker.Any).ToList();

        Assert.Single(filtered);
        Assert.Equal("Marked", filtered[0].Name);
    }

    [Fact]
    public void 중첩_필터는_Unity와_비Unity를_구분한다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("UnityNested", "UnityNested/", "UnityNested/game_Data/Managed/Assembly-CSharp.dll");
        lib.AddGame("NonUnityNested", "NonUnityNested/");

        var result = lib.Scan();

        var unityNested = SteamScanner.Filter(result, Marker.NestedUnity).ToList();
        Assert.Single(unityNested);
        Assert.Equal("UnityNested", unityNested[0].Name);

        var nonUnityNested = SteamScanner.Filter(result, Marker.NestedNonUnity).ToList();
        Assert.Single(nonUnityNested);
        Assert.Equal("NonUnityNested", nonUnityNested[0].Name);

        var allNested = SteamScanner.Filter(result, Marker.Nested).ToList();
        Assert.Equal(2, allNested.Count);
    }

    [Theory]
    [InlineData(Marker.UnityMono, "MonoGame")]
    [InlineData(Marker.UnityMono, "LegacyMonoGame")]
    [InlineData(Marker.MonoLegacy, "LegacyMonoGame")]
    [InlineData(Marker.UnityIl2Cpp, "Il2CppGame")]
    [InlineData(Marker.MonoBleedingEdge, "MonoGame")]
    [InlineData(Marker.Managed, "MonoGame")]
    [InlineData(Marker.NestedUnity, "UnityNestedGame")]
    [InlineData(Marker.NestedNonUnity, "NonUnityNestedGame")]
    [InlineData(Marker.Nested, "UnityNestedGame")]
    public void 마커별_필터가_해당_게임만_돌려준다(Marker marker, string expected)
    {
        using var lib = new FakeLibrary();
        lib.AddGame("MonoGame", "MonoBleedingEdge/", "game_Data/Managed/Assembly-CSharp.dll");
        lib.AddGame("LegacyMonoGame", "game_Data/Mono/", "game_Data/Managed/Assembly-CSharp.dll");
        lib.AddGame("Il2CppGame", "game_Data/il2cpp_data/Metadata/global-metadata.dat");
        lib.AddGame("UnityNestedGame", "UnityNestedGame/", "UnityNestedGame/game_Data/Managed/Assembly-CSharp.dll");
        lib.AddGame("NonUnityNestedGame", "NonUnityNestedGame/");

        var filtered = SteamScanner.Filter(lib.Scan(), marker).ToList();

        Assert.Contains(filtered, g => g.Name == expected);
    }

    [Fact]
    public void 단일_게임_보존본_폴더_자체를_루트로_지정해도_정상_스캔된다()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N"), "muse dash hwa");
        try
        {
            Directory.CreateDirectory(Path.Combine(tempDir, "MuseDash_Data"));
            File.WriteAllText(Path.Combine(tempDir, "MuseDash.exe"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "GameAssembly.dll"), "dummy");
            File.WriteAllText(Path.Combine(tempDir, "UnityPlayer.dll"), "dummy");

            var result = SteamScanner.Scan(new[] { tempDir });

            Assert.Single(result.Games);
            var game = result.Games[0];
            Assert.Equal("muse dash hwa", game.Name);
            Assert.True(game.HasUnityIl2Cpp);
            Assert.False(game.HasUnityMono);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(tempDir)!, true); } catch { }
        }
    }
}
