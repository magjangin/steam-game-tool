# 11. MelonLoader 일괄 설치

체크된 게임에 MelonLoader를 설치합니다. 방법은 두 가지이고, 둘 다 「MelonLoader 설치」 메뉴에 있습니다. **자동 설치는 보기와 상관없이 전체 Unity 게임**이 대상이고, **zip 직접 선택은 지금 보기 목록**이 대상입니다. 두 경우 모두 「게임만」과 게임별 체크가 적용됩니다.

| 메뉴 항목 | zip | 버전 |
|---|---|---|
| 「자동 설치…」 | GitHub 릴리스에서 받음 (한 번 받은 것은 다시 씀) | **MonoBleedingEdge·IL2CPP 게임만**, 0.7.3 이상 중 최신 정식 릴리스. 구형 레거시 Mono는 건너뜀 |
| 「zip 직접 선택…」 | 가지고 있는 zip (nightly 등) | 고른 zip을 체크된 게임 모두에 씀(구형 레거시 포함). 규칙 미적용, 네트워크 안 씀 |

이 도구에서 게임 폴더에 파일을 쓰는 곳(`MelonLoaderInstaller.cs`)과 네트워크를 쓰는 곳(`MelonLoaderReleases.cs`의 `MelonReleaseClient`)은 여기뿐입니다.
구현은 같은 개발자의 다른 프로젝트 muse dash tool의 `MelonLoaderService.cs`를 참고했고, 일괄 설치에 맞게 바꿨습니다(아래 「muse dash tool과 다른 점」).
실제 라이브러리에 설치·교체한 기록은 [12-MelonLoader 작업 기록](12-MelonLoader-작업-기록.md)에 따로 있습니다.

## 쓰는 법

1. 스캔합니다. 자동 설치는 어느 보기에서 눌러도 전체 Unity 게임을 봅니다. 게임마다 규칙이 MBE·IL2CPP만 고르기 때문입니다. zip 직접 선택만 「보기」로 대상을 고릅니다.
   - 전에는 자동 설치도 지금 보기에 묶여 있었습니다. 그래서 「MonoBleedingEdge」 보기에서 누른 일괄 설치(486개)에서 IL2CPP 게임 79개(Gestalt: Steam & Cinder 등)가 빠졌습니다(2026-09-21, 사용자 지적).
2. 빼고 싶은 게임은 목록 왼쪽 체크를 풉니다. 목록 위 「MelonLoader 설치 대상」 체크박스는 **지금 보기 목록**을 모두 체크하거나 모두 풉니다(일부만 체크돼 있으면 모두 체크). 옆에 `체크 수 / 전체` 개수가 나옵니다. 풀어 둔 체크는 다시 스캔해도 유지되고, 앱을 닫으면 초기화됩니다.
3. 「MelonLoader 설치」 메뉴 맨 아래의 「다른 버전이 설치된 게임도 교체」는 **기본으로 켜져 있습니다**(사용자 요청, 2026-09-21). 이미 설치된 게임을 그대로 두려면 눌러서 끕니다. 눌러도 메뉴는 닫히지 않습니다.
4. 「MelonLoader 설치 ▸ 자동 설치…」 또는 「MelonLoader 설치 ▸ zip 직접 선택…」을 누릅니다.
5. 확인 창에서 버전 규칙, 설치·교체 대상, 건너뛸 게임과 이유를 확인한 뒤 「N개 설치」를 누릅니다.

## 버전 규칙 (자동 설치)

`MelonVersions.For(game)` — 사용자가 정했습니다(2026-09-21).

| 게임 | 규칙 | 2026-09-21 선택 | 근거 |
|---|---|---|---|
| MonoBleedingEdge·IL2CPP | **0.7.3 이상 중 최신 정식 릴리스** | v0.7.3 | 사용자 지정: GUNVOLT RECORDS Cychronicle(Unity 2021.3, MBE)은 0.7.3 이상이어야 합니다 |
| 구형 레거시 Mono (`_Data\Mono`, .NET 3.5 런타임) | **자동 설치 안 함** — 「구형 레거시 Mono — 자동 설치 대상 아님」으로 건너뜀. 이미 깔린 MelonLoader도 건드리지 않음 | — | 사용자 결정: "사이크로니클 같은 케이스만 0.7.3 설치로 하자" |

이렇게 정하기까지 규칙이 두 번 바뀌었습니다(2026-09-21).
1. 처음에는 구형 레거시에 0.6.1을 넣었는데, 사용자가 "Cuphead는 0.7.1"이라고 바로잡았습니다.
2. 이어서 LiEat를 실행해 보니, Steam판 LiEat는 Unity가 **런처(`LiEat_Launcher.exe`, Unity 5.4, x86)뿐**이었습니다. 본편 `EN\LiEat1~3\Game.exe`는 WOLF RPG 에디터 게임입니다. 즉 MelonLoader가 떠도 본편은 모딩할 수 없습니다.
3. 구형 레거시에는 이런 게임이 섞여 있어서, 사용자가 구형 레거시는 자동 설치에서 빼기로 했습니다.

- 「구형 레거시」는 보기 목록의 「Mono (구형 레거시 - Data/Mono)」와 같은 기준입니다(`SteamGame.HasLegacyMono`). `MelonVersions.For`가 null을 돌려줍니다.
- 0.7.3보다 새 정식 릴리스가 나오면 자동으로 그것을 씁니다. 프리릴리스는 쓰지 않습니다.
- 규칙을 바꾸려면 `MelonVersions.For`와 `MelonVersions.Modern`을 고칩니다. `MelonRequirement`는 "정확히 이 버전"(`Exact`) 규칙도 지원합니다.

## 이미 설치된 게임

설치된 버전은 `MelonLoader\` 아래 `MelonLoader.dll`의 ProductVersion(없으면 FileVersion)에서 읽습니다. `+커밋` 꼬리는 뗍니다.

| 상태 | 교체 꺼짐 | 교체 켜짐 |
|---|---|---|
| 규칙에 맞음 (자동) / zip과 같은 버전 (직접) | 건너뜀 「이미 MelonLoader 있음」 | 건너뜀 |
| 규칙과 다름 / zip과 다른 버전 | 건너뜀 「다른 버전이 설치됨」 (확인 창에 `0.7.1 → 0.7.3`처럼 표시) | **교체** |
| 버전을 읽지 못함 | 건너뜀 「이미 MelonLoader 있음」 | **교체** |

- **CI(nightly) 빌드는 같은 번호 정식 릴리스의 시험판으로 봅니다**(`MelonVersions.AtLeast`). 순서는 `0.7.3-ci.2497 < 0.7.3 < 0.8.0-ci.2548`입니다. 그리고 **자동 설치 규칙은 정식 릴리스만 만족합니다**(`MelonRequirement.IsSatisfiedBy`). CI 빌드는 번호와 상관없이 교체 대상이라, 0.8.0-ci도 최신 정식판(지금은 0.7.3)으로 바뀝니다. 사용자가 모든 게임을 정식판으로 맞추기로 했습니다(2026-09-21).
  - CI 빌드는 DLL 버전의 네 번째 자리가 빌드 번호입니다(`0.7.3.2497`). 정식 릴리스는 0입니다(`0.7.3.0`, v0.7.1·v0.7.3 zip에서 확인). 확인 창에는 MelonLoader 로그와 같이 `0.7.3-ci.2497`로 표시합니다.
  - 전에는 `0.7.3.2497`을 `0.7.3`보다 높은 버전으로 비교했습니다. 그래서 Halchemist 등 0.7.3 CI 빌드가 깔린 46개가 교체되지 않았습니다(2026-09-21, 사용자 지적).
- **교체**할 때는 `version.dll`, `dobby.dll`, `NOTICE.txt`, `MelonLoader\`만 바꿉니다. `Mods`, `Plugins`, `UserData`, `UserLibs`는 그대로 둡니다. 0.6.x zip은 루트에 `dobby.dll`·`NOTICE.txt`를 두지만 0.7.x zip에는 없어서, 둘 다 MelonLoader 것으로 보고 치웁니다.
- 교체 전에 옛 파일을 지우지 않고 `*.replaced-xxxxxxxx`로 옮겨 둡니다. 새 설치가 성공하면 지우고, 실패하면 되돌립니다.
- `MelonLoader\` 없이 `version.dll`만 있으면, 교체를 켜도 건너뜁니다. 다른 모드의 프록시일 수 있기 때문입니다.

## GitHub 다운로드

- `GET https://api.github.com/repos/LavaGang/MelonLoader/releases?per_page=100`을 씁니다(User-Agent 필수, 인증 없이 시간당 60회). 목록은 버튼을 누를 때만 받고, 앱을 시작할 때는 받지 않습니다.
- 에셋은 이름이 정확히 `MelonLoader.x64.zip`, `MelonLoader.x86.zip`인 것만 씁니다.
- 필요한 (버전, 비트수) 조합만 **한 번씩** 받아서 모든 게임에 씁니다. 2026-09-21 전체 Unity 기준 2개(v0.7.3 x64·x86, 약 38MB)입니다.
- 받은 zip은 `%LOCALAPPDATA%\SteamGameTool\MelonLoader\<태그>\`에 둡니다. 크기가 릴리스 정보와 같으면 다음에 받지 않고 다시 씁니다.
- 크기는 항상 대조합니다. `.sha512` 에셋은 v0.7.0 이하에만 있어서, SHA-512는 그런 릴리스만 대조합니다. **지금 규칙이 쓰는 v0.7.3에는 없어서 크기만 대조합니다.**
- `.sha512` 인코딩은 릴리스마다 다릅니다. v0.7.0은 ASCII 128바이트, v0.6.1은 BOM 있는 UTF-16 LE 258바이트입니다. `ReadSha512`가 둘 다 읽고, 못 읽으면 설치하지 않습니다.
- 받은 zip도 아래 「zip」 검사를 거칩니다.
- GitHub에 접속하지 못하면 설치하지 않고, 「zip 직접 선택…」을 쓰라고 알립니다.

## zip

- zip 최상위에 `version.dll`과 `MelonLoader/`가 있어야 합니다. 소스 코드 zip(`MelonLoader-0.6.5.1.zip`처럼 최상위가 `MelonLoader-<버전>/` 폴더인 것)은 거부합니다.
- 비트수는 파일 이름이 아니라 zip 안 `version.dll`의 PE 헤더에서 읽습니다. 같은 비트수 zip을 두 개 고르면 거부합니다.
- 버전은 자동 설치에서는 릴리스 태그에서, 직접 고른 zip에서는 zip 안 `MelonLoader.dll`에서 읽습니다.
- Mono와 IL2CPP는 같은 zip을 씁니다. IL2CPP 게임은 첫 실행 때 MelonLoader가 직접 필요한 파일을 내려받습니다.

## 설치 위치

판별 단계에서 고른 **대표 플레이어의 실행 파일이 있는 폴더**에 설치합니다(`SteamGame.MainPlayer.ExecutablePath`).
중첩 게임이면 안쪽 폴더에 설치되고, 확인 창에 `→ 하위 경로`로 표시됩니다.

## 건너뛰는 경우

위에서부터 먼저 해당하는 이유 하나만 표시합니다.

| 이유 | 조건 |
|---|---|
| 대표 실행 파일을 찾지 못함 | 대표 플레이어에 `<이름>_Data` 옆 `<이름>.exe`가 없음 (예: Chrono Ark는 exe가 `x64\Master\`에 따로 있음) |
| 대표 실행 파일이 확실하지 않음 | 대표 플레이어 선택 신뢰도가 `low` — 크기나 경로 순서로 고른 후보이거나 번들 빌드 |
| BepInEx 설치됨 | `BepInEx\` 폴더 또는 `doorstop_config.ini` |
| MelonLoader가 아닌 version.dll 있음 | `MelonLoader\` 없이 `version.dll`만 있음 |
| 안티치트 폴더 있음 | 대상 폴더나 설치 루트에 `EasyAntiCheat\` 또는 `BattlEye\` |
| 실행 파일 비트수를 읽지 못함 | exe가 x86·x64 PE가 아님 |
| 구형 레거시 Mono — 자동 설치 대상 아님 | 자동 설치에서 구형 레거시 Mono(`HasLegacyMono`). 「zip 직접 선택…」에서는 해당 없음 |
| MelonLoader를 지운 흔적 있음 — 자동 설치 안 함 | 자동 설치에서, `MelonLoader\`는 없는데 `UserLibs\`가 있거나 `UserData\`·`Mods\`·`Plugins\`가 모두 있음(`MelonTarget.WasRemoved`). 「zip 직접 선택…」에서는 해당 없음 |
| 이미 MelonLoader 있음 / 다른 버전이 설치됨 | 위 「이미 설치된 게임」 표 |
| 규칙에 맞는 릴리스가 GitHub에 없음 | 자동 설치에서 규칙을 만족하는 릴리스가 없음 |
| 맞는 x86(x64) zip 없음 | 게임 비트수의 zip이 없음(직접 선택에서 고르지 않음, 또는 릴리스에 없음) |
| zip 안의 파일과 같은 이름이 이미 있음 | zip의 파일이 대상 폴더에 이미 있음(교체로 바뀔 파일은 제외) |

신뢰도 `low`를 건너뛰는 이유: 2026-09-21 점검에서 low 5개 중 2개가 잘못된 폴더를 가리켰습니다.
The Long Dark는 `tld_dlc\wintermute`, CUSTOM ORDER MAID 3D2 It's a Night Magic는 `dlc\enpublic\inm_update\inm_update\data`였습니다.
판별 규칙은 바꾸지 않았습니다. 이런 게임은 직접 설치하세요.

「MelonLoader를 지운 흔적」을 건너뛰는 이유: `UserLibs\`는 MelonLoader가 처음 실행될 때, `Mods\`·`Plugins\`·`UserData\`는 설치할 때 생기고, 로더를 지워도 남습니다. 사용자가 모드가 안 되는 게임에서 일부러 지운 로더를 일괄 설치가 되살린 일이 있어서 넣은 규칙입니다([12-MelonLoader 작업 기록](12-MelonLoader-작업-기록.md)). `Mods\`나 `UserData\` 하나만으로는 판단하지 않습니다(게임 자체 폴더일 수 있음).

## 안전장치

- **덮어쓰지 않습니다.** 파일은 `FileMode.CreateNew`로 만듭니다. 교체 대상(`version.dll`, `dobby.dll`, `NOTICE.txt`, `MelonLoader\`)은 미리 옮겨 둡니다.
- **다 풀고 나서 확인합니다.** zip의 파일이 모두 대상 폴더에 있는지 확인합니다. 빠진 게 있으면 "백신(Windows Defender 등)이 격리했을 가능성"을 안내하고 실패로 처리합니다.
- **실패하면 되돌립니다.** 그 게임에 이번에 새로 만든 파일과 빈 폴더를 지우고, 옮겨 둔 옛 설치를 제자리로 돌립니다. 되돌리지 못한 것이 있으면 오류 메시지에 경로를 적습니다. 다른 게임의 설치는 계속됩니다.
- **zip 밖으로 나가지 않습니다.** 대상 폴더 밖을 가리키는 zip 항목이 있으면 실패로 처리합니다.
- **성공하면** `Mods`, `Plugins`, `UserData`를 만들어 둡니다.
- **판별 결과는 바뀌지 않습니다.** 스캐너가 이름에 `MelonLoader`가 들어간 폴더를 무시하므로, 설치나 교체 뒤에도 분류와 6개 보기 목록은 그대로입니다.
- **로그 파일을 남기지 않습니다.** 결과는 상태 줄과, 실패가 있을 때 뜨는 창으로만 보여 줍니다.

## 제거

이 도구에는 제거 기능이 없습니다. 게임 폴더에서 `version.dll`과 `MelonLoader\`를 지우면 됩니다.

## muse dash tool과 다른 점

| | muse dash tool | 이 도구 |
|---|---|---|
| 대상 | Muse Dash 한 게임, x64 | 체크된 게임 전부, x64·x86 |
| 버전 | 사용자가 콤보에서 고름 | 게임마다 규칙으로 고름 (직접 zip도 가능) |
| 에셋 찾기 | 이름에 `x64`가 들어간 zip | 이름이 정확히 `MelonLoader.x64.zip` / `x86.zip` |
| 기존 설치 | 항상 지우고 새로 설치 | 규칙과 다를 때만 교체(기본 켜짐, 끌 수 있음). 옮겨 뒀다가 실패하면 되돌림 |
| `version.dll` | 항상 지움 | `MelonLoader\`가 함께 있을 때만 교체 |
| 다운로드 | 매번 임시 폴더에 받고 지움 | 캐시에 두고 다시 씀, 크기 대조(+ `.sha512`가 있으면 SHA-512) |
| 설치 후 검증 · Mods 등 폴더 만들기 | 있음 | 같은 방식으로 가져옴 |

## 다음 문서

- 실제 설치·교체 기록 → [12-MelonLoader 작업 기록](12-MelonLoader-작업-기록.md)
- 대표 플레이어와 신뢰도 → [04-판별 로직](04-판별-로직.md)「대표 플레이어 고르기」
- 멤버별 상세 → [05-코드 레퍼런스](05-코드-레퍼런스.md)「MelonLoader」

