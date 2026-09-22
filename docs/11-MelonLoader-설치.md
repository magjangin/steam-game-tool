# 11. MelonLoader 일괄 설치

체크된 게임에 MelonLoader를 설치합니다. 방법은 두 가지이고, 둘 다 「MelonLoader 설치」 메뉴에 있습니다. **자동 설치는 보기와 상관없이 전체 Unity 게임**이 대상이고, **zip 직접 선택은 지금 보기 목록**이 대상입니다. 두 경우 모두 「게임만」과 게임별 체크가 적용됩니다.

| 메뉴 항목 | zip | 버전 |
|---|---|---|
| 「자동 설치…」 | GitHub 릴리스에서 받음 (한 번 받은 것은 다시 씀) | **MonoBleedingEdge·IL2CPP 게임만**, 0.7.3 이상 중 최신 정식 릴리스. 구형 레거시 Mono는 건너뜀 |
| 「zip 직접 선택…」 | 가지고 있는 zip (nightly 등) | 고른 zip을 체크된 게임 모두에 씀(구형 레거시 포함). 규칙 미적용, 네트워크 안 씀 |

이 도구에서 게임 폴더에 파일을 쓰는 곳(`MelonLoaderInstaller.cs`)과 네트워크를 쓰는 곳(`MelonLoaderReleases.cs`의 `MelonReleaseClient`)은 여기뿐입니다.
구현은 사용자가 쓰던 `H:\source\repos\muse dash tool`의 `MelonLoaderService.cs`를 참고했고, 일괄 설치에 맞게 바꿨습니다(맨 아래 표 참고).

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

## 실제 설치 시험 (2026-09-21, 사용자 허락)

앱의 `OnMelonAutoClick`을 그대로 실행했습니다(headless). 보기는 구형 레거시, Cuphead만 체크, 교체는 켠 상태였습니다.
이때는 규칙이 잘못된 0.6.1이었기 때문에 Cuphead가 **0.7.1 → 0.6.1로 교체**됐습니다. 설치 과정 자체는 모두 정상이었습니다.

- 확인 창: `설치 대상 1개 (0.6.1 x64 1)`, `Cuphead (0.6.1 x64)  [교체 0.7.1 → 0.6.1]`
- v0.6.1 `MelonLoader.x64.zip`을 받았습니다. 22,743,856바이트이고, 크기와 SHA-512가 모두 일치했습니다.
- 상태 줄: `MelonLoader 설치 완료 1개(교체 1) · 건너뜀 0개 · 실패 0개`
- 설치 후 상태:
  - `MelonLoader\...\MelonLoader.dll`이 0.6.1이 됐습니다.
  - 루트에 `dobby.dll`과 `NOTICE.txt`가 추가됐습니다.
  - `Mods\CupheadMods.dll`과 `UserData`는 그대로이고, `*.replaced-*` 잔여물은 없습니다. zip의 213개 파일이 모두 있습니다.

**되돌림:** 규칙을 0.7.1로 바로잡은 뒤, 시험 전에 떠 둔 백업으로 Cuphead를 원래 상태로 복원했습니다.
- 0.6.1이 추가한 `dobby.dll`·`NOTICE.txt`·`MelonLoader\`를 치웠습니다.
- 복원 결과 `version.dll` 해시, 루트 파일 목록, `MelonLoader\` 104개 파일이 시험 전과 모두 같습니다.
- 다시 계획을 세우면 Cuphead는 「이미 MelonLoader 있음 (0.7.1)」로 건너뜁니다.

**복원 후 실행 확인 (2026-09-21 19:09, 사용자가 실행):** `Latest.log`를 보면 `MelonLoader v0.7.1`, `Unity Version: 2017.4.9f1`, `Melon Assembly loaded: '.\Mods\CupheadMods.dll'`, `1 Mod loaded.`, `Cuphead Trainer Loaded!`까지 찍혔고, 정상 종료(`Preferences Saved!`)했습니다.
`Exception`이 들어간 줄은 `AccessTools.Method: Could not find method for type System.Exception and name PrepForRemoting` 하나뿐입니다. 이 줄은 9/14 로그에도 똑같이 있고, 오류가 아니라 경고입니다.

## 버전 불일치 13개 일괄 교체 (2026-09-21, 사용자 요청)

사용자가 "LiEat도 0.7.1로 맞추고, 다른 버전이 설치된 게임도 교체"하라고 요청했습니다.
그래서 이미 설치돼 있으나 규칙과 다른 13개만 체크하고 앱의 `OnMelonAutoClick`을 실행했습니다(headless, 보기 「전체 Unity」, 교체 기본값 켜짐).
**MelonLoader가 없는 게임에는 새로 설치하지 않았습니다.**

- 시작 전 확인: 대상 게임 중 실행 중인 것이 없었습니다. 13개의 `version.dll`·`dobby.dll`·`NOTICE.txt`·`MelonLoader\`를 모두 백업했습니다(442MB, 세션 scratchpad `replace-backup`).
- 받은 zip: v0.7.3 x64·x86, v0.7.1 x86. v0.7.3 x64는 이미 받아 둔 것이 있어 다시 썼습니다.
- 상태 줄: `MelonLoader 설치 완료 13개(교체 13) · 건너뜀 0개 · 실패 0개`
- 결과:

| 게임 | 교체 전 → 후 |
|---|---|
| Dunjungle, Egging On, Going Under, Guidus Zero, Hollow Survivors, Ira, PROJECT TACHYON, Tower Hunter, Wild Bastards | 0.7.1 → 0.7.3 x64 |
| Panta Rhei | 0.7.1 → 0.7.3 x86 |
| Slayer : the Demon Haunted World | 0.7.2.2385(nightly) → 0.7.3 x64 |
| 幻想乡妖怪塔防 ~ Touhou Monster TD | 0.7.2 → 0.7.3 x64 |
| LiEat (구형 레거시) | 0.7.3 → 0.7.1 x86 |

- 13개 모두 zip 파일이 빠짐없이 들어갔고, `Mods` 목록이 교체 전과 같으며(Touhou Monster TD의 모드 1개 포함), `*.replaced-*` 잔여물은 없습니다.
- 다시 계획을 세우면 버전 불일치는 0개이고, 「이미 MelonLoader 있음」은 146개에서 159개가 됐습니다.
- **각 게임을 실행해 보지는 않았습니다.**

## 구형 레거시 10개에서 MelonLoader 제거 (2026-09-21, 사용자 선택)

구형 레거시를 자동 설치에서 뺀 뒤, 사용자가 이미 깔린 것도 「10개 전부 제거」로 골랐습니다. 이 작업은 도구 기능이 아니라 한 번만 한 수동 작업입니다.

- 대상(모두 0.7.1): Adventures of Chris, ARIA CHRONICLE, Cuphead, Dandara, ICEY, Joggernauts, LiEat, Mages of Mystralia, The Last Tinker, 永遠消失的幻想鄉.
- 실행 중인 대상 게임이 없는 것을 확인했습니다. 10개의 `version.dll`과 `MelonLoader\`를 백업하고 해시와 파일 수까지 검증한 뒤 지웠습니다. 백업은 세션 scratchpad `legacy-remove-backup`에 있습니다.
- 결과: 10개 모두 `version.dll`·`MelonLoader\`가 없습니다. `Mods`·`Plugins`·`UserData`·`UserLibs`는 파일 목록과 크기가 제거 전과 같습니다.
- Cuphead의 `Mods\CupheadMods.dll`(Cuphead Trainer)은 남아 있지만, MelonLoader가 없으니 로드되지 않습니다.

## 직접 지운 게임은 자동 설치하지 않음 (2026-09-21, 사용자 선택)

사용자는 모드 로더가 동작하지 않는 게임에서 MelonLoader를 일부러 지웠습니다. 사용자가 든 예는 Paper Animal Adventure, Menherarium, Skul입니다.
그런데 자동 설치는 그런 게임을 "MelonLoader 없음"으로 보고 다시 설치했습니다. Skul은 19:44 일괄 설치로 0.7.3이 다시 들어갔고, 19:52에 실행하자 `Failed to invoke the managed init function`으로 실패했습니다.

- 판단 기준: `UserLibs\`는 MelonLoader가 처음 실행될 때 만들고, `Mods\`·`Plugins\`·`UserData\`는 설치할 때 만듭니다. 로더를 지워도 이 폴더들은 남습니다. `Mods\`·`UserData\` 하나만으로는 판단하지 않습니다(게임 자체 폴더일 수 있음).
- 실측: 이 기준에 걸린 게임은 16개였고, 모두 네 폴더를 다 갖고 있었습니다. 그중 11개는 구형 레거시라 이미 제외돼 있습니다. 나머지 6개가 새로 건너뜀 대상입니다: Cursed to Golf, Menherarium: Deadly Dice, Mushroom Island Work Diary, Paper Animal Adventure, Skul: The Hero Slayer, Viewfinder.
- Skul은 사용자 선택으로 19:44에 들어간 `version.dll`·`MelonLoader\`를 휴지통으로 옮겼습니다. `Mods`·`Plugins`·`UserData`·`UserLibs`(4/30)는 그대로입니다.
- 다시 넣고 싶으면 「zip 직접 선택…」으로 설치합니다.

## 0.7.3 CI 빌드 46개 교체 (2026-09-21, 사용자 지적 후 「46개 모두 교체」 선택)

사용자가 "Halchemist는 교체가 안 되어 있다, 0.7.3"이라고 지적했습니다. Halchemist의 MelonLoader는 `v0.7.3-ci.2497 Open-Beta`(DLL 0.7.3.2497, 4/17 빌드)였습니다. 정식 v0.7.3(DLL 0.7.3.0, 5/15 빌드)보다 먼저 나온 CI 빌드인데, 도구가 이를 「0.7.3 이상」으로 판정해 건너뛰었습니다. 비교를 고친 뒤(위 「이미 설치된 게임」) 교체했습니다.

- 대상: 0.7.3 CI 빌드가 깔린 46개. 0.7.3-ci.2446 25개, ci.2466 1개, ci.2497 20개입니다. 0.8.0 CI 빌드 5개는 이때는 그대로 뒀다가, 아래 「0.8.0 CI 빌드 5개를 0.7.3으로」에서 바꿨습니다. 새 설치는 하지 않았습니다.
- 방법: 앱과 같은 `PlanAuto`(교체 켜짐)로 계획을 세웠습니다. 그중 위 46개만 골라 `InstallAll`을 불렀습니다.
- 백업: 46개의 `version.dll`·`dobby.dll`·`NOTICE.txt`·`MelonLoader\`를 세션 scratchpad에 떠 뒀습니다(2.3GB). 사용자 요청으로 교체 확인 뒤 휴지통으로 옮겼습니다. 앞선 작업의 백업(`replace-backup`, `legacy-remove-backup` 등)도 함께 옮겼습니다.
- 결과: 46개 모두 `MelonLoader.dll`·`version.dll`이 0.7.3.0입니다. `*.replaced-*` 잔여물은 없고, 교체 시작 뒤 `Mods`·`Plugins`·`UserData`·`UserLibs`에서 바뀐 파일도 없습니다. 교체 후 라이브러리 분포는 0.7.3 629 · 0.8.0-ci.2548 3 · 0.8.0-ci.2576 2입니다.
- **각 게임을 실행해 보지는 않았습니다.**
- 같은 시간대에 Paper Animal Adventure의 `version.dll`·`MelonLoader\`가 사라졌습니다(폴더 수정 20:08). 사용자가 직접 지운 것이었습니다(위 「직접 지운 게임은 자동 설치하지 않음」).

## 0.8.0 CI 빌드 5개를 0.7.3으로 (2026-09-21, 사용자 선택 「5개 모두 0.7.3」)

사용자는 MelonLoader Installer에 모든 게임이 `v0.7.3`으로 보이기를 원했습니다. 그래서 0.8.0-ci가 깔린 5개도 정식 0.7.3으로 낮췄고, 규칙도 "CI 빌드는 항상 교체"로 바꿨습니다(위 「이미 설치된 게임」).

- 대상: MOMO Crash(0.8.0-ci.2576, 모드 없음), Repit(0.8.0-ci.2548), With the Devilish Her…(0.8.0-ci.2576), Yokai Art 2(0.8.0-ci.2548), Yokai Art: Night Parade(0.8.0-ci.2548). 뒤의 4개에는 사용자가 만든 모드가 1개씩 있고, 0.8.0에서 로드된 로그가 있었습니다.
- 교체 전 호환성 점검: 모드 4개가 참조하는 MelonLoader·0Harmony의 형식과 멤버(14~28개)를 정식 0.7.3 `net35` DLL과 이름·매개변수 개수로 대조했습니다. 없는 것은 0개였습니다. 모드는 `MelonLoader 0.8.0.x`를 참조하지만 강력한 이름이 아니어서 버전이 달라도 로드됩니다. `0Harmony`는 양쪽 모두 2.10.2.0입니다. **게임을 실행해서 확인하지는 않았습니다.**
- 백업: 세션 scratchpad `ci-check\v080-replace-backup`(257MB). 모드가 정상인지 확인되면 지웁니다.
- 결과: 5개 모두 0.7.3.0이고 Mods 등은 그대로입니다. MelonLoader Installer를 새로 열어 보니 설치된 669개가 모두 `v0.7.3`이었습니다.
- 주의: MelonLoader Installer는 게임 목록을 켤 때 한 번만 읽습니다. 교체 전에 열어 둔 창은 옛 버전을 그대로 보여 줍니다. 사용자가 20:06에 연 창에서 교체된 게임이 `0.7.3-ci`로 보였던 것도 이 때문입니다.

## 실측 (2026-09-21, 계획만 세우고 설치하지 않음, 구형 레거시를 자동 설치에서 뺀 뒤)

보기 「전체 Unity」, 「게임만」 기준 831개입니다. 교체를 켜든 끄든 결과는 같습니다.

| | 개수 |
|---|---|
| 설치 (전부 0.7.3) | 569 (x64 513 · x86 56) |
| 이미 MelonLoader 있음 (규칙에 맞음) | 149 |
| 구형 레거시 Mono — 자동 설치 대상 아님 | 107 |
| 다른 버전이 설치됨 | 0 |
| 신뢰도 low · exe 없음 · BepInEx | 5 · 1 · 1 |

GUNVOLT RECORDS Cychronicle(0.7.3)은 규칙에 맞으므로 그대로 둡니다. Cuphead는 구형 레거시라 건드리지 않습니다.

이미 설치된 MelonLoader 폴더 153개의 버전 분포(`MelonLoader.dll` ProductVersion):
0.7.3 79 · 0.7.3.2446 25 · 0.7.3.2497 20 · 0.7.1 19 · 0.8.0.2548 4 · 0.8.0.2576 2 · 0.7.2.x 4.
