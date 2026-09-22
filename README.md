# steam game tool

설치된 Steam 게임을 훑어서 **Unity 엔진 게임인지**, 그리고 **Mono와 IL2CPP 중 어떤 스크립팅 백엔드를 쓰는지** 판별하는 Windows 데스크톱 도구입니다.

> 모드 로더(BepInEx·MelonLoader)는 Mono용과 IL2CPP용이 서로 다릅니다. 게임마다 폴더를 열어 확인하는 대신, 라이브러리 전체를 한 번에 스캔해 목록으로 뽑습니다.

## 빠른 시작

```bash
dotnet run --project "steam game tool/steam game tool.csproj"
```

1. **「폴더 ▸ 자동 감지」** — 레지스트리와 `libraryfolders.vdf`로 모든 Steam 라이브러리를 찾습니다
2. **「스캔 시작」** — 각 게임 폴더에서 Unity 흔적을 검사합니다
3. **「게임만」** — 사운드트랙·소프트웨어·도구와 잔여 폴더를 제외합니다
4. **「보기」** — Unity Mono / Unity IL2CPP / MonoBleedingEdge / Managed / 중첩 별로 필터링
5. **「저장 ▸ TXT로 저장…」** — 현재 목록을 파일로 내보냅니다
6. **「저장 ▸ 6개 모두 저장…」** — 보기 목록 6개를 선택한 폴더에 기존 파일명으로 한 번에 내보냅니다
7. **「MelonLoader 설치 ▸ 자동 설치…」** — **보기와 상관없이** 전체 Unity 게임 중 체크된 MonoBleedingEdge·IL2CPP 게임에 0.7.3 이상 중 최신 정식 릴리스를 GitHub에서 받아 설치합니다. 구형 레거시 Mono와 MelonLoader를 직접 지운 게임은 자동 설치하지 않습니다. 가지고 있는 zip은 「MelonLoader 설치 ▸ zip 직접 선택…」으로 지금 보기 목록에 설치합니다. 목록 위 「MelonLoader 설치 대상」 체크박스로 지금 보기 목록을 모두 체크하거나 풉니다 ([docs/11](docs/11-MelonLoader-설치.md))

.NET 10 SDK가 필요합니다. 자세한 내용은 [docs/06-빌드와 실행](docs/06-빌드와-실행.md).

```bash
dotnet test "steam game tool.slnx"
```

## 판별 방식 요약

| 발견한 것 | 판정 |
|---|---|
| `MonoBleedingEdge\` 폴더 | **Mono** |
| `<게임>_Data\Managed\Assembly-CSharp.dll` 등 | **Mono** |
| `<게임>_Data\il2cpp_data\Metadata\global-metadata.dat` | **IL2CPP** |
| `GameAssembly.dll` + `UnityPlayer.dll` | **IL2CPP** |

한 폴더에 런처와 본편의 백엔드가 다르면 **두 태그가 동시에** 붙습니다. 규칙 전문과 오탐·미탐 사례는 [docs/04-판별 로직](docs/04-판별-로직.md)에 있습니다.

집계는 **실제 게임만** 셉니다 — `appmanifest_*.acf`와 `appcacheppinfo.vdf`를 읽어 사운드트랙·소프트웨어·도구와 잔여 폴더를 가려냅니다 ([docs/09-앱 종류 판별](docs/09-앱-종류-판별.md)). 목록에는 폴더명 대신 **스토어 표기 이름**이 나옵니다.

## 문서

**[→ docs/ 전체 문서 보기](docs/README.md)**

| | |
|---|---|
| [01-개요](docs/01-개요.md) | 무엇을 하는 도구인가, 무엇을 하지 않는가 |
| [02-비유로 이해하기](docs/02-비유로-이해하기.md) | Mono/IL2CPP를 일상 비유로 설명 |
| [03-아키텍처](docs/03-아키텍처.md) | 구조, 데이터 흐름, 스레딩 |
| [04-판별 로직](docs/04-판별-로직.md) | 판별 규칙 명세 |
| [05-코드 레퍼런스](docs/05-코드-레퍼런스.md) | 타입·멤버별 상세 |
| [06-빌드와 실행](docs/06-빌드와-실행.md) | 빌드, 배포, 문제 해결 |
| [07-개선 로드맵](docs/07-개선-로드맵.md) | 알려진 문제와 개선 과제 |
| [08-용어집](docs/08-용어집.md) | Steam·Unity·.NET 용어 사전 |
| [09-앱 종류 판별](docs/09-앱-종류-판별.md) | 게임과 사운드트랙·도구 가르기 |
| [11-MelonLoader 설치](docs/11-MelonLoader-설치.md) | 일괄 설치, 버전 규칙, 교체, 건너뛰는 경우, 안전장치 |

## 안전성

- **스캔은 읽기 전용입니다.** 게임 파일을 쓰거나 지우거나 실행하지 않습니다
- **예외는 MelonLoader 설치뿐입니다.** 확인 창에서 설치를 눌렀을 때만, 체크한 게임 폴더에 zip 내용을 추가합니다. 기존 파일은 덮어쓰지 않습니다. 이미 MelonLoader가 있는데 버전이 규칙과 다른 게임은 `version.dll`·`dobby.dll`·`NOTICE.txt`·`MelonLoader\`를 바꿉니다. 「다른 버전이 설치된 게임도 교체」가 기본으로 켜져 있고, 끄면 건너뜁니다. 실패하면 되돌립니다
- **네트워크는 「MelonLoader 설치 ▸ 자동 설치…」에서만 씁니다.** GitHub의 LavaGang/MelonLoader 릴리스 목록과 zip을 받습니다. 스캔은 전부 로컬 파일시스템과 레지스트리 검사입니다
- **관리자 권한이 필요 없습니다**

## 기술 스택

C# / .NET 10 (`net10.0-windows`) · Avalonia 12.0.5 · MVVM 없이 code-behind 직결 · xUnit 테스트 34개

실제 라이브러리(게임 885개) 스캔에 **약 0.7초**가 걸립니다.
