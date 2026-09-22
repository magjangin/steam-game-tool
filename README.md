# steam game tool

**내 Steam 라이브러리에서 Unity 게임을 찾아 Mono / IL2CPP를 가려내고, 고른 게임에 MelonLoader를 한 번에 설치하는 Windows 데스크톱 도구입니다.**

![Windows](https://img.shields.io/badge/platform-Windows-0078D6) ![.NET 10](https://img.shields.io/badge/.NET-10-512BD4) ![Avalonia 12](https://img.shields.io/badge/UI-Avalonia%2012-8B44AC)

게임 885개가 든 라이브러리를 약 0.7초 만에 훑습니다.

## 왜 만들었나

Unity 게임은 C# 코드를 실행 파일로 만드는 방식(스크립팅 백엔드)이 두 가지입니다. 겉으로는 구분되지 않지만, 모드를 깔거나 코드를 들여다보려면 어느 쪽인지부터 알아야 합니다.

| | Mono | IL2CPP |
|---|---|---|
| 게임 안의 코드 | `.dll`로 거의 원본 그대로 들어 있음 | C++을 거쳐 네이티브 기계어로 바뀌어 있음 |
| 모드 로더 | BepInEx (Mono 빌드), MelonLoader | BepInEx (IL2CPP 빌드), MelonLoader |
| 코드 열어 보기 | dnSpy·ILSpy로 바로 읽힘 | Il2CppDumper 등으로 복원해야 함 |

이걸 알려면 게임 폴더를 하나씩 열어 봐야 합니다. 게임이 수백 개면 수백 번입니다. 이 도구는 라이브러리 전체를 한 번에 스캔해 목록으로 뽑고, 그 목록에서 고른 게임에 MelonLoader까지 설치합니다.

## 주요 기능

- **Steam 라이브러리 자동 감지** — 레지스트리와 `libraryfolders.vdf`로 모든 라이브러리 폴더를 찾습니다. 폴더를 직접 지정할 수도 있습니다.
- **Unity 백엔드 판별** — 게임마다 Mono와 IL2CPP를 가리고, Mono는 다시 구형 레거시(`_Data\Mono`)와 MonoBleedingEdge로 나눠 배지로 표시합니다.
- **게임만 집계** — `appmanifest_*.acf`와 `appcacheppinfo.vdf`를 읽어 사운드트랙·소프트웨어·도구와 잔여 폴더를 뺍니다. 목록에는 폴더명 대신 스토어 이름이 나옵니다.
- **보기 필터와 TXT 내보내기** — 6가지 보기로 목록을 거릅니다. 지금 보는 목록을 TXT로 저장하거나, 6개 목록을 한 번에 저장합니다.
- **MelonLoader 일괄 설치** — 체크한 게임에 GitHub의 최신 정식 릴리스를 받아 설치합니다. 가지고 있는 zip을 골라 설치할 수도 있습니다.

## 시작하기

Windows와 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)가 필요합니다. 관리자 권한은 필요 없습니다.

```bash
git clone https://github.com/magjangin/steam-game-tool.git
cd steam-game-tool
dotnet run --project "steam game tool/steam game tool.csproj"
```

.NET 없이도 실행되는 exe 파일 하나(약 100MB)로 만들려면 다음 명령을 씁니다. 결과물은 `steam game tool\bin\Release\net10.0-windows\win-x64\publish\`에 생깁니다.

```bash
dotnet publish "steam game tool/steam game tool.csproj" -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 사용법

1. **「폴더 ▸ 자동 감지」** — Steam 라이브러리를 찾습니다. 못 찾으면 「폴더 ▸ 찾아보기…」로 `steamapps\common`을 직접 고릅니다.
2. **「스캔 시작」** — 게임 폴더마다 Unity 흔적을 검사합니다.
3. **「보기」** — 아래 6가지 중 하나로 거릅니다. 「게임만」을 켜 두면 사운드트랙·도구 등이 빠집니다.

   | 보기 | 보여 주는 게임 |
   |---|---|
   | 전체 Unity 게임 | Mono와 IL2CPP 전부 |
   | Unity Mono | Mono 백엔드 |
   | Mono (구형 레거시) | 옛 Unity의 `_Data\Mono` 런타임 |
   | MonoBleedingEdge | 최근 Unity의 `MonoBleedingEdge\` 런타임 |
   | Unity IL2CPP | IL2CPP 백엔드 |
   | 중첩 | 설치 폴더 아래 하위 폴더에 Unity 게임이 들어 있는 경우 |

4. **「저장 ▸ TXT로 저장…」** 또는 **「저장 ▸ 6개 모두 저장…」** — 목록을 파일로 내보냅니다.
5. **「MelonLoader 설치」** — 아래 설명을 참고하세요.

## MelonLoader 설치

「MelonLoader 설치」 메뉴에 두 가지 방법이 있습니다. 목록 왼쪽 체크를 풀면 그 게임은 빠집니다.

| 메뉴 | 대상 | 설치하는 버전 |
|---|---|---|
| 자동 설치… | 전체 Unity 게임 중 체크된 MonoBleedingEdge·IL2CPP 게임 | GitHub [LavaGang/MelonLoader](https://github.com/LavaGang/MelonLoader)의 0.7.3 이상 최신 정식 릴리스 |
| zip 직접 선택… | 지금 보기 목록에서 체크된 게임 | 직접 고른 zip (nightly 등) |

- **먼저 보여 주고 설치합니다.** 확인 창에 설치·교체할 게임과 건너뛸 게임, 건너뛰는 이유가 나옵니다.
- **위험한 게임은 건너뜁니다.** BepInEx가 이미 있는 게임, 안티치트(EasyAntiCheat·BattlEye) 폴더가 있는 게임, 실행 파일을 확실히 찾지 못한 게임이 그렇습니다. 자동 설치는 구형 레거시 Mono 게임과 MelonLoader를 직접 지운 흔적이 있는 게임도 건너뜁니다.
- **다른 버전은 교체합니다.** 이미 깔린 MelonLoader가 규칙과 다른 버전이거나 CI(nightly) 빌드면 교체합니다. 이때 `Mods`·`Plugins`·`UserData`·`UserLibs`는 그대로 둡니다. 메뉴의 「다른 버전이 설치된 게임도 교체」를 끄면 교체하지 않습니다.
- **실패하면 되돌립니다.** 기존 파일은 덮어쓰지 않습니다. 설치 중 문제가 생기면 그 게임을 설치 전 상태로 돌려놓습니다.
- **x64·x86을 알아서 고릅니다.** 게임 실행 파일의 헤더를 읽어 맞는 zip을 씁니다.

제거 기능은 없습니다. 게임 폴더에서 `version.dll`과 `MelonLoader\`를 지우면 됩니다. 자세한 규칙은 [docs/11-MelonLoader 설치](docs/11-MelonLoader-설치.md)에 있습니다.

## 판별 방식

| 발견한 것 | 판정 |
|---|---|
| `MonoBleedingEdge\` 폴더 | **Mono** |
| `<게임>_Data\Managed\Assembly-CSharp.dll` 등 | **Mono** |
| `<게임>_Data\il2cpp_data\Metadata\global-metadata.dat` | **IL2CPP** |
| `GameAssembly.dll` + `UnityPlayer.dll` | **IL2CPP** |

한 폴더에 런처와 본편의 백엔드가 다르면 두 태그가 함께 붙습니다. 규칙 전문과 오탐·미탐 사례는 [docs/04-판별 로직](docs/04-판별-로직.md)에 있습니다.

## 안전성

- **스캔은 읽기 전용입니다.** 게임 파일을 쓰거나 지우거나 실행하지 않습니다.
- **게임 폴더를 바꾸는 건 MelonLoader 설치뿐입니다.** 확인 창에서 「설치」를 눌렀을 때만 바꿉니다.
- **네트워크는 자동 설치에서만 씁니다.** GitHub에서 MelonLoader 릴리스 목록과 zip을 받습니다. 스캔은 로컬 파일과 레지스트리만 봅니다.

> 모드 사용은 게임마다 약관이 다릅니다. 특히 온라인 게임에 설치하기 전에는 약관을 확인하세요.

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
| [10-이름과 내보내기](docs/10-이름과-내보내기.md) | 이름 정규화, 스캔 범위, TXT 형식 |
| [11-MelonLoader 설치](docs/11-MelonLoader-설치.md) | 일괄 설치, 버전 규칙, 교체, 건너뛰는 경우, 안전장치 |

## 개발

C# / .NET 10 (`net10.0-windows`) · Avalonia 12.0.5 · MVVM 없이 code-behind 직결

```bash
dotnet test "steam game tool.slnx"
```

xUnit 테스트 163개가 판별 규칙과 MelonLoader 설치 규칙을 고정합니다.

---

Valve, Unity, LavaGang과 관계없는 개인 프로젝트입니다.
