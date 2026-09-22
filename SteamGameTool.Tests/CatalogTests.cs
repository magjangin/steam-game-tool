using System.Text;
using steam_game_tool;

namespace SteamGameTool.Tests;

/// <summary>
/// appmanifest(.acf) 와 appinfo.vdf 를 읽어 게임/사운드트랙/도구를 구분하는 부분에 대한 테스트.
/// appinfo.vdf 는 Valve 의 비공개 바이너리 포맷이라 여기서 직접 합성해 검증한다.
/// </summary>
public class CatalogTests
{
    // ── ACF 파싱 ─────────────────────────────────────────────────

    [Fact]
    public void ACF에서_최상위_값을_읽는다()
    {
        const string acf = """
            "AppState"
            {
                "appid"		"367520"
                "name"		"Hollow Knight"
                "installdir"		"Hollow Knight"
            }
            """;

        Assert.Equal("367520", SteamCatalog.AcfValue(acf, "appid"));
        Assert.Equal("Hollow Knight", SteamCatalog.AcfValue(acf, "name"));
        Assert.Equal("Hollow Knight", SteamCatalog.AcfValue(acf, "installdir"));
        Assert.Null(SteamCatalog.AcfValue(acf, "없는키"));
    }

    [Theory]
    [InlineData("Game", SteamAppType.Game)]
    [InlineData("game", SteamAppType.Game)]
    [InlineData("Demo", SteamAppType.Demo)]
    [InlineData("Music", SteamAppType.Music)]
    [InlineData("Application", SteamAppType.Application)]
    [InlineData("Tool", SteamAppType.Tool)]
    [InlineData("Video", SteamAppType.Video)]
    [InlineData("Config", SteamAppType.Config)]
    [InlineData("Series", SteamAppType.Other)]
    [InlineData(null, SteamAppType.Unknown)]
    public void 앱_종류_문자열을_열거형으로_옮긴다(string? raw, SteamAppType expected)
        => Assert.Equal(expected, SteamCatalog.MapType(raw));

    [Fact]
    public void 스토어이름이_있으면_DisplayName에_스토어이름이_표시된다()
    {
        var game = new SteamGame
        {
            Name = "Agreeee",
            InstallDir = @"C:\dummy\Agreeee",
            StoreName = "利用規約に同意したい",
        };

        Assert.Equal("利用規約に同意したい", game.DisplayName);
    }

    // ── appinfo.vdf 바이너리 리더 ────────────────────────────────

    [Fact]
    public void 문자열_테이블_방식_appinfo를_읽는다()
    {
        var bytes = FakeAppInfo.BuildV29(
            (appid: 10u, type: "Game"),
            (appid: 20u, type: "Music"),
            (appid: 30u, type: "Tool"));

        using var s = new MemoryStream(bytes);
        var types = AppInfoCache.ReadTypes(s);

        Assert.Equal(SteamAppType.Game, types[10]);
        Assert.Equal(SteamAppType.Music, types[20]);
        Assert.Equal(SteamAppType.Tool, types[30]);
    }

    [Fact]
    public void 모르는_버전이면_조용히_빈_결과를_준다()
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(0x0BADF00Du).CopyTo(bytes, 0);

        using var s = new MemoryStream(bytes);
        Assert.Empty(AppInfoCache.ReadTypes(s));
    }

    [Fact]
    public void 파일이_없으면_빈_결과를_준다()
    {
        var missing = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N") + ".vdf");
        Assert.Empty(AppInfoCache.TryReadTypes(missing));
    }

    [Fact]
    public void 파일이_손상되어도_예외를_던지지_않는다()
    {
        var path = Path.Combine(Path.GetTempPath(), "sgt-tests-" + Guid.NewGuid().ToString("N") + ".vdf");
        try
        {
            // 올바른 매직 + 뒤이어 쓰레기 바이트.
            var junk = new byte[64];
            BitConverter.GetBytes(0x07564429u).CopyTo(junk, 0);
            Random.Shared.NextBytes(junk.AsSpan(16));
            File.WriteAllBytes(path, junk);

            var warnings = new List<string>();
            var types = AppInfoCache.TryReadTypes(path, warnings.Add);

            Assert.Empty(types);          // 결과는 비어 있고
            // 예외 대신 경고로 처리되었거나, 조용히 끝났거나 — 둘 다 허용된다.
        }
        finally { try { File.Delete(path); } catch { } }
    }

    // ── 카탈로그 조립 ────────────────────────────────────────────

    [Fact]
    public void appmanifest에서_이름과_설치폴더를_읽는다()
    {
        using var lib = new FakeSteamLibrary();
        lib.AddApp(367520, "Hollow Knight", "hollow_knight_folder");

        var catalog = SteamCatalog.Build([lib.LibraryRoot], null);

        Assert.True(catalog.HasApps);
        Assert.False(catalog.HasTypeInfo);        // appinfo 를 주지 않았으므로
        var entry = catalog.Find("hollow_knight_folder");
        Assert.NotNull(entry);
        Assert.Equal(367520u, entry!.AppId);
        Assert.Equal("Hollow Knight", entry.Name);
        Assert.Equal(SteamAppType.Unknown, entry.Type);
    }

    [Fact]
    public void appinfo가_있으면_종류가_채워진다()
    {
        using var lib = new FakeSteamLibrary();
        lib.AddApp(10, "어떤 게임", "GameDir");
        lib.AddApp(20, "어떤 사운드트랙", "OstDir");
        lib.WriteAppInfo((10, "Game"), (20, "Music"));

        var catalog = SteamCatalog.Build([lib.LibraryRoot], lib.SteamRoot);

        Assert.True(catalog.HasTypeInfo);
        Assert.Equal(SteamAppType.Game, catalog.Find("GameDir")!.Type);
        Assert.Equal(SteamAppType.Music, catalog.Find("OstDir")!.Type);
    }

    [Fact] // 사운드트랙 DLC 가 본편과 같은 폴더를 쓰는 경우
    public void 한_폴더를_공유하면_게임이_대표가_된다()
    {
        using var lib = new FakeSteamLibrary();
        // 파일 열거가 이름순이라 appmanifest_10 이 먼저 읽혀 대표가 된다.
        // 그 대표가 Music 이어야 승격 로직이 실제로 동작하는지 확인할 수 있다.
        lib.AddApp(10, "본편 사운드트랙", "SharedDir");
        lib.AddApp(20, "본편", "SharedDir");
        lib.WriteAppInfo((10, "Music"), (20, "Game"));

        var catalog = SteamCatalog.Build([lib.LibraryRoot], lib.SteamRoot);

        Assert.Equal(SteamAppType.Game, catalog.Find("SharedDir")!.Type);
        Assert.Equal("본편", catalog.Find("SharedDir")!.Name);
    }

    [Fact]
    public void 라이브러리가_없으면_빈_카탈로그()
    {
        var missing = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N"));
        var catalog = SteamCatalog.Build([missing], null);

        Assert.False(catalog.HasApps);
        Assert.Null(catalog.Find("무엇이든"));
    }

    // ── 라이브러리 루트 되짚기 ───────────────────────────────────

    [Fact]
    public void common경로에서_라이브러리_루트를_되짚는다()
    {
        var common = Path.Combine("X:", "SteamLibrary", "steamapps", "common");
        Assert.Equal(
            Path.Combine("X:", "SteamLibrary"),
            SteamScanner.LibraryRootOf(common));
    }

    [Fact]
    public void steamapps_구조가_아니면_루트를_되짚지_않는다()
    {
        var odd = Path.Combine("X:", "어딘가", "다른폴더");
        Assert.Null(SteamScanner.LibraryRootOf(odd));
    }
}

/// <summary>테스트용 appinfo.vdf(문자열 테이블 방식, 매직 0x07564429) 생성기.</summary>
internal static class FakeAppInfo
{
    public static byte[] BuildV29(params (uint appid, string type)[] apps)
    {
        // 문자열 테이블: 키 이름들이 인덱스로 참조된다.
        string[] table = ["appinfo", "common", "type"];
        const int iAppInfo = 0, iCommon = 1, iType = 2;

        var body = new MemoryStream();
        var w = new BinaryWriter(body, Encoding.UTF8);

        foreach (var (appid, type) in apps)
        {
            // 섹션 본문: appinfo { common { type = "..." } }
            var section = new MemoryStream();
            var sw = new BinaryWriter(section, Encoding.UTF8);
            sw.Write(new byte[4 + 4 + 8 + 20 + 4 + 20]);   // 고정 헤더(내용은 안 읽는다)
            sw.Write((byte)0x00); sw.Write(iAppInfo);      // appinfo {
            sw.Write((byte)0x00); sw.Write(iCommon);       //   common {
            sw.Write((byte)0x01); sw.Write(iType);         //     type = "..."
            sw.Write(Encoding.UTF8.GetBytes(type)); sw.Write((byte)0);
            sw.Write((byte)0x08);                          //   }  (common 끝)
            sw.Write((byte)0x08);                          // }    (appinfo 끝)
            sw.Write((byte)0x08);                          //      (루트 끝)
            sw.Flush();

            var data = section.ToArray();
            w.Write(appid);
            w.Write((uint)data.Length);
            w.Write(data);
        }
        w.Write(0u);                                       // appid 0 = 끝
        w.Flush();

        var strings = new MemoryStream();
        var stw = new BinaryWriter(strings, Encoding.UTF8);
        stw.Write(table.Length);
        foreach (var s in table) { stw.Write(Encoding.UTF8.GetBytes(s)); stw.Write((byte)0); }
        stw.Flush();

        var result = new MemoryStream();
        var rw = new BinaryWriter(result, Encoding.UTF8);
        rw.Write(0x07564429u);                             // magic
        rw.Write(1u);                                      // universe
        rw.Write((long)(16 + body.Length));                // 문자열 테이블 오프셋
        rw.Write(body.ToArray());
        rw.Write(strings.ToArray());
        rw.Flush();
        return result.ToArray();
    }
}

/// <summary>테스트용 가짜 Steam 라이브러리(appmanifest + appcache).</summary>
internal sealed class FakeSteamLibrary : IDisposable
{
    public string LibraryRoot { get; }
    public string SteamRoot => LibraryRoot;

    private readonly string _steamapps;

    public FakeSteamLibrary()
    {
        LibraryRoot = Path.Combine(Path.GetTempPath(), "sgt-tests", Guid.NewGuid().ToString("N"));
        _steamapps = Path.Combine(LibraryRoot, "steamapps");
        Directory.CreateDirectory(Path.Combine(_steamapps, "common"));
    }

    public void AddApp(uint appid, string name, string installDir) =>
        File.WriteAllText(
            Path.Combine(_steamapps, $"appmanifest_{appid}.acf"),
            $$"""
            "AppState"
            {
            	"appid"		"{{appid}}"
            	"name"		"{{name}}"
            	"installdir"		"{{installDir}}"
            }
            """);

    public void WriteAppInfo(params (uint appid, string type)[] apps)
    {
        var dir = Path.Combine(SteamRoot, "appcache");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "appinfo.vdf"), FakeAppInfo.BuildV29(apps));
    }

    public void Dispose()
    {
        try { Directory.Delete(LibraryRoot, recursive: true); } catch { }
    }
}
