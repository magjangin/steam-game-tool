# 12. MelonLoader 작업 기록

> 이 저장소의 도구로 실제 라이브러리에 MelonLoader를 설치·교체한 기록입니다. 규칙과 동작은 [11-MelonLoader 설치](11-MelonLoader-설치.md)에 있습니다.
> 날짜와 개수는 그때의 라이브러리 기준입니다. 「세션 scratchpad」는 작업한 Claude Code 세션의 임시 폴더라서 지금은 없습니다.

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

사용자가 "Halchemist는 교체가 안 되어 있다, 0.7.3"이라고 지적했습니다. Halchemist의 MelonLoader는 `v0.7.3-ci.2497 Open-Beta`(DLL 0.7.3.2497, 4/17 빌드)였습니다. 정식 v0.7.3(DLL 0.7.3.0, 5/15 빌드)보다 먼저 나온 CI 빌드인데, 도구가 이를 「0.7.3 이상」으로 판정해 건너뛰었습니다. 비교를 고친 뒤([11-MelonLoader 설치](11-MelonLoader-설치.md)「이미 설치된 게임」) 교체했습니다.

- 대상: 0.7.3 CI 빌드가 깔린 46개. 0.7.3-ci.2446 25개, ci.2466 1개, ci.2497 20개입니다. 0.8.0 CI 빌드 5개는 이때는 그대로 뒀다가, 아래 「0.8.0 CI 빌드 5개를 0.7.3으로」에서 바꿨습니다. 새 설치는 하지 않았습니다.
- 방법: 앱과 같은 `PlanAuto`(교체 켜짐)로 계획을 세웠습니다. 그중 위 46개만 골라 `InstallAll`을 불렀습니다.
- 백업: 46개의 `version.dll`·`dobby.dll`·`NOTICE.txt`·`MelonLoader\`를 세션 scratchpad에 떠 뒀습니다(2.3GB). 사용자 요청으로 교체 확인 뒤 휴지통으로 옮겼습니다. 앞선 작업의 백업(`replace-backup`, `legacy-remove-backup` 등)도 함께 옮겼습니다.
- 결과: 46개 모두 `MelonLoader.dll`·`version.dll`이 0.7.3.0입니다. `*.replaced-*` 잔여물은 없고, 교체 시작 뒤 `Mods`·`Plugins`·`UserData`·`UserLibs`에서 바뀐 파일도 없습니다. 교체 후 라이브러리 분포는 0.7.3 629 · 0.8.0-ci.2548 3 · 0.8.0-ci.2576 2입니다.
- **각 게임을 실행해 보지는 않았습니다.**
- 같은 시간대에 Paper Animal Adventure의 `version.dll`·`MelonLoader\`가 사라졌습니다(폴더 수정 20:08). 사용자가 직접 지운 것이었습니다(위 「직접 지운 게임은 자동 설치하지 않음」).

## 0.8.0 CI 빌드 5개를 0.7.3으로 (2026-09-21, 사용자 선택 「5개 모두 0.7.3」)

사용자는 MelonLoader Installer에 모든 게임이 `v0.7.3`으로 보이기를 원했습니다. 그래서 0.8.0-ci가 깔린 5개도 정식 0.7.3으로 낮췄고, 규칙도 "CI 빌드는 항상 교체"로 바꿨습니다([11-MelonLoader 설치](11-MelonLoader-설치.md)「이미 설치된 게임」).

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

## 다음 문서

- 규칙과 안전장치 → [11-MelonLoader 설치](11-MelonLoader-설치.md)
