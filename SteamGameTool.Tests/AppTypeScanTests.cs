using steam_game_tool;

namespace SteamGameTool.Tests;

/// <summary>
/// 카탈로그를 붙였을 때 스캔 결과가 게임/비게임을 어떻게 나누는지에 대한 테스트.
/// 실제 머신의 appinfo.vdf 를 읽지 않도록 카탈로그를 직접 주입한다.
/// </summary>
public class AppTypeScanTests
{
    /// <summary>게임 폴더들과, 그 폴더들을 설명하는 카탈로그를 함께 만든다.</summary>
    private static (FakeLibrary Lib, FakeSteamLibrary Meta, SteamCatalog Catalog) Setup(
        params (string folder, string storeName, string type)[] apps)
    {
        var lib = new FakeLibrary();
        var meta = new FakeSteamLibrary();

        uint appid = 1000;
        var infos = new List<(uint, string)>();
        foreach (var (folder, storeName, type) in apps)
        {
            meta.AddApp(appid, storeName, folder);
            infos.Add((appid, type));
            appid++;
        }
        meta.WriteAppInfo(infos.ToArray());

        return (lib, meta, SteamCatalog.Build([meta.LibraryRoot], meta.SteamRoot));
    }

    [Fact]
    public void 스토어_이름이_있으면_그것을_보여_준다()
    {
        var (lib, meta, catalog) = Setup(("HatinTime", "A Hat in Time", "Game"));
        using (lib) using (meta)
        {
            lib.AddGame("HatinTime", "game_Data/Managed/Assembly-CSharp.dll");

            var game = SteamScanner.Scan([lib.CommonDir], catalog: catalog).Games.Single();

            Assert.Equal("HatinTime", game.Name);            // 폴더명은 그대로
            Assert.Equal("A Hat in Time", game.StoreName);
            Assert.Equal("A Hat in Time", game.DisplayName); // 화면에는 스토어명
            Assert.Equal(1000u, game.AppId);
        }
    }

    [Fact]
    public void 사운드트랙과_소프트웨어는_게임_집계에서_빠진다()
    {
        var (lib, meta, catalog) = Setup(
            ("RealGame", "진짜 게임", "Game"),
            ("SomeOst", "어떤 사운드트랙", "Music"),
            ("SomeApp", "어떤 소프트웨어", "Application"));
        using (lib) using (meta)
        {
            // 셋 다 Unity 마커를 갖고 있어도, 게임이 아닌 것은 세지 않아야 한다.
            lib.AddGame("RealGame", "game_Data/Managed/Assembly-CSharp.dll");
            lib.AddGame("SomeOst", "game_Data/Managed/Assembly-CSharp.dll");
            lib.AddGame("SomeApp", "game_Data/Managed/Assembly-CSharp.dll");

            var r = SteamScanner.Scan([lib.CommonDir], catalog: catalog);

            Assert.True(r.HasCatalog);
            Assert.True(r.HasTypeInfo);
            Assert.Equal(3, r.Games.Count);          // 스캔은 다 한다
            Assert.Equal(1, r.GameCount);            // 그러나 게임은 하나
            Assert.Equal(1, r.UnityMonoCount);       // 마커 집계도 게임 기준
            Assert.Equal(1, r.CountOf(SteamAppType.Music));
            Assert.Equal(1, r.CountOf(SteamAppType.Application));
        }
    }

    [Fact]
    public void 게임만_필터가_비게임을_숨긴다()
    {
        var (lib, meta, catalog) = Setup(
            ("RealGame", "진짜 게임", "Game"),
            ("SomeOst", "어떤 사운드트랙", "Music"));
        using (lib) using (meta)
        {
            lib.AddGame("RealGame", "MonoBleedingEdge/");
            lib.AddGame("SomeOst", "MonoBleedingEdge/");

            var r = SteamScanner.Scan([lib.CommonDir], catalog: catalog);

            Assert.Equal(2, SteamScanner.Filter(r, Marker.Any, gamesOnly: false).Count());
            var onlyGames = SteamScanner.Filter(r, Marker.Any, gamesOnly: true).ToList();
            Assert.Single(onlyGames);
            Assert.Equal("진짜 게임", onlyGames[0].DisplayName);
        }
    }

    [Fact]
    public void 데모는_게임으로_친다()
    {
        var (lib, meta, catalog) = Setup(("SomeDemo", "어떤 데모", "Demo"));
        using (lib) using (meta)
        {
            lib.AddGame("SomeDemo", "MonoBleedingEdge/");

            var r = SteamScanner.Scan([lib.CommonDir], catalog: catalog);

            Assert.Equal(1, r.GameCount);
            Assert.Single(SteamScanner.Filter(r, Marker.Any, gamesOnly: true));
        }
    }

    [Fact]
    public void appmanifest가_없는_비Unity_폴더는_잔여로_분류된다()
    {
        var (lib, meta, catalog) = Setup(("RealGame", "진짜 게임", "Game"));
        using (lib) using (meta)
        {
            lib.AddGame("RealGame", "MonoBleedingEdge/");
            lib.AddGame("LeftoverFolder", "readme.txt", "some_log.txt");   // 카탈로그에도 없고 Unity 백엔드도 없음

            var r = SteamScanner.Scan([lib.CommonDir], catalog: catalog);

            var leftover = r.Games.Single(g => g.Name == "LeftoverFolder");
            Assert.True(leftover.IsOrphan);
            Assert.Null(leftover.AppId);
            Assert.Equal("LeftoverFolder", leftover.DisplayName);   // 폴더명으로 대체

            Assert.Equal(1, r.OrphanCount);
            Assert.Equal(1, r.GameCount);                            // 순수 잔여물은 게임에서 제외
            Assert.Single(SteamScanner.Filter(r, Marker.Any, gamesOnly: true));
        }
    }

    [Fact] // Muse Dash 처럼 appmanifest 는 없지만 실제 Unity 실행 파일이 살아있는 오프라인 보존 게임
    public void appmanifest가_없어도_Unity_백엔드가_있으면_오프라인_게임으로_인정된다()
    {
        var (lib, meta, catalog) = Setup(("RealGame", "진짜 게임", "Game"));
        using (lib) using (meta)
        {
            lib.AddGame("RealGame", "MonoBleedingEdge/");
            lib.AddGame("MuseDashOffline", "game_Data/il2cpp_data/Metadata/global-metadata.dat");

            var r = SteamScanner.Scan([lib.CommonDir], catalog: catalog);

            var offline = r.Games.Single(g => g.Name == "MuseDashOffline");
            Assert.True(offline.IsOrphan);
            Assert.True(offline.IsPlayableUnity);
            Assert.True(offline.IsGameLike);

            Assert.Equal(0, r.OrphanCount);                          // 플레이 가능한 Unity 게임이므로 찌꺼기로 보지 않음
            Assert.Equal(2, r.GameCount);                            // 정식 게임 수에 포함
            Assert.Equal(2, SteamScanner.Filter(r, Marker.Any, gamesOnly: true).Count());
        }
    }

    [Fact] // 종류를 모르는 앱을 숨겨 버리면 안 된다 — 확실한 비게임만 배제한다
    public void 종류를_모르는_앱은_게임쪽에_남는다()
    {
        var (lib, meta, catalog) = Setup(("Known", "아는 게임", "Game"));
        using (lib) using (meta)
        {
            lib.AddGame("Known", "MonoBleedingEdge/");
            // 카탈로그에는 있지만 appinfo 에 종류가 없는 앱을 흉내 낸다.
            meta.AddApp(9999, "종류 모를 앱", "TypeUnknown");
            var catalog2 = SteamCatalog.Build([meta.LibraryRoot], meta.SteamRoot);
            lib.AddGame("TypeUnknown", "MonoBleedingEdge/");

            var r = SteamScanner.Scan([lib.CommonDir], catalog: catalog2);

            var unknown = r.Games.Single(g => g.Name == "TypeUnknown");
            Assert.Equal(SteamAppType.Unknown, unknown.AppType);
            Assert.False(unknown.IsOrphan);
            Assert.True(unknown.IsGameLike);
            Assert.Equal(2, r.GameCount);
        }
    }

    [Fact] // 카탈로그를 못 읽는 환경에서 결과가 비어 버리면 안 된다
    public void 카탈로그가_없으면_모두_집계하고_게임만_필터는_무시된다()
    {
        using var lib = new FakeLibrary();
        lib.AddGame("A", "MonoBleedingEdge/");
        lib.AddGame("B", "MonoBleedingEdge/");

        var r = SteamScanner.Scan([lib.CommonDir], catalog: SteamCatalog.Empty);

        Assert.False(r.HasCatalog);
        Assert.Equal(2, r.GameCount);        // 구분할 근거가 없으니 전부 센다
        Assert.Equal(0, r.OrphanCount);      // "잔여"라고 단정하지 않는다
        Assert.Equal(2, SteamScanner.Filter(r, Marker.Any, gamesOnly: true).Count());
    }

    [Fact]
    public void 정렬은_스토어_이름_기준이다()
    {
        var (lib, meta, catalog) = Setup(
            ("zzz_folder", "A 로 시작하는 게임", "Game"),
            ("aaa_folder", "Z 로 시작하는 게임", "Game"));
        using (lib) using (meta)
        {
            lib.AddGame("zzz_folder");
            lib.AddGame("aaa_folder");

            var names = SteamScanner.Scan([lib.CommonDir], catalog: catalog)
                                    .Games.Select(g => g.DisplayName).ToArray();

            Assert.Equal(["A 로 시작하는 게임", "Z 로 시작하는 게임"], names);
        }
    }
}
