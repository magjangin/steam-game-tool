using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace steam_game_tool;

/// <summary>한 설치 폴더에서 발견한 Unity 스크립팅 백엔드. 여러 실행 파일이 있으면 둘 다 설정될 수 있다.</summary>
[Flags]
public enum UnityBackend
{
    Unknown = 0,
    Mono = 1,
    Il2Cpp = 2,
}

/// <summary>
/// 배지에 표시할 태그 문자열.
/// SteamGame.Tags 와 Converters.TagBrush 가 이 상수를 공유해야 한다.
/// 문자열을 직접 적으면 한쪽만 바뀌어도 컴파일러가 잡지 못한다.
/// </summary>
public static class TagNames
{
    public const string UnityMono = "Unity Mono";
    public const string UnityIl2Cpp = "Unity IL2CPP";
    public const string MonoBleedingEdge = "MonoBleedingEdge";
    public const string Managed = "Managed";
    public const string Nested = "중첩";
}

/// <summary>steamapps\common 아래의 게임 폴더 한 개에 대한 정보.</summary>
public sealed class SteamGame : INotifyPropertyChanged
{
    private bool _installChecked = true;

    /// <summary>
    /// 목록 체크박스 = MelonLoader 설치 대상 여부. 판별·집계·내보내기에는 쓰지 않는다.
    /// 목록 체크박스와 「MelonLoader 설치 대상」 전체 체크박스가 서로 따라가도록 이 속성만 변경 알림을 보낸다.
    /// </summary>
    public bool InstallChecked
    {
        get => _installChecked;
        set
        {
            if (_installChecked == value) return;
            _installChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InstallChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>디스크의 폴더 이름. 스토어 표기명과 다를 수 있다.</summary>
    public required string Name { get; init; }
    public required string InstallDir { get; init; }

    /// <summary>Steam AppID. null 이면 appmanifest 가 없다 = 설치된 앱이 아니다(잔여 폴더).</summary>
    public uint? AppId { get; set; }

    /// <summary>Steam 스토어 표기 이름. 카탈로그를 못 읽었으면 null.</summary>
    public string? StoreName { get; set; }

    /// <summary>Steam 이 분류한 앱 종류.</summary>
    public SteamAppType AppType { get; set; } = SteamAppType.Unknown;

    /// <summary>화면에 보여 줄 이름. 스토어 이름이 있으면 그것, 없으면 폴더명.</summary>
    public string DisplayName => StoreName ?? Name;
    private string? _nameKey;

    /// <summary>
    /// 정규화한 표시 이름. 정렬과 내보내기의 키다.
    /// 정렬 비교마다 정규식이 다시 돌지 않도록 <see cref="Selection"/> 과 같은 방식으로 한 번만 계산한다.
    /// 스캔이 <see cref="StoreName"/> 을 채운 뒤에야 처음 읽히므로 캐시해도 값이 어긋나지 않는다.
    /// </summary>
    public string NameKey => _nameKey ??= GameNames.Normalize(DisplayName);
    public List<BackendEvidence> BackendEvidence { get; } = new();
    public int? DataDirectoryCount { get; set; }
    public string? BackendDataPath => BackendEvidence.Count == 1 ? BackendEvidence[0].DataPath : null;
    public string BackendSource => BackendEvidence.Count == 0 || BackendEvidence.All(e => e.Backend == UnityBackend.Unknown) ? "unknown" :
        DataDirectoryCount == 1 && BackendEvidence.Count == 1 ? "single" :
        BackendEvidence.Select(e => e.Layer).Distinct().Count() == 1 ? BackendEvidence[0].Layer : "mixed";
    /// <summary>appmanifest 가 없는 폴더(삭제 후 남은 잔여물 또는 오프라인 보존 게임 등).</summary>
    public bool IsOrphan => AppId is null;

    /// <summary>엔진 검사로 Unity 백엔드가 확인된 실제 플레이 가능한 게임인가.</summary>
    public bool IsPlayableUnity => Backend != UnityBackend.Unknown;

    /// <summary>
    /// 게임으로 볼 만한가. 명시적 비게임(사운드트랙·소프트웨어·도구 등)은 엄격히 배제하며,
    /// AppId 가 없어도(IsOrphan) 실제 Unity 백엔드가 감지된 오프라인 게임(Muse Dash 등)은 포함한다.
    /// </summary>
    public bool IsGameLike =>
        AppType is SteamAppType.Game or SteamAppType.Demo ||
        (IsOrphan && IsPlayableUnity) ||
        (!IsOrphan && AppType is SteamAppType.Unknown);

    /// <summary>게임 폴더 아래 깊이 8 안 어딘가에 MonoBleedingEdge 폴더(=Unity Mono 런타임) 존재. 배지용이며 분류에는 쓰지 않는다.</summary>
    public bool HasMonoBleedingEdge { get; set; }

    /// <summary>게임 폴더 아래 깊이 8 안 어딘가에 Managed 폴더(=Unity 어셈블리) 존재. 배지용이며 분류에는 쓰지 않는다.</summary>
    public bool HasManaged { get; set; }

    /// <summary>폴더명과 같은 이름의 하위 폴더 존재(중첩).</summary>
    public bool HasNestedMatch { get; set; }

    /// <summary>Unity 데이터 폴더와 런타임 파일로 판별한 스크립팅 백엔드.</summary>
    public UnityBackend Backend { get; set; }

    public bool HasUnityMono => (Backend & UnityBackend.Mono) != 0;
    public bool HasUnityIl2Cpp => (Backend & UnityBackend.Il2Cpp) != 0;

    /// <summary>설치 폴더 바로 아래가 아닌 곳(inner)에 백엔드가 정해진 Unity 플레이어가 있다. 보기 「중첩」의 기준이다.</summary>
    public bool HasNestedUnity => BackendEvidence.Any(e => e.DataPath is not null && e.Layer == "inner" && e.Backend != UnityBackend.Unknown);
    private PlayerSelection? _selection;
    public PlayerSelection Selection => _selection ??= GameClassification.Select(this);
    public BackendEvidence? MainPlayer => Selection.Player;
    public UnityBackend ClassifiedBackend => MainPlayer?.Backend is UnityBackend.Mono or UnityBackend.Il2Cpp
        ? MainPlayer.Backend : UnityBackend.Unknown;
    public bool IsBackendUnresolved => ClassifiedBackend == UnityBackend.Unknown;
    public bool IsRuntimeUnresolved => ClassifiedBackend == UnityBackend.Mono && MainPlayer?.Runtime is null;
    public bool HasModernMono => ClassifiedBackend == UnityBackend.Mono && MainPlayer?.Runtime == "monobleedingedge";
    public bool HasLegacyMono => ClassifiedBackend == UnityBackend.Mono && !HasModernMono && !IsRuntimeUnresolved;

    /// <summary>중첩 폴더이지만 Unity 백엔드가 없는 게임(언리얼 엔진 패키징 구조 등).</summary>
    public bool HasNestedNonUnity => HasNestedMatch && !HasUnityMono && !HasUnityIl2Cpp;

    public bool HasAny => Backend != UnityBackend.Unknown ||
                          HasMonoBleedingEdge || HasManaged;

    /// <summary>배지로 표시할 태그 목록.</summary>
    public IEnumerable<string> Tags
    {
        get
        {
            if (HasUnityMono) yield return TagNames.UnityMono;
            if (HasUnityIl2Cpp) yield return TagNames.UnityIl2Cpp;
            if (HasMonoBleedingEdge) yield return TagNames.MonoBleedingEdge;
            if (HasManaged) yield return TagNames.Managed;
            if (HasNestedMatch) yield return TagNames.Nested;
        }
    }
}
