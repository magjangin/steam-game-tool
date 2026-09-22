using System.Collections.Generic;
using System.Linq;

namespace steam_game_tool;

/// <summary>스캔할 마커 종류.</summary>
public enum Marker
{
    Any,
    UnityMono,
    MonoBleedingEdge,
    MonoLegacy,
    UnityIl2Cpp,
    Managed,
    NestedUnity,
    NestedNonUnity,
    Nested,
    RuntimeUnresolved, BackendUnresolved, Scanned, ShadowedName, PlayerUnresolved, NonUnity
}

public sealed class ScanResult
{
    public List<SteamGame> Games { get; } = new();
    public List<string> Roots { get; } = new();

    /// <summary>
    /// 스캔 중 삼킨 오류들. 비어 있지 않으면 결과가 불완전할 수 있다는 뜻이다.
    /// (권한 없는 폴더는 EnumerationOptions.IgnoreInaccessible 로 조용히 건너뛰므로 여기 잡히지 않는다.)
    /// </summary>
    public List<string> Warnings { get; } = new();

    /// <summary>appmanifest 를 하나라도 읽었는가. false 면 게임/비게임을 구분할 수 없다.</summary>
    public bool HasCatalog { get; set; }

    /// <summary>appcache\appinfo.vdf 에서 앱 종류를 읽어냈는가. false 면 종류별 집계는 의미가 없다.</summary>
    public bool HasTypeInfo { get; set; }

    /// <summary>
    /// 마커 집계의 대상. 카탈로그가 있으면 "설치된 게임(또는 Unity 백엔드가 감지된 오프라인 게임)"만, 없으면 전부 센다.
    /// 사운드트랙·소프트웨어·도구나 내용 없는 잔여 폴더가 섞이면 수치가 부풀기 때문이다.
    /// </summary>
    public IEnumerable<SteamGame> CountedGames =>
        HasCatalog ? Games.Where(g => (!g.IsOrphan || g.IsPlayableUnity) && g.IsGameLike) : Games;

    public int UnityMonoCount => CountedGames.Count(g => g.ClassifiedBackend == UnityBackend.Mono);
    public int UnityIl2CppCount => CountedGames.Count(g => g.ClassifiedBackend == UnityBackend.Il2Cpp);
    public int MonoBleedingEdgeCount => CountedGames.Count(g => g.HasModernMono);
    public int MonoLegacyCount => CountedGames.Count(g => g.HasLegacyMono);
    public int ManagedCount => CountedGames.Count(g => g.HasManaged);
    public int NestedUnityCount => CountedGames.Count(g => g.HasNestedUnity);
    public int NestedNonUnityCount => CountedGames.Count(g => g.HasNestedNonUnity);
    public int NestedCount => CountedGames.Count(g => g.HasNestedMatch);

    /// <summary>집계 대상이 된 폴더 수(= 위 마커 수치들의 분모).</summary>
    public int GameCount => CountedGames.Count();

    /// <summary>appmanifest 가 없고 Unity 백엔드도 없는 순수 잔여물 폴더 수.</summary>
    public int OrphanCount => HasCatalog ? Games.Count(g => g.IsOrphan && !g.IsPlayableUnity) : 0;

    /// <summary>지정한 종류의 폴더 수.</summary>
    public int CountOf(SteamAppType type) => Games.Count(g => !g.IsOrphan && g.AppType == type);
}
