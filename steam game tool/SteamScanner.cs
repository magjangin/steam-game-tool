using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace steam_game_tool;

/// <summary>
/// 스캔의 조립 지점. 폴더 찾기(<see cref="SteamLibraries"/>), 앱 카탈로그(<see cref="SteamCatalog"/>),
/// 폴더 내부 검사(<see cref="UnityInspector"/>) 를 엮어 <see cref="ScanResult"/> 를 만들고 걸러 낸다.
/// </summary>
public static class SteamScanner
{
    /// <summary>스캔할 steamapps\common 폴더들과 Steam 외부 보존본 게임 폴더들을 찾는다.</summary>
    public static List<string> FindCommonFolders() => SteamLibraries.FindCommonFolders();

    /// <summary>steamapps\common 경로에서 라이브러리 루트를 되짚는다. 구조가 다르면 null.</summary>
    internal static string? LibraryRootOf(string commonDir) => SteamLibraries.LibraryRootOf(commonDir);

    /// <summary>
    /// 폴더 하나의 백엔드. 모든 플레이어 판정의 합집합이라 Mono | Il2Cpp 가 될 수 있다.
    /// <see cref="Scan"/> 과 같은 값을 낸다. 대표 플레이어 기준의 분류는 <see cref="SteamGame.ClassifiedBackend"/>.
    /// </summary>
    public static UnityBackend DetectUnityBackend(string installDir) =>
        UnityInspector.Inspect(installDir).Backend;

    /// <summary>지정한 common 폴더(들)을 스캔한다. roots 가 비면 자동 감지.</summary>
    /// <param name="catalog">
    /// 쓸 Steam 앱 카탈로그. null 이면 레지스트리와 라이브러리에서 직접 만든다.
    /// 테스트나, 카탈로그를 여러 스캔에 재사용하고 싶을 때 넘긴다.
    /// </param>
    public static ScanResult Scan(
        IEnumerable<string>? roots = null,
        Action<string>? progress = null,
        SteamCatalog? catalog = null)
    {
        var result = new ScanResult();

        var rootList = (roots ?? FindCommonFolders())
            .Where(Directory.Exists)
            .ToList();

        if (rootList.Count == 0)
        {
            progress?.Invoke("스캔할 steamapps\\common 폴더를 찾지 못했습니다.");
            return result;
        }

        // 각 common 의 라이브러리 루트(= steamapps 의 부모)에서 appmanifest 를 읽고,
        // Steam 설치 폴더의 appinfo 캐시에서 앱 종류를 읽는다. 실패해도 스캔은 계속된다.
        if (catalog is null)
        {
            progress?.Invoke("Steam 앱 목록을 읽는 중...");
            catalog = SteamCatalog.Build(
                rootList.Select(LibraryRootOf).Where(p => p is not null).Distinct(StringComparer.OrdinalIgnoreCase)!,
                SteamLibraries.FindSteamPath(),
                w => result.Warnings.Add(w));
        }
        result.HasCatalog = catalog.HasApps;
        result.HasTypeInfo = catalog.HasTypeInfo;

        foreach (var root in rootList)
        {
            result.Roots.Add(root);

            string[] dirs;
            if (SteamLibraries.IsGameDirectory(root))
            {
                dirs = new[] { root };
            }
            else
            {
                try { dirs = Directory.GetDirectories(root); }
                catch (Exception ex)
                {
                    result.Warnings.Add($"{root}: {ex.Message}");
                    continue;
                }
            }

            // 게임 폴더끼리는 서로 독립적이고 작업이 I/O 바운드라 병렬로 훑는다.
            var gate = new object();
            Parallel.ForEach(dirs, dir =>
            {
                var name = Path.GetFileName(dir);
                progress?.Invoke($"검사 중: {name}");

                var game = new SteamGame { Name = name, InstallDir = dir };

                // Steam 카탈로그에 있으면 실제 이름과 종류를 채운다.
                // 없으면 AppId 가 null 로 남아 "잔여 폴더"로 분류된다.
                if (catalog.Find(name) is { } entry)
                {
                    game.AppId = entry.AppId;
                    game.StoreName = entry.Name;
                    game.AppType = entry.Type;
                }

                // Unity 데이터/런타임/어셈블리 마커를 하위 폴더 순회로 함께 수집한다.
                var inspection = UnityInspector.Inspect(dir);

                // 1) MonoBleedingEdge: 루트 또는 하위 깊이에 존재하는가
                game.HasMonoBleedingEdge = inspection.HasMonoBleedingEdge;

                // 2) Managed: 하위 어딘가에 유효한 Managed 폴더 존재(BackUpThisFolder 제외).
                game.HasManaged = inspection.HasManaged;

                // 3) 중첩: 폴더명과 같은 이름의 하위 폴더.
                try { game.HasNestedMatch = Directory.Exists(Path.Combine(dir, name)); }
                catch { }

                // 4) Unity 스크립팅 백엔드: 실제 Unity 데이터/런타임 파일 조합으로 판별.
                game.Backend = inspection.Backend;
                game.BackendEvidence.AddRange(inspection.Evidence);
                game.DataDirectoryCount = inspection.DataDirectoryCount;

                lock (gate)
                {
                    result.Games.Add(game);
                    if (inspection.Error is not null)
                        result.Warnings.Add($"{name}: {inspection.Error}");
                }
            });
        }

        result.Games.Sort((a, b) =>
            string.Compare(a.NameKey, b.NameKey, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    /// <summary>마커로 걸러 낸다.</summary>
    /// <param name="gamesOnly">
    /// true 면 <b>확실히 게임이 아닌 것</b>(사운드트랙·소프트웨어·도구 등)과 잔여 폴더를 제외한다.
    /// 종류를 모르는 폴더는 남겨 둔다 — 카탈로그를 못 읽었다고 결과가 비면 안 되기 때문이다.
    /// </param>
    public static IEnumerable<SteamGame> Filter(
        ScanResult result, Marker marker, bool gamesOnly = false)
    {
        // 카탈로그가 없으면 게임/비게임을 구분할 근거가 없으므로 gamesOnly 는 무시한다.
        var source = gamesOnly && result.HasCatalog
            ? result.CountedGames
            : result.Games;

        return marker switch
        {
            Marker.UnityMono        => source.Where(g => g.ClassifiedBackend == UnityBackend.Mono),
            Marker.MonoBleedingEdge => source.Where(g => g.HasModernMono),
            Marker.MonoLegacy       => source.Where(g => g.HasLegacyMono),
            Marker.UnityIl2Cpp      => source.Where(g => g.ClassifiedBackend == UnityBackend.Il2Cpp),
            Marker.Managed          => source.Where(g => g.HasManaged),
            Marker.NestedUnity      => source.Where(g => g.HasNestedUnity),
            Marker.NestedNonUnity   => source.Where(g => g.HasNestedNonUnity),
            Marker.Nested           => source.Where(g => g.HasNestedMatch),
            Marker.ShadowedName => source.Where(g => g.HasNestedMatch && g.Backend != UnityBackend.Unknown),
            Marker.RuntimeUnresolved => source.Where(g => g.IsRuntimeUnresolved),
            Marker.BackendUnresolved => source.Where(g => g.IsBackendUnresolved),
            Marker.PlayerUnresolved => source.Where(g => g.IsBackendUnresolved && (g.HasAny || g.AppType == SteamAppType.Unknown)),
            Marker.NonUnity => source.Where(g => g.IsBackendUnresolved && !g.HasAny && g.AppType != SteamAppType.Unknown),
            Marker.Scanned => source,
            _ => source.Where(g => !g.IsBackendUnresolved),
        };
    }
}
